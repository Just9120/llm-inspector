using System.ComponentModel;
using System.Net;
using LlmInspector.Domain;

namespace LlmInspector.Gateway;

public sealed class ProxyGatewayOptions
{
    public const int DefaultListenerPort = 5117;

    public static Uri DefaultBackendBaseAddress { get; } = new("http://127.0.0.1:11434/");

    private ProxyGatewayOptions(int listenerPort, Uri backendBaseAddress, BackendKind backend)
    {
        ListenerPort = listenerPort;
        BackendBaseAddress = backendBaseAddress;
        Backend = backend;
    }

    public int ListenerPort { get; }

    public Uri BackendBaseAddress { get; }

    public BackendKind Backend { get; }

    public static ProxyGatewayOptions CreateDefault() =>
        Create(DefaultListenerPort, DefaultBackendBaseAddress);

    public static ProxyGatewayOptions Create(int listenerPort, Uri backendBaseAddress) =>
        Create(listenerPort, backendBaseAddress, BackendKind.Ollama);

    public static ProxyGatewayOptions Create(
        int listenerPort,
        Uri backendBaseAddress,
        BackendKind backend) =>
        CreateCore(listenerPort, backendBaseAddress, backend, allowDynamicPort: false);

    [EditorBrowsable(EditorBrowsableState.Never)]
    public static ProxyGatewayOptions CreateForTesting(int listenerPort, Uri backendBaseAddress) =>
        CreateForTesting(listenerPort, backendBaseAddress, BackendKind.Ollama);

    [EditorBrowsable(EditorBrowsableState.Never)]
    public static ProxyGatewayOptions CreateForTesting(
        int listenerPort,
        Uri backendBaseAddress,
        BackendKind backend) =>
        CreateCore(listenerPort, backendBaseAddress, backend, allowDynamicPort: true);

    private static ProxyGatewayOptions CreateCore(
        int listenerPort,
        Uri backendBaseAddress,
        BackendKind backend,
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

        if (!TryNormalizeLoopbackHost(backendBaseAddress.Host, out string? normalizedHost))
        {
            throw new ArgumentException(
                "Initial-release backend destination must use localhost, 127.0.0.1 or ::1.",
                nameof(backendBaseAddress));
        }

        UriBuilder normalizedBackend = new(backendBaseAddress)
        {
            Host = normalizedHost,
        };
        return new ProxyGatewayOptions(listenerPort, normalizedBackend.Uri, backend);
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
}
