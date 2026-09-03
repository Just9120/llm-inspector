using LlmInspector.Domain;

namespace LlmInspector.Application;

public enum HistoryErrorType
{
    None = 0,
    BackendUnavailable = 1,
    ClientCancelled = 2,
    RelayFailed = 3,
    ConnectionRefused = 4,
    ModelLoading = 5,
    HttpApiError = 6,
    Timeout = 7,
    ContextOverflow = 8,
    BackendCrash = 9,
}

public enum HistoryErrorOrigin
{
    NotApplicable = 0,
    Unknown = 1,
    Inspector = 2,
    Client = 3,
    Backend = 4,
    Model = 5,
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
    HistoryErrorType ErrorType)
{
    private const string SourceVersion = "openai-agent-metadata-v1";

    public MetricValue AvailableToolCount { get; init; } = MetricValue.Unavailable(
        MetricUnit.Count,
        MetricSource.Inspector,
        SourceVersion);

    public MetricValue InvokedToolCount { get; init; } = MetricValue.Unavailable(
        MetricUnit.Count,
        MetricSource.Inspector,
        SourceVersion);
}

public sealed record TechnicalToolEventRecord(
    Guid ToolEventId,
    Guid OperationId,
    int TurnSequence,
    int Sequence,
    TechnicalIdentifier ToolName,
    DateTimeOffset StartedAt,
    TimeSpan Duration,
    TechnicalToolStatus Status,
    HistoryErrorType ErrorType)
{
    public MetricValue DurationMetric { get; init; } = MetricValue.Calculated(
        (decimal)Duration.TotalMilliseconds,
        MetricUnit.Milliseconds,
        MetricSource.Inspector,
        "agent-operation-tracker-v1",
        "tool-call-wall-duration-v1");
}

public sealed record TechnicalResourceSampleRecord(
    Guid SampleId,
    Guid? OperationId,
    DateTimeOffset CapturedAt,
    MetricValue CpuPercent,
    MetricValue MemoryPercent)
{
    private const string UnavailableSourceVersion = "resource-monitor-unavailable-v1";

    public Guid? RequestId { get; init; }

    public RequestStageValue? Stage { get; init; }

    public TechnicalProcessAssociation? RelatedProcess { get; init; }

    public TechnicalIdentifier? GpuDeviceId { get; init; }

    public TechnicalIdentifier? GpuDriverVersion { get; init; }

    public int DroppedSampleCount { get; init; }

    public MetricValue MemoryUsedBytes { get; init; } = Unavailable(MetricUnit.Bytes);

    public MetricValue ProcessCpuPercent { get; init; } = Unavailable(MetricUnit.Percent);

    public MetricValue ProcessMemoryBytes { get; init; } = Unavailable(MetricUnit.Bytes);

    public MetricValue DiskReadBytes { get; init; } = Unavailable(MetricUnit.Bytes);

    public MetricValue DiskWriteBytes { get; init; } = Unavailable(MetricUnit.Bytes);

    public MetricValue ClientToBackendBytes { get; init; } = Unavailable(MetricUnit.Bytes);

    public MetricValue BackendToClientBytes { get; init; } = Unavailable(MetricUnit.Bytes);

    public MetricValue GpuUtilizationPercent { get; init; } = Unavailable(MetricUnit.Percent);

    public MetricValue GpuVramUsedBytes { get; init; } = Unavailable(MetricUnit.Bytes);

    public MetricValue GpuVramTotalBytes { get; init; } = Unavailable(MetricUnit.Bytes);

    public MetricValue GpuTemperatureCelsius { get; init; } = Unavailable(MetricUnit.Celsius);

    public MetricValue GpuPowerWatts { get; init; } = Unavailable(MetricUnit.Watts);

    private static MetricValue Unavailable(MetricUnit unit) =>
        MetricValue.Unavailable(unit, MetricSource.Inspector, UnavailableSourceVersion);
}

public sealed record TechnicalProcessAssociation
{
    public TechnicalProcessAssociation(
        int processId,
        DateTimeOffset processStartedAt,
        TechnicalIdentifier imageName,
        string sourceVersion)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(processId, 1);
        ArgumentNullException.ThrowIfNull(imageName);
        if (string.IsNullOrWhiteSpace(sourceVersion))
        {
            throw new ArgumentException("Process-association source version is required.", nameof(sourceVersion));
        }

        ProcessId = processId;
        ProcessStartedAt = processStartedAt;
        ImageName = imageName;
        SourceVersion = sourceVersion;
    }

    public int ProcessId { get; }

    public DateTimeOffset ProcessStartedAt { get; }

    public TechnicalIdentifier ImageName { get; }

    public string SourceVersion { get; }
}

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
    IReadOnlyDictionary<HistoryMetric, MetricValue> Metrics,
    ModelLoadDisposition ModelLoadDisposition = ModelLoadDisposition.Unavailable,
    Guid? CorrelatedTurnId = null,
    int? CorrelatedTurnSequence = null)
{
    public int ErrorGroupOccurrenceCount { get; init; }

    public HistoryErrorOrigin ErrorOrigin { get; init; } = HistoryErrorOrigin.NotApplicable;

    public TechnicalRuntimeFacts? RuntimeFacts { get; init; }

    public bool IsRecurringError =>
        ErrorType != HistoryErrorType.None &&
        ErrorGroupOccurrenceCount >= HistoryPolicies.RecurringErrorMinimumOccurrences;
}

