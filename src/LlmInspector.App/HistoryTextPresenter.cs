using System.Globalization;
using System.Text;
using LlmInspector.Application;
using LlmInspector.Domain;

namespace LlmInspector.App;

public static class HistoryTextPresenter
{
    public static string FormatRequests(IReadOnlyList<RequestHistoryItem> requests)
    {
        if (requests.Count == 0)
        {
            return "No technical history records match the selected filters.";
        }

        StringBuilder text = new();
        text.Append(requests.Count.ToString(CultureInfo.InvariantCulture)).AppendLine(" request record(s):");
        foreach (RequestHistoryItem request in requests)
        {
            text.Append(request.StartedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))
                .Append(" | ").Append(request.RequestId.ToString("N")[..8])
                .Append(" | ").Append(request.Client)
                .Append(" -> ").Append(request.Backend)
                .Append(" | model=").Append(request.Model?.Value ?? "unavailable")
                .Append(" | status=").Append(request.Outcome)
                .Append(" | error=").Append(request.ErrorType)
                .Append(" | session=").Append(FullId(request.SessionId))
                .Append(" | turn=").Append(FullId(request.CorrelatedTurnId))
                .Append('/').Append(request.CorrelatedTurnSequence?.ToString(CultureInfo.InvariantCulture) ?? "unavailable")
                .Append(" | operation=").Append(FullId(request.OperationId))
                .Append(" | model-load=").Append(request.ModelLoadDisposition)
                .AppendLine();
        }

        return text.ToString().TrimEnd();
    }

    public static string FormatOperation(TechnicalOperationDetail? detail)
    {
        if (detail is null)
        {
            return "Operation not found.";
        }

        StringBuilder text = new();
        text.Append("Operation ").Append(detail.Operation.OperationId.ToString("N"))
            .Append(" | status=").Append(detail.Operation.Status)
            .Append(" | error=").Append(detail.Operation.ErrorType)
            .AppendLine();
        foreach (TechnicalTurnRecord turn in detail.Turns)
        {
            text.Append("Turn ").Append(turn.Sequence.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(FormatMilliseconds(turn.Duration.TotalMilliseconds))
                .Append(" | tools available=").Append(FormatMetric(turn.AvailableToolCount))
                .Append(" | tools invoked=").Append(FormatMetric(turn.InvokedToolCount))
                .Append(" | status=").Append(turn.Outcome)
                .Append(" | error=").Append(turn.ErrorType)
                .AppendLine();
            foreach (TechnicalToolEventRecord tool in detail.ToolEvents.Where(item => item.TurnSequence == turn.Sequence))
            {
                text.Append("  Tool ").Append(tool.Sequence.ToString(CultureInfo.InvariantCulture))
                    .Append(' ').Append(tool.ToolName.Value)
                    .Append(" | ").Append(FormatMetric(tool.DurationMetric))
                    .Append(" | status=").Append(tool.Status)
                    .Append(" | error=").Append(tool.ErrorType)
                    .AppendLine();
            }
        }

        foreach (TechnicalResourceSampleRecord resource in detail.ResourceSamples)
        {
            text.Append("Resource ").Append(resource.CapturedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))
                .Append(" | CPU=").Append(FormatMetric(resource.CpuPercent))
                .Append(" | memory=").Append(FormatMetric(resource.MemoryPercent))
                .AppendLine();
        }

        return text.ToString().TrimEnd();
    }

    public static string FormatAnalytics(PeriodAnalytics analytics)
    {
        if (analytics.Trend.Count == 0)
        {
            return "No samples match the selected analytics range.";
        }

        StringBuilder text = new();
        text.Append("Model load classification | cold=")
            .Append(analytics.ModelLoads.ColdRequests.ToString(CultureInfo.InvariantCulture))
            .Append(" | warm=")
            .Append(analytics.ModelLoads.WarmRequests.ToString(CultureInfo.InvariantCulture))
            .Append(" | unavailable=")
            .Append(analytics.ModelLoads.UnavailableRequests.ToString(CultureInfo.InvariantCulture))
            .AppendLine();
        foreach (AnalyticsTrendPoint point in analytics.Trend)
        {
            text.AppendLine(point.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            foreach ((HistoryMetric metric, MetricAggregate aggregate) in point.Metrics)
            {
                text.Append("  ").Append(metric)
                    .Append(": n=").Append(aggregate.SampleCount.ToString(CultureInfo.InvariantCulture))
                    .Append(" | mean=").Append(FormatDecimal(aggregate.ArithmeticMean))
                    .Append(" | median=").Append(FormatDecimal(aggregate.Median))
                    .Append(" | P95(nearest-rank)=").Append(FormatDecimal(aggregate.P95))
                    .Append(" | ").Append(aggregate.IsStatisticallySufficient ? "sufficient" : "insufficient (<3)")
                    .AppendLine();
            }
        }

        return text.ToString().TrimEnd();
    }

    public static string FormatComparison(AnalyticsComparison comparison) =>
        $"{comparison.Metric}: baseline mean={FormatDecimal(comparison.Baseline.ArithmeticMean)} " +
        $"(n={comparison.Baseline.SampleCount}), candidate mean={FormatDecimal(comparison.Candidate.ArithmeticMean)} " +
        $"(n={comparison.Candidate.SampleCount}), delta={FormatDecimal(comparison.MeanDelta)}, " +
        $"result={(comparison.IsConfirmedDegradation ? "CONFIRMED DEGRADATION" : "no confirmed degradation")}.";

    public static string FormatClearPreview(HistoryClearPreview preview) =>
        $"Scope: {FormatScope(preview.Scope)}. " +
        $"Requests={preview.Requests}, sessions={preview.Sessions}, operations={preview.Operations}, " +
        $"turns={preview.Turns}, tools={preview.ToolEvents}, resource samples={preview.ResourceSamples}. " +
        "Review this exact scope, then confirm.";

    private static string FullId(Guid? value) => value is Guid id ? id.ToString("N") : "unavailable";

    private static string FormatScope(HistoryClearScope scope) => scope.AllHistory
        ? "all history"
        : $"UTC range from {scope.From?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? "unbounded"} " +
          $"to {scope.To?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? "unbounded"}";

    private static string FormatMetric(MetricValue metric) =>
        metric.Value is decimal value
            ? $"{value.ToString("0.###", CultureInfo.InvariantCulture)} [{metric.Quality.ToString().ToLowerInvariant()}]"
            : "unavailable";

    private static string FormatMilliseconds(double value) =>
        $"{value.ToString("0.###", CultureInfo.InvariantCulture)} ms";

    private static string FormatDecimal(decimal? value) =>
        value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "unavailable";
}
