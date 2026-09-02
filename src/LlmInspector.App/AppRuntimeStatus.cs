namespace LlmInspector.App;

public sealed record AppRuntimeStatus(
    bool ProxyRunning,
    string Listener,
    string Backend,
    string State)
{
    public static AppRuntimeStatus NotStarted { get; } = new(
        false,
        "Not listening",
        "Not configured",
        "Proxy has not started.");

    public static AppRuntimeStatus Running(Uri listener, Uri backend) => new(
        true,
        listener.ToString(),
        backend.ToString(),
        "Loopback proxy is running.");

    public static AppRuntimeStatus ListenerUnavailable(Uri backend, int listenerPort) => new(
        false,
        "Not listening",
        backend.ToString(),
        $"Loopback listener could not start. Check whether port {listenerPort} is already in use.");
}
