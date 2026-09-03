namespace LlmInspector.Domain;

public enum MonitoringPerformanceProfileId
{
    Saver,
    Balanced,
    Detailed,
    Custom,
}

public sealed record MonitoringPerformanceBudget(
    decimal ActiveCpuMeanPercentagePoints,
    decimal ActiveCpuP95PercentagePoints,
    long ProcessPrivateBytesP95,
    long ActiveRamGrowthPerThirtyMinutes,
    decimal GpuUtilizationDeltaMeanPercentagePoints,
    decimal GpuUtilizationDeltaP95PercentagePoints,
    long DedicatedVramP95,
    long DiskWritesPerMinute,
    decimal ThroughputRegressionMedianPercent,
    decimal ThroughputRegressionP95Percent,
    decimal IdleCpuMeanPercent,
    decimal IdleCpuP95Percent,
    long IdleRamGrowthPerHour,
    long IdleDiskWritesPerHour,
    decimal IdleWakeupsMeanPerSecond,
    decimal IdleWakeupsP95PerSecond);

public sealed record MonitoringPerformanceProfile(
    MonitoringPerformanceProfileId Id,
    string DisplayName,
    TimeSpan SamplingInterval,
    MonitoringPerformanceBudget? ReleaseBudget)
{
    public bool IsBuiltIn => Id != MonitoringPerformanceProfileId.Custom;
}

public static class MonitoringPerformanceProfiles
{
    public const string ContractVersion = "performance-profiles-v1";
    public const int MinimumCustomSamplingMilliseconds = 250;
    public const int MaximumCustomSamplingMilliseconds = 10_000;

    private const long Mebibyte = 1_048_576;

    public static MonitoringPerformanceProfile Saver { get; } = new(
        MonitoringPerformanceProfileId.Saver,
        "Бережный",
        TimeSpan.FromSeconds(2),
        new MonitoringPerformanceBudget(
            1.5m,
            4m,
            192 * Mebibyte,
            16 * Mebibyte,
            1m,
            3m,
            128 * Mebibyte,
            1 * Mebibyte,
            3m,
            5m,
            0.25m,
            1m,
            8 * Mebibyte,
            Mebibyte / 4,
            2m,
            8m));

    public static MonitoringPerformanceProfile Balanced { get; } = new(
        MonitoringPerformanceProfileId.Balanced,
        "Сбалансированный",
        TimeSpan.FromSeconds(1),
        new MonitoringPerformanceBudget(
            3m,
            8m,
            256 * Mebibyte,
            32 * Mebibyte,
            2m,
            5m,
            192 * Mebibyte,
            2 * Mebibyte,
            5m,
            10m,
            0.5m,
            2m,
            16 * Mebibyte,
            1 * Mebibyte,
            5m,
            15m));

    public static MonitoringPerformanceProfile Detailed { get; } = new(
        MonitoringPerformanceProfileId.Detailed,
        "Детальный",
        TimeSpan.FromMilliseconds(500),
        new MonitoringPerformanceBudget(
            5m,
            12m,
            384 * Mebibyte,
            64 * Mebibyte,
            3m,
            8m,
            256 * Mebibyte,
            5 * Mebibyte,
            8m,
            15m,
            1m,
            4m,
            32 * Mebibyte,
            5 * Mebibyte,
            15m,
            30m));

    public static IReadOnlyList<MonitoringPerformanceProfile> BuiltIn { get; } =
        [Saver, Balanced, Detailed];

    public static MonitoringPerformanceProfile Resolve(
        MonitoringPerformanceProfileId id,
        int customSamplingMilliseconds = 1_000) => id switch
        {
            MonitoringPerformanceProfileId.Saver => Saver,
            MonitoringPerformanceProfileId.Balanced => Balanced,
            MonitoringPerformanceProfileId.Detailed => Detailed,
            MonitoringPerformanceProfileId.Custom => CreateCustom(customSamplingMilliseconds),
            _ => throw new ArgumentOutOfRangeException(nameof(id)),
        };

    public static MonitoringPerformanceProfile CreateCustom(int samplingMilliseconds)
    {
        if (samplingMilliseconds is < MinimumCustomSamplingMilliseconds or > MaximumCustomSamplingMilliseconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(samplingMilliseconds),
                $"Custom sampling interval must be {MinimumCustomSamplingMilliseconds}..{MaximumCustomSamplingMilliseconds} ms.");
        }

        return new MonitoringPerformanceProfile(
            MonitoringPerformanceProfileId.Custom,
            "Свой профиль",
            TimeSpan.FromMilliseconds(samplingMilliseconds),
            ReleaseBudget: null);
    }
}
