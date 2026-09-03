namespace LlmInspector.Domain;

public enum AgentCompletionDisposition
{
    Unavailable,
    ToolCalls,
    Final,
}

public sealed record AgentToolCall(int Sequence, TechnicalIdentifier ToolName);

public sealed record AgentTurnTelemetry(
    MetricValue AvailableToolCount,
    MetricValue InvokedToolCount,
    int? ToolResultCount,
    IReadOnlyList<AgentToolCall> ToolCalls,
    bool ToolDetailsComplete,
    AgentCompletionDisposition Completion)
{
    private const string SourceVersion = "openai-agent-metadata-v1";

    public static AgentTurnTelemetry Unavailable { get; } = new(
        MetricValue.Unavailable(MetricUnit.Count, MetricSource.Inspector, SourceVersion),
        MetricValue.Unavailable(MetricUnit.Count, MetricSource.Inspector, SourceVersion),
        null,
        [],
        false,
        AgentCompletionDisposition.Unavailable);
}
