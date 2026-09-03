using LlmInspector.Domain;

namespace LlmInspector.Application;

public sealed record RequestResourceContext(
    Guid RequestId,
    Guid? OperationId,
    BackendKind Backend,
    Uri BackendBaseAddress,
    DateTimeOffset StartedAt);

public interface IRequestResourceMonitor
{
    IRequestResourceSession Start(RequestResourceContext context);
}

public interface IRequestResourceSession : IAsyncDisposable
{
    void StageChanged(RequestStageValue stage);

    void AddClientToBackendBytes(int count);

    void AddBackendToClientBytes(int count);

    Task<IReadOnlyList<TechnicalResourceSampleRecord>> CompleteAsync(
        CancellationToken cancellationToken = default);
}

public interface IResourceTelemetrySnapshotSource
{
    TechnicalResourceSampleRecord? Latest { get; }
}

public sealed class NullRequestResourceMonitor : IRequestResourceMonitor, IResourceTelemetrySnapshotSource
{
    public static NullRequestResourceMonitor Instance { get; } = new();

    private NullRequestResourceMonitor()
    {
    }

    public TechnicalResourceSampleRecord? Latest => null;

    public IRequestResourceSession Start(RequestResourceContext context) => NullRequestResourceSession.Instance;

    private sealed class NullRequestResourceSession : IRequestResourceSession
    {
        public static NullRequestResourceSession Instance { get; } = new();

        public void StageChanged(RequestStageValue stage)
        {
        }

        public void AddClientToBackendBytes(int count)
        {
        }

        public void AddBackendToClientBytes(int count)
        {
        }

        public Task<IReadOnlyList<TechnicalResourceSampleRecord>> CompleteAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TechnicalResourceSampleRecord>>([]);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
