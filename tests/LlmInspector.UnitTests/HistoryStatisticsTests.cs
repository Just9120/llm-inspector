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

    [TestMethod]
    public void RuntimeCorrelationLinksSufficientVersionChangeToPerformanceAndErrorRegression()
    {
        DateTimeOffset at = DateTimeOffset.UnixEpoch;
        TechnicalRuntimeFacts baseline = RuntimeFacts("config-a", "backend-1.0", "client-1.0", "model-1.0", "driver-1.0");
        TechnicalRuntimeFacts candidate = RuntimeFacts("config-b", "backend-2.0", "client-2.0", "model-2.0", "driver-2.0");
        RequestHistoryItem[] requests =
        [
            RuntimeRequest(at, baseline, 100, 20, HistoryErrorType.None),
            RuntimeRequest(at.AddMinutes(1), baseline, 100, 20, HistoryErrorType.None),
            RuntimeRequest(at.AddMinutes(2), baseline, 100, 20, HistoryErrorType.None),
            RuntimeRequest(at.AddDays(1), candidate, 200, 10, HistoryErrorType.BackendUnavailable),
            RuntimeRequest(at.AddDays(1).AddMinutes(1), candidate, 200, 10, HistoryErrorType.BackendUnavailable),
            RuntimeRequest(at.AddDays(1).AddMinutes(2), candidate, 200, 10, HistoryErrorType.BackendUnavailable),
        ];

        RuntimeChangeCorrelation correlation = HistoryStatistics.CorrelateRuntimeChanges(requests);

        Assert.AreEqual(RuntimeCorrelationStatus.Sufficient, correlation.Status);
        Assert.AreEqual("config-a", correlation.Baseline?.Facts.ConfigurationId.Value);
        Assert.AreEqual("config-b", correlation.Candidate?.Facts.ConfigurationId.Value);
        Assert.IsTrue(correlation.PerformanceComparisons.Single(
            item => item.Metric == HistoryMetric.TotalDurationMilliseconds).IsConfirmedDegradation);
        Assert.IsTrue(correlation.PerformanceComparisons.Single(
            item => item.Metric == HistoryMetric.GenerationTokensPerSecond).IsConfirmedDegradation);
        Assert.IsTrue(correlation.ErrorRateComparison?.IsConfirmedDegradation);
        Assert.IsTrue(correlation.HasConfirmedRegression);
    }

    [TestMethod]
    public void RuntimeCorrelationExplainsMissingSingleAndInsufficientConfigurationData()
    {
        DateTimeOffset at = DateTimeOffset.UnixEpoch;
        TechnicalRuntimeFacts first = RuntimeFacts("config-a", "backend-1", "client-1", "model-1", "driver-1");
        TechnicalRuntimeFacts second = RuntimeFacts("config-b", "backend-2", "client-2", "model-2", "driver-2");

        Assert.AreEqual(
            RuntimeCorrelationStatus.NoRuntimeFacts,
            HistoryStatistics.CorrelateRuntimeChanges([Request(at, HistoryErrorType.None)]).Status);
        Assert.AreEqual(
            RuntimeCorrelationStatus.SingleConfiguration,
            HistoryStatistics.CorrelateRuntimeChanges([
                RuntimeRequest(at, first, 100, 20, HistoryErrorType.None),
            ]).Status);
        Assert.AreEqual(
            RuntimeCorrelationStatus.InsufficientSamples,
            HistoryStatistics.CorrelateRuntimeChanges([
                RuntimeRequest(at, first, 100, 20, HistoryErrorType.None),
                RuntimeRequest(at.AddDays(1), second, 200, 10, HistoryErrorType.BackendUnavailable),
            ]).Status);
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

    private static RequestHistoryItem RuntimeRequest(
        DateTimeOffset at,
        TechnicalRuntimeFacts facts,
        decimal duration,
        decimal generationThroughput,
        HistoryErrorType error) => Request(at, error) with
        {
            RuntimeFacts = facts,
            Metrics = new Dictionary<HistoryMetric, MetricValue>
            {
                [HistoryMetric.TotalDurationMilliseconds] = MetricValue.Exact(
                    duration,
                    MetricUnit.Milliseconds,
                    MetricSource.Inspector,
                    "runtime-correlation-test-v1"),
                [HistoryMetric.GenerationTokensPerSecond] = MetricValue.Exact(
                    generationThroughput,
                    MetricUnit.TokensPerSecond,
                    MetricSource.BackendExtension,
                    "runtime-correlation-test-v1"),
            },
        };

    private static TechnicalRuntimeFacts RuntimeFacts(
        string configuration,
        string backend,
        string client,
        string model,
        string driver) => new(Id(configuration))
        {
            BackendVersion = Id(backend),
            ClientVersion = Id(client),
            ModelVersion = Id(model),
            GpuDriverVersion = Id(driver),
        };

    private static TechnicalIdentifier Id(string value) =>
        TechnicalIdentifier.FromBackend(value) ?? throw new InvalidOperationException("Invalid test identifier.");

    private static T AssertSingle<T>(IReadOnlyList<T> items)
    {
        Assert.HasCount(1, items);
        return items[0];
    }
}
