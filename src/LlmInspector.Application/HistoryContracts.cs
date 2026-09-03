using LlmInspector.Domain;

namespace LlmInspector.Application;

public enum HistoryErrorType
{
    None,
    BackendUnavailable,
    ClientCancelled,
    RelayFailed,
}

public enum TechnicalOperationStatus
{
    Running,
    Completed,
    Cancelled,
    Error,
}

public enum TechnicalToolStatus
{
    Started,
    Completed,
    Error,
}

public enum HistoryMetric
{
    InputTokens,
    OutputTokens,
    TotalTokens,
    CachedTokens,
    ReasoningTokens,
    ContextUsageTokens,
    ContextLimitTokens,
    ContextHistoryTokens,
    ContextToolTokens,
    PromptTokensPerSecond,
    GenerationTokensPerSecond,
    TimeToFirstTokenMilliseconds,
    ModelLoadMilliseconds,
    QueueMilliseconds,
    TotalDurationMilliseconds,
    CpuPercent,
    MemoryPercent,
    ErrorRatePercent,
}

public enum HistoryRetention
{
    SevenDays,
    ThirtyDays,
    NinetyDays,
    Indefinite,
}

public sealed record TechnicalSessionRecord(
    Guid SessionId,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    ClientKind Client,
    BackendKind Backend,
    TechnicalIdentifier? Model);

public sealed record TechnicalOperationRecord(
    Guid OperationId,
    Guid? SessionId,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    ClientKind Client,
    BackendKind Backend,
    TechnicalIdentifier? Model,
    TechnicalOperationStatus Status,
    HistoryErrorType ErrorType);

public sealed record TechnicalTurnRecord(
    Guid TurnId,
    Guid OperationId,
    int Sequence,
    Guid? RequestId,
    DateTimeOffset StartedAt,
    TimeSpan Duration,
    ProxyOutcome Outcome,
    HistoryErrorType ErrorType);

public sealed record TechnicalToolEventRecord(
    Guid ToolEventId,
    Guid OperationId,
    int TurnSequence,
    int Sequence,
    TechnicalIdentifier ToolName,
    DateTimeOffset StartedAt,
    TimeSpan Duration,
    TechnicalToolStatus Status,
    HistoryErrorType ErrorType);

public sealed record TechnicalResourceSampleRecord(
    Guid SampleId,
    Guid? OperationId,
    DateTimeOffset CapturedAt,
    MetricValue CpuPercent,
    MetricValue MemoryPercent);

public sealed record TechnicalOperationGraph(
    TechnicalSessionRecord? Session,
    TechnicalOperationRecord Operation,
    IReadOnlyList<TechnicalTurnRecord> Turns,
    IReadOnlyList<TechnicalToolEventRecord> ToolEvents,
    IReadOnlyList<TechnicalResourceSampleRecord> ResourceSamples);

public sealed record HistoryFilter(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    ClientKind? Client = null,
    BackendKind? Backend = null,
    TechnicalIdentifier? Model = null,
    Guid? SessionId = null,
    ProxyOutcome? Status = null,
    HistoryErrorType? ErrorType = null,
    int Limit = 200);

public sealed record RequestHistoryItem(
    Guid RequestId,
    Guid? SessionId,
    Guid? OperationId,
    DateTimeOffset StartedAt,
    int? HttpStatusCode,
    ProxyOutcome Outcome,
    HistoryErrorType ErrorType,
    ClientKind Client,
    BackendKind Backend,
    TechnicalIdentifier? Model,
    IReadOnlyDictionary<HistoryMetric, MetricValue> Metrics);

public sealed record TechnicalOperationDetail(
    TechnicalOperationRecord Operation,
    IReadOnlyList<TechnicalTurnRecord> Turns,
    IReadOnlyList<TechnicalToolEventRecord> ToolEvents,
    IReadOnlyList<TechnicalResourceSampleRecord> ResourceSamples);

public sealed record MetricAggregate(
    int SampleCount,
    bool IsStatisticallySufficient,
    decimal? ArithmeticMean,
    decimal? Median,
    decimal? P95);

public sealed record AnalyticsTrendPoint(
    DateOnly Day,
    IReadOnlyDictionary<HistoryMetric, MetricAggregate> Metrics);

public sealed record PeriodAnalytics(
    HistoryFilter Filter,
    IReadOnlyList<AnalyticsTrendPoint> Trend);

public sealed record AnalyticsComparison(
    HistoryMetric Metric,
    MetricAggregate Baseline,
    MetricAggregate Candidate,
    decimal? MeanDelta,
    bool IsConfirmedDegradation);

public sealed record HistoryClearScope
{
    public HistoryClearScope(bool allHistory, DateTimeOffset? from = null, DateTimeOffset? to = null)
    {
        if (!allHistory && from is null && to is null)
        {
            throw new ArgumentException("A bounded range or explicit all-history scope is required.");
        }

        if (allHistory && (from is not null || to is not null))
        {
            throw new ArgumentException("All-history scope cannot include a bounded range.");
        }

        if (from is not null && to is not null && from > to)
        {
            throw new ArgumentException("History clear range start cannot be after its end.");
        }

        AllHistory = allHistory;
        From = from;
        To = to;
    }

    public bool AllHistory { get; }

    public DateTimeOffset? From { get; }

    public DateTimeOffset? To { get; }
}

public sealed record HistoryClearPreview(
    HistoryClearScope Scope,
    int Requests,
    int Sessions,
    int Operations,
    int Turns,
    int ToolEvents,
    int ResourceSamples)
{
    public int TotalRecords => Requests + Sessions + Operations + Turns + ToolEvents + ResourceSamples;
}

public static class HistoryPolicies
{
    public const int MinimumAggregateSamples = 3;

    public const int RetentionDeleteBatchSize = 500;

    public static IReadOnlyList<HistoryRetention> RetentionOptions { get; } =
    [
        HistoryRetention.SevenDays,
        HistoryRetention.ThirtyDays,
        HistoryRetention.NinetyDays,
        HistoryRetention.Indefinite,
    ];

    public static TimeSpan? GetRetentionDuration(HistoryRetention retention) => retention switch
    {
        HistoryRetention.SevenDays => TimeSpan.FromDays(7),
        HistoryRetention.ThirtyDays => TimeSpan.FromDays(30),
        HistoryRetention.NinetyDays => TimeSpan.FromDays(90),
        HistoryRetention.Indefinite => null,
        _ => throw new ArgumentOutOfRangeException(nameof(retention)),
    };
}
