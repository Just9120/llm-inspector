using LlmInspector.Domain;

namespace LlmInspector.Application;

public interface IProxyObservationSnapshotSource
{
    ProxyObservation? Latest { get; }
}

public sealed class LatestProxyObservationStore : IProxyObservationSink, IProxyObservationSnapshotSource
{
    private ProxyObservation? _latest;
    private long _acceptedCount;

    public ProxyObservation? Latest => Volatile.Read(ref _latest);

    public long AcceptedCount => Interlocked.Read(ref _acceptedCount);

    public ValueTask RecordAsync(ProxyObservation observation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(observation);
        cancellationToken.ThrowIfCancellationRequested();

        Volatile.Write(ref _latest, observation);
        Interlocked.Increment(ref _acceptedCount);
        return ValueTask.CompletedTask;
    }
}
