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
                .Append(" [").Append(request.ErrorType == HistoryErrorType.None
                    ? "none"
                    : request.IsRecurringError
                        ? $"recurring x{request.ErrorGroupOccurrenceCount.ToString(CultureInfo.InvariantCulture)}"
                        : "single failure").Append(']')
                .Append(" | origin=").Append(request.ErrorOrigin)
                .Append(" | session=").Append(FullId(request.SessionId))
                .Append(" | turn=").Append(FullId(request.CorrelatedTurnId))
                .Append('/').Append(request.CorrelatedTurnSequence?.ToString(CultureInfo.InvariantCulture) ?? "unavailable")
                .Append(" | operation=").Append(FullId(request.OperationId))
                .Append(" | model-load=").Append(request.ModelLoadDisposition)
                .AppendLine();
            if (request.RuntimeFacts is TechnicalRuntimeFacts facts)
            {
                text.Append("  Runtime config=").Append(facts.ConfigurationId.Value)
                    .Append(" | Inspector=").Append(Identifier(facts.InspectorVersion))
                    .Append(" | backend=").Append(Identifier(facts.BackendVersion))
                    .Append(" | client=").Append(Identifier(facts.ClientVersion))
                    .Append(" | model=").Append(Identifier(facts.ModelVersion))
                    .Append(" | GPU driver=").Append(Identifier(facts.GpuDriverVersion))
                    .AppendLine();
            }
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
                .Append(" | request=").Append(FullId(resource.RequestId))
                .Append(" | stage=").Append(resource.Stage?.Stage.ToString() ?? "unavailable")
                .Append(" | gaps=").Append(resource.DroppedSampleCount.ToString(CultureInfo.InvariantCulture))
                .Append(" | CPU=").Append(FormatMetric(resource.CpuPercent))
                .Append(" | memory=").Append(FormatMetric(resource.MemoryPercent))
                .Append(" | process=").Append(resource.RelatedProcess?.ImageName.Value ?? "unavailable")
                .Append(" | process CPU=").Append(FormatMetric(resource.ProcessCpuPercent))
                .Append(" | disk read/write=").Append(FormatMetric(resource.DiskReadBytes))
                .Append('/').Append(FormatMetric(resource.DiskWriteBytes))
                .Append(" | traffic in/out=").Append(FormatMetric(resource.ClientToBackendBytes))
                .Append('/').Append(FormatMetric(resource.BackendToClientBytes))
                .Append(" | GPU=").Append(resource.GpuDeviceId?.Value ?? "unavailable")
                .Append(" | GPU load=").Append(FormatMetric(resource.GpuUtilizationPercent))
                .Append(" | VRAM=").Append(FormatMetric(resource.GpuVramUsedBytes))
                .Append('/').Append(FormatMetric(resource.GpuVramTotalBytes))
                .Append(" | temperature=").Append(FormatMetric(resource.GpuTemperatureCelsius))
                .Append(" | power=").Append(FormatMetric(resource.GpuPowerWatts))
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
        if (analytics.ErrorGroups.Count == 0)
        {
            text.AppendLine("Error groups: none in the selected period.");
        }
        else
        {
            foreach (ErrorGroupSummary group in analytics.ErrorGroups)
            {
                text.Append("Error group ").Append(group.ErrorType)
                    .Append(" | occurrences=").Append(group.Occurrences.ToString(CultureInfo.InvariantCulture))
                    .Append(" | ").Append(group.IsRecurring ? "recurring" : "single failure")
                    .Append(" | first=").Append(group.FirstObservedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))
                    .Append(" | last=").Append(group.LastObservedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))
                    .AppendLine();
            }
        }

        foreach (CorrelatedErrorGroup group in analytics.ErrorCorrelations.ConfirmedGroups)
        {
            text.Append("Confirmed error correlation by ").Append(group.Basis)
                .Append('=').Append(group.CorrelationId.ToString("N"))
                .Append(" | occurrences=").Append(group.Occurrences.ToString(CultureInfo.InvariantCulture))
                .Append(" | types=").Append(string.Join(',', group.ErrorTypes))
                .Append(" | from=").Append(group.FirstObservedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))
                .Append(" | to=").Append(group.LastObservedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))
                .AppendLine();
        }

        text.Append("Uncorrelated errors: ")
            .Append(analytics.ErrorCorrelations.UncorrelatedErrors.ToString(CultureInfo.InvariantCulture))
            .AppendLine("; time proximity alone is not treated as proof.");
        AppendRuntimeCorrelation(text, analytics.RuntimeCorrelation);
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

    private static void AppendRuntimeCorrelation(StringBuilder text, RuntimeChangeCorrelation correlation)
    {
        text.Append("Runtime-change correlation: ").Append(correlation.Status);
        if (!correlation.IsStatisticallySufficient)
        {
            text.AppendLine("; insufficient correlation data.");
            return;
        }

        text.Append(" | baseline=").Append(correlation.Baseline!.Facts.ConfigurationId.Value)
            .Append(" | candidate=").Append(correlation.Candidate!.Facts.ConfigurationId.Value)
            .Append(" | result=")
            .Append(correlation.HasConfirmedRegression ? "CONFIRMED REGRESSION" : "no confirmed regression")
            .AppendLine();
        foreach (AnalyticsComparison comparison in correlation.PerformanceComparisons
                     .Append(correlation.ErrorRateComparison!))
        {
            text.Append("  ").Append(comparison.Metric)
                .Append(" | baseline n=").Append(comparison.Baseline.SampleCount.ToString(CultureInfo.InvariantCulture))
                .Append(" | candidate n=").Append(comparison.Candidate.SampleCount.ToString(CultureInfo.InvariantCulture))
                .Append(" | delta=").Append(FormatDecimal(comparison.MeanDelta))
                .Append(" | ").Append(comparison.IsConfirmedDegradation ? "degradation" : "no confirmed degradation")
                .AppendLine();
        }

        foreach (ErrorFrequencyComparison error in correlation.ErrorRateComparison!.RecurringErrorFrequency)
        {
            text.Append("  Runtime-linked ").Append(error.ErrorType)
                .Append(" rate delta=")
                .Append(error.RateDeltaPercentagePoints.ToString("+0.###;-0.###;0", CultureInfo.InvariantCulture))
                .AppendLine(" p.p.");
        }
    }

    public static string FormatComparison(AnalyticsComparison comparison)
    {
        StringBuilder text = new();
        text.Append(comparison.Metric)
            .Append(": baseline mean=").Append(FormatDecimal(comparison.Baseline.ArithmeticMean))
            .Append(" (n=").Append(comparison.Baseline.SampleCount.ToString(CultureInfo.InvariantCulture))
            .Append("), candidate mean=").Append(FormatDecimal(comparison.Candidate.ArithmeticMean))
            .Append(" (n=").Append(comparison.Candidate.SampleCount.ToString(CultureInfo.InvariantCulture))
            .Append("), delta=").Append(FormatDecimal(comparison.MeanDelta))
            .Append(", result=")
            .Append(comparison.IsConfirmedDegradation ? "CONFIRMED DEGRADATION" : "no confirmed degradation")
            .Append('.');
        foreach (ErrorFrequencyComparison error in comparison.RecurringErrorFrequency)
        {
            text.AppendLine()
                .Append("Recurring ").Append(error.ErrorType)
                .Append(": baseline=").Append(error.BaselineOccurrences.ToString(CultureInfo.InvariantCulture))
                .Append(" (").Append(error.BaselineRatePercent.ToString("0.###", CultureInfo.InvariantCulture)).Append("%)")
                .Append(", candidate=").Append(error.CandidateOccurrences.ToString(CultureInfo.InvariantCulture))
                .Append(" (").Append(error.CandidateRatePercent.ToString("0.###", CultureInfo.InvariantCulture)).Append("%)")
                .Append(", delta=").Append(error.RateDeltaPercentagePoints.ToString("+0.###;-0.###;0", CultureInfo.InvariantCulture))
                .Append(" p.p.");
        }

        return text.ToString();
    }

    public static string FormatClearPreview(HistoryClearPreview preview) =>
        $"Scope: {FormatScope(preview.Scope)}. " +
        $"Requests={preview.Requests}, sessions={preview.Sessions}, operations={preview.Operations}, " +
        $"turns={preview.Turns}, tools={preview.ToolEvents}, resource samples={preview.ResourceSamples}. " +
        "Review this exact scope, then confirm.";

    private static string FullId(Guid? value) => value is Guid id ? id.ToString("N") : "unavailable";

    private static string Identifier(TechnicalIdentifier? value) => value?.Value ?? "unavailable";

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
