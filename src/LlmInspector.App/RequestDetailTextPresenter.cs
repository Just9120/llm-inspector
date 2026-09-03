using System.Globalization;
using System.Text;
using LlmInspector.Application;
using LlmInspector.Domain;

namespace LlmInspector.App;

public static class RequestDetailTextPresenter
{
    public static string Format(ProxyObservation? observation)
    {
        if (observation is null)
        {
            return "Latest request: none.";
        }

        BackendResponseTelemetry telemetry = observation.BackendTelemetry;
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
        text.Append(" | Error origin: ");
        text.Append(HistoryErrorClassifier.OriginFrom(observation));

        if (observation.RuntimeFacts is TechnicalRuntimeFacts runtimeFacts)
        {
            text.AppendLine();
            text.Append("Runtime | config: ").Append(runtimeFacts.ConfigurationId.Value)
                .Append(" | Inspector: ").Append(runtimeFacts.InspectorVersion?.Value ?? "unavailable")
                .Append(" | backend: ").Append(runtimeFacts.BackendVersion?.Value ?? "unavailable")
                .Append(" | client: ").Append(runtimeFacts.ClientVersion?.Value ?? "unavailable")
                .Append(" | model: ").Append(runtimeFacts.ModelVersion?.Value ?? "unavailable")
                .Append(" | GPU driver: ").Append(runtimeFacts.GpuDriverVersion?.Value ?? "unavailable");
        }

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
        text.Append(FormatMetric(telemetry.ContextUsageTokens));
        text.Append(" | Limit: ");
        text.Append(FormatMetric(telemetry.ContextLimitTokens));
        text.Append(" | Change vs previous session turn: ");
        text.Append(FormatMetric(observation.ContextChangeTokens));

        text.AppendLine();
        text.Append("Context breakdown | History: ");
        text.Append(FormatMetric(telemetry.ContextHistoryTokens));
        text.Append(" | Tools: ");
        text.Append(FormatMetric(telemetry.ContextToolTokens));
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
        text.Append(FormatMetric(telemetry.ModelLoadTime));
        text.Append(" | Queue: ");
        text.Append(FormatMetric(telemetry.QueueTime));
        text.Append(" | Total: ");
        text.Append(FormatMetric(totalDuration));

        text.AppendLine();
        text.Append("Cold/warm classification: ");
        text.Append(telemetry.ModelLoadDisposition switch
        {
            ModelLoadDisposition.Cold => "cold / model load [exact]",
            ModelLoadDisposition.Warm => "warm [exact]",
            ModelLoadDisposition.Unavailable => "unavailable [unavailable]",
            _ => throw new ArgumentOutOfRangeException(nameof(observation)),
        });
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
            MetricUnit.TokenDelta => "tokens",
            MetricUnit.Nanoseconds => "ns",
            MetricUnit.Milliseconds => "ms",
            MetricUnit.TokensPerSecond => "tokens/s",
            MetricUnit.Percent => "%",
            _ => throw new ArgumentOutOfRangeException(nameof(metric)),
        };
        return $"{metric.Value.Value.ToString("0.##", CultureInfo.InvariantCulture)} {unit} [{quality}]";
    }
}