public sealed record TechnicalOperationDetail(
    TechnicalOperationRecord Operation,
    IReadOnlyList<TechnicalTurnRecord> Turns,
    IReadOnlyList<TechnicalToolEventRecord> ToolEvents,
    IReadOnlyList<TechnicalResourceSampleRecord> ResourceSamples);

public sealed record TechnicalHistorySlice(
    IReadOnlyList<RequestHistoryItem> Requests,
    IReadOnlyList<TechnicalResourceSampleRecord> ResourceSamples,
    bool RequestsTruncated,
    bool ResourceSamplesTruncated);

public static class TechnicalHistorySnapshotPolicy
{
    public const int MaximumRequests = 1_000;
    public const int MaximumResourceSamples = 5_000;
}

public sealed record MetricAggregate(
    int SampleCount,
    bool IsStatisticallySufficient,
    decimal? ArithmeticMean,
    decimal? Median,
    decimal? P95);

public sealed record AnalyticsTrendPoint(
    DateOnly Day,
    IReadOnlyDictionary<HistoryMetric, MetricAggregate> Metrics);

public sealed record ModelLoadBreakdown
{
    public ModelLoadBreakdown(int coldRequests, int warmRequests, int unavailableRequests)
    {
        if (coldRequests < 0 || warmRequests < 0 || unavailableRequests < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(coldRequests),
                "Model-load request counts cannot be negative.");
        }

        ColdRequests = coldRequests;
        WarmRequests = warmRequests;
        UnavailableRequests = unavailableRequests;
    }

    public int ColdRequests { get; }

    public int WarmRequests { get; }

    public int UnavailableRequests { get; }

    public int TotalRequests => checked(ColdRequests + WarmRequests + UnavailableRequests);
}

public sealed record PeriodAnalytics(
    HistoryFilter Filter,
    IReadOnlyList<AnalyticsTrendPoint> Trend,
    ModelLoadBreakdown ModelLoads)
{
    public PeriodAnalytics(
        HistoryFilter filter,
        IReadOnlyList<AnalyticsTrendPoint> trend)
        : this(filter, trend, new ModelLoadBreakdown(0, 0, 0))
    {
    }

    public IReadOnlyList<ErrorGroupSummary> ErrorGroups { get; init; } = [];

    public ErrorCorrelationSummary ErrorCorrelations { get; init; } = ErrorCorrelationSummary.Empty;

    public RuntimeChangeCorrelation RuntimeCorrelation { get; init; } = RuntimeChangeCorrelation.Empty;
}

public sealed record AnalyticsComparison(
    HistoryMetric Metric,
    MetricAggregate Baseline,
    MetricAggregate Candidate,
    decimal? MeanDelta,
    bool IsConfirmedDegradation)
{
    public IReadOnlyList<ErrorFrequencyComparison> RecurringErrorFrequency { get; init; } = [];
}

public sealed record ErrorGroupSummary(
    HistoryErrorType ErrorType,
    int Occurrences,
    DateTimeOffset FirstObservedAt,
    DateTimeOffset LastObservedAt)
{
    public bool IsRecurring => Occurrences >= HistoryPolicies.RecurringErrorMinimumOccurrences;
}

public sealed record ErrorFrequencyComparison(
    HistoryErrorType ErrorType,
    int BaselineOccurrences,
    int CandidateOccurrences,
    decimal BaselineRatePercent,
    decimal CandidateRatePercent,
    decimal RateDeltaPercentagePoints);

public enum ErrorCorrelationBasis
{
    Operation,
    Session,
}

public sealed record CorrelatedErrorGroup(
    ErrorCorrelationBasis Basis,
    Guid CorrelationId,
    DateTimeOffset FirstObservedAt,
    DateTimeOffset LastObservedAt,
    IReadOnlyList<HistoryErrorType> ErrorTypes,
    int Occurrences);

public sealed record ErrorCorrelationSummary(
    IReadOnlyList<CorrelatedErrorGroup> ConfirmedGroups,
    int UncorrelatedErrors)
{
    public static ErrorCorrelationSummary Empty { get; } = new([], 0);
}

public enum RuntimeCorrelationStatus
{
    NoRuntimeFacts,
    SingleConfiguration,
    InsufficientSamples,
    Sufficient,
}

public sealed record RuntimeConfigurationAggregate(
    TechnicalRuntimeFacts Facts,
    DateTimeOffset FirstObservedAt,
    DateTimeOffset LastObservedAt,
    int RequestCount,
    IReadOnlyDictionary<HistoryMetric, MetricAggregate> Metrics);

public sealed record RuntimeChangeCorrelation(
    RuntimeCorrelationStatus Status,
    IReadOnlyList<RuntimeConfigurationAggregate> Configurations,
    RuntimeConfigurationAggregate? Baseline,
    RuntimeConfigurationAggregate? Candidate,
    IReadOnlyList<AnalyticsComparison> PerformanceComparisons,
    AnalyticsComparison? ErrorRateComparison)
{
    public static RuntimeChangeCorrelation Empty { get; } = new(
        RuntimeCorrelationStatus.NoRuntimeFacts,
        [],
        null,
        null,
        [],
        null);

    public bool IsStatisticallySufficient => Status == RuntimeCorrelationStatus.Sufficient;

    public bool HasConfirmedRegression =>
        PerformanceComparisons.Any(comparison => comparison.IsConfirmedDegradation) ||
        ErrorRateComparison?.IsConfirmedDegradation == true;
}

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

    public const int RecurringErrorMinimumOccurrences = 2;

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
