using System.Net;
using System.Net.Sockets;
using System.Text;
using LlmInspector.Adapters;
using LlmInspector.Application;
using LlmInspector.Domain;
using LlmInspector.Gateway;
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
        await using ProxyGateway gateway = ProxyGateway.Create(
            ProxyGatewayOptions.CreateForTesting(0, backend.Address));
        await gateway.StartAsync();
        using HttpClient client = CreateProxyClient(gateway.ListeningAddress!);
        using HttpRequestMessage request = new(HttpMethod.Post, ProxyGateway.ChatCompletionsPath)
        {
            Content = new StringContent(requestBody, Encoding.UTF8, "application/json"),
        };

        using HttpResponseMessage response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead);
        string actualResponse = await response.Content.ReadAsStringAsync();

        Assert.AreEqual("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.AreEqual(requestBody, forwardedBody);
        Assert.AreEqual(expectedResponse, actualResponse);
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
        await using ProxyGateway gateway = ProxyGateway.Create(
            ProxyGatewayOptions.CreateForTesting(0, backend.Address));
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

            releaseBackend.TrySetResult(true);
            using StreamReader remainderReader = new(stream, Encoding.UTF8);
            Assert.AreEqual(finalFragment, await remainderReader.ReadToEndAsync());
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
        await using ProxyGateway gateway = ProxyGateway.Create(
            ProxyGatewayOptions.CreateForTesting(0, backend.Address));
        await gateway.StartAsync();
        using HttpClient client = CreateProxyClient(gateway.ListeningAddress!);
        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(10));

        Task<HttpResponseMessage> pendingResponse = client.PostAsync(
            ProxyGateway.ChatCompletionsPath,
            new StringContent("{}", Encoding.UTF8, "application/json"),
            cancellation.Token);
        await backendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
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
            new ThrowingObservationSink());
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
        await using ProxyGateway gateway = ProxyGateway.Create(
            ProxyGatewayOptions.CreateForTesting(0, unavailableBackend),
            sink);
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
    }

    [TestMethod]
    public async Task BackendBodyAbortKeepsOriginalStatusAndRecordsRelayFailure()
    {
        await using LoopbackStubServer backend = await LoopbackStubServer.StartAsync(async context =>
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentLength = 1024;
            await context.Response.WriteAsync("partial", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
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
            using HttpResponseMessage response = await client.PostAsync(
                ProxyGateway.ChatCompletionsPath,
                new StringContent("{}", Encoding.UTF8, "application/json"));
            _ = await response.Content.ReadAsStringAsync();
            Assert.Fail("A truncated backend response unexpectedly completed successfully.");
        }
        catch (HttpRequestException)
        {
        }

        ProxyObservation observation = await sink.NextObservation.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(StatusCodes.Status200OK, observation.HttpStatusCode);
        Assert.AreEqual(ProxyOutcome.RelayFailed, observation.Outcome);
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

    private sealed class ThrowingObservationSink : IProxyObservationSink
    {
        public ValueTask RecordAsync(ProxyObservation observation, CancellationToken cancellationToken) =>
            ValueTask.FromException(new InvalidOperationException("Synthetic sink failure."));
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
            public void Observe(ReadOnlySpan<byte> responseBytes) =>
                throw new InvalidOperationException("Synthetic parser failure.");

            public BackendResponseTelemetry Complete() =>
                throw new InvalidOperationException("Synthetic parser failure.");
        }
    }
}
