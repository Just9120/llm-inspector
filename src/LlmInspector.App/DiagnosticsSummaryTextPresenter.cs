using System.Globalization;
using System.Text;
using LlmInspector.Application;
using LlmInspector.Diagnostics;
using LlmInspector.Domain;

namespace LlmInspector.App;

public static class DiagnosticsSummaryTextPresenter
{
    public static string Format(
        AppRuntimeStatus runtimeStatus,
        ProxyObservation? latestObservation,
        string historyState,
        TechnicalResourceSampleRecord? latestResourceSample = null,
        LiveRequestCollectionSnapshot? liveRequests = null,
        IReadOnlyList<BackendActivitySignal>? backendActivity = null)
    {
        ArgumentNullException.ThrowIfNull(runtimeStatus);

        StringBuilder text = new();
        text.Append("Gateway: ");
        text.Append(runtimeStatus.ProxyRunning ? "available" : "unavailable");
        text.Append(". ");
        text.Append(runtimeStatus.State);

        text.AppendLine();
        text.Append("Technical history: ");
        text.Append(string.IsNullOrWhiteSpace(historyState) ? "state unavailable." : historyState.Trim());

        text.AppendLine();
        text.Append("Latest completed request: ");
        if (latestObservation is null)
        {
            text.Append("none; send a request through an Inspector base URL to collect technical diagnostics.");
        }
        else
        {
            text.Append(latestObservation.RequestId.ToString("N", CultureInfo.InvariantCulture)[..8]);
            text.Append(" | outcome=");
            text.Append(latestObservation.Outcome);
            text.Append(" | error=");
            text.Append(HistoryErrorClassifier.From(latestObservation));
            text.Append(" | HTTP=");
            text.Append(latestObservation.HttpStatusCode?.ToString(CultureInfo.InvariantCulture) ?? "unavailable");
            text.Append(" | duration=");
            text.Append(latestObservation.Duration.TotalMilliseconds.ToString("0.##", CultureInfo.InvariantCulture));
            text.Append(" ms [calculated]");
        }

        IReadOnlyList<DiagnosticConclusion> conclusions = DiagnosticRuleset.Default.Evaluate(
            new DiagnosticInput(latestObservation, latestResourceSample, liveRequests, backendActivity));
        text.AppendLine();
        text.Append("Ruleset conclusions:");
        foreach (DiagnosticConclusion conclusion in conclusions)
        {
            text.AppendLine();
            text.Append("- ").Append(conclusion.Kind.ToString().ToUpperInvariant())
                .Append(" | ").Append(conclusion.Rule)
                .Append(" [").Append(conclusion.RuleVersion).Append("]: ")
                .Append(conclusion.Explanation)
                .Append(" Evidence: ")
                .Append(conclusion.Evidence.Count == 0
                    ? "unavailable."
                    : string.Join("; ", conclusion.Evidence.Select(FormatEvidence)) + ".");
        }

        return text.ToString();
    }

    private static string FormatEvidence(DiagnosticEvidence evidence)
    {
        if (evidence.Metric is MetricValue metric)
        {
            string value = metric.Value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "unavailable";
            return $"{evidence.Kind}={value} {metric.Unit} [{metric.Quality}; {metric.Source}/{metric.SourceVersion}]";
        }

        if (evidence.Stage is RequestStageValue stage)
        {
            return $"{evidence.Kind}={stage.Stage} [{stage.Evidence}; {stage.SourceVersion}]";
        }

        if (evidence.Error is ProxyErrorType error)
        {
            return $"{evidence.Kind}={error}";
        }

        if (evidence.ModelLoad is ModelLoadDisposition modelLoad)
        {
            return $"{evidence.Kind}={modelLoad}";
        }

        if (evidence.Activity is BackendActivitySignal activity)
        {
            return $"{evidence.Kind}={activity.State} [{activity.SourceVersion}; {activity.ObservedAt:O}]";
        }

        return $"{evidence.Kind}=unavailable";
    }
}
