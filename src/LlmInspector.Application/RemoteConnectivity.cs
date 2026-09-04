using System.Security.Cryptography;
using LlmInspector.Domain;

namespace LlmInspector.Application;

public sealed record RemoteAccessStoredConfiguration(
    bool Enabled,
    byte[]? BearerToken,
    DateTimeOffset? UpdatedAt);

public interface IRemoteAccessCredentialStore
{
    ValueTask<RemoteAccessStoredConfiguration> LoadAsync(CancellationToken cancellationToken = default);

    ValueTask SaveAsync(
        RemoteAccessStoredConfiguration configuration,
        CancellationToken cancellationToken = default);
}

public sealed record RemoteAccessSnapshot(
    bool IsAvailable,
    bool Enabled,
    bool HasCredential,
    DateTimeOffset? UpdatedAt,
    string Message)
{
    public static RemoteAccessSnapshot Unavailable { get; } = new(
        false,
        false,
        false,
        null,
        "Secure remote access is unavailable; remote ingress is denied.");
}

public sealed record RemoteAccessChangeResult(
    RemoteAccessSnapshot Snapshot,
    string? OneTimeBearerToken);

public interface IRemoteAccessAuthorizer
{
    RemoteAccessSnapshot Snapshot { get; }

    bool IsBearerTokenValid(string candidate);
}

public sealed class RemoteAccessManager : IRemoteAccessAuthorizer, IDisposable
{
    public const int BearerTokenBytes = 32;

    private readonly IRemoteAccessCredentialStore _store;
    private readonly SemaphoreSlim _mutationLock = new(1, 1);
    private RemoteAccessSnapshot _snapshot = RemoteAccessSnapshot.Unavailable;
    private byte[]? _bearerToken;
    private int _disposed;

