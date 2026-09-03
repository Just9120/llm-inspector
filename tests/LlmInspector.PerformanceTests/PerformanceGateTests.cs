using System.Security.Cryptography;
using System.Text.Json;
using LlmInspector.Domain;

namespace LlmInspector.PerformanceTests;

[TestClass]
public sealed class PerformanceGateTests
{
    private const string ExpectedFixtureSha256 = "1c38874fb393cfe094bf1d44a281859c2a6e340b9acb46b86df3b458a41f3aca";

    private static readonly PerformanceBenchmarkProtocol ValidProtocol = new(
        TimeSpan.FromMinutes(10),
        TimeSpan.FromHours(1),
        [
            PerformancePairOrder.BaselineThenInspector,
            PerformancePairOrder.InspectorThenBaseline,
            PerformancePairOrder.BaselineThenInspector,
            PerformancePairOrder.InspectorThenBaseline,
            PerformancePairOrder.BaselineThenInspector,
        ],
        ReliableDiscreteGpuSourceAvailable: true);

    private static readonly string[] RequiredWorkloadIds =
    [
        "idle",
        "cold-load",
        "hybrid-streaming-c1",
        "hybrid-nonstreaming-c1",
        "hybrid-streaming-c4",
        "cpu-only",
        "tools-fragmented-stream",
        "collector-unavailable",
        "collector-failure",
    ];

    [TestMethod]
    public void ExactBudgetBoundariesPassForEveryBuiltInProfile()
    {
        foreach (MonitoringPerformanceProfile profile in MonitoringPerformanceProfiles.BuiltIn)
        {
            PerformanceGateResult result = PerformanceGateEvaluator.Evaluate(
                profile,
                ValidProtocol,
                AtBudget(profile.ReleaseBudget!));

            Assert.IsTrue(result.Passed, profile.DisplayName);
            Assert.IsTrue(result.Findings.All(finding => finding.Status == PerformanceGateStatus.Passed));
        }
    }

    [TestMethod]
    public void MissingMandatoryMetricAndAboveBudgetValueFailClosed()
    {
        PerformanceMeasurement measurement = AtBudget(MonitoringPerformanceProfiles.Balanced.ReleaseBudget!) with
        {
            ActiveCpuMeanPercentagePoints = null,
            ThroughputRegressionP95Percent = 10.01m,
        };

        PerformanceGateResult result = PerformanceGateEvaluator.Evaluate(
            MonitoringPerformanceProfiles.Balanced,
            ValidProtocol,
            measurement);

        Assert.IsFalse(result.Passed);
        Assert.AreEqual(
            PerformanceGateStatus.Unavailable,
            result.Findings.Single(finding => finding.Metric == "active_cpu_mean_pp").Status);
        Assert.AreEqual(
            PerformanceGateStatus.Failed,
            result.Findings.Single(finding => finding.Metric == "throughput_regression_p95_percent").Status);
    }

    [TestMethod]
    public void InvalidProtocolAndCustomProfileCannotProduceReleasePass()
    {
        PerformanceBenchmarkProtocol invalidProtocol = ValidProtocol with
        {
            IdleWarmup = TimeSpan.FromMinutes(9),
            IdleMeasurement = TimeSpan.FromMinutes(59),
            ActivePairOrders =
            [
                PerformancePairOrder.BaselineThenInspector,
                PerformancePairOrder.BaselineThenInspector,
                PerformancePairOrder.BaselineThenInspector,
                PerformancePairOrder.BaselineThenInspector,
            ],
        };

        PerformanceGateResult invalid = PerformanceGateEvaluator.Evaluate(
            MonitoringPerformanceProfiles.Balanced,
            invalidProtocol,
            AtBudget(MonitoringPerformanceProfiles.Balanced.ReleaseBudget!));
        PerformanceGateResult custom = PerformanceGateEvaluator.Evaluate(
            MonitoringPerformanceProfiles.CreateCustom(750),
            ValidProtocol,
            AtBudget(MonitoringPerformanceProfiles.Balanced.ReleaseBudget!));

        Assert.IsFalse(invalid.Passed);
        Assert.AreEqual(4, invalid.Findings.Count(finding => finding.Status == PerformanceGateStatus.Failed));
        Assert.IsFalse(custom.Passed);
        Assert.AreEqual("release_profile", AssertSingle(custom.Findings).Metric);
    }

    [TestMethod]
    public void GpuGatesAreOnlyNotApplicableWhenReliableDiscreteSourceIsAbsent()
    {
        PerformanceMeasurement measurement = AtBudget(MonitoringPerformanceProfiles.Saver.ReleaseBudget!) with
        {
            GpuUtilizationDeltaMeanPercentagePoints = null,
            GpuUtilizationDeltaP95PercentagePoints = null,
            DedicatedVramP95 = null,
        };
        PerformanceBenchmarkProtocol noGpu = ValidProtocol with
        {
            ReliableDiscreteGpuSourceAvailable = false,
        };

        PerformanceGateResult result = PerformanceGateEvaluator.Evaluate(
            MonitoringPerformanceProfiles.Saver,
            noGpu,
            measurement);

        Assert.IsTrue(result.Passed);
        Assert.AreEqual(3, result.Findings.Count(finding => finding.Status == PerformanceGateStatus.NotApplicable));
    }

    [TestMethod]
    public void SyntheticCorpusHasRequiredWorkloadsAndFrozenDigest()
    {
        string path = Path.Combine(
            FindRepositoryRoot().FullName,
            "benchmarks",
            "fixtures",
            "epic12",
            "v1",
            "reference-workloads.json");
        byte[] bytes = File.ReadAllBytes(path);
        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement root = document.RootElement;
        string[] workloadIds = root.GetProperty("workloads")
            .EnumerateArray()
            .Select(item => item.GetProperty("id").GetString()!)
            .ToArray();

        Assert.AreEqual("performance-corpus-v1", root.GetProperty("schema_version").GetString());
        Assert.AreEqual(8192, root.GetProperty("fixed_context_tokens").GetInt32());
        CollectionAssert.AreEquivalent(RequiredWorkloadIds, workloadIds);
        Assert.AreEqual(ExpectedFixtureSha256, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    private static PerformanceMeasurement AtBudget(MonitoringPerformanceBudget budget) => new(
        budget.ActiveCpuMeanPercentagePoints,
        budget.ActiveCpuP95PercentagePoints,
        budget.ProcessPrivateBytesP95,
        budget.ActiveRamGrowthPerThirtyMinutes,
        budget.GpuUtilizationDeltaMeanPercentagePoints,
        budget.GpuUtilizationDeltaP95PercentagePoints,
        budget.DedicatedVramP95,
        budget.DiskWritesPerMinute,
        budget.ThroughputRegressionMedianPercent,
        budget.ThroughputRegressionP95Percent,
        budget.IdleCpuMeanPercent,
        budget.IdleCpuP95Percent,
        budget.IdleRamGrowthPerHour,
        budget.IdleDiskWritesPerHour,
        budget.IdleWakeupsMeanPerSecond,
        budget.IdleWakeupsP95PerSecond);

    private static T AssertSingle<T>(IReadOnlyList<T> values)
    {
        Assert.HasCount(1, values);
        return values[0];
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "LlmInspector.slnx")))
            {
                return current;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root containing LlmInspector.slnx was not found.");
    }
}
