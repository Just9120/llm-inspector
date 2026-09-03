using LlmInspector.Application;

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
}
