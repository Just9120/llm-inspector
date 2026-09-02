namespace LlmInspector.Domain;

public enum RequestStage
{
    ModelLoading,
    QueueWaiting,
    PromptProcessing,
    ReasoningGeneration,
    ToolWait,
    Completed,
    Cancelled,
    Error,
}

public enum RequestStageEvidence
{
    ProtocolObserved,
    BackendReported,
}

public sealed record RequestStageValue
{
    public RequestStageValue(
        RequestStage stage,
        RequestStageEvidence evidence,
        string sourceVersion)
    {
        if (string.IsNullOrWhiteSpace(sourceVersion))
        {
            throw new ArgumentException("Stage source version is required.", nameof(sourceVersion));
        }

        Stage = stage;
        Evidence = evidence;
        SourceVersion = sourceVersion;
    }

    public RequestStage Stage { get; }

    public RequestStageEvidence Evidence { get; }

    public string SourceVersion { get; }

    public bool IsTerminal => Stage is RequestStage.Completed or RequestStage.Cancelled or RequestStage.Error;

    public static RequestStageValue ProtocolObserved(RequestStage stage, string sourceVersion) =>
        new(stage, RequestStageEvidence.ProtocolObserved, sourceVersion);

    public static RequestStageValue BackendReported(RequestStage stage, string sourceVersion) =>
        new(stage, RequestStageEvidence.BackendReported, sourceVersion);
}

public sealed record BackendProgressSignal
{
    public BackendProgressSignal(decimal percentage, string sourceVersion)
    {
        if (percentage is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(percentage), "Backend progress must be within 0..100.");
        }

        if (string.IsNullOrWhiteSpace(sourceVersion))
        {
            throw new ArgumentException("Backend progress source version is required.", nameof(sourceVersion));
        }

        Percentage = percentage;
        SourceVersion = sourceVersion;
    }

    public decimal Percentage { get; }

    public string SourceVersion { get; }

    public MetricValue ToMetric() =>
        MetricValue.Exact(Percentage, MetricUnit.Percent, MetricSource.BackendExtension, SourceVersion);
}

public sealed record LiveRequestSnapshot(
    Guid RequestId,
    ClientKind Client,
    RequestStageValue Stage,
    DateTimeOffset StartedAt,
    MetricValue Elapsed,
    MetricValue Progress,
    MetricValue Eta);

public sealed record LiveRequestCollectionSnapshot(
    IReadOnlyList<LiveRequestSnapshot> ActiveRequests,
    LiveRequestSnapshot? LatestTerminalRequest);
