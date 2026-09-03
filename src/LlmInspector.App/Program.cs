using Avalonia;
using LlmInspector.Adapters;
using LlmInspector.Application;
using LlmInspector.Domain;
using LlmInspector.Gateway;
using LlmInspector.Resources.Windows;
using LlmInspector.Storage.Sqlite;
using LlmInspector.Telemetry;
using Microsoft.Data.Sqlite;

namespace LlmInspector.App;

public static class Program
{
    private const string SmokeTestArgument = "--smoke-test";
    private const string AvaloniaSmokeTestArgument = "--avalonia-smoke-test";
    private const string GatewaySmokeTestArgument = "--gateway-smoke-test";

    public static AppRuntimeStatus RuntimeStatus { get; private set; } = AppRuntimeStatus.NotStarted;

    public static LatestProxyObservationStore ObservationStore { get; private set; } = new();

    public static LiveRequestTracker LiveStateTracker { get; private set; } = new();

    public static ITechnicalHistoryStore? HistoryStore { get; private set; }

    public static WindowsRequestResourceMonitor ResourceMonitor { get; private set; } = new();

    public static string HistoryState { get; private set; } = "Technical history has not started.";

    [STAThread]
    public static int Main(string[] args)
    {
        if (args is [AvaloniaSmokeTestArgument])
        {
            BuildAvaloniaApp().SetupWithoutStarting();
            return 0;
        }

        if (args is [GatewaySmokeTestArgument])
        {
            return Task.Run(RunGatewaySmoke).GetAwaiter().GetResult();
        }

        if (args is [SmokeTestArgument])
        {
            BuildAvaloniaApp().SetupWithoutStarting();
            return Task.Run(RunGatewaySmoke).GetAwaiter().GetResult();
        }

        ProxyGatewayOptions options;
        try
        {
            options = AppLaunchConfiguration.Parse(args).CreateProxyOptions();
        }
        catch (ArgumentException)
        {
            RuntimeStatus = AppRuntimeStatus.ConfigurationInvalid;
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime([]);
        }

        ProxyGateway? gateway = null;
        SqliteTechnicalHistoryStore? historyStore = null;
        BufferedTechnicalHistorySink? historySink = null;
        ObservationStore = new LatestProxyObservationStore();
        LiveStateTracker = new LiveRequestTracker();
        ResourceMonitor = new WindowsRequestResourceMonitor();
        HistoryStore = null;
        HistoryState = "Technical history is unavailable.";

        try
        {
            historyStore = new SqliteTechnicalHistoryStore(GetDefaultHistoryPath());
            historyStore.InitializeAsync().GetAwaiter().GetResult();
            HistoryRetention retention = historyStore.GetRetentionAsync().GetAwaiter().GetResult();
            int deleted = historyStore.ApplyRetentionAsync(retention, DateTimeOffset.UtcNow).GetAwaiter().GetResult();
            historySink = new BufferedTechnicalHistorySink(historyStore);
            HistoryStore = historyStore;
            HistoryState = $"Technical history is available. Retention: {retention}; startup cleanup: {deleted} record(s).";
        }
        catch (Exception exception) when (IsExpectedHistoryFailure(exception))
        {
            historyStore?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            historyStore = null;
            HistoryState = $"Technical history is unavailable ({exception.GetType().Name}); proxying can continue.";
        }

        try
        {
            IProxyObservationSink observationSink = historySink is null
                ? ObservationStore
                : new CompositeProxyObservationSink(ObservationStore, historySink);
            gateway = ProxyGateway.Create(
                options,
                observationSink,
                telemetryAdapter: BackendTelemetryAdapters.Create(options.Backend),
                liveRequestStateSink: LiveStateTracker,
                lmStudioNativeTelemetryAdapter: options.Backend == BackendKind.LmStudio
                    ? BackendTelemetryAdapters.CreateLmStudioNative()
                    : null,
                operationSink: historySink,
                resourceSink: historySink,
                resourceMonitor: ResourceMonitor);
            gateway.Start();
            RuntimeStatus = AppRuntimeStatus.Running(
                gateway.ListeningAddress!,
                options.BackendBaseAddress,
                options.Backend);
        }
        catch (Exception exception)
            when (gateway is not null && exception is IOException or InvalidOperationException)
        {
            gateway.Dispose();
            gateway = null;
            RuntimeStatus = AppRuntimeStatus.ListenerUnavailable(
                options.BackendBaseAddress,
                options.ListenerPort,
                options.Backend);
        }

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime([]);
        }
        finally
        {
            gateway?.Dispose();
            historySink?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            historyStore?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static int RunGatewaySmoke()
    {
        ProxyGatewayOptions options = ProxyGatewayOptions.CreateDefault();
        using ProxyGateway gateway = ProxyGateway.Create(
            options,
            telemetryAdapter: BackendTelemetryAdapters.Create(options.Backend));
        gateway.Start();
        return gateway.ListeningAddress is not null ? 0 : 1;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

    private static string GetDefaultHistoryPath()
    {
        string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
        {
            throw new IOException("Windows local application data directory is unavailable.");
        }

        return Path.Combine(localData, "LLM Inspector", "data", "inspector.db");
    }

    private static bool IsExpectedHistoryFailure(Exception exception) => exception is
        IOException or
        UnauthorizedAccessException or
        InvalidDataException or
        InvalidOperationException or
        SqliteException;
}