    public RemoteAccessManager(IRemoteAccessCredentialStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public RemoteAccessSnapshot Snapshot => Volatile.Read(ref _snapshot);

    public async ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RemoteAccessStoredConfiguration stored = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ValidateStoredConfiguration(stored);
                ReplaceToken(stored.BearerToken);
                Volatile.Write(
                    ref _snapshot,
                    new RemoteAccessSnapshot(
                        true,
                        stored.Enabled,
                        stored.BearerToken is not null,
                        stored.UpdatedAt,
                        stored.Enabled
                            ? "Secure remote access is enabled."
                            : "Secure remote access is disabled."));
            }
            finally
            {
                if (stored.BearerToken is not null)
                {
                    CryptographicOperations.ZeroMemory(stored.BearerToken);
                }
            }
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    public async ValueTask<RemoteAccessChangeResult> EnableAsync(
        bool exactBoundaryConfirmed,
        CancellationToken cancellationToken = default)
    {
        if (!exactBoundaryConfirmed)
        {
            throw new InvalidOperationException("The loopback, Tailscale Serve and no-Funnel boundary must be confirmed.");
        }

        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureAvailable();
            if (Snapshot.Enabled)
            {
                return new RemoteAccessChangeResult(Snapshot, null);
            }

            byte[] token = RandomNumberGenerator.GetBytes(BearerTokenBytes);
            try
            {
                DateTimeOffset updatedAt = DateTimeOffset.UtcNow;
                await _store.SaveAsync(
                    new RemoteAccessStoredConfiguration(true, token, updatedAt),
                    cancellationToken).ConfigureAwait(false);
                ReplaceToken(token);
                RemoteAccessSnapshot snapshot = new(
                    true,
                    true,
                    true,
                    updatedAt,
                    "Secure remote access is enabled; the new token is shown only for this operation.");
                Volatile.Write(ref _snapshot, snapshot);
                return new RemoteAccessChangeResult(snapshot, EncodeBearerToken(token));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(token);
            }
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    public async ValueTask<RemoteAccessChangeResult> RotateAsync(
        bool exactBoundaryConfirmed,
        CancellationToken cancellationToken = default)
    {
        if (!exactBoundaryConfirmed)
        {
            throw new InvalidOperationException("The loopback, Tailscale Serve and no-Funnel boundary must be confirmed.");
        }

        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureAvailable();
            if (!Snapshot.Enabled)
            {
                throw new InvalidOperationException("Remote access must be enabled before its token can be rotated.");
            }

            byte[] token = RandomNumberGenerator.GetBytes(BearerTokenBytes);
            try
            {
                DateTimeOffset updatedAt = DateTimeOffset.UtcNow;
                await _store.SaveAsync(
                    new RemoteAccessStoredConfiguration(true, token, updatedAt),
                    cancellationToken).ConfigureAwait(false);
                ReplaceToken(token);
                RemoteAccessSnapshot snapshot = new(
                    true,
                    true,
                    true,
                    updatedAt,
                    "The remote bearer token was rotated; the previous token is no longer valid.");
                Volatile.Write(ref _snapshot, snapshot);
                return new RemoteAccessChangeResult(snapshot, EncodeBearerToken(token));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(token);
            }
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    public async ValueTask<RemoteAccessChangeResult> DisableAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureAvailable();
            DateTimeOffset updatedAt = DateTimeOffset.UtcNow;
            await _store.SaveAsync(
                new RemoteAccessStoredConfiguration(false, null, updatedAt),
                cancellationToken).ConfigureAwait(false);
            ReplaceToken(null);
            RemoteAccessSnapshot snapshot = new(
                true,
                false,
                false,
                updatedAt,
                "Secure remote access is disabled and its token was revoked.");
            Volatile.Write(ref _snapshot, snapshot);
            return new RemoteAccessChangeResult(snapshot, null);
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    public bool IsBearerTokenValid(string candidate)
    {
        RemoteAccessSnapshot snapshot = Snapshot;
        byte[]? expected = Volatile.Read(ref _bearerToken);
        if (!snapshot.IsAvailable || !snapshot.Enabled || expected is null ||
            !TryDecodeBearerToken(candidate, out byte[]? actual))
        {
            return false;
        }

        try
        {
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(actual);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        ReplaceToken(null);
        _mutationLock.Dispose();
        GC.SuppressFinalize(this);
    }

    private static void ValidateStoredConfiguration(RemoteAccessStoredConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.BearerToken is not null && configuration.BearerToken.Length != BearerTokenBytes)
        {
            throw new InvalidDataException("The protected remote bearer token has an invalid length.");
        }

        if (configuration.Enabled && configuration.BearerToken is null)
        {
            throw new InvalidDataException("Enabled remote access requires a protected bearer token.");
        }
    }

    private void EnsureAvailable()
    {
        if (!Snapshot.IsAvailable)
        {
            throw new InvalidOperationException("Secure remote access storage is unavailable.");
        }
    }

    private void ReplaceToken(byte[]? token)
    {
        byte[]? replacement = token?.ToArray();
        byte[]? previous = Interlocked.Exchange(ref _bearerToken, replacement);
        if (previous is not null)
        {
            CryptographicOperations.ZeroMemory(previous);
        }
    }

    private static string EncodeBearerToken(byte[] token) =>
        Convert.ToBase64String(token)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static bool TryDecodeBearerToken(string candidate, out byte[]? token)
    {
        token = null;
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length != 43)
        {
            return false;
        }

        try
        {
            byte[] decoded = Convert.FromBase64String(
                candidate.Replace('-', '+').Replace('_', '/') + "=");
            if (decoded.Length != BearerTokenBytes)
            {
                CryptographicOperations.ZeroMemory(decoded);
                return false;
            }

            token = decoded;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public enum RemoteBackendAvailability
{
    NotConfigured,
    Unknown,
    Probing,
    Available,
    Unavailable,
}

public sealed record RemoteBackendProbeResult(bool Available, TimeSpan? ConnectDuration, string ResultCode)
{
    public static RemoteBackendProbeResult Success(TimeSpan connectDuration) =>
        new(true, connectDuration, "tcp-connect-succeeded");

    public static RemoteBackendProbeResult Failure(string resultCode) =>
        new(false, null, resultCode);
}

public interface IRemoteBackendProbe
{
    ValueTask<RemoteBackendProbeResult> ProbeAsync(Uri destination, CancellationToken cancellationToken = default);
}

public interface IRemoteBackendStatusSource
{
    RemoteBackendStatus Snapshot { get; }
}

public sealed record RemoteBackendStatus(
    RemoteBackendAvailability Availability,
    Uri? Destination,
    MetricValue NetworkConnectLatency,
    DateTimeOffset? CheckedAt,
    string Message)
{
    public static RemoteBackendStatus NotConfigured { get; } = new(
        RemoteBackendAvailability.NotConfigured,
        null,
        MetricValue.Unavailable(
            MetricUnit.Milliseconds,
            MetricSource.Inspector,
            RemoteBackendMonitor.SourceVersion),
        null,
        "Remote backend is not configured.");
}

public sealed class RemoteBackendMonitor : IRemoteBackendStatusSource, IDisposable
{
    public const string SourceVersion = "remote-dns-tcp-connect-probe-v1";
    public const string DerivationVersion = "stopwatch-elapsed-v1";

    private readonly Uri _destination;
    private readonly IRemoteBackendProbe _probe;
    private readonly SemaphoreSlim _probeLock = new(1, 1);
    private RemoteBackendStatus _snapshot;
    private int _disposed;

    public RemoteBackendMonitor(Uri destination, IRemoteBackendProbe probe)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(probe);
        _destination = destination;
        _probe = probe;
        _snapshot = new RemoteBackendStatus(
            RemoteBackendAvailability.Unknown,
            destination,
            UnavailableLatency(),
            null,
            "Remote backend availability has not been probed yet.");
    }

    public RemoteBackendStatus Snapshot => Volatile.Read(ref _snapshot);

    public async ValueTask<RemoteBackendStatus> ProbeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _probeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Volatile.Write(
                ref _snapshot,
                Snapshot with
                {
                    Availability = RemoteBackendAvailability.Probing,
                    Message = "Remote DNS+TCP availability probe is running.",
                });

            RemoteBackendProbeResult result;
            try
            {
                result = await _probe.ProbeAsync(_destination, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or TimeoutException)
            {
                result = RemoteBackendProbeResult.Failure("probe-failed");
            }

            DateTimeOffset checkedAt = DateTimeOffset.UtcNow;
            MetricValue latency = result.Available && result.ConnectDuration is TimeSpan duration
                ? MetricValue.Calculated(
                    (decimal)duration.TotalMilliseconds,
                    MetricUnit.Milliseconds,
                    MetricSource.Inspector,
                    SourceVersion,
                    DerivationVersion)
                : UnavailableLatency();
            RemoteBackendStatus snapshot = new(
                result.Available ? RemoteBackendAvailability.Available : RemoteBackendAvailability.Unavailable,
                _destination,
                latency,
                checkedAt,
                result.Available
                    ? "Remote HTTPS target accepted a DNS+TCP connection. This is not inference latency."
                    : $"Remote HTTPS target is unavailable ({result.ResultCode}); network latency is unavailable.");
            Volatile.Write(ref _snapshot, snapshot);
            return snapshot;
        }
        finally
        {
            _probeLock.Release();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _probeLock.Dispose();
        GC.SuppressFinalize(this);
    }

    private static MetricValue UnavailableLatency() =>
        MetricValue.Unavailable(MetricUnit.Milliseconds, MetricSource.Inspector, SourceVersion);
}
