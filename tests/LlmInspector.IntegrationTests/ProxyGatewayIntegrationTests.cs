using System.Net;
using System.Net.Sockets;
using System.Text;
using LlmInspector.Adapters;
using LlmInspector.Application;
using LlmInspector.Domain;
using LlmInspector.Gateway;
using LlmInspector.Storage.Sqlite;
using LlmInspector.Telemetry;
using LlmInspector.TestInfrastructure;
using Microsoft.AspNetCore.Http;

namespace LlmInspector.IntegrationTests;

[TestClass]
[DoNotParallelize]
public sealed class ProxyGatewayIntegrationTests
{
    [TestMethod]
    public void ConfigurationRejectsNonLoopbackAndAmbiguousDestinations()
    {
        string[] rejectedDestinations =
        [
            "https://example.com/",
            "http://0.0.0.0:11434/",
            "http://192.168.1.50:11434/",
            "http://wildcard.invalid:11434/",
            "ftp://127.0.0.1/",
            "http://user:secret@127.0.0.1:11434/",
            "http://127.0.0.1:11434/v1",
            "http://127.0.0.1:11434/?target=elsewhere",
            "http://127.0.0.1:11434/#fragment",
        ];

        foreach (string destination in rejectedDestinations)
        {
            _ = Assert.ThrowsExactly<ArgumentException>(
                () => ProxyGatewayOptions.Create(ProxyGatewayOptions.DefaultListenerPort, new Uri(destination)),
                destination);
        }

        _ = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => ProxyGatewayOptions.Create(0, ProxyGatewayOptions.DefaultBackendBaseAddress));
        _ = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => ProxyGatewayOptions.Create(ushort.MaxValue + 1, ProxyGatewayOptions.DefaultBackendBaseAddress));

        Assert.AreEqual(IPAddress.Loopback.ToString(), ProxyGatewayOptions.DefaultBackendBaseAddress.Host);

        ProxyGatewayOptions normalizedLocalhost = ProxyGatewayOptions.Create(
            ProxyGatewayOptions.DefaultListenerPort,
            new Uri("http://localhost:11434/"));
        ProxyGatewayOptions normalizedIpv6 = ProxyGatewayOptions.Create(
            ProxyGatewayOptions.DefaultListenerPort,
            new Uri("http://[::1]:11434/"));
        Assert.AreEqual(IPAddress.Loopback.ToString(), normalizedLocalhost.BackendBaseAddress.Host);
        Assert.AreEqual(IPAddress.IPv6Loopback.ToString(), normalizedIpv6.BackendBaseAddress.Host.Trim('[', ']'));
    }

    [TestMethod]
    public void RemoteBackendConfigurationRequiresExplicitTailscaleHttpsDestination()
    {
        ProxyGatewayOptions remote = ProxyGatewayOptions.CreateTailscaleRemote(
            ProxyGatewayOptions.DefaultListenerPort,
            new Uri("https://backend.example-tailnet.ts.net/"),
            BackendKind.Ollama);

        Assert.AreEqual(BackendConnectionScope.TailscaleHttps, remote.BackendConnectionScope);
        Assert.AreEqual("backend.example-tailnet.ts.net", remote.BackendBaseAddress.Host);

        string[] rejected =
        [
            "http://backend.example-tailnet.ts.net/",
            "https://example.com/",
            "https://100.64.0.1/",
            "https://backend.example-tailnet.ts.net/v1",
            "https://user:secret@backend.example-tailnet.ts.net/",
        ];
        foreach (string destination in rejected)
        {
            _ = Assert.ThrowsExactly<ArgumentException>(() => ProxyGatewayOptions.CreateTailscaleRemote(
                ProxyGatewayOptions.DefaultListenerPort,
                new Uri(destination),
                BackendKind.Ollama));
        }
    }

    [TestMethod]
    public async Task RemoteBackendProbeMeasuresTcpConnectWithoutClaimingInferenceLatency()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Task<TcpClient> accepted = listener.AcceptTcpClientAsync();

        RemoteBackendProbeResult result = await new TcpRemoteBackendProbe(TimeSpan.FromSeconds(2))
            .ProbeAsync(new Uri($"https://127.0.0.1:{port}/"));
        using TcpClient connection = await accepted.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.IsTrue(result.Available);
        Assert.IsNotNull(result.ConnectDuration);
        Assert.IsGreaterThanOrEqualTo(TimeSpan.Zero, result.ConnectDuration.Value);
        Assert.AreEqual("tcp-connect-succeeded", result.ResultCode);
    }

    [TestMethod]
    public async Task ListenerIgnoresGenericHostingUrlAndBindsExactlyOneLoopbackEndpoint()
    {
        await using LoopbackStubServer backend = await LoopbackStubServer.StartAsync(
            context => context.Response.WriteAsync("{}"));

        string? previousUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
        try
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_URLS", "http://0.0.0.0:0");
            await using ProxyGateway gateway = ProxyGateway.Create(
                ProxyGatewayOptions.CreateForTesting(0, backend.Address));

            await gateway.StartAsync();

            Assert.IsNotNull(gateway.ListeningAddress);
            Assert.AreEqual(IPAddress.Loopback.ToString(), gateway.ListeningAddress.Host);
            Assert.AreNotEqual(0, gateway.ListeningAddress.Port);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_URLS", previousUrls);
        }
    }

    [TestMethod]
    public async Task PrivateServeIngressRequiresEnabledBearerAndStripsAllIngressCredentials()
    {
        string? forwardedAuthorization = null;
        string? forwardedIdentity = null;
        await using LoopbackStubServer backend = await LoopbackStubServer.StartAsync(context =>
        {
            forwardedAuthorization = context.Request.Headers.Authorization.SingleOrDefault();
            forwardedIdentity = context.Request.Headers["Tailscale-User-Login"].SingleOrDefault();
            return context.Response.WriteAsync("{}");
        });
        FixtureRemoteAuthorizer authorizer = new(enabled: true, "fixture-remote-token");
        await using ProxyGateway gateway = ProxyGateway.Create(
            ProxyGatewayOptions.CreateForTesting(0, backend.Address),
            remoteAccessAuthorizer: authorizer);
        await gateway.StartAsync();
        using HttpClient client = CreateProxyClient(gateway.ListeningAddress!);

        using HttpRequestMessage missingToken = CreateRemoteModelsRequest();
        using HttpResponseMessage missingResponse = await client.SendAsync(missingToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, missingResponse.StatusCode);

        using HttpRequestMessage wrongToken = CreateRemoteModelsRequest();
        wrongToken.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "wrong");
        using HttpResponseMessage wrongResponse = await client.SendAsync(wrongToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, wrongResponse.StatusCode);

        using HttpRequestMessage accepted = CreateRemoteModelsRequest();
        accepted.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            "fixture-remote-token");
        accepted.Headers.TryAddWithoutValidation("X-Forwarded-For", "100.64.0.10");
        using HttpResponseMessage acceptedResponse = await client.SendAsync(accepted);

        Assert.AreEqual(HttpStatusCode.OK, acceptedResponse.StatusCode);
        Assert.IsNull(forwardedAuthorization);
        Assert.IsNull(forwardedIdentity);

        HttpRequestMessage CreateRemoteModelsRequest()
        {
            HttpRequestMessage request = new(HttpMethod.Get, ProxyGateway.ModelsPath);
            request.Headers.Host = "inspector.example-tailnet.ts.net";
            request.Headers.TryAddWithoutValidation("Tailscale-User-Login", "user@example.com");
            return request;
        }
    }

    [TestMethod]
    public async Task DisabledServeAndFunnelLikeIngressFailClosedWhileLocalTrafficStillWorks()
    {
        int backendRequests = 0;
        string? localAuthorization = null;
        await using LoopbackStubServer backend = await LoopbackStubServer.StartAsync(context =>
        {
            Interlocked.Increment(ref backendRequests);
            localAuthorization = context.Request.Headers.Authorization.SingleOrDefault();
            return context.Response.WriteAsync("{}");
        });
        await using ProxyGateway gateway = ProxyGateway.Create(
            ProxyGatewayOptions.CreateForTesting(0, backend.Address),
            remoteAccessAuthorizer: new FixtureRemoteAuthorizer(enabled: false, "fixture-remote-token"));
        await gateway.StartAsync();
        using HttpClient client = CreateProxyClient(gateway.ListeningAddress!);

        using HttpRequestMessage disabledServe = new(HttpMethod.Get, ProxyGateway.ModelsPath);
        disabledServe.Headers.Host = "inspector.example-tailnet.ts.net";
        disabledServe.Headers.TryAddWithoutValidation("Tailscale-User-Login", "user@example.com");
        disabledServe.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            "fixture-remote-token");
        using HttpResponseMessage disabledResponse = await client.SendAsync(disabledServe);
        Assert.AreEqual(HttpStatusCode.Forbidden, disabledResponse.StatusCode);

        using HttpRequestMessage funnelLike = new(HttpMethod.Get, ProxyGateway.ModelsPath);
        funnelLike.Headers.Host = "inspector.example-tailnet.ts.net";
        funnelLike.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            "fixture-remote-token");
        using HttpResponseMessage funnelResponse = await client.SendAsync(funnelLike);
        Assert.AreEqual(HttpStatusCode.Forbidden, funnelResponse.StatusCode);

        using HttpRequestMessage local = new(HttpMethod.Get, ProxyGateway.ModelsPath);
        local.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            "local-backend-credential");
        using HttpResponseMessage localResponse = await client.SendAsync(local);
        Assert.AreEqual(HttpStatusCode.OK, localResponse.StatusCode);
        Assert.AreEqual(1, backendRequests);
        Assert.AreEqual("Bearer local-backend-credential", localAuthorization);
    }

    [TestMethod]
    public async Task NonStreamingRequestPreservesBodyQueryHeadersAndBackendResponse()
    {
        string? forwardedBody = null;
        string? forwardedPathAndQuery = null;
        string? forwardedHeader = null;
        const string requestBody = "{\"model\":\"fixture\",\"messages\":[{\"role\":\"user\",\"content\":\"opaque\"}]}";
        const string responseBody = "{\"id\":\"fixture-response\",\"choices\":[{\"message\":{\"content\":\"opaque-reply\"}}]}";

        await using LoopbackStubServer backend = await LoopbackStubServer.StartAsync(async context =>
        {
            using StreamReader reader = new(context.Request.Body, Encoding.UTF8);
            forwardedBody = await reader.ReadToEndAsync(context.RequestAborted);
            forwardedPathAndQuery = $"{context.Request.Path}{context.Request.QueryString}";
            forwardedHeader = context.Request.Headers["X-Fixture"].Single();

            context.Response.StatusCode = StatusCodes.Status201Created;
            context.Response.ContentType = "application/json";
            context.Response.Headers.Append("X-Backend-Fixture", "preserved");
            await context.Response.WriteAsync(responseBody, context.RequestAborted);
        });
        CollectingObservationSink sink = new();
        await using ProxyGateway gateway = ProxyGateway.Create(
            ProxyGatewayOptions.CreateForTesting(0, backend.Address),
            sink);
        await gateway.StartAsync();
        using HttpClient client = CreateProxyClient(gateway.ListeningAddress!);
        using HttpRequestMessage request = new(HttpMethod.Post, "/v1/chat/completions?fixture=1")
        {
            Content = new StringContent(requestBody, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("X-Fixture", "preserved");

        using HttpResponseMessage response = await client.SendAsync(request);
        string actualResponseBody = await response.Content.ReadAsStringAsync();
        ProxyObservation observation = await sink.NextObservation.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        Assert.AreEqual("preserved", response.Headers.GetValues("X-Backend-Fixture").Single());
        Assert.AreEqual(responseBody, actualResponseBody);
        Assert.AreEqual(requestBody, forwardedBody);
        Assert.AreEqual("/v1/chat/completions?fixture=1", forwardedPathAndQuery);
        Assert.AreEqual("preserved", forwardedHeader);
        Assert.AreEqual(StatusCodes.Status201Created, observation.HttpStatusCode);
        Assert.AreEqual(ProxyOutcome.Completed, observation.Outcome);
    }

    [TestMethod]
    public async Task FragmentedSseAndToolPayloadsPreserveExactOrder()
    {
        const string requestBody = "{\"messages\":[{\"content\":\"call a tool\"}],\"tools\":[{\"function\":{\"name\":\"fixture\",\"parameters\":{\"type\":\"object\"}}}]}";
        string? forwardedBody = null;
        string[] fragments =
        [
            "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"function\":{\"name\":\"fix",
            "ture\",\"arguments\":\"{\\\"secret\\\":",
            "\\\"opaque\\\"}\"}}]}}]}\n\n",
            "data: [DONE]\n\n",
        ];
        string expectedResponse = string.Concat(fragments);

        await using LoopbackStubServer backend = await LoopbackStubServer.StartAsync(async context =>
        {
            using StreamReader reader = new(context.Request.Body, Encoding.UTF8);
            forwardedBody = await reader.ReadToEndAsync(context.RequestAborted);
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "text/event-stream";

            foreach (string fragment in fragments)
            {
                await context.Response.WriteAsync(fragment, context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
                await Task.Delay(5, context.RequestAborted);
            }
        });
        SequenceOperationSink operations = new();
        await using ProxyGateway gateway = ProxyGateway.Create(
            ProxyGatewayOptions.CreateForTesting(0, backend.Address),
            operationSink: operations);
        await gateway.StartAsync();
        using HttpClient client = CreateProxyClient(gateway.ListeningAddress!);
        using HttpRequestMessage request = new(HttpMethod.Post, ProxyGateway.ChatCompletionsPath)
        {
            Content = new StringContent(requestBody, Encoding.UTF8, "application/json"),
        };
        Guid operationId = Guid.NewGuid();
        request.Headers.TryAddWithoutValidation(InspectorCorrelationHeaders.OperationId, operationId.ToString("N"));
        request.Headers.TryAddWithoutValidation(InspectorCorrelationHeaders.SessionId, Guid.NewGuid().ToString("N"));
        request.Headers.TryAddWithoutValidation(InspectorCorrelationHeaders.TurnId, Guid.NewGuid().ToString("N"));
        request.Headers.TryAddWithoutValidation(InspectorCorrelationHeaders.TurnSequence, "1");

        using HttpResponseMessage response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead);
        string actualResponse = await response.Content.ReadAsStringAsync();

        Assert.AreEqual("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.AreEqual(requestBody, forwardedBody);
        Assert.AreEqual(expectedResponse, actualResponse);
        TechnicalOperationGraph operation = await operations.ReadAsync();
        Assert.AreEqual(operationId, operation.Operation.OperationId);
        Assert.AreEqual(1m, operation.Turns[0].AvailableToolCount.Value);
        Assert.AreEqual(1m, operation.Turns[0].InvokedToolCount.Value);
        Assert.AreEqual("fixture", operation.ToolEvents[0].ToolName.Value);
    }

    [TestMethod]
    public async Task FirstSseFragmentReachesClientBeforeBackendCompletes()
    {
        const string firstFragment = "data: {\"delta\":\"first\"}\n\n";
        const string finalFragment = "data: [DONE]\n\n";
        TaskCompletionSource<bool> firstFragmentWritten = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> releaseBackend = new(TaskCreationOptions.RunContinuationsAsynchronously);

        await using LoopbackStubServer backend = await LoopbackStubServer.StartAsync(async context =>
        {
            context.Response.ContentType = "text/event-stream";
            await context.Response.WriteAsync(firstFragment, context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
            firstFragmentWritten.TrySetResult(true);
            await releaseBackend.Task.WaitAsync(context.RequestAborted);
            await context.Response.WriteAsync(finalFragment, context.RequestAborted);
        });
        LiveRequestTracker liveState = new();
        CollectingObservationSink sink = new();
        await using ProxyGateway gateway = ProxyGateway.Create(
            ProxyGatewayOptions.CreateForTesting(0, backend.Address),
            sink,
            liveRequestStateSink: liveState);
        await gateway.StartAsync();
        using HttpClient client = CreateProxyClient(gateway.ListeningAddress!);
        using HttpRequestMessage request = new(HttpMethod.Post, ProxyGateway.ChatCompletionsPath)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };

        try
        {
            Task<HttpResponseMessage> responseTask = client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead);
            await firstFragmentWritten.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using HttpResponseMessage response = await responseTask.WaitAsync(TimeSpan.FromSeconds(5));
            await using Stream stream = await response.Content.ReadAsStreamAsync();
            byte[] firstBytes = new byte[Encoding.UTF8.GetByteCount(firstFragment)];
            using CancellationTokenSource readTimeout = new(TimeSpan.FromSeconds(5));
            int bytesRead = await stream.ReadAtLeastAsync(
                firstBytes,
                firstBytes.Length,
                throwOnEndOfStream: true,
                readTimeout.Token);

            Assert.AreEqual(firstBytes.Length, bytesRead);
            Assert.AreEqual(firstFragment, Encoding.UTF8.GetString(firstBytes));
            LiveRequestSnapshot active = liveState.GetSnapshot().ActiveRequests.Single();
            Assert.AreEqual(RequestStage.ReasoningGeneration, active.Stage.Stage);
            Assert.AreEqual(RequestStageEvidence.ProtocolObserved, active.Stage.Evidence);
            Assert.AreEqual(MetricQuality.Unavailable, active.Progress.Quality);
            Assert.AreEqual(MetricQuality.Unavailable, active.Eta.Quality);

            releaseBackend.TrySetResult(true);
            using StreamReader remainderReader = new(stream, Encoding.UTF8);
            Assert.AreEqual(finalFragment, await remainderReader.ReadToEndAsync());

            LiveRequestCollectionSnapshot completed = await WaitForLiveStateAsync(
                liveState,
                snapshot => snapshot.ActiveRequests.Count == 0 &&
                    snapshot.LatestTerminalRequest?.Stage.Stage == RequestStage.Completed);
            Assert.AreEqual(RequestStage.Completed, completed.LatestTerminalRequest?.Stage.Stage);
        }
        finally
        {
            releaseBackend.TrySetResult(true);
        }
    }

    [TestMethod]
    public async Task BackendRedirectIsRelayedWithoutFollowingExternalLocation()
    {
        Uri externalLocation = new("https://example.com/must-not-be-called");
        await using LoopbackStubServer backend = await LoopbackStubServer.StartAsync(context =>
        {
            context.Response.StatusCode = StatusCodes.Status307TemporaryRedirect;
            context.Response.Headers.Location = externalLocation.ToString();
            return Task.CompletedTask;
        });
        await using ProxyGateway gateway = ProxyGateway.Create(
            ProxyGatewayOptions.CreateForTesting(0, backend.Address));
        await gateway.StartAsync();
        using HttpClient client = CreateProxyClient(gateway.ListeningAddress!);

        using HttpResponseMessage response = await client.PostAsync(
            ProxyGateway.ChatCompletionsPath,
            new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.AreEqual(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.AreEqual(externalLocation, response.Headers.Location);
    }

    [TestMethod]
    public async Task ClientCancellationPropagatesToBackendRequest()
    {
        TaskCompletionSource<bool> backendStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> backendCancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using LoopbackStubServer backend = await LoopbackStubServer.StartAsync(async context =>
        {
            backendStarted.TrySetResult(true);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                backendCancelled.TrySetResult(true);
            }
        });
        LiveRequestTracker liveState = new();
        CollectingObservationSink sink = new();
        await using ProxyGateway gateway = ProxyGateway.Create(
            ProxyGatewayOptions.CreateForTesting(0, backend.Address),
            sink,
            liveRequestStateSink: liveState);
        await gateway.StartAsync();
        using HttpClient client = CreateProxyClient(gateway.ListeningAddress!);
        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(10));

        Task<HttpResponseMessage> pendingResponse = client.PostAsync(
            ProxyGateway.ChatCompletionsPath,
            new StringContent("{}", Encoding.UTF8, "application/json"),
            cancellation.Token);
        await backendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        LiveRequestSnapshot waiting = liveState.GetSnapshot().ActiveRequests.Single();
        Assert.AreEqual(RequestStage.PromptProcessing, waiting.Stage.Stage);
        Assert.AreEqual(RequestStageEvidence.ProtocolObserved, waiting.Stage.Evidence);
        Assert.AreEqual(MetricQuality.Unavailable, waiting.Progress.Quality);
        cancellation.Cancel();

        try
        {
            using HttpResponseMessage unexpectedResponse = await pendingResponse;
            Assert.Fail("The client request unexpectedly completed instead of being cancelled.");
        }
        catch (OperationCanceledException)
        {
        }

        Assert.IsTrue(await backendCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        LiveRequestCollectionSnapshot cancelled = await WaitForLiveStateAsync(
            liveState,
            snapshot => snapshot.LatestTerminalRequest?.Stage.Stage == RequestStage.Cancelled);
        Assert.AreEqual(RequestStage.Cancelled, cancelled.LatestTerminalRequest?.Stage.Stage);
        ProxyObservation observation = await sink.NextObservation.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(ProxyErrorType.ClientCancellation, observation.ErrorType);
    }

    [TestMethod]
    public async Task ConcurrentBodiesRemainIsolated()
    {
        await using LoopbackStubServer backend = await LoopbackStubServer.StartAsync(async context =>
        {
            context.Response.ContentType = "application/json";
            await context.Request.Body.CopyToAsync(context.Response.Body, context.RequestAborted);
        });
        await using ProxyGateway gateway = ProxyGateway.Create(
            ProxyGatewayOptions.CreateForTesting(0, backend.Address));
        await gateway.StartAsync();
        using HttpClient client = CreateProxyClient(gateway.ListeningAddress!);
        string[] payloads = Enumerable.Range(0, 16)
            .Select(index => $"{{\"request\":{index},\"content\":\"opaque-{Guid.NewGuid():N}\"}}")
            .ToArray();

        Task<string>[] requests = payloads
            .Select(async payload =>
            {
                using HttpResponseMessage response = await client.PostAsync(
                    ProxyGateway.ChatCompletionsPath,
                    new StringContent(payload, Encoding.UTF8, "application/json"));
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            })
            .ToArray();

        string[] responses = await Task.WhenAll(requests);
        CollectionAssert.AreEquivalent(payloads, responses);
    }

    [TestMethod]
    public async Task ObservationSinkFailureCannotBreakSuccessfulRelay()
    {
        await using LoopbackStubServer backend = await LoopbackStubServer.StartAsync(
            context => context.Response.WriteAsync("{\"ok\":true}"));
        await using ProxyGateway gateway = ProxyGateway.Create(
            ProxyGatewayOptions.CreateForTesting(0, backend.Address),
            new ThrowingObservationSink(),
            liveRequestStateSink: new ThrowingLiveStateSink());
        await gateway.StartAsync();
        using HttpClient client = CreateProxyClient(gateway.ListeningAddress!);

        using HttpResponseMessage response = await client.PostAsync(
            ProxyGateway.ChatCompletionsPath,
            new StringContent("{}", Encoding.UTF8, "application/json"));
        string body = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        Assert.AreEqual("{\"ok\":true}", body);
    }

    [TestMethod]
    public async Task ResourceCollectorStartCallbackCompletionAndDisposalFailuresCannotBreakRelay()
    {
        const string backendBody = "{\"ok\":true}";
        await using LoopbackStubServer backend = await LoopbackStubServer.StartAsync(
            context => context.Response.WriteAsync(backendBody));
        IRequestResourceMonitor[] failingCollectors =
        [
            new ThrowingStartResourceMonitor(),
            new ThrowingSessionResourceMonitor(),
        ];

        foreach (IRequestResourceMonitor collector in failingCollectors)
        {
            CollectingObservationSink sink = new();
            await using ProxyGateway gateway = ProxyGateway.Create(
                ProxyGatewayOptions.CreateForTesting(0, backend.Address),
                sink,
                resourceMonitor: collector);
            await gateway.StartAsync();
            using HttpClient client = CreateProxyClient(gateway.ListeningAddress!);

            using HttpResponseMessage response = await client.PostAsync(
                ProxyGateway.ChatCompletionsPath,
                new StringContent("{}", Encoding.UTF8, "application/json"));
            string actual = await response.Content.ReadAsStringAsync();
            ProxyObservation observation = await sink.NextObservation.Task.WaitAsync(TimeSpan.FromSeconds(5));

            response.EnsureSuccessStatusCode();
            Assert.AreEqual(backendBody, actual);
            Assert.AreEqual(ProxyOutcome.Completed, observation.Outcome);
            Assert.IsNotNull(observation.RuntimeFacts);
        }
    }

    [TestMethod]
    [DataRow(BackendKind.Ollama, "ollama-fixture", 11, 7, 18)]
    [DataRow(BackendKind.LlamaCpp, "llama-cpp-fixture", 13, 8, 21)]
    [DataRow(BackendKind.LmStudio, "lm-studio-fixture", 17, 9, 26)]
    public async Task ConfiguredBackendAdapterProjectsTelemetryWithoutChangingResponse(
        BackendKind backendKind,
        string model,
        int promptTokens,
        int completionTokens,
        int totalTokens)
    {
        string backendBody =
            $"{{\"model\":\"{model}\",\"choices\":[{{\"message\":{{\"content\":\"synthetic\"}}}}]," +
            $"\"usage\":{{\"prompt_tokens\":{promptTokens},\"completion_tokens\":{completionTokens},\"total_tokens\":{totalTokens}}}}}";
        await using LoopbackStubServer backend = await LoopbackStubServer.StartAsync(async context =>
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(backendBody, context.RequestAborted);
        });
        CollectingObservationSink sink = new();
        ProxyGatewayOptions options = ProxyGatewayOptions.CreateForTesting(0, backend.Address, backendKind);
        await using ProxyGateway gateway = ProxyGateway.Create(
            options,
            sink,
            BackendTelemetryAdapters.Create(backendKind));
        await gateway.StartAsync();
        using HttpClient client = CreateProxyClient(gateway.ListeningAddress!);

        using HttpResponseMessage response = await client.PostAsync(
            ProxyGateway.ChatCompletionsPath,
            new StringContent("{}", Encoding.UTF8, "application/json"));
        string actualBody = await response.Content.ReadAsStringAsync();
        ProxyObservation observation = await sink.NextObservation.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(backendBody, actualBody);
        Assert.AreEqual(backendKind, observation.BackendTelemetry.Backend);
        Assert.AreEqual(model, observation.BackendTelemetry.Model?.Value);
        Assert.AreEqual(promptTokens, observation.BackendTelemetry.PromptTokens.Value);
        Assert.AreEqual(completionTokens, observation.BackendTelemetry.CompletionTokens.Value);
        Assert.AreEqual(totalTokens, observation.BackendTelemetry.TotalTokens.Value);
        Assert.AreEqual(ClientKind.GenericUnknown, observation.Client);
    }

    [TestMethod]
    public async Task DedicatedBasePathsProvideExplicitKnownClientAttribution()
    {
        List<ClientEndpoint?> endpoints = [null, .. ClientEndpointCatalog.KnownClients];
        foreach (ClientEndpoint? endpoint in endpoints)
        {
            string? backendPath = null;
            await using LoopbackStubServer backend = await LoopbackStubServer.StartAsync(async context =>
            {
                backendPath = context.Request.Path;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(
                    "{\"model\":\"fixture\",\"usage\":{\"prompt_tokens\":1,\"completion_tokens\":2,\"total_tokens\":3}}",
                    context.RequestAborted);
            });
            CollectingObservationSink sink = new();
            ProxyGatewayOptions options = ProxyGatewayOptions.CreateForTesting(0, backend.Address);
            await using ProxyGateway gateway = ProxyGateway.Create(
                options,
                sink,
                BackendTelemetryAdapters.Create(BackendKind.Ollama));
            await gateway.StartAsync();
            using HttpClient client = CreateProxyClient(gateway.ListeningAddress!);
            string path = endpoint?.ChatCompletionsPath ?? ClientEndpointCatalog.GenericChatCompletionsPath;

            using HttpResponseMessage response = await client.PostAsync(
                path,
                new StringContent("{}", Encoding.UTF8, "application/json"));
            ProxyObservation observation = await sink.NextObservation.Task.WaitAsync(TimeSpan.FromSeconds(5));

            response.EnsureSuccessStatusCode();
            Assert.AreEqual(ProxyGateway.ChatCompletionsPath, backendPath);
            Assert.AreEqual(endpoint?.Client ?? ClientKind.GenericUnknown, observation.Client);
        }
    }

    [TestMethod]
    public async Task ModelDiscoveryPassesThroughEveryConfiguredClientBasePath()
    {
        const string modelsBody =
            "{\"object\":\"list\",\"data\":[{\"id\":\"fixture-model\",\"object\":\"model\"}]}";
        List<string> backendPaths = [];
        await using LoopbackStubServer backend = await LoopbackStubServer.StartAsync(async context =>
        {
            backendPaths.Add(context.Request.Path);
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(modelsBody, context.RequestAborted);
        });
        ProxyGatewayOptions options = ProxyGatewayOptions.CreateForTesting(0, backend.Address);
        await using ProxyGateway gateway = ProxyGateway.Create(
            options,
            telemetryAdapter: BackendTelemetryAdapters.Create(BackendKind.Ollama));
        await gateway.StartAsync();
        using HttpClient client = CreateProxyClient(gateway.ListeningAddress!);
        string[] modelPaths =
        [
            ClientEndpointCatalog.GenericModelsPath,
            .. ClientEndpointCatalog.KnownClients.Select(endpoint => endpoint.ModelsPath),
        ];

        foreach (string path in modelPaths)
        {
            using HttpResponseMessage response = await client.GetAsync(path);
            string actual = await response.Content.ReadAsStringAsync();

            response.EnsureSuccessStatusCode();
            Assert.AreEqual(modelsBody, actual);
        }

        Assert.HasCount(modelPaths.Length, backendPaths);
        Assert.IsTrue(backendPaths.All(path => path == ProxyGateway.ModelsPath));
    }

    [TestMethod]
    public async Task StreamingToolCallOrderAndFinalUsageSurviveObservedRelay()
    {
        string[] events =
        [
            "data: {\"model\":\"fixture\",\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"name\":\"fixture_tool\",\"arguments\":\"{\\\"x\\\":\"}}]}}]}\n\n",
            "data: {\"model\":\"fixture\",\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"1}\"}}]},\"finish_reason\":\"tool_calls\"}],\"usage\":{\"prompt_tokens\":4,\"completion_tokens\":2,\"total_tokens\":6}}\n\n",
            "data: [DONE]\n\n",
        ];
        await using LoopbackStubServer backend = await LoopbackStubServer.StartAsync(async context =>
        {
            context.Response.ContentType = "text/event-stream";
            foreach (string item in events)
            {
                await context.Response.WriteAsync(item, context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
            }
        });
        CollectingObservationSink sink = new();
        ProxyGatewayOptions options = ProxyGatewayOptions.CreateForTesting(0, backend.Address, BackendKind.LlamaCpp);
        await using ProxyGateway gateway = ProxyGateway.Create(
            options,
            sink,
            BackendTelemetryAdapters.Create(BackendKind.LlamaCpp));
        await gateway.StartAsync();
        using HttpClient client = CreateProxyClient(gateway.ListeningAddress!);

        using HttpResponseMessage response = await client.PostAsync(
            ClientEndpointCatalog.KnownClients.Single(item => item.Client == ClientKind.Cline).ChatCompletionsPath,
            new StringContent("{\"stream\":true}", Encoding.UTF8, "application/json"));
        string actual = await response.Content.ReadAsStringAsync();
        ProxyObservation observation = await sink.NextObservation.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(string.Concat(events), actual);
        Assert.AreEqual(ClientKind.Cline, observation.Client);
        Assert.AreEqual(6, observation.BackendTelemetry.TotalTokens.Value);
        Assert.AreEqual(MetricQuality.Unavailable, observation.TimeToFirstToken.Quality);
    }

    [TestMethod]
    public async Task StreamingContentDeltaProducesCalculatedTtftWithoutChangingEvents()
    {
        string[] events =
        [
            "data: {\"model\":\"fixture\",\"choices\":[{\"delta\":{\"role\":\"assistant\",\"content\":\"\"}}]}\n\n",
            "data: {\"model\":\"fixture\",\"choices\":[{\"delta\":{\"content\":\"synthetic\"}}]}\n\n",
            "data: [DONE]\n\n",
        ];
        await using LoopbackStubServer backend = await LoopbackStubServer.StartAsync(async context =>
        {
            context.Response.ContentType = "text/event-stream";
            foreach (string item in events)
            {
                await context.Response.WriteAsync(item, context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
            }
        });
        CollectingObservationSink sink = new();
        ProxyGatewayOptions options = ProxyGatewayOptions.CreateForTesting(0, backend.Address);
        await using ProxyGateway gateway = ProxyGateway.Create(
            options,
            sink,
            BackendTelemetryAdapters.Create(BackendKind.Ollama));
        await gateway.StartAsync();
        using HttpClient client = CreateProxyClient(gateway.ListeningAddress!);

        using HttpResponseMessage response = await client.PostAsync(
            ProxyGateway.ChatCompletionsPath,
            new StringContent("{\"stream\":true}", Encoding.UTF8, "application/json"));
        string actual = await response.Content.ReadAsStringAsync();
        ProxyObservation observation = await sink.NextObservation.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(string.Concat(events), actual);
        Assert.IsNotNull(observation.TimeToFirstToken.Value);
        Assert.AreEqual(MetricUnit.Milliseconds, observation.TimeToFirstToken.Unit);
        Assert.AreEqual(MetricQuality.Calculated, observation.TimeToFirstToken.Quality);
        Assert.AreEqual("first-nonempty-chat-content-delta-v1", observation.TimeToFirstToken.DerivationVersion);
    }

    [TestMethod]
    public async Task CompletePseudonymousCorrelationProducesOnlyAdjacentSignedContextDelta()
    {
        int responseIndex = -1;
        int[] promptTokens = [100, 140, 80, 90];
        List<string[]> forwardedInspectorHeaders = [];
        await using LoopbackStubServer backend = await LoopbackStubServer.StartAsync(async context =>
        {
            forwardedInspectorHeaders.Add(context.Request.Headers.Keys
                .Where(InspectorCorrelationHeaders.Names.Contains)
                .ToArray());
            int index = Interlocked.Increment(ref responseIndex);
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                $"{{\"model\":\"fixture\",\"usage\":{{\"prompt_tokens\":{promptTokens[index]},\"completion_tokens\":1,\"total_tokens\":{promptTokens[index] + 1}}}}}",
                context.RequestAborted);
        });
        SequenceObservationSink sink = new();
        await using ProxyGateway gateway = ProxyGateway.Create(
            ProxyGatewayOptions.CreateForTesting(0, backend.Address),
            sink,
            BackendTelemetryAdapters.Create(BackendKind.Ollama));
        await gateway.StartAsync();
        using HttpClient client = CreateProxyClient(gateway.ListeningAddress!);
        Guid sessionId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        ProxyObservation first = await SendCorrelatedAsync(client, sink, sessionId, Guid.NewGuid(), 1);
        ProxyObservation second = await SendCorrelatedAsync(client, sink, sessionId, Guid.NewGuid(), 2);
        ProxyObservation third = await SendCorrelatedAsync(client, sink, sessionId, Guid.NewGuid(), 3);

        Assert.AreEqual(MetricQuality.Unavailable, first.ContextChangeTokens.Quality);
        Assert.AreEqual(40, second.ContextChangeTokens.Value);
        Assert.AreEqual(-60, third.ContextChangeTokens.Value);
        Assert.AreEqual(MetricUnit.TokenDelta, third.ContextChangeTokens.Unit);
        Assert.AreEqual(MetricQuality.Calculated, third.ContextChangeTokens.Quality);
        Assert.AreEqual("adjacent-context-delta-v1", third.ContextChangeTokens.DerivationVersion);
        Assert.AreEqual(3, third.Correlation?.TurnSequence);

        using HttpRequestMessage incomplete = new(HttpMethod.Post, ProxyGateway.ChatCompletionsPath)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        incomplete.Headers.TryAddWithoutValidation(InspectorCorrelationHeaders.SessionId, sessionId.ToString("N"));
        using HttpResponseMessage incompleteResponse = await client.SendAsync(incomplete);
        incompleteResponse.EnsureSuccessStatusCode();
        ProxyObservation ignored = await sink.ReadAsync();
        Assert.IsNull(ignored.Correlation);
        Assert.AreEqual(MetricQuality.Unavailable, ignored.ContextChangeTokens.Quality);
        Assert.IsTrue(forwardedInspectorHeaders.All(headers => headers.Length == 0));
    }

    [TestMethod]
    public async Task ExplicitOperationCorrelationBuildsOrderedToolLifecycleWithoutForwardingMetadata()
    {
        const string firstResponse =
            "{\"model\":\"fixture\",\"choices\":[{\"message\":{\"tool_calls\":[{\"function\":{\"name\":\"read_file\",\"arguments\":\"opaque-secret\"}}]},\"finish_reason\":\"tool_calls\"}]}";
        const string finalResponse =
            "{\"model\":\"fixture\",\"choices\":[{\"message\":{\"content\":\"opaque-final\"},\"finish_reason\":\"stop\"}]}";
        int requestIndex = -1;
        List<string[]> forwardedInspectorHeaders = [];
        await using LoopbackStubServer backend = await LoopbackStubServer.StartAsync(async context =>
        {
            forwardedInspectorHeaders.Add(context.Request.Headers.Keys
                .Where(InspectorCorrelationHeaders.Names.Contains)
                .ToArray());
            using StreamReader reader = new(context.Request.Body, Encoding.UTF8);
            _ = await reader.ReadToEndAsync(context.RequestAborted);
            int index = Interlocked.Increment(ref requestIndex);
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(index == 0 ? firstResponse : finalResponse, context.RequestAborted);
        });
        SequenceOperationSink operations = new();
        await using ProxyGateway gateway = ProxyGateway.Create(
            ProxyGatewayOptions.CreateForTesting(0, backend.Address),
            telemetryAdapter: BackendTelemetryAdapters.Create(BackendKind.Ollama),
            operationSink: operations);
        await gateway.StartAsync();
        using HttpClient client = CreateProxyClient(gateway.ListeningAddress!);
        Guid operationId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();

        string firstBody =
            "{\"messages\":[{\"role\":\"user\",\"content\":\"opaque-prompt\"}]," +
            "\"tools\":[{\"type\":\"function\",\"function\":{\"name\":\"read_file\"}},{\"type\":\"function\",\"function\":{\"name\":\"list_files\"}}]}";
        using HttpResponseMessage firstHttp = await SendOperationTurnAsync(
            client, operationId, sessionId, Guid.NewGuid(), 1, firstBody);
        Assert.AreEqual(firstResponse, await firstHttp.Content.ReadAsStringAsync());
        TechnicalOperationGraph first = await operations.ReadAsync();

        string secondBody =
            "{\"messages\":[{\"role\":\"assistant\",\"tool_calls\":[{}]},{\"role\":\"tool\",\"content\":\"opaque-result\"}]," +
            "\"tools\":[{\"type\":\"function\",\"function\":{\"name\":\"read_file\"}}]}";
        using HttpResponseMessage secondHttp = await SendOperationTurnAsync(
            client, operationId, sessionId, Guid.NewGuid(), 2, secondBody);
        Assert.AreEqual(finalResponse, await secondHttp.Content.ReadAsStringAsync());
        TechnicalOperationGraph completed = await operations.ReadAsync();

        Assert.AreEqual(TechnicalOperationStatus.Running, first.Operation.Status);
        Assert.AreEqual(2m, first.Turns[0].AvailableToolCount.Value);
        Assert.AreEqual(1m, first.Turns[0].InvokedToolCount.Value);
        Assert.AreEqual("read_file", first.ToolEvents[0].ToolName.Value);
        Assert.AreEqual(TechnicalOperationStatus.Completed, completed.Operation.Status);
        Assert.HasCount(2, completed.Turns);
        Assert.AreEqual(1m, completed.Turns[1].AvailableToolCount.Value);
        Assert.AreEqual(0m, completed.Turns[1].InvokedToolCount.Value);
        Assert.HasCount(1, completed.ToolEvents);
        Assert.AreEqual(TechnicalToolStatus.Completed, completed.ToolEvents[0].Status);
        Assert.AreEqual(MetricQuality.Calculated, completed.ToolEvents[0].DurationMetric.Quality);
        Assert.IsTrue(forwardedInspectorHeaders.All(headers => headers.Length == 0));
    }

    [TestMethod]
    public async Task ParallelClientsProduceIndependentOperationMembershipAndRequestIds()
    {
        const string responseBody =
            "{\"choices\":[{\"message\":{\"content\":\"opaque\"},\"finish_reason\":\"stop\"}]}";
        await using LoopbackStubServer backend = await LoopbackStubServer.StartAsync(async context =>
        {
            await context.Request.Body.CopyToAsync(Stream.Null, context.RequestAborted);
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(responseBody, context.RequestAborted);
        });
        SequenceOperationSink operations = new();
        await using ProxyGateway gateway = ProxyGateway.Create(
            ProxyGatewayOptions.CreateForTesting(0, backend.Address),
            operationSink: operations);
        await gateway.StartAsync();
        using HttpClient client = CreateProxyClient(gateway.ListeningAddress!);
        (Guid Operation, Guid Session, string Path, ClientKind Client)[] expected = Enumerable
            .Range(0, 8)
            .Select(index => (
                Guid.NewGuid(),
                Guid.NewGuid(),
                index % 2 == 0 ? "/clients/cline/v1/chat/completions" : "/clients/open-webui/v1/chat/completions",
                index % 2 == 0 ? ClientKind.Cline : ClientKind.OpenWebUi))
            .ToArray();

        Task<HttpResponseMessage>[] pending = expected
            .Select(item => SendOperationTurnAsync(
                client,
                item.Operation,
                item.Session,
                Guid.NewGuid(),
                1,
                "{\"messages\":[],\"tools\":[]}",
                item.Path))
            .ToArray();
        HttpResponseMessage[] responses = await Task.WhenAll(pending);
        foreach (HttpResponseMessage response in responses)
        {
            using (response)
            {
                response.EnsureSuccessStatusCode();
            }
        }

        TechnicalOperationGraph[] actual = new TechnicalOperationGraph[expected.Length];
        for (int index = 0; index < actual.Length; index++)
        {
            actual[index] = await operations.ReadAsync();
        }

        Assert.HasCount(expected.Length, actual.Select(item => item.Operation.OperationId).Distinct());
        Assert.HasCount(expected.Length, actual.Select(item => item.Operation.SessionId).Distinct());
        Assert.HasCount(expected.Length, actual.Select(item => item.Turns.Single().RequestId).Distinct());
        foreach (TechnicalOperationGraph operation in actual)
        {
            (Guid Operation, Guid Session, string Path, ClientKind Client) match = expected.Single(
                item => item.Operation == operation.Operation.OperationId);
            Assert.AreEqual(match.Session, operation.Operation.SessionId);
            Assert.AreEqual(match.Client, operation.Operation.Client);
            Assert.AreEqual(TechnicalOperationStatus.Completed, operation.Operation.Status);
        }
    }

    [TestMethod]
    public async Task GatewayPersistsCorrelatedOperationGraphAndRequestMembership()
    {
        const string finalResponse =
            "{\"model\":\"fixture\",\"choices\":[{\"message\":{\"content\":\"opaque\"},\"finish_reason\":\"stop\"}]}";
        await using LoopbackStubServer backend = await LoopbackStubServer.StartAsync(async context =>
        {
            await context.Request.Body.CopyToAsync(Stream.Null, context.RequestAborted);
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(finalResponse, context.RequestAborted);
        });
        string directory = Path.Combine(Path.GetTempPath(), $"llm-inspector-operation-e2e-{Guid.NewGuid():N}");
        string databasePath = Path.Combine(directory, "history.db");
        Guid operationId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        try
        {
            await using SqliteTechnicalHistoryStore store = new(databasePath);
            await store.InitializeAsync();
            await using ProxyGateway gateway = ProxyGateway.Create(
                ProxyGatewayOptions.CreateForTesting(0, backend.Address),
                store,
                BackendTelemetryAdapters.Create(BackendKind.Ollama),
                operationSink: store);
            await gateway.StartAsync();
            using HttpClient client = CreateProxyClient(gateway.ListeningAddress!);

            using HttpResponseMessage response = await SendOperationTurnAsync(
                client,
                operationId,
                sessionId,
                Guid.NewGuid(),
                1,
                "{\"messages\":[],\"tools\":[{\"type\":\"function\"}]}");
            response.EnsureSuccessStatusCode();
            _ = await response.Content.ReadAsStringAsync();

            TechnicalOperationDetail? detail = await store.GetOperationDetailAsync(operationId);
            IReadOnlyList<RequestHistoryItem> requests = await store.QueryRequestsAsync(
                new HistoryFilter(SessionId: sessionId));
            Assert.IsNotNull(detail);
            Assert.AreEqual(TechnicalOperationStatus.Completed, detail.Operation.Status);
            Assert.HasCount(1, detail.Turns);
            Assert.AreEqual(1m, detail.Turns[0].AvailableToolCount.Value);
            Assert.AreEqual(0m, detail.Turns[0].InvokedToolCount.Value);
            Assert.HasCount(1, requests);
            Assert.AreEqual(operationId, requests[0].OperationId);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task MalformedOperationCorrelationRemainsUngrouped()
    {
        await using LoopbackStubServer backend = await LoopbackStubServer.StartAsync(async context =>
        {
            await context.Request.Body.CopyToAsync(Stream.Null, context.RequestAborted);
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                "{\"choices\":[{\"finish_reason\":\"stop\"}]}",
                context.RequestAborted);
        });
        CollectingObservationSink observations = new();
        SequenceOperationSink operations = new();
        await using ProxyGateway gateway = ProxyGateway.Create(
            ProxyGatewayOptions.CreateForTesting(0, backend.Address),
            observations,
            operationSink: operations);
        await gateway.StartAsync();
        using HttpClient client = CreateProxyClient(gateway.ListeningAddress!);
        using HttpRequestMessage request = new(HttpMethod.Post, ProxyGateway.ChatCompletionsPath)
        {
            Content = new StringContent("{\"messages\":[]}", Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation(InspectorCorrelationHeaders.OperationId, "ambiguous");
        request.Headers.TryAddWithoutValidation(InspectorCorrelationHeaders.SessionId, Guid.NewGuid().ToString("N"));
        request.Headers.TryAddWithoutValidation(InspectorCorrelationHeaders.TurnId, Guid.NewGuid().ToString("N"));
        request.Headers.TryAddWithoutValidation(InspectorCorrelationHeaders.TurnSequence, "1");

        using HttpResponseMessage response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        ProxyObservation observation = await observations.NextObservation.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsNotNull(observation.Correlation);
        Assert.IsNull(observation.Correlation.OperationId);
        Assert.AreEqual(0, operations.RecordedCount);
    }

    [TestMethod]
    public async Task LmStudioNativeChatIsRelayedVerbatimAndProjectsExactColdEvidence()
    {
        const string requestBody = "{\"model\":\"fixture\",\"input\":\"opaque\",\"stream\":false}";
        const string responseBody =
            "{\"model_instance_id\":\"lmstudio-community/qwen2.5\",\"output\":[{\"type\":\"message\",\"content\":\"opaque-response\"}]," +
            "\"stats\":{\"input_tokens\":25,\"total_output_tokens\":7,\"reasoning_output_tokens\":2," +
            "\"tokens_per_second\":35.5,\"time_to_first_token_seconds\":0.5,\"model_load_time_seconds\":1.25}}";
        string? forwardedPath = null;
        string? forwardedBody = null;
        await using LoopbackStubServer backend = await LoopbackStubServer.StartAsync(async context =>
        {
            forwardedPath = context.Request.Path;
            using StreamReader reader = new(context.Request.Body, Encoding.UTF8);
            forwardedBody = await reader.ReadToEndAsync(context.RequestAborted);
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(responseBody, context.RequestAborted);
        });
        SequenceObservationSink sink = new();
        ProxyGatewayOptions options = ProxyGatewayOptions.CreateForTesting(
            0,
            backend.Address,
            BackendKind.LmStudio);
        await using ProxyGateway gateway = ProxyGateway.Create(
            options,
            sink,
            BackendTelemetryAdapters.Create(BackendKind.LmStudio),
            lmStudioNativeTelemetryAdapter: BackendTelemetryAdapters.CreateLmStudioNative());
        await gateway.StartAsync();
        using HttpClient client = CreateProxyClient(gateway.ListeningAddress!);

        using HttpResponseMessage response = await client.PostAsync(
            ProxyGateway.LmStudioNativeChatPath,
            new StringContent(requestBody, Encoding.UTF8, "application/json"));
        string actualBody = await response.Content.ReadAsStringAsync();
        ProxyObservation observation = await sink.ReadAsync();

        response.EnsureSuccessStatusCode();
        Assert.AreEqual(ProxyGateway.LmStudioNativeChatPath, forwardedPath);
        Assert.AreEqual(requestBody, forwardedBody);
        Assert.AreEqual(responseBody, actualBody);
        Assert.AreEqual(ModelLoadDisposition.Cold, observation.BackendTelemetry.ModelLoadDisposition);
        Assert.AreEqual(1250, observation.BackendTelemetry.ModelLoadTime.Value);
        Assert.AreEqual(25, observation.BackendTelemetry.PromptTokens.Value);
        Assert.AreEqual(7, observation.BackendTelemetry.CompletionTokens.Value);
        Assert.AreEqual(32, observation.BackendTelemetry.TotalTokens.Value);
    }

    [TestMethod]
    public async Task LmStudioNativeStreamingEventsAreRelayedInOrderAndProjectColdEvidence()
    {
        string[] events =
        [
            "event: chat.start\ndata: {\"type\":\"chat.start\",\"model_instance_id\":\"native-stream-model\"}\n\n",
            "event: model_load.start\ndata: {\"type\":\"model_load.start\"}\n\n",
            "event: model_load.end\ndata: {\"type\":\"model_load.end\",\"load_time_seconds\":2.5}\n\n",
            "event: message.delta\ndata: {\"type\":\"message.delta\",\"content\":\"opaque-response\"}\n\n",
            "event: chat.end\ndata: {\"type\":\"chat.end\",\"result\":{\"stats\":{\"input_tokens\":12,\"total_output_tokens\":4,\"reasoning_output_tokens\":1,\"tokens_per_second\":20}}}\n\n",
        ];
        await using LoopbackStubServer backend = await LoopbackStubServer.StartAsync(async context =>
        {
            context.Response.ContentType = "text/event-stream";
            foreach (string item in events)
            {
                await context.Response.WriteAsync(item, context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
            }
        });
        SequenceObservationSink sink = new();
        ProxyGatewayOptions options = ProxyGatewayOptions.CreateForTesting(
            0,
            backend.Address,
            BackendKind.LmStudio);
        await using ProxyGateway gateway = ProxyGateway.Create(
            options,
            sink,
            BackendTelemetryAdapters.Create(BackendKind.LmStudio),
            lmStudioNativeTelemetryAdapter: BackendTelemetryAdapters.CreateLmStudioNative());
        await gateway.StartAsync();
        using HttpClient client = CreateProxyClient(gateway.ListeningAddress!);

        using HttpResponseMessage response = await client.PostAsync(
            ProxyGateway.LmStudioNativeChatPath,
            new StringContent("{\"stream\":true}", Encoding.UTF8, "application/json"));
        string actualBody = await response.Content.ReadAsStringAsync();
        ProxyObservation observation = await sink.ReadAsync();

        response.EnsureSuccessStatusCode();
        Assert.AreEqual(string.Concat(events), actualBody);
        Assert.AreEqual(ModelLoadDisposition.Cold, observation.BackendTelemetry.ModelLoadDisposition);
        Assert.AreEqual(2500, observation.BackendTelemetry.ModelLoadTime.Value);
        Assert.AreEqual(12, observation.BackendTelemetry.PromptTokens.Value);
        Assert.AreEqual(4, observation.BackendTelemetry.CompletionTokens.Value);
        Assert.AreEqual(16, observation.BackendTelemetry.TotalTokens.Value);
        Assert.AreEqual(MetricQuality.Calculated, observation.TimeToFirstToken.Quality);
    }

    [TestMethod]
    public async Task TelemetryParserFailureCannotBreakRelay()
    {
        const string backendBody = "{\"choices\":[{\"message\":{\"content\":\"synthetic\"}}]}";
        await using LoopbackStubServer backend = await LoopbackStubServer.StartAsync(async context =>
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(backendBody, context.RequestAborted);
        });
        CollectingObservationSink sink = new();
        ProxyGatewayOptions options = ProxyGatewayOptions.CreateForTesting(0, backend.Address);
        await using ProxyGateway gateway = ProxyGateway.Create(options, sink, new ThrowingTelemetryAdapter());
        await gateway.StartAsync();
        using HttpClient client = CreateProxyClient(gateway.ListeningAddress!);

        using HttpResponseMessage response = await client.PostAsync(
            ProxyGateway.ChatCompletionsPath,
            new StringContent("{}", Encoding.UTF8, "application/json"));
        string actual = await response.Content.ReadAsStringAsync();
        ProxyObservation observation = await sink.NextObservation.Task.WaitAsync(TimeSpan.FromSeconds(5));

        response.EnsureSuccessStatusCode();
        Assert.AreEqual(backendBody, actual);
        Assert.AreEqual(MetricQuality.Unavailable, observation.BackendTelemetry.TotalTokens.Quality);
    }

    [TestMethod]
    public async Task BackendConnectionFailureReturnsOnlySafeInspectorError()
    {
        int unavailablePort;
        TcpListener reservation = new(IPAddress.Loopback, 0);
        reservation.Start();
        unavailablePort = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();

        Uri unavailableBackend = new($"http://127.0.0.1:{unavailablePort}/");
        CollectingObservationSink sink = new();
        LiveRequestTracker liveState = new();
        await using ProxyGateway gateway = ProxyGateway.Create(
            ProxyGatewayOptions.CreateForTesting(0, unavailableBackend),
            sink,
            liveRequestStateSink: liveState);
        await gateway.StartAsync();
        using HttpClient client = CreateProxyClient(gateway.ListeningAddress!);

        using HttpResponseMessage response = await client.PostAsync(
            ProxyGateway.ChatCompletionsPath,
            new StringContent("{}", Encoding.UTF8, "application/json"));
        string body = await response.Content.ReadAsStringAsync();
        ProxyObservation observation = await sink.NextObservation.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.AreEqual("{\"error\":{\"type\":\"inspector_backend_unavailable\"}}", body);
        Assert.DoesNotContain(unavailablePort.ToString(System.Globalization.CultureInfo.InvariantCulture), body, StringComparison.Ordinal);
        Assert.AreEqual(ProxyOutcome.BackendUnavailable, observation.Outcome);
        Assert.AreEqual(ProxyErrorType.ConnectionRefused, observation.ErrorType);
        Assert.AreEqual(
            RequestStage.Error,
            liveState.GetSnapshot().LatestTerminalRequest?.Stage.Stage);
    }

    [TestMethod]
    public async Task BackendBodyAbortKeepsOriginalStatusAndRecordsRelayFailure()
    {
        TaskCompletionSource<bool> releaseBackendAbort = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using LoopbackStubServer backend = await LoopbackStubServer.StartAsync(async context =>
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentLength = 1024;
            await context.Response.WriteAsync("partial", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
            await releaseBackendAbort.Task.WaitAsync(context.RequestAborted);
            context.Abort();
        });
        CollectingObservationSink sink = new();
        await using ProxyGateway gateway = ProxyGateway.Create(
            ProxyGatewayOptions.CreateForTesting(0, backend.Address),
            sink);
        await gateway.StartAsync();
        using HttpClient client = CreateProxyClient(gateway.ListeningAddress!);

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Post, ProxyGateway.ChatCompletionsPath)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };
            using HttpResponseMessage response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead);
            await using Stream body = await response.Content.ReadAsStreamAsync();
            byte[] partial = new byte["partial".Length];
            using CancellationTokenSource readTimeout = new(TimeSpan.FromSeconds(5));
            int bytesRead = await body.ReadAtLeastAsync(
                partial,
                partial.Length,
                throwOnEndOfStream: true,
                readTimeout.Token);

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(partial.Length, bytesRead);
            Assert.AreEqual("partial", Encoding.UTF8.GetString(partial));

            releaseBackendAbort.TrySetResult(true);
            ProxyObservation observation = await sink.NextObservation.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(StatusCodes.Status200OK, observation.HttpStatusCode);
            Assert.AreEqual(ProxyOutcome.RelayFailed, observation.Outcome);
            Assert.AreEqual(ProxyErrorType.BackendCrash, observation.ErrorType);
        }
        finally
        {
            releaseBackendAbort.TrySetResult(true);
        }
    }

    [TestMethod]
    [DataRow(StatusCodes.Status503ServiceUnavailable, ProxyErrorType.ModelLoading)]
    [DataRow(StatusCodes.Status500InternalServerError, ProxyErrorType.HttpApiError)]
    [DataRow(StatusCodes.Status413PayloadTooLarge, ProxyErrorType.ContextOverflow)]
    [DataRow(StatusCodes.Status504GatewayTimeout, ProxyErrorType.Timeout)]
    public async Task BackendHttpFailuresReceiveTypedContentFreeErrorCategories(
        int statusCode,
        ProxyErrorType expected)
    {
        await using LoopbackStubServer backend = await LoopbackStubServer.StartAsync(async context =>
        {
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"error\":{\"message\":\"opaque\"}}", context.RequestAborted);
        });
        CollectingObservationSink sink = new();
        await using ProxyGateway gateway = ProxyGateway.Create(
            ProxyGatewayOptions.CreateForTesting(0, backend.Address),
            sink);
        await gateway.StartAsync();
        using HttpClient client = CreateProxyClient(gateway.ListeningAddress!);

        using HttpResponseMessage response = await client.PostAsync(
            ProxyGateway.ChatCompletionsPath,
            new StringContent("{}", Encoding.UTF8, "application/json"));
        ProxyObservation observation = await sink.NextObservation.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(statusCode, (int)response.StatusCode);
        Assert.AreEqual(expected, observation.ErrorType);
    }

    [TestMethod]
    public async Task AllowlistedContextOverflowCodeIsClassifiedWithoutRetainingErrorMessage()
    {
        const string body = "{\"error\":{\"type\":\"context_length_exceeded\",\"message\":\"private-message\"}}";
        await using LoopbackStubServer backend = await LoopbackStubServer.StartAsync(async context =>
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(body, context.RequestAborted);
        });
        CollectingObservationSink sink = new();
        await using ProxyGateway gateway = ProxyGateway.Create(
            ProxyGatewayOptions.CreateForTesting(0, backend.Address),
            sink);
        await gateway.StartAsync();
        using HttpClient client = CreateProxyClient(gateway.ListeningAddress!);

        using HttpResponseMessage response = await client.PostAsync(
            ProxyGateway.ChatCompletionsPath,
            new StringContent("{}", Encoding.UTF8, "application/json"));
        ProxyObservation observation = await sink.NextObservation.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual(ProxyErrorType.ContextOverflow, observation.ErrorType);
        Assert.IsFalse(typeof(ProxyObservation).GetProperties().Any(property => property.PropertyType == typeof(string)));
    }

    [TestMethod]
    public async Task ResourceMonitorReceivesRequestStageTrafficAndPersistsCorrelatedSampleBatch()
    {
        const string requestBody = "{\"model\":\"fixture\",\"messages\":[]}";
        const string responseBody = "{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}";
        await using LoopbackStubServer backend = await LoopbackStubServer.StartAsync(async context =>
        {
            using StreamReader reader = new(context.Request.Body, Encoding.UTF8);
            _ = await reader.ReadToEndAsync(context.RequestAborted);
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(responseBody, context.RequestAborted);
        });
        CapturingResourceMonitor monitor = new();
        CollectingResourceSink resourceSink = new();
        await using ProxyGateway gateway = ProxyGateway.Create(
            ProxyGatewayOptions.CreateForTesting(0, backend.Address),
            resourceSink: resourceSink,
            resourceMonitor: monitor);
        await gateway.StartAsync();
        using HttpClient client = CreateProxyClient(gateway.ListeningAddress!);

        using HttpResponseMessage response = await client.PostAsync(
            ProxyGateway.ChatCompletionsPath,
            new StringContent(requestBody, Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
        _ = await response.Content.ReadAsStringAsync();
        IReadOnlyList<TechnicalResourceSampleRecord> samples =
            await resourceSink.Next.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.HasCount(1, samples);
        TechnicalResourceSampleRecord sample = samples[0];
        Assert.AreEqual(monitor.Context?.RequestId, sample.RequestId);
        Assert.AreEqual(RequestStage.Completed, sample.Stage?.Stage);
        Assert.AreEqual(Encoding.UTF8.GetByteCount(requestBody), sample.ClientToBackendBytes.Value);
        Assert.AreEqual(Encoding.UTF8.GetByteCount(responseBody), sample.BackendToClientBytes.Value);
        Assert.AreEqual(backend.Address, monitor.Context?.BackendBaseAddress);
        Assert.AreEqual(MetricQuality.Unavailable, sample.CpuPercent.Quality);
    }

    private static HttpClient CreateProxyClient(Uri address)
    {
        SocketsHttpHandler handler = new()
        {
            AllowAutoRedirect = false,
            UseCookies = false,
        };
        return new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = address,
            Timeout = TimeSpan.FromSeconds(15),
        };
    }

    private static async Task<ProxyObservation> SendCorrelatedAsync(
        HttpClient client,
        SequenceObservationSink sink,
        Guid sessionId,
        Guid turnId,
        int turnSequence)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, ProxyGateway.ChatCompletionsPath)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation(InspectorCorrelationHeaders.SessionId, sessionId.ToString("N"));
        request.Headers.TryAddWithoutValidation(InspectorCorrelationHeaders.TurnId, turnId.ToString("N"));
        request.Headers.TryAddWithoutValidation(
            InspectorCorrelationHeaders.TurnSequence,
            turnSequence.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using HttpResponseMessage response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await sink.ReadAsync();
    }

    private static async Task<HttpResponseMessage> SendOperationTurnAsync(
        HttpClient client,
        Guid operationId,
        Guid sessionId,
        Guid turnId,
        int turnSequence,
        string body,
        string path = ProxyGateway.ChatCompletionsPath)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, path)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation(InspectorCorrelationHeaders.OperationId, operationId.ToString("N"));
        request.Headers.TryAddWithoutValidation(InspectorCorrelationHeaders.SessionId, sessionId.ToString("N"));
        request.Headers.TryAddWithoutValidation(InspectorCorrelationHeaders.TurnId, turnId.ToString("N"));
        request.Headers.TryAddWithoutValidation(
            InspectorCorrelationHeaders.TurnSequence,
            turnSequence.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return await client.SendAsync(request);
    }

    private static async Task<LiveRequestCollectionSnapshot> WaitForLiveStateAsync(
        LiveRequestTracker tracker,
        Func<LiveRequestCollectionSnapshot, bool> predicate)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        while (true)
        {
            LiveRequestCollectionSnapshot snapshot = tracker.GetSnapshot();
            if (predicate(snapshot))
            {
                return snapshot;
            }

            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class CollectingObservationSink : IProxyObservationSink
    {
        public TaskCompletionSource<ProxyObservation> NextObservation { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask RecordAsync(ProxyObservation observation, CancellationToken cancellationToken)
        {
            NextObservation.TrySetResult(observation);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SequenceObservationSink : IProxyObservationSink
    {
        private readonly System.Threading.Channels.Channel<ProxyObservation> _observations =
            System.Threading.Channels.Channel.CreateUnbounded<ProxyObservation>();

        public ValueTask RecordAsync(ProxyObservation observation, CancellationToken cancellationToken) =>
            _observations.Writer.WriteAsync(observation, cancellationToken);

        public async Task<ProxyObservation> ReadAsync() =>
            await _observations.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class SequenceOperationSink : ITechnicalOperationSink
    {
        private readonly System.Threading.Channels.Channel<TechnicalOperationGraph> _operations =
            System.Threading.Channels.Channel.CreateUnbounded<TechnicalOperationGraph>();
        private int _recordedCount;

        public int RecordedCount => Volatile.Read(ref _recordedCount);

        public Task RecordOperationGraphAsync(
            TechnicalOperationGraph graph,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _recordedCount);
            return _operations.Writer.WriteAsync(graph, cancellationToken).AsTask();
        }

        public async Task<TechnicalOperationGraph> ReadAsync() =>
            await _operations.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class CollectingResourceSink : ITechnicalResourceSampleSink
    {
        public TaskCompletionSource<IReadOnlyList<TechnicalResourceSampleRecord>> Next { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task RecordResourceSamplesAsync(
            IReadOnlyList<TechnicalResourceSampleRecord> samples,
            CancellationToken cancellationToken = default)
        {
            Next.TrySetResult(samples);
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingResourceMonitor : IRequestResourceMonitor
    {
        public RequestResourceContext? Context { get; private set; }

        public IRequestResourceSession Start(RequestResourceContext context)
        {
            Context = context;
            return new Session(context);
        }

        private sealed class Session(RequestResourceContext context) : IRequestResourceSession
        {
            private RequestStageValue _stage = RequestStageValue.ProtocolObserved(
                RequestStage.QueueWaiting,
                "resource-gateway-test-v1");
            private long _requestBytes;
            private long _responseBytes;

            public void StageChanged(RequestStageValue stage) => _stage = stage;

            public void AddClientToBackendBytes(int count) => _requestBytes += count;

            public void AddBackendToClientBytes(int count) => _responseBytes += count;

            public Task<IReadOnlyList<TechnicalResourceSampleRecord>> CompleteAsync(
                CancellationToken cancellationToken = default)
            {
                TechnicalResourceSampleRecord sample = new(
                    Guid.NewGuid(),
                    context.OperationId,
                    DateTimeOffset.UtcNow,
                    MetricValue.Unavailable(
                        MetricUnit.Percent,
                        MetricSource.Inspector,
                        "resource-gateway-test-v1"),
                    MetricValue.Unavailable(
                        MetricUnit.Percent,
                        MetricSource.Inspector,
                        "resource-gateway-test-v1"))
                {
                    RequestId = context.RequestId,
                    Stage = _stage,
                    ClientToBackendBytes = MetricValue.Exact(
                        _requestBytes,
                        MetricUnit.Bytes,
                        MetricSource.GatewayTraffic,
                        "resource-gateway-test-v1"),
                    BackendToClientBytes = MetricValue.Exact(
                        _responseBytes,
                        MetricUnit.Bytes,
                        MetricSource.GatewayTraffic,
                        "resource-gateway-test-v1"),
                };
                return Task.FromResult<IReadOnlyList<TechnicalResourceSampleRecord>>([sample]);
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class FixtureRemoteAuthorizer(bool enabled, string acceptedToken) : IRemoteAccessAuthorizer
    {
        public RemoteAccessSnapshot Snapshot { get; } = new(
            true,
            enabled,
            enabled,
            DateTimeOffset.UtcNow,
            enabled ? "Fixture enabled." : "Fixture disabled.");

        public bool IsBearerTokenValid(string candidate) =>
            enabled && string.Equals(candidate, acceptedToken, StringComparison.Ordinal);
    }

    private sealed class ThrowingStartResourceMonitor : IRequestResourceMonitor
    {
        public IRequestResourceSession Start(RequestResourceContext context) =>
            throw new InvalidOperationException("Synthetic collector start failure.");
    }

    private sealed class ThrowingSessionResourceMonitor : IRequestResourceMonitor
    {
        public IRequestResourceSession Start(RequestResourceContext context) => new ThrowingSession();

        private sealed class ThrowingSession : IRequestResourceSession
        {
            public void StageChanged(RequestStageValue stage) => Throw();

            public void AddClientToBackendBytes(int count) => Throw();

            public void AddBackendToClientBytes(int count) => Throw();

            public Task<IReadOnlyList<TechnicalResourceSampleRecord>> CompleteAsync(
                CancellationToken cancellationToken = default) =>
                Task.FromException<IReadOnlyList<TechnicalResourceSampleRecord>>(
                    new InvalidOperationException("Synthetic collector completion failure."));

            public ValueTask DisposeAsync() =>
                ValueTask.FromException(new InvalidOperationException("Synthetic collector disposal failure."));

            private static void Throw() =>
                throw new InvalidOperationException("Synthetic collector callback failure.");
        }
    }

    private sealed class ThrowingObservationSink : IProxyObservationSink
    {
        public ValueTask RecordAsync(ProxyObservation observation, CancellationToken cancellationToken) =>
            ValueTask.FromException(new InvalidOperationException("Synthetic sink failure."));
    }

    private sealed class ThrowingLiveStateSink : ILiveRequestStateSink
    {
        public void RequestStarted(Guid requestId, DateTimeOffset startedAt, ClientKind client) => Throw();

        public void StageChanged(Guid requestId, RequestStageValue stage) => Throw();

        public void BackendProgressChanged(Guid requestId, BackendProgressSignal progress) => Throw();

        public void RequestFinished(Guid requestId, ProxyOutcome outcome) => Throw();

        private static void Throw() => throw new InvalidOperationException("Synthetic live-state sink failure.");
    }

    private sealed class ThrowingTelemetryAdapter : IBackendTelemetryAdapter
    {
        public BackendKind Backend => BackendKind.Ollama;

        public string FixtureVersion => "throwing-fixture-v1";

        public IBackendTelemetrySession CreateSession(string? responseMediaType) => new ThrowingSession();

        public BackendResponseTelemetry CreateUnavailable() =>
            BackendResponseTelemetry.Unavailable(Backend, FixtureVersion);

        private sealed class ThrowingSession : IBackendTelemetrySession
        {
            public bool HasObservedOutputContent => false;

            public void Observe(ReadOnlySpan<byte> responseBytes) =>
                throw new InvalidOperationException("Synthetic parser failure.");

            public BackendResponseTelemetry Complete() =>
                throw new InvalidOperationException("Synthetic parser failure.");
        }
    }
}
