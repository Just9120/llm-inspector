namespace LlmInspector.Domain;

public enum PerformancePairOrder
{
    BaselineThenInspector,
    InspectorThenBaseline,
}

public enum PerformanceGateStatus
{
    Passed,
    Failed,
    Unavailable,
    NotApplicable,
}

public sealed record PerformanceBenchmarkProtocol(
    TimeSpan IdleWarmup,
    TimeSpan IdleMeasurement,
    IReadOnlyList<PerformancePairOrder> ActivePairOrders,
    bool ReliableDiscreteGpuSourceAvailable);

public sealed record PerformanceMeasurement(
    decimal? ActiveCpuMeanPercentagePoints,
    decimal? ActiveCpuP95PercentagePoints,
    decimal? ProcessPrivateBytesP95,
    decimal? ActiveRamGrowthPerThirtyMinutes,
    decimal? GpuUtilizationDeltaMeanPercentagePoints,
    decimal? GpuUtilizationDeltaP95PercentagePoints,
    decimal? DedicatedVramP95,
    decimal? DiskWritesPerMinute,
    decimal? ThroughputRegressionMedianPercent,
    decimal? ThroughputRegressionP95Percent,
    decimal? IdleCpuMeanPercent,
    decimal? IdleCpuP95Percent,
    decimal? IdleRamGrowthPerHour,
    decimal? IdleDiskWritesPerHour,
    decimal? IdleWakeupsMeanPerSecond,
    decimal? IdleWakeupsP95PerSecond);

public sealed record PerformanceGateFinding(
    string Metric,
    PerformanceGateStatus Status,
    decimal? Observed,
    decimal? Threshold,
    string Detail);

public sealed record PerformanceGateResult(
    MonitoringPerformanceProfileId Profile,
    IReadOnlyList<PerformanceGateFinding> Findings)
{
    public bool Passed => Findings.All(finding => finding.Status is
        PerformanceGateStatus.Passed or PerformanceGateStatus.NotApplicable);
}

public static class PerformanceGateEvaluator
{
    public const int MinimumPairedRepetitions = 5;
    public static readonly TimeSpan MinimumIdleWarmup = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan MinimumIdleMeasurement = TimeSpan.FromHours(1);

    public static PerformanceGateResult Evaluate(
        MonitoringPerformanceProfile profile,
        PerformanceBenchmarkProtocol protocol,
        PerformanceMeasurement measurement)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(protocol);
        ArgumentNullException.ThrowIfNull(measurement);
        ArgumentNullException.ThrowIfNull(protocol.ActivePairOrders);

        List<PerformanceGateFinding> findings = [];
        if (!profile.IsBuiltIn || profile.ReleaseBudget is null)
        {
            findings.Add(new PerformanceGateFinding(
                "release_profile",
                PerformanceGateStatus.Failed,
                null,
                null,
                "Only a canonical built-in profile can produce release performance evidence."));
            return new PerformanceGateResult(profile.Id, findings);
        }

        AddProtocolFinding(
            findings,
            "idle_warmup_minutes",
            (decimal)protocol.IdleWarmup.TotalMinutes,
            (decimal)MinimumIdleWarmup.TotalMinutes,
            protocol.IdleWarmup >= MinimumIdleWarmup,
            "Idle warm-up must be at least 10 minutes.");
        AddProtocolFinding(
            findings,
            "idle_measurement_minutes",
            (decimal)protocol.IdleMeasurement.TotalMinutes,
            (decimal)MinimumIdleMeasurement.TotalMinutes,
            protocol.IdleMeasurement >= MinimumIdleMeasurement,
            "Idle measurement must be at least 60 minutes.");
        AddProtocolFinding(
            findings,
            "paired_repetitions",
            protocol.ActivePairOrders.Count,
            MinimumPairedRepetitions,
            protocol.ActivePairOrders.Count >= MinimumPairedRepetitions,
            "At least five paired active repetitions are required.");
        bool alternating = protocol.ActivePairOrders.Count >= MinimumPairedRepetitions &&
            protocol.ActivePairOrders
                .Zip(protocol.ActivePairOrders.Skip(1), (current, next) => current != next)
                .All(value => value);
        findings.Add(new PerformanceGateFinding(
            "paired_order_alternation",
            alternating ? PerformanceGateStatus.Passed : PerformanceGateStatus.Failed,
            null,
            null,
            "Active repetitions must alternate AB/BA order."));

