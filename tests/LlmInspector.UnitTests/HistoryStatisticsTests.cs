using LlmInspector.Application;
using LlmInspector.Domain;

namespace LlmInspector.UnitTests;

[TestClass]
public sealed class HistoryStatisticsTests
{
    [TestMethod]
    public void CalculateUsesArithmeticMeanMedianAndNearestRankP95()
    {
        MetricAggregate aggregate = HistoryStatistics.Calculate([1m, 2m, 3m, 4m]);

        Assert.AreEqual(4, aggregate.SampleCount);
        Assert.IsTrue(aggregate.IsStatisticallySufficient);
        Assert.AreEqual(2.5m, aggregate.ArithmeticMean);
        Assert.AreEqual(2.5m, aggregate.Median);
        Assert.AreEqual(4m, aggregate.P95);
    }

    [TestMethod]
    public void MinimumSamplePolicyHasBoundaryAtThreeSamples()
    {
        MetricAggregate belowBoundary = HistoryStatistics.Calculate([10m, 20m]);
        MetricAggregate atBoundary = HistoryStatistics.Calculate([10m, 20m, 30m]);

        Assert.IsFalse(belowBoundary.IsStatisticallySufficient);
        Assert.IsTrue(atBoundary.IsStatisticallySufficient);
        Assert.IsNull(HistoryStatistics.Calculate([]).ArithmeticMean);
    }

    [TestMethod]
    public void ComparisonHighlightsOnlyDirectionallyWorseSufficientSamples()
    {
        AnalyticsComparison latency = HistoryStatistics.Compare(
            HistoryMetric.TimeToFirstTokenMilliseconds,
            [100m, 100m, 100m],
            [150m, 150m, 150m]);
        AnalyticsComparison throughput = HistoryStatistics.Compare(
            HistoryMetric.GenerationTokensPerSecond,
            [20m, 20m, 20m],
            [10m, 10m, 10m]);
        AnalyticsComparison neutralTokenCount = HistoryStatistics.Compare(
            HistoryMetric.InputTokens,
            [10m, 10m, 10m],
            [20m, 20m, 20m]);
        AnalyticsComparison insufficient = HistoryStatistics.Compare(
            HistoryMetric.TimeToFirstTokenMilliseconds,
            [100m, 100m],
            [150m, 150m]);

        Assert.IsTrue(latency.IsConfirmedDegradation);
        Assert.AreEqual(50m, latency.MeanDelta);
        Assert.IsTrue(throughput.IsConfirmedDegradation);
        Assert.IsFalse(neutralTokenCount.IsConfirmedDegradation);
        Assert.IsFalse(insufficient.IsConfirmedDegradation);
    }

    [TestMethod]
    public void RetentionPolicyExposesOnlyRatifiedOptions()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                HistoryRetention.SevenDays,
                HistoryRetention.ThirtyDays,
                HistoryRetention.NinetyDays,
                HistoryRetention.Indefinite,
            },
            HistoryPolicies.RetentionOptions.ToArray());
        Assert.AreEqual(TimeSpan.FromDays(7), HistoryPolicies.GetRetentionDuration(HistoryRetention.SevenDays));
        Assert.AreEqual(TimeSpan.FromDays(30), HistoryPolicies.GetRetentionDuration(HistoryRetention.ThirtyDays));
        Assert.AreEqual(TimeSpan.FromDays(90), HistoryPolicies.GetRetentionDuration(HistoryRetention.NinetyDays));
        Assert.IsNull(HistoryPolicies.GetRetentionDuration(HistoryRetention.Indefinite));
    }

    [TestMethod]
    public void RecurringErrorFrequencyUsesPerPeriodRequestDenominators()
    {
        DateTimeOffset at = DateTimeOffset.UnixEpoch;
        RequestHistoryItem[] baseline =
        [
            Request(at, HistoryErrorType.ConnectionRefused),
            Request(at.AddSeconds(1), HistoryErrorType.ConnectionRefused),
            Request(at.AddSeconds(2), HistoryErrorType.None),
            Request(at.AddSeconds(3), HistoryErrorType.None),
        ];
        RequestHistoryItem[] candidate =
        [
            Request(at.AddDays(1), HistoryErrorType.ConnectionRefused),
            Request(at.AddDays(1).AddSeconds(1), HistoryErrorType.ConnectionRefused),
            Request(at.AddDays(1).AddSeconds(2), HistoryErrorType.ConnectionRefused),
            Request(at.AddDays(1).AddSeconds(3), HistoryErrorType.None),
        ];

        ErrorFrequencyComparison comparison = AssertSingle(
            HistoryStatistics.CompareRecurringErrors(baseline, candidate));

        Assert.AreEqual(2, comparison.BaselineOccurrences);
        Assert.AreEqual(3, comparison.CandidateOccurrences);
        Assert.AreEqual(50m, comparison.BaselineRatePercent);
        Assert.AreEqual(75m, comparison.CandidateRatePercent);
        Assert.AreEqual(25m, comparison.RateDeltaPercentagePoints);
    }

    [TestMethod]
    public void ErrorCorrelationRequiresExplicitOperationOrSessionAndNeverUsesTimeAlone()
    {
        Guid operationId = Guid.NewGuid();
        DateTimeOffset at = DateTimeOffset.UnixEpoch;
        RequestHistoryItem operationError = Request(at, HistoryErrorType.ConnectionRefused) with
        {
            OperationId = operationId,
        };
        RequestHistoryItem relatedClientError = Request(at.AddMilliseconds(1), HistoryErrorType.ClientCancelled) with
        {
            OperationId = operationId,
        };
        RequestHistoryItem nearbyButUncorrelated = Request(at.AddMilliseconds(2), HistoryErrorType.BackendCrash);

        ErrorCorrelationSummary summary = HistoryStatistics.CorrelateErrors(
            [operationError, relatedClientError, nearbyButUncorrelated]);

        CorrelatedErrorGroup group = AssertSingle(summary.ConfirmedGroups);
        Assert.AreEqual(ErrorCorrelationBasis.Operation, group.Basis);
        Assert.AreEqual(operationId, group.CorrelationId);
        Assert.AreEqual(2, group.Occurrences);
        CollectionAssert.AreEquivalent(
            new[] { HistoryErrorType.ConnectionRefused, HistoryErrorType.ClientCancelled },
            group.ErrorTypes.ToArray());
        Assert.AreEqual(1, summary.UncorrelatedErrors);
    }

    private static RequestHistoryItem Request(DateTimeOffset at, HistoryErrorType error) => new(
        Guid.NewGuid(),
        null,
        null,
        at,
        error == HistoryErrorType.None ? 200 : 502,
        ProxyOutcome.Completed,
        error,
        ClientKind.Cline,
        BackendKind.Ollama,
        null,
        new Dictionary<HistoryMetric, MetricValue>());

    private static T AssertSingle<T>(IReadOnlyList<T> items)
    {
        Assert.HasCount(1, items);
        return items[0];
    }
}
