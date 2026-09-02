using LlmInspector.Domain;

namespace LlmInspector.Application;

public interface ILiveRequestStateSink
{
    void RequestStarted(Guid requestId, DateTimeOffset startedAt, ClientKind client);

    void StageChanged(Guid requestId, RequestStageValue stage);

    void BackendProgressChanged(Guid requestId, BackendProgressSignal progress);

    void RequestFinished(Guid requestId, ProxyOutcome outcome);
}

public interface ILiveRequestSnapshotSource
{
    LiveRequestCollectionSnapshot GetSnapshot();
}

public sealed class NullLiveRequestStateSink : ILiveRequestStateSink
{
    public static NullLiveRequestStateSink Instance { get; } = new();

    private NullLiveRequestStateSink()
    {
    }

    public void RequestStarted(Guid requestId, DateTimeOffset startedAt, ClientKind client)
    {
    }

    public void StageChanged(Guid requestId, RequestStageValue stage)
    {
    }

    public void BackendProgressChanged(Guid requestId, BackendProgressSignal progress)
    {
    }

    public void RequestFinished(Guid requestId, ProxyOutcome outcome)
    {
    }
}
