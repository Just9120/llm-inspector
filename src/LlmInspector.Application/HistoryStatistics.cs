namespace LlmInspector.Application;

public static class HistoryStatistics
{
    public static MetricAggregate Calculate(IEnumerable<decimal> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        decimal[] ordered = samples.Order().ToArray();
        if (ordered.Length == 0)
        {
            return new MetricAggregate(0, false, null, null, null);
        }

        decimal mean = ordered.Sum() / ordered.Length;
        decimal median = ordered.Length % 2 == 1
            ? ordered[ordered.Length / 2]
            : (ordered[(ordered.Length / 2) - 1] + ordered[ordered.Length / 2]) / 2;
        int p95Index = Math.Max(0, (int)Math.Ceiling(ordered.Length * 0.95m) - 1);

        return new MetricAggregate(
            ordered.Length,
            ordered.Length >= HistoryPolicies.MinimumAggregateSamples,
            mean,
            median,
            ordered[p95Index]);
    }

    public static AnalyticsComparison Compare(
        HistoryMetric metric,
        IEnumerable<decimal> baselineSamples,
        IEnumerable<decimal> candidateSamples)
    {
        MetricAggregate baseline = Calculate(baselineSamples);
        MetricAggregate candidate = Calculate(candidateSamples);
        decimal? delta = baseline.ArithmeticMean is decimal baselineMean &&
                         candidate.ArithmeticMean is decimal candidateMean
            ? candidateMean - baselineMean
            : null;
        bool degradation = baseline.IsStatisticallySufficient &&
            candidate.IsStatisticallySufficient &&
            delta is decimal difference &&
            IsDegradation(metric, difference);

        return new AnalyticsComparison(metric, baseline, candidate, delta, degradation);
    }

    private static bool IsDegradation(HistoryMetric metric, decimal candidateMinusBaseline) => metric switch
    {
        HistoryMetric.TimeToFirstTokenMilliseconds or
        HistoryMetric.ModelLoadMilliseconds or
        HistoryMetric.QueueMilliseconds or
        HistoryMetric.TotalDurationMilliseconds or
        HistoryMetric.CpuPercent or
        HistoryMetric.MemoryPercent or
        HistoryMetric.ErrorRatePercent => candidateMinusBaseline > 0,
        HistoryMetric.PromptTokensPerSecond or
        HistoryMetric.GenerationTokensPerSecond => candidateMinusBaseline < 0,
        _ => false,
    };
}
