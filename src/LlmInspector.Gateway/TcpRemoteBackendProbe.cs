using System.Diagnostics;
using System.Net.Sockets;
using LlmInspector.Application;

namespace LlmInspector.Gateway;

public sealed class TcpRemoteBackendProbe : IRemoteBackendProbe
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);

    private readonly TimeSpan _timeout;

    public TcpRemoteBackendProbe(TimeSpan? timeout = null)
    {
        _timeout = timeout ?? DefaultTimeout;
        if (_timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }
    }

    public async ValueTask<RemoteBackendProbeResult> ProbeAsync(
        Uri destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.IsAbsoluteUri || destination.Scheme != Uri.UriSchemeHttps)
        {
            return RemoteBackendProbeResult.Failure("destination-not-https");
        }

        int port = destination.IsDefaultPort ? 443 : destination.Port;
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        using TcpClient client = new();
        long started = Stopwatch.GetTimestamp();
        try
        {
            await client.ConnectAsync(destination.IdnHost, port, timeout.Token).ConfigureAwait(false);
            return RemoteBackendProbeResult.Success(Stopwatch.GetElapsedTime(started));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return RemoteBackendProbeResult.Failure("connect-timeout");
        }
        catch (SocketException)
        {
            return RemoteBackendProbeResult.Failure("connect-failed");
        }
    }
}
