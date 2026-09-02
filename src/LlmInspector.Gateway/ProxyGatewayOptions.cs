using System.ComponentModel;
using System.Net;

namespace LlmInspector.Gateway;

public sealed class ProxyGatewayOptions
{
    public const int DefaultListenerPort = 5117;

    public static Uri DefaultBackendBaseAddress { get; } = new("http://127.0.0.1:11434/");

    private ProxyGatewayOptions(int listenerPort, Uri backendBaseAddress)
    {
        ListenerPort = listenerPort;
        BackendBaseAddress = backendBaseAddress;
    }

    public int ListenerPort { get; }

    public Uri BackendBaseAddress { get; }

    public static ProxyGatewayOptions CreateDefault() =>
        Create(DefaultListenerPort, DefaultBackendBaseAddress);

    public static ProxyGatewayOptions Create(int listenerPort, Uri backendBaseAddress) =>
        CreateCore(listenerPort, backendBaseAddress, allowDynamicPort: false);

    [EditorBrowsable(EditorBrowsableState.Never)]
    public static ProxyGatewayOptions CreateForTesting(int listenerPort, Uri backendBaseAddress) =>
        CreateCore(listenerPort, backendBaseAddress, allowDynamicPort: true);

    private static ProxyGatewayOptions CreateCore(
        int listenerPort,
        Uri backendBaseAddress,
        bool allowDynamicPort)
    {
        if (listenerPort is < 0 or > ushort.MaxValue || (!allowDynamicPort && listenerPort == 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(listenerPort),
                "Listener port must be between 1 and 65535; port 0 is reserved for test fixtures.");
        }

        ArgumentNullException.ThrowIfNull(backendBaseAddress);

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

        if (!IsExplicitLoopbackHost(backendBaseAddress.Host))
        {
            throw new ArgumentException(
                "Initial-release backend destination must use localhost, 127.0.0.1 or ::1.",
                nameof(backendBaseAddress));
        }

        return new ProxyGatewayOptions(listenerPort, backendBaseAddress);
    }

    private static bool IsExplicitLoopbackHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string unbracketedHost = host.Trim('[', ']');
        return IPAddress.TryParse(unbracketedHost, out IPAddress? address) && IPAddress.IsLoopback(address);
    }
}
