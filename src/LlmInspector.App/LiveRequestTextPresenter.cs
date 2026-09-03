using System.Globalization;
using System.Text;
using LlmInspector.Domain;

namespace LlmInspector.App;

public static class LiveRequestTextPresenter
{
    public static string Format(LiveRequestCollectionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        StringBuilder text = new();
        if (snapshot.ActiveRequests.Count == 0)
        {
            text.Append("Active requests: none.");
        }
        else
        {
            text.Append("Active requests: ");
            text.Append(snapshot.ActiveRequests.Count.ToString(CultureInfo.InvariantCulture));
            text.AppendLine(".");
            for (int index = 0; index < snapshot.ActiveRequests.Count; index++)
            {
                if (index > 0)
                {
                    text.AppendLine();
                }

                AppendRequest(text, snapshot.ActiveRequests[index], "Active");
            }
        }

        if (snapshot.LatestTerminalRequest is not null)
        {
            text.AppendLine();
            AppendRequest(text, snapshot.LatestTerminalRequest, "Latest terminal");
        }

        return text.ToString();
    }

    public static string GetStageLabel(RequestStage stage) => stage switch
    {
        RequestStage.ModelLoading => "Model loading",
        RequestStage.QueueWaiting => "Queue / waiting",
        RequestStage.PromptProcessing => "Prompt processing",
        RequestStage.ReasoningGeneration => "Reasoning / generation",
        RequestStage.ToolWait => "Tool wait",
        RequestStage.Completed => "Completed",
        RequestStage.Cancelled => "Cancelled",
        RequestStage.Error => "Error",
        _ => throw new ArgumentOutOfRangeException(nameof(stage)),
    };

    private static void AppendRequest(StringBuilder text, LiveRequestSnapshot request, string prefix)
    {
        string evidence = request.Stage.Evidence switch
        {
            RequestStageEvidence.ProtocolObserved => "protocol observed",
            RequestStageEvidence.BackendReported => "backend reported",
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };
        text.Append(prefix);
        text.Append(' ');
        text.Append(request.RequestId.ToString("N", CultureInfo.InvariantCulture)[..8]);
        text.Append(" | Stage: ");
        text.Append(GetStageLabel(request.Stage.Stage));
        text.Append(" [");
        text.Append(evidence);
        text.Append("] | Elapsed: ");
        text.Append(FormatMetric(request.Elapsed));

        text.Append(" | Progress: ");
        text.Append(request.Progress.Quality == MetricQuality.Unavailable
            ? "unavailable [unavailable]"
            : FormatMetric(request.Progress));

        if (request.Eta.Quality == MetricQuality.Estimated)
        {
            text.Append(" | ETA: ");
            text.Append(FormatMetric(request.Eta));
        }
    }

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
