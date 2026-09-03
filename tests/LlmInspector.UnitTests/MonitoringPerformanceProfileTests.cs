using LlmInspector.Domain;

namespace LlmInspector.UnitTests;

[TestClass]
public sealed class MonitoringPerformanceProfileTests
{
    [TestMethod]
    public void BuiltInProfilesExposeTheRatifiedIntervalsAndBudgets()
    {
        AssertProfile(
            MonitoringPerformanceProfiles.Saver,
            MonitoringPerformanceProfileId.Saver,
            "Бережный",
            2_000,
            [1.5m, 4m, 192m, 16m, 1m, 3m, 128m, 1m, 3m, 5m, 0.25m, 1m, 8m, 0.25m, 2m, 8m]);
        AssertProfile(
            MonitoringPerformanceProfiles.Balanced,
            MonitoringPerformanceProfileId.Balanced,
            "Сбалансированный",
            1_000,
            [3m, 8m, 256m, 32m, 2m, 5m, 192m, 2m, 5m, 10m, 0.5m, 2m, 16m, 1m, 5m, 15m]);
        AssertProfile(
            MonitoringPerformanceProfiles.Detailed,
            MonitoringPerformanceProfileId.Detailed,
            "Детальный",
            500,
            [5m, 12m, 384m, 64m, 3m, 8m, 256m, 5m, 8m, 15m, 1m, 4m, 32m, 5m, 15m, 30m]);
    }

    [TestMethod]
    public void CustomProfileHasValidatedBoundsAndCannotSupplyAReleaseBudget()
    {
        MonitoringPerformanceProfile minimum = MonitoringPerformanceProfiles.CreateCustom(250);
        MonitoringPerformanceProfile maximum = MonitoringPerformanceProfiles.CreateCustom(10_000);

        Assert.AreEqual(TimeSpan.FromMilliseconds(250), minimum.SamplingInterval);
        Assert.AreEqual(TimeSpan.FromSeconds(10), maximum.SamplingInterval);
        Assert.IsNull(minimum.ReleaseBudget);
        Assert.IsFalse(minimum.IsBuiltIn);
        _ = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            MonitoringPerformanceProfiles.CreateCustom(249));
        _ = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            MonitoringPerformanceProfiles.CreateCustom(10_001));
    }

    private static void AssertProfile(
        MonitoringPerformanceProfile profile,
        MonitoringPerformanceProfileId expectedId,
        string expectedName,
        int expectedIntervalMilliseconds,
        decimal[] expected)
    {
        const decimal Mebibyte = 1_048_576m;
        MonitoringPerformanceBudget budget = profile.ReleaseBudget ??
            throw new AssertFailedException("A built-in profile must have a release budget.");
        decimal[] actual =
        [
            budget.ActiveCpuMeanPercentagePoints,
            budget.ActiveCpuP95PercentagePoints,
            budget.ProcessPrivateBytesP95 / Mebibyte,
            budget.ActiveRamGrowthPerThirtyMinutes / Mebibyte,
            budget.GpuUtilizationDeltaMeanPercentagePoints,
            budget.GpuUtilizationDeltaP95PercentagePoints,
            budget.DedicatedVramP95 / Mebibyte,
            budget.DiskWritesPerMinute / Mebibyte,
            budget.ThroughputRegressionMedianPercent,
            budget.ThroughputRegressionP95Percent,
            budget.IdleCpuMeanPercent,
            budget.IdleCpuP95Percent,
            budget.IdleRamGrowthPerHour / Mebibyte,
            budget.IdleDiskWritesPerHour / Mebibyte,
            budget.IdleWakeupsMeanPerSecond,
            budget.IdleWakeupsP95PerSecond,
        ];

        Assert.AreEqual(expectedId, profile.Id);
        Assert.AreEqual(expectedName, profile.DisplayName);
        Assert.AreEqual(TimeSpan.FromMilliseconds(expectedIntervalMilliseconds), profile.SamplingInterval);
        Assert.IsTrue(profile.IsBuiltIn);
        CollectionAssert.AreEqual(expected, actual);
    }
}
