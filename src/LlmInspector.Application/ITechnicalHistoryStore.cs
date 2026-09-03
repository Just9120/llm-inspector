using LlmInspector.Domain;

namespace LlmInspector.Application;

public interface ITechnicalHistoryStore : IProxyObservationSink
{
    Task<IReadOnlyList<RequestHistoryItem>> QueryRequestsAsync(
        HistoryFilter filter,
        CancellationToken cancellationToken = default);

    Task<TechnicalOperationDetail?> GetOperationDetailAsync(
        Guid operationId,
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
