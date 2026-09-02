using LlmInspector.Domain;

namespace LlmInspector.App;

public sealed record AppRuntimeStatus(
    bool ProxyRunning,
    string Listener,
    string Backend,
    string BackendType,
    string State)
{
    public static AppRuntimeStatus NotStarted { get; } = new(
        false,
        "Not listening",
        "Not configured",
        "Not configured",
        "Proxy has not started.");

    public static AppRuntimeStatus ConfigurationInvalid { get; } = new(
        false,
        "Not listening",
        "Invalid configuration",
        "Not configured",
        "Gateway configuration is invalid. Check the documented launch options.");

    public static AppRuntimeStatus Running(Uri listener, Uri backend, BackendKind backendType) => new(
        true,
        listener.ToString(),
        backend.ToString(),
        backendType.ToString(),
        "Loopback proxy is running.");

    public static AppRuntimeStatus ListenerUnavailable(
        Uri backend,
        int listenerPort,
        BackendKind backendType) => new(
        false,
        "Not listening",
        backend.ToString(),
        backendType.ToString(),
        $"Loopback listener could not start. Check whether port {listenerPort} is already in use.");
}
