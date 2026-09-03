using LlmInspector.Domain;

namespace LlmInspector.Application;

public interface ITechnicalOperationSink
{
    Task RecordOperationGraphAsync(
        TechnicalOperationGraph graph,
        CancellationToken cancellationToken = default);
}

public interface ITechnicalResourceSampleSink
{
    Task RecordResourceSamplesAsync(
        IReadOnlyList<TechnicalResourceSampleRecord> samples,
        CancellationToken cancellationToken = default);
}

public sealed class NullTechnicalResourceSampleSink : ITechnicalResourceSampleSink
{
    public static NullTechnicalResourceSampleSink Instance { get; } = new();

    private NullTechnicalResourceSampleSink()
    {
    }

    public Task RecordResourceSamplesAsync(
        IReadOnlyList<TechnicalResourceSampleRecord> samples,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed class NullTechnicalOperationSink : ITechnicalOperationSink
{
    public static NullTechnicalOperationSink Instance { get; } = new();

    private NullTechnicalOperationSink()
    {
    }

    public Task RecordOperationGraphAsync(
        TechnicalOperationGraph graph,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public interface ITechnicalHistoryStore : IProxyObservationSink, ITechnicalOperationSink, ITechnicalResourceSampleSink
{
    Task<IReadOnlyList<RequestHistoryItem>> QueryRequestsAsync(
        HistoryFilter filter,
        CancellationToken cancellationToken = default);

    Task<TechnicalOperationDetail?> GetOperationDetailAsync(
        Guid operationId,
        CancellationToken cancellationToken = default);

    Task<TechnicalHistorySlice> QuerySnapshotSliceAsync(
        HistoryFilter filter,
        Guid? operationId,
        CancellationToken cancellationToken = default);

    Task<PeriodAnalytics> AnalyzePeriodAsync(
        HistoryFilter filter,
        CancellationToken cancellationToken = default);

    Task<AnalyticsComparison> CompareAsync(
        HistoryFilter baseline,
        HistoryFilter candidate,
        HistoryMetric metric,
        CancellationToken cancellationToken = default);

    Task<HistoryRetention> GetRetentionAsync(CancellationToken cancellationToken = default);

    Task SetRetentionAsync(
        HistoryRetention retention,
        CancellationToken cancellationToken = default);

    Task<int> ApplyRetentionAsync(
        HistoryRetention retention,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<HistoryClearPreview> PreviewClearAsync(
        HistoryClearScope scope,
        CancellationToken cancellationToken = default);

    Task<HistoryClearPreview> ClearAsync(
        HistoryClearPreview preview,
        bool confirmed,
        CancellationToken cancellationToken = default);
}
