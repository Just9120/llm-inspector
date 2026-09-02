using System.Buffers;
using System.Diagnostics;
using System.Net;
using LlmInspector.Application;
using LlmInspector.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace LlmInspector.Gateway;

public sealed class ProxyGateway : IDisposable, IAsyncDisposable
{
    public const string ChatCompletionsPath = "/v1/chat/completions";
    public const string ModelsPath = "/v1/models";

    private static readonly HashSet<string> HopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection",
        "Keep-Alive",
        "Proxy-Authenticate",
        "Proxy-Authorization",
        "TE",
        "Trailer",
        "Transfer-Encoding",
        "Upgrade",
    };

    private readonly ProxyGatewayOptions _options;
    private readonly IProxyObservationSink _observationSink;
    private readonly ILiveRequestStateSink _liveRequestStateSink;
    private readonly IBackendTelemetryAdapter _telemetryAdapter;
    private readonly HttpClient _httpClient;
    private readonly WebApplication _application;
    private int _started;
    private int _disposed;

    private ProxyGateway(
        ProxyGatewayOptions options,
        IProxyObservationSink observationSink,
        ILiveRequestStateSink liveRequestStateSink,
        IBackendTelemetryAdapter telemetryAdapter,
        HttpClient httpClient,
        WebApplication application)
    {
        _options = options;
        _observationSink = observationSink;
        _liveRequestStateSink = liveRequestStateSink;
        _telemetryAdapter = telemetryAdapter;
        _httpClient = httpClient;
        _application = application;
    }

    public Uri? ListeningAddress { get; private set; }

    public static ProxyGateway Create(
        ProxyGatewayOptions options,
        IProxyObservationSink? observationSink = null,
        IBackendTelemetryAdapter? telemetryAdapter = null,
        ILiveRequestStateSink? liveRequestStateSink = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        telemetryAdapter ??= new UnavailableBackendTelemetryAdapter(options.Backend);
        if (telemetryAdapter.Backend != options.Backend)
        {
            throw new ArgumentException(
                "Telemetry adapter backend must match the configured backend.",
                nameof(telemetryAdapter));
        }

        SocketsHttpHandler handler = new()
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
        };
        HttpClient httpClient = new(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(
            new WebApplicationOptions
            {
                ApplicationName = typeof(ProxyGateway).Assembly.GetName().Name,
                EnvironmentName = Environments.Production,
                Args = [],
            });

        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection();
        builder.Logging.ClearProviders();
        builder.WebHost.UseSetting(WebHostDefaults.PreventHostingStartupKey, "true");
        builder.WebHost.ConfigureKestrel(serverOptions =>
        {
            serverOptions.AddServerHeader = false;
            serverOptions.Listen(
                IPAddress.Loopback,
                options.ListenerPort,
                listenOptions => listenOptions.Protocols = HttpProtocols.Http1);
        });

        WebApplication application = builder.Build();
        ProxyGateway gateway = new(
            options,
            observationSink ?? NullProxyObservationSink.Instance,
            liveRequestStateSink ?? NullLiveRequestStateSink.Instance,
            telemetryAdapter,
            httpClient,
            application);

        application.MapMethods(
            ChatCompletionsPath,
            [HttpMethods.Post],
            context => gateway.RelayAsync(context, ClientKind.GenericUnknown));
        application.MapMethods(
            ModelsPath,
            [HttpMethods.Get],
            gateway.RelayModelsAsync);
        foreach (ClientEndpoint endpoint in ClientEndpointCatalog.KnownClients)
        {
            application.MapMethods(
                endpoint.ChatCompletionsPath,
                [HttpMethods.Post],
                context => gateway.RelayAsync(context, endpoint.Client));
            application.MapMethods(
                endpoint.ModelsPath,
                [HttpMethods.Get],
                gateway.RelayModelsAsync);
        }

        return gateway;
    }

    public void Start() => StartAsync().GetAwaiter().GetResult();

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("The proxy gateway has already been started.");
        }

        try
        {
            await _application.StartAsync(cancellationToken).ConfigureAwait(false);

            IServer server = _application.Services.GetRequiredService<IServer>();
            string[] addresses = server.Features
                .Get<IServerAddressesFeature>()?
                .Addresses
                .ToArray() ?? [];

            if (addresses.Length != 1 ||
                !Uri.TryCreate(addresses[0], UriKind.Absolute, out Uri? address) ||
                !IPAddress.TryParse(address.Host.Trim('[', ']'), out IPAddress? ipAddress) ||
                !IPAddress.IsLoopback(ipAddress))
            {
                await _application.StopAsync(CancellationToken.None).ConfigureAwait(false);
                throw new InvalidOperationException("Kestrel did not bind to exactly one explicit loopback endpoint.");
            }

            ListeningAddress = address;
        }
        catch
        {
            Interlocked.Exchange(ref _started, 0);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 0) == 0)
        {
            return;
        }

        await _application.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose() =>
        Task.Run(async () => await DisposeAsync().ConfigureAwait(false)).GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
            await _application.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _httpClient.Dispose();
        }
    }

    private async Task RelayAsync(HttpContext context, ClientKind client)
    {
        Guid requestId = Guid.NewGuid();
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        long startedTimestamp = Stopwatch.GetTimestamp();
        int? statusCode = null;
        ProxyOutcome outcome = ProxyOutcome.RelayFailed;
        BackendResponseTelemetry backendTelemetry = _telemetryAdapter.CreateUnavailable();
        NotifyLiveStateSafely(sink => sink.RequestStarted(requestId, startedAt, client));

        try
        {
            NotifyLiveStateSafely(sink => sink.StageChanged(
                requestId,
                RequestStageValue.ProtocolObserved(
                    RequestStage.PromptProcessing,
                    "gateway-openai-lifecycle-v1")));
            using HttpRequestMessage outboundRequest = CreateOutboundRequest(
                context,
                HttpMethod.Post,
                ChatCompletionsPath,
                includeBody: true);
            using HttpResponseMessage backendResponse = await _httpClient.SendAsync(
                outboundRequest,
                HttpCompletionOption.ResponseHeadersRead,
                context.RequestAborted).ConfigureAwait(false);

            statusCode = (int)backendResponse.StatusCode;
            context.Response.StatusCode = statusCode.Value;
            CopyResponseHeaders(backendResponse, context.Response);

            await context.Response.StartAsync(context.RequestAborted).ConfigureAwait(false);
            backendTelemetry = await RelayResponseBodyAsync(
                backendResponse.Content,
                context.Response.Body,
                () => NotifyLiveStateSafely(sink => sink.StageChanged(
                    requestId,
                    RequestStageValue.ProtocolObserved(
                        RequestStage.ReasoningGeneration,
                        "gateway-openai-lifecycle-v1"))),
                context.RequestAborted).ConfigureAwait(false);

            outcome = ProxyOutcome.Completed;
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            outcome = ProxyOutcome.ClientCancelled;
            context.Abort();
        }
        catch (HttpRequestException)
        {
            if (context.Response.HasStarted)
            {
                outcome = ProxyOutcome.RelayFailed;
                context.Abort();
            }
            else
            {
                outcome = ProxyOutcome.BackendUnavailable;
                statusCode = StatusCodes.Status502BadGateway;
                await WriteSafeGatewayFailureAsync(context, statusCode.Value).ConfigureAwait(false);
            }
        }
        catch (IOException)
        {
            outcome = ProxyOutcome.RelayFailed;
            statusCode ??= StatusCodes.Status502BadGateway;
            await AbortOrWriteSafeGatewayFailureAsync(context).ConfigureAwait(false);
        }
        finally
        {
            NotifyLiveStateSafely(sink => sink.RequestFinished(requestId, outcome));
            ProxyObservation observation = new(
                requestId,
                startedAt,
                Stopwatch.GetElapsedTime(startedTimestamp),
                statusCode,
                outcome,
                client,
                backendTelemetry);
            await RecordSafelyAsync(observation).ConfigureAwait(false);
        }
    }

    private async Task RelayModelsAsync(HttpContext context)
    {
        try
        {
            using HttpRequestMessage outboundRequest = CreateOutboundRequest(
                context,
                HttpMethod.Get,
                ModelsPath,
                includeBody: false);
            using HttpResponseMessage backendResponse = await _httpClient.SendAsync(
                outboundRequest,
                HttpCompletionOption.ResponseHeadersRead,
                context.RequestAborted).ConfigureAwait(false);

            context.Response.StatusCode = (int)backendResponse.StatusCode;
            CopyResponseHeaders(backendResponse, context.Response);
            await context.Response.StartAsync(context.RequestAborted).ConfigureAwait(false);
            await backendResponse.Content.CopyToAsync(
                context.Response.Body,
                context.RequestAborted).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            context.Abort();
        }
        catch (HttpRequestException)
        {
            await WriteSafeGatewayFailureAsync(
                context,
                StatusCodes.Status502BadGateway).ConfigureAwait(false);
        }
        catch (Exception) when (!context.RequestAborted.IsCancellationRequested)
        {
            await AbortOrWriteSafeGatewayFailureAsync(context).ConfigureAwait(false);
        }
    }

    private async Task<BackendResponseTelemetry> RelayResponseBodyAsync(
        HttpContent backendContent,
        Stream clientBody,
        Action responseBodyObserved,
        CancellationToken cancellationToken)
    {
        IBackendTelemetrySession? telemetrySession;
        try
        {
            telemetrySession = _telemetryAdapter.CreateSession(backendContent.Headers.ContentType?.MediaType);
        }
        catch (Exception)
        {
            telemetrySession = null;
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        bool responseBodyStageObserved = false;
        try
        {
            using Stream backendBody = await backendContent
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            while (true)
            {
                int bytesRead = await backendBody
                    .ReadAsync(buffer.AsMemory(), cancellationToken)
                    .ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    break;
                }

                if (!responseBodyStageObserved)
                {
                    responseBodyObserved();
                    responseBodyStageObserved = true;
                }

                if (telemetrySession is not null)
                {
                    try
                    {
                        telemetrySession.Observe(buffer.AsSpan(0, bytesRead));
                    }
                    catch (Exception)
                    {
                        telemetrySession = null;
                    }
                }

                await clientBody
                    .WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken)
                    .ConfigureAwait(false);
                await clientBody.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        if (telemetrySession is not null)
        {
            try
            {
                return telemetrySession.Complete();
            }
            catch (Exception)
            {
            }
        }

        return _telemetryAdapter.CreateUnavailable();
    }

    private HttpRequestMessage CreateOutboundRequest(
        HttpContext context,
        HttpMethod method,
        string backendPath,
        bool includeBody)
    {
        UriBuilder destination = new(_options.BackendBaseAddress)
        {
            Path = backendPath,
            Query = context.Request.QueryString.HasValue
                ? context.Request.QueryString.Value![1..]
                : string.Empty,
        };

        HttpRequestMessage outbound = new(method, destination.Uri)
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
        if (includeBody)
        {
            outbound.Content = new StreamContent(context.Request.Body);
        }

        HashSet<string> excludedHeaders = CreateExcludedHeaders(context.Request.Headers);
        foreach ((string name, StringValues values) in context.Request.Headers)
        {
            if (name.Equals("Host", StringComparison.OrdinalIgnoreCase) || excludedHeaders.Contains(name))
            {
                continue;
            }

            string[] headerValues = values.OfType<string>().ToArray();
            if (!outbound.Headers.TryAddWithoutValidation(name, headerValues) && outbound.Content is not null)
            {
                _ = outbound.Content.Headers.TryAddWithoutValidation(name, headerValues);
            }
        }

        return outbound;
    }

    private static void CopyResponseHeaders(HttpResponseMessage source, HttpResponse destination)
    {
        HashSet<string> excludedHeaders = new(HopByHopHeaders, StringComparer.OrdinalIgnoreCase);
        AddConnectionTokens(source.Headers.Connection, excludedHeaders);

        foreach ((string name, IEnumerable<string> values) in source.Headers)
        {
            if (!excludedHeaders.Contains(name))
            {
                destination.Headers.Append(name, values.ToArray());
            }
        }

        foreach ((string name, IEnumerable<string> values) in source.Content.Headers)
        {
            if (!excludedHeaders.Contains(name))
            {
                destination.Headers.Append(name, values.ToArray());
            }
        }
    }

    private static HashSet<string> CreateExcludedHeaders(IHeaderDictionary headers)
    {
        HashSet<string> excludedHeaders = new(HopByHopHeaders, StringComparer.OrdinalIgnoreCase);
        if (headers.TryGetValue("Connection", out StringValues connectionValues))
        {
            AddConnectionTokens(connectionValues, excludedHeaders);
        }

        return excludedHeaders;
    }

    private static void AddConnectionTokens(IEnumerable<string> values, HashSet<string> destination)
    {
        foreach (string value in values)
        {
            foreach (string token in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                destination.Add(token);
            }
        }
    }

    private static async Task WriteSafeGatewayFailureAsync(HttpContext context, int statusCode)
    {
        if (context.Response.HasStarted)
        {
            context.Abort();
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsync(
            "{\"error\":{\"type\":\"inspector_backend_unavailable\"}}",
            CancellationToken.None).ConfigureAwait(false);
    }

    private static Task AbortOrWriteSafeGatewayFailureAsync(HttpContext context)
    {
        if (context.Response.HasStarted)
        {
            context.Abort();
            return Task.CompletedTask;
        }

        return WriteSafeGatewayFailureAsync(context, StatusCodes.Status502BadGateway);
    }

    private async ValueTask RecordSafelyAsync(ProxyObservation observation)
    {
        try
        {
            await _observationSink.RecordAsync(observation, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Observation is best-effort. A telemetry sink must never break request forwarding.
        }
    }

    private void NotifyLiveStateSafely(Action<ILiveRequestStateSink> notification)
    {
        try
        {
            notification(_liveRequestStateSink);
        }
        catch (Exception)
        {
            // Live UI state is best-effort and must never affect request forwarding.
        }
    }

    private sealed class UnavailableBackendTelemetryAdapter(BackendKind backend) : IBackendTelemetryAdapter
    {
        private const string SourceVersion = "gateway-no-adapter-v1";

        public BackendKind Backend { get; } = backend;

        public string FixtureVersion => SourceVersion;

        public IBackendTelemetrySession CreateSession(string? responseMediaType) =>
            new UnavailableBackendTelemetrySession(CreateUnavailable());

        public BackendResponseTelemetry CreateUnavailable() =>
            BackendResponseTelemetry.Unavailable(Backend, SourceVersion);
    }

    private sealed class UnavailableBackendTelemetrySession(BackendResponseTelemetry telemetry) : IBackendTelemetrySession
    {
        public void Observe(ReadOnlySpan<byte> responseBytes)
        {
        }

        public BackendResponseTelemetry Complete() => telemetry;
    }
}
