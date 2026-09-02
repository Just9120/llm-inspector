using System.Globalization;
using System.Text;
using LlmInspector.Domain;

namespace LlmInspector.App;

public static class RequestDetailTextPresenter
{
    private const string ProjectionVersion = "request-detail-projection-v1";

    public static string Format(ProxyObservation? observation)
    {
        if (observation is null)
        {
            return "Latest request: none.";
        }

        BackendResponseTelemetry telemetry = observation.BackendTelemetry;
        MetricValue unavailableTokenMetric = MetricValue.Unavailable(
            MetricUnit.TokenCount,
            MetricSource.Inspector,
            ProjectionVersion);
        MetricValue unavailableTimeMetric = MetricValue.Unavailable(
            MetricUnit.Milliseconds,
            MetricSource.Inspector,
            ProjectionVersion);
        MetricValue totalDuration = MetricValue.Calculated(
            (decimal)observation.Duration.TotalMilliseconds,
            MetricUnit.Milliseconds,
            MetricSource.Inspector,
            "monotonic-clock-v1",
            "monotonic-request-duration-v1");

        StringBuilder text = new();
        text.Append("Latest request ");
        text.Append(observation.RequestId.ToString("N", CultureInfo.InvariantCulture)[..8]);
        text.Append(" | Backend: ");
        text.Append(GetBackendLabel(telemetry.Backend));
        text.Append(" | Model: ");
        text.Append(telemetry.Model?.Value ?? "unavailable");

        text.AppendLine();
        text.Append("Tokens | Input: ");
        text.Append(FormatMetric(telemetry.PromptTokens));
        text.Append(" | Output: ");
        text.Append(FormatMetric(telemetry.CompletionTokens));
        text.Append(" | Cached input: ");
        text.Append(FormatMetric(telemetry.CachedPromptTokens));
        text.Append(" | Reasoning: ");
        text.Append(FormatMetric(telemetry.ReasoningTokens));

        text.AppendLine();
        text.Append("Context | Usage: ");
        text.Append(FormatMetric(telemetry.PromptTokens));
        text.Append(" | Limit: ");
        text.Append(FormatMetric(unavailableTokenMetric));
        text.Append(" | Change vs previous session turn: ");
        text.Append(FormatMetric(unavailableTokenMetric));

        text.AppendLine();
        text.Append("Context breakdown | History: ");
        text.Append(FormatMetric(unavailableTokenMetric));
        text.Append(" | Tools: ");
        text.Append(FormatMetric(unavailableTokenMetric));
        text.Append(" | Cache: ");
        text.Append(FormatMetric(telemetry.CachedPromptTokens));

        text.AppendLine();
        text.Append("Performance | Prompt/prefill: ");
        text.Append(FormatMetric(telemetry.PromptTokensPerSecond));
        text.Append(" | Generation: ");
        text.Append(FormatMetric(telemetry.CompletionTokensPerSecond));
        text.Append(" | TTFT: ");
        text.Append(FormatMetric(observation.TimeToFirstToken));
        text.Append(" | Model load: ");
        text.Append(FormatMetric(unavailableTimeMetric));
        text.Append(" | Queue: ");
        text.Append(FormatMetric(unavailableTimeMetric));
        text.Append(" | Total: ");
        text.Append(FormatMetric(totalDuration));

        text.AppendLine();
        text.Append("Cold/warm classification: unavailable [unavailable]. ");
        text.Append("The current OpenAI-compatible flow has no trusted session or model-load evidence.");
        return text.ToString();
    }

    private static string GetBackendLabel(BackendKind backend) => backend switch
    {
        BackendKind.Ollama => "Ollama",
        BackendKind.LlamaCpp => "llama.cpp",
        BackendKind.LmStudio => "LM Studio",
        _ => throw new ArgumentOutOfRangeException(nameof(backend)),
    };

    private static string FormatMetric(MetricValue metric)
    {
        string quality = metric.Quality switch
        {
            MetricQuality.Exact => "exact",
            MetricQuality.Calculated => "calculated",
            MetricQuality.Estimated => "estimated",
            MetricQuality.Unavailable => "unavailable",
            _ => throw new ArgumentOutOfRangeException(nameof(metric)),
        };
        if (metric.Value is null)
        {
            return $"unavailable [{quality}]";
        }

        string unit = metric.Unit switch
        {
            MetricUnit.TokenCount => "tokens",
            MetricUnit.Nanoseconds => "ns",
            MetricUnit.Milliseconds => "ms",
            MetricUnit.TokensPerSecond => "tokens/s",
            MetricUnit.Percent => "%",
            _ => throw new ArgumentOutOfRangeException(nameof(metric)),
        };
        return $"{metric.Value.Value.ToString("0.##", CultureInfo.InvariantCulture)} {unit} [{quality}]";
    }
}
