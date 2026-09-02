using Avalonia;
using LlmInspector.Adapters;
using LlmInspector.Gateway;

namespace LlmInspector.App;

public static class Program
{
    private const string SmokeTestArgument = "--smoke-test";
    private const string AvaloniaSmokeTestArgument = "--avalonia-smoke-test";
    private const string GatewaySmokeTestArgument = "--gateway-smoke-test";

    public static AppRuntimeStatus RuntimeStatus { get; private set; } = AppRuntimeStatus.NotStarted;

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

        ProxyGatewayOptions options = ProxyGatewayOptions.CreateDefault();
        ProxyGateway? gateway = null;

        try
        {
            gateway = ProxyGateway.Create(
                options,
                telemetryAdapter: BackendTelemetryAdapters.Create(options.Backend));
            gateway.Start();
            RuntimeStatus = AppRuntimeStatus.Running(gateway.ListeningAddress!, options.BackendBaseAddress);
        }
        catch (Exception exception)
            when (gateway is not null && exception is IOException or InvalidOperationException)
        {
            gateway.Dispose();
            gateway = null;
            RuntimeStatus = AppRuntimeStatus.ListenerUnavailable(
                options.BackendBaseAddress,
                options.ListenerPort);
        }

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            gateway?.Dispose();
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
}
