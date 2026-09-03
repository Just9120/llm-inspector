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

    public static IReadOnlyList<ErrorFrequencyComparison> CompareRecurringErrors(
        IReadOnlyList<RequestHistoryItem> baseline,
        IReadOnlyList<RequestHistoryItem> candidate)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(candidate);
        Dictionary<HistoryErrorType, int> baselineCounts = CountErrors(baseline);
        Dictionary<HistoryErrorType, int> candidateCounts = CountErrors(candidate);

        return baselineCounts.Keys
            .Union(candidateCounts.Keys)
            .Where(error => baselineCounts.GetValueOrDefault(error) >= HistoryPolicies.RecurringErrorMinimumOccurrences ||
                            candidateCounts.GetValueOrDefault(error) >= HistoryPolicies.RecurringErrorMinimumOccurrences)
            .Order()
            .Select(error =>
            {
                int baselineCount = baselineCounts.GetValueOrDefault(error);
                int candidateCount = candidateCounts.GetValueOrDefault(error);
                decimal baselineRate = Rate(baselineCount, baseline.Count);
                decimal candidateRate = Rate(candidateCount, candidate.Count);
                return new ErrorFrequencyComparison(
                    error,
                    baselineCount,
                    candidateCount,
                    baselineRate,
                    candidateRate,
                    candidateRate - baselineRate);
            })
            .ToArray();
    }

    public static IReadOnlyList<ErrorGroupSummary> SummarizeErrors(
        IReadOnlyList<RequestHistoryItem> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);
        return requests
            .Where(request => request.ErrorType != HistoryErrorType.None)
            .GroupBy(request => request.ErrorType)
            .OrderBy(group => group.Key)
            .Select(group => new ErrorGroupSummary(
                group.Key,
                group.Count(),
                group.Min(request => request.StartedAt),
                group.Max(request => request.StartedAt)))
            .ToArray();
    }

    public static ErrorCorrelationSummary CorrelateErrors(IReadOnlyList<RequestHistoryItem> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);
        RequestHistoryItem[] errors = requests
            .Where(request => request.ErrorType != HistoryErrorType.None)
            .ToArray();
        var candidates = errors
            .Select(request => request.OperationId is Guid operationId
                ? new CorrelationCandidate(ErrorCorrelationBasis.Operation, operationId, request)
                : request.SessionId is Guid sessionId
                    ? new CorrelationCandidate(ErrorCorrelationBasis.Session, sessionId, request)
                    : null)
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .GroupBy(candidate => new { candidate.Basis, candidate.CorrelationId })
            .Where(group => group.Count() >= 2)
            .OrderBy(group => group.Min(item => item.Request.StartedAt))
            .ToArray();
        HashSet<Guid> correlatedRequests = candidates
            .SelectMany(group => group.Select(item => item.Request.RequestId))
            .ToHashSet();
        CorrelatedErrorGroup[] groups = candidates
            .Select(group => new CorrelatedErrorGroup(
                group.Key.Basis,
                group.Key.CorrelationId,
                group.Min(item => item.Request.StartedAt),
                group.Max(item => item.Request.StartedAt),
                group.Select(item => item.Request.ErrorType).Distinct().Order().ToArray(),
                group.Count()))
            .ToArray();
        return new ErrorCorrelationSummary(
            groups,
            errors.Count(request => !correlatedRequests.Contains(request.RequestId)));
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

    private static Dictionary<HistoryErrorType, int> CountErrors(IEnumerable<RequestHistoryItem> requests) =>
        requests
            .Where(request => request.ErrorType != HistoryErrorType.None)
            .GroupBy(request => request.ErrorType)
            .ToDictionary(group => group.Key, group => group.Count());

    private static decimal Rate(int occurrences, int total) =>
        total == 0 ? 0 : 100m * occurrences / total;

    private sealed record CorrelationCandidate(
        ErrorCorrelationBasis Basis,
        Guid CorrelationId,
        RequestHistoryItem Request);
}