        MonitoringPerformanceBudget budget = profile.ReleaseBudget;
        AddMaximum(findings, "active_cpu_mean_pp", measurement.ActiveCpuMeanPercentagePoints, budget.ActiveCpuMeanPercentagePoints);
        AddMaximum(findings, "active_cpu_p95_pp", measurement.ActiveCpuP95PercentagePoints, budget.ActiveCpuP95PercentagePoints);
        AddMaximum(findings, "process_private_bytes_p95", measurement.ProcessPrivateBytesP95, budget.ProcessPrivateBytesP95);
        AddMaximum(findings, "active_ram_growth_30m", measurement.ActiveRamGrowthPerThirtyMinutes, budget.ActiveRamGrowthPerThirtyMinutes);
        AddGpuMaximum(
            findings,
            "gpu_utilization_delta_mean_pp",
            measurement.GpuUtilizationDeltaMeanPercentagePoints,
            budget.GpuUtilizationDeltaMeanPercentagePoints,
            protocol.ReliableDiscreteGpuSourceAvailable);
        AddGpuMaximum(
            findings,
            "gpu_utilization_delta_p95_pp",
            measurement.GpuUtilizationDeltaP95PercentagePoints,
            budget.GpuUtilizationDeltaP95PercentagePoints,
            protocol.ReliableDiscreteGpuSourceAvailable);
        AddGpuMaximum(
            findings,
            "dedicated_vram_p95",
            measurement.DedicatedVramP95,
            budget.DedicatedVramP95,
            protocol.ReliableDiscreteGpuSourceAvailable);
        AddMaximum(findings, "disk_writes_per_minute", measurement.DiskWritesPerMinute, budget.DiskWritesPerMinute);
        AddMaximum(findings, "throughput_regression_median_percent", measurement.ThroughputRegressionMedianPercent, budget.ThroughputRegressionMedianPercent);
        AddMaximum(findings, "throughput_regression_p95_percent", measurement.ThroughputRegressionP95Percent, budget.ThroughputRegressionP95Percent);
        AddMaximum(findings, "idle_cpu_mean_percent", measurement.IdleCpuMeanPercent, budget.IdleCpuMeanPercent);
        AddMaximum(findings, "idle_cpu_p95_percent", measurement.IdleCpuP95Percent, budget.IdleCpuP95Percent);
        AddMaximum(findings, "idle_ram_growth_per_hour", measurement.IdleRamGrowthPerHour, budget.IdleRamGrowthPerHour);
        AddMaximum(findings, "idle_disk_writes_per_hour", measurement.IdleDiskWritesPerHour, budget.IdleDiskWritesPerHour);
        AddMaximum(findings, "idle_wakeups_mean_per_second", measurement.IdleWakeupsMeanPerSecond, budget.IdleWakeupsMeanPerSecond);
        AddMaximum(findings, "idle_wakeups_p95_per_second", measurement.IdleWakeupsP95PerSecond, budget.IdleWakeupsP95PerSecond);
        return new PerformanceGateResult(profile.Id, findings);
    }

    private static void AddProtocolFinding(
        List<PerformanceGateFinding> findings,
        string metric,
        decimal observed,
        decimal required,
        bool passed,
        string detail) => findings.Add(new PerformanceGateFinding(
            metric,
            passed ? PerformanceGateStatus.Passed : PerformanceGateStatus.Failed,
            observed,
            required,
            detail));

    private static void AddGpuMaximum(
        List<PerformanceGateFinding> findings,
        string metric,
        decimal? observed,
        decimal maximum,
        bool required)
    {
        if (!required)
        {
            findings.Add(new PerformanceGateFinding(
                metric,
                PerformanceGateStatus.NotApplicable,
                observed,
                maximum,
                "A reliable discrete GPU source is unavailable; this gate is not applicable on this host."));
            return;
        }

        AddMaximum(findings, metric, observed, maximum);
    }

    private static void AddMaximum(
        List<PerformanceGateFinding> findings,
        string metric,
        decimal? observed,
        decimal maximum)
    {
        PerformanceGateStatus status;
        if (observed is null)
        {
            status = PerformanceGateStatus.Unavailable;
        }
        else
        {
            status = observed <= maximum
                ? PerformanceGateStatus.Passed
                : PerformanceGateStatus.Failed;
        }

        findings.Add(new PerformanceGateFinding(
            metric,
            status,
            observed,
            maximum,
            observed is null
                ? "A mandatory metric is unavailable and cannot pass."
                : "Observed value must be less than or equal to the profile budget."));
    }
}
