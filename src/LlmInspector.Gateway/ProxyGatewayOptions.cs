using System.ComponentModel;
using System.Net;
using LlmInspector.Domain;

namespace LlmInspector.Gateway;

public sealed class ProxyGatewayOptions
{
    public const int DefaultListenerPort = 5117;

    public static Uri DefaultBackendBaseAddress { get; } = new("http://127.0.0.1:11434/");

    public static Uri DefaultLlamaCppBaseAddress { get; } = new("http://127.0.0.1:8080/");

    public static Uri DefaultLmStudioBaseAddress { get; } = new("http://127.0.0.1:1234/");

    private ProxyGatewayOptions(
        int listenerPort,
        Uri backendBaseAddress,
        BackendKind backend,
        BackendConnectionScope backendConnectionScope)
    {
        ListenerPort = listenerPort;
        BackendBaseAddress = backendBaseAddress;
        Backend = backend;
        BackendConnectionScope = backendConnectionScope;
    }

    public int ListenerPort { get; }

    public Uri BackendBaseAddress { get; }

    public BackendKind Backend { get; }

    public BackendConnectionScope BackendConnectionScope { get; }

    public static ProxyGatewayOptions CreateDefault() =>
        CreateDefault(BackendKind.Ollama);

    public static ProxyGatewayOptions CreateDefault(BackendKind backend) =>
        Create(DefaultListenerPort, GetDefaultBackendBaseAddress(backend), backend);

    public static Uri GetDefaultBackendBaseAddress(BackendKind backend) => backend switch
    {
        BackendKind.Ollama => DefaultBackendBaseAddress,
        BackendKind.LlamaCpp => DefaultLlamaCppBaseAddress,
        BackendKind.LmStudio => DefaultLmStudioBaseAddress,
        _ => throw new InvalidEnumArgumentException(nameof(backend), (int)backend, typeof(BackendKind)),
    };

    public static ProxyGatewayOptions Create(int listenerPort, Uri backendBaseAddress) =>
        Create(listenerPort, backendBaseAddress, BackendKind.Ollama);

    public static ProxyGatewayOptions Create(
        int listenerPort,
        Uri backendBaseAddress,
        BackendKind backend) =>
        CreateCore(
            listenerPort,
            backendBaseAddress,
            backend,
            BackendConnectionScope.LocalLoopback,
            allowDynamicPort: false);

    public static ProxyGatewayOptions CreateTailscaleRemote(
        int listenerPort,
        Uri backendBaseAddress,
        BackendKind backend) =>
        CreateCore(
            listenerPort,
            backendBaseAddress,
            backend,
            BackendConnectionScope.TailscaleHttps,
            allowDynamicPort: false);

    [EditorBrowsable(EditorBrowsableState.Never)]
    public static ProxyGatewayOptions CreateForTesting(int listenerPort, Uri backendBaseAddress) =>
        CreateForTesting(listenerPort, backendBaseAddress, BackendKind.Ollama);

    [EditorBrowsable(EditorBrowsableState.Never)]
    public static ProxyGatewayOptions CreateForTesting(
        int listenerPort,
        Uri backendBaseAddress,
        BackendKind backend) =>
        CreateCore(
            listenerPort,
            backendBaseAddress,
            backend,
            BackendConnectionScope.LocalLoopback,
            allowDynamicPort: true);

    private static ProxyGatewayOptions CreateCore(
        int listenerPort,
        Uri backendBaseAddress,
        BackendKind backend,
        BackendConnectionScope backendConnectionScope,
        bool allowDynamicPort)
    {
        if (listenerPort is < 0 or > ushort.MaxValue || (!allowDynamicPort && listenerPort == 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(listenerPort),
                "Listener port must be between 1 and 65535; port 0 is reserved for test fixtures.");
        }

        ArgumentNullException.ThrowIfNull(backendBaseAddress);

        if (!Enum.IsDefined(backend))
        {
            throw new InvalidEnumArgumentException(nameof(backend), (int)backend, typeof(BackendKind));
        }

        if (!backendBaseAddress.IsAbsoluteUri ||
            (backendBaseAddress.Scheme != Uri.UriSchemeHttp && backendBaseAddress.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Backend destination must be an absolute HTTP or HTTPS URI.", nameof(backendBaseAddress));
        }

        if (!string.IsNullOrEmpty(backendBaseAddress.UserInfo) ||
            !string.IsNullOrEmpty(backendBaseAddress.Query) ||
            !string.IsNullOrEmpty(backendBaseAddress.Fragment) ||
            backendBaseAddress.AbsolutePath != "/")
        {
            throw new ArgumentException(
                "Backend destination cannot contain credentials, path, query or fragment.",
                nameof(backendBaseAddress));
        }

        UriBuilder normalizedBackend = new(backendBaseAddress);
        if (backendConnectionScope == BackendConnectionScope.LocalLoopback)
        {
            if (!TryNormalizeLoopbackHost(backendBaseAddress.Host, out string? normalizedHost))
            {
                throw new ArgumentException(
                    "A local backend destination must use localhost, 127.0.0.1 or ::1.",
                    nameof(backendBaseAddress));
            }

            normalizedBackend.Host = normalizedHost;
        }
        else if (backendConnectionScope == BackendConnectionScope.TailscaleHttps)
        {
            if (backendBaseAddress.Scheme != Uri.UriSchemeHttps ||
                !IsTailscaleDnsName(backendBaseAddress.IdnHost))
            {
                throw new ArgumentException(
                    "A Tailscale remote backend must use an HTTPS *.ts.net DNS name.",
                    nameof(backendBaseAddress));
            }
        }
        else
        {
            throw new InvalidEnumArgumentException(
                nameof(backendConnectionScope),
                (int)backendConnectionScope,
                typeof(BackendConnectionScope));
        }

        return new ProxyGatewayOptions(listenerPort, normalizedBackend.Uri, backend, backendConnectionScope);
    }

    private static bool TryNormalizeLoopbackHost(string host, out string? normalizedHost)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            normalizedHost = IPAddress.Loopback.ToString();
            return true;
        }

        string unbracketedHost = host.Trim('[', ']');
        if (IPAddress.TryParse(unbracketedHost, out IPAddress? address) && IPAddress.IsLoopback(address))
        {
            normalizedHost = address.ToString();
            return true;
        }

        normalizedHost = null;
        return false;
    }

    internal static bool IsTailscaleDnsName(string host) =>
        host.Length > ".ts.net".Length &&
        host.EndsWith(".ts.net", StringComparison.OrdinalIgnoreCase) &&
        !IPAddress.TryParse(host, out _);
}

public enum BackendConnectionScope
{
    LocalLoopback,
    TailscaleHttps,
}
