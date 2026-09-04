using System.Net;
using System.Net.Sockets;
using LlmInspector.Application;
using LlmInspector.Domain;
using LlmInspector.Resources.Windows;

namespace LlmInspector.WindowsTests;

[TestClass]
[DoNotParallelize]
public sealed class ResourceMonitoringTests
{
    [TestMethod]
    public void MonitorAppliesOnlyCanonicalValidatedPerformanceProfiles()
    {
        WindowsRequestResourceMonitor monitor = new(
            new ThrowingProbe(),
            new FixedProcessResolver(null));

        Assert.AreEqual(TimeSpan.FromSeconds(1), monitor.SamplingInterval);
        monitor.ApplyProfile(MonitoringPerformanceProfiles.Saver);
        Assert.AreEqual(TimeSpan.FromSeconds(2), monitor.SamplingInterval);
        monitor.ApplyProfile(MonitoringPerformanceProfiles.CreateCustom(250));
        Assert.AreEqual(TimeSpan.FromMilliseconds(250), monitor.SamplingInterval);

        _ = Assert.ThrowsExactly<ArgumentException>(() => monitor.ApplyProfile(
            MonitoringPerformanceProfiles.Balanced with { SamplingInterval = TimeSpan.FromMilliseconds(750) }));
        Assert.AreEqual(TimeSpan.FromMilliseconds(250), monitor.SamplingInterval);
    }

    [TestMethod]
    public async Task MonitorCorrelatesTimestampedHostProcessGpuDiskAndTrafficMetrics()
    {
        DateTimeOffset started = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        TechnicalProcessAssociation process = new(42, started.AddHours(-1), Id("ollama"), "test-listener-owner-v1");
        FakeProbe probe = new(
            Snapshot(started, idle: 100, kernel: 500, user: 500, processCpuMs: 100, read: 1_000, write: 2_000),
            Snapshot(started.AddSeconds(1), idle: 120, kernel: 600, user: 600, processCpuMs: 200, read: 1_500, write: 2_500));
        WindowsRequestResourceMonitor monitor = new(
            probe,
            new FixedProcessResolver(process),
            TimeSpan.FromMilliseconds(10));
        Guid requestId = Guid.NewGuid();
        Guid operationId = Guid.NewGuid();
        await using IRequestResourceSession session = monitor.Start(new RequestResourceContext(
            requestId,
            operationId,
            BackendKind.Ollama,
            new Uri("http://127.0.0.1:11434/"),
            started));

        await probe.FirstCapture.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await probe.SecondCapture.Task.WaitAsync(TimeSpan.FromSeconds(5));
        session.StageChanged(RequestStageValue.ProtocolObserved(
            RequestStage.ReasoningGeneration,
            "test-stage-v1"));
        session.AddClientToBackendBytes(123);
        session.AddBackendToClientBytes(456);
        IReadOnlyList<TechnicalResourceSampleRecord> samples = await session.CompleteAsync();

        Assert.IsGreaterThanOrEqualTo(6, samples.Count);
        TechnicalResourceSampleRecord sample = samples.Last(item => item.GpuDeviceId?.Value == "GPU-primary");
        Assert.AreEqual(requestId, sample.RequestId);
        Assert.AreEqual(operationId, sample.OperationId);
        Assert.AreEqual(RequestStage.ReasoningGeneration, sample.Stage?.Stage);
        Assert.IsTrue(samples.Any(item => item.CapturedAt == started.AddSeconds(1)));
        Assert.IsGreaterThanOrEqualTo(started.AddSeconds(1), sample.CapturedAt);
        Assert.AreEqual(MetricQuality.Calculated, sample.CpuPercent.Quality);
        Assert.AreEqual(90m, sample.CpuPercent.Value);
        Assert.AreEqual(70m, sample.MemoryPercent.Value);
        Assert.AreEqual(700UL, (ulong)sample.MemoryUsedBytes.Value!.Value);
        Assert.AreEqual(process, sample.RelatedProcess);
        Assert.AreEqual(MetricQuality.Calculated, sample.ProcessCpuPercent.Quality);
        Assert.AreEqual(500m, sample.DiskReadBytes.Value);
        Assert.AreEqual(500m, sample.DiskWriteBytes.Value);
        Assert.AreEqual(123m, sample.ClientToBackendBytes.Value);
        Assert.AreEqual(456m, sample.BackendToClientBytes.Value);
        Assert.AreEqual("GPU-primary", sample.GpuDeviceId?.Value);
        Assert.AreEqual("572.83", sample.GpuDriverVersion?.Value);
        Assert.AreEqual(50m, sample.GpuUtilizationPercent.Value);
        Assert.AreEqual(100m * 1_048_576m, sample.GpuVramUsedBytes.Value);
        Assert.AreEqual(80m, sample.GpuTemperatureCelsius.Value);
        Assert.AreEqual(125.5m, sample.GpuPowerWatts.Value);
        Assert.AreSame(sample, monitor.Latest);
        Assert.HasCount(2, monitor.LatestSamples);
        Assert.IsTrue(monitor.LatestSamples.Any(item => item.GpuDeviceId?.Value == "GPU-secondary"));
        TechnicalResourceSampleRecord secondary = monitor.LatestSamples.Single(
            item => item.GpuDeviceId?.Value == "GPU-secondary");
        Assert.AreEqual(75m, secondary.GpuUtilizationPercent.Value);
        Assert.AreEqual(MetricQuality.Unavailable, secondary.CpuPercent.Quality);
        Assert.AreEqual(MetricQuality.Unavailable, secondary.ClientToBackendBytes.Quality);
    }

    [TestMethod]
    public async Task ProbeFailureAndMissingAssociationRemainUnavailableWithoutLosingTrafficCorrelation()
    {
        WindowsRequestResourceMonitor monitor = new(
            new ThrowingProbe(),
            new FixedProcessResolver(null),
            TimeSpan.FromMinutes(1));
        Guid requestId = Guid.NewGuid();
        await using IRequestResourceSession session = monitor.Start(new RequestResourceContext(
            requestId,
            null,
            BackendKind.LlamaCpp,
            new Uri("http://127.0.0.1:8080/"),
            DateTimeOffset.UtcNow));
        session.AddClientToBackendBytes(10);
        session.AddBackendToClientBytes(20);

        IReadOnlyList<TechnicalResourceSampleRecord> samples = await session.CompleteAsync();
        TechnicalResourceSampleRecord sample = samples[^1];

        Assert.AreEqual(requestId, sample.RequestId);
        Assert.IsNull(sample.RelatedProcess);
        Assert.AreEqual(MetricQuality.Unavailable, sample.CpuPercent.Quality);
        Assert.AreEqual(MetricQuality.Unavailable, sample.GpuUtilizationPercent.Quality);
        Assert.AreEqual(10m, sample.ClientToBackendBytes.Value);
        Assert.AreEqual(20m, sample.BackendToClientBytes.Value);
    }

    [TestMethod]
    public async Task RemoteBackendNeverReceivesFabricatedLocalResourceAttribution()
    {
        CountingProbe probe = new();
        CountingProcessResolver resolver = new();
        WindowsRequestResourceMonitor monitor = new(probe, resolver, TimeSpan.FromMinutes(1));
        await using IRequestResourceSession session = monitor.Start(new RequestResourceContext(
            Guid.NewGuid(),
            null,
            BackendKind.Ollama,
            new Uri("https://backend.example-tailnet.ts.net/"),
            DateTimeOffset.UtcNow));
        session.AddClientToBackendBytes(100);
        session.AddBackendToClientBytes(200);

        IReadOnlyList<TechnicalResourceSampleRecord> samples = await session.CompleteAsync();
        TechnicalResourceSampleRecord sample = samples[^1];

        Assert.AreEqual(0, probe.CaptureCount);
        Assert.AreEqual(0, resolver.ResolveCount);
        Assert.IsNull(sample.RelatedProcess);
        Assert.IsNull(sample.GpuDeviceId);
        Assert.AreEqual(MetricQuality.Unavailable, sample.CpuPercent.Quality);
        Assert.AreEqual(MetricQuality.Unavailable, sample.MemoryPercent.Quality);
        Assert.AreEqual(MetricQuality.Unavailable, sample.ProcessCpuPercent.Quality);
        Assert.AreEqual(MetricQuality.Unavailable, sample.GpuUtilizationPercent.Quality);
        Assert.AreEqual(MetricSource.Inspector, sample.CpuPercent.Source);
        Assert.AreEqual(100m, sample.ClientToBackendBytes.Value);
        Assert.AreEqual(200m, sample.BackendToClientBytes.Value);
    }

    [TestMethod]
    public void NvidiaCsvReturnsEveryDistinctDeviceAndTreatsUnsupportedFieldsAsUnavailable()
    {
        IReadOnlyList<GpuResourceSnapshot> gpus = NvidiaSmiGpuProbe.ParseCsv(
            "1, GPU-secondary, 572.83, 80, 200, 300, 70, 90\n" +
            "0, GPU-primary, 572.83, 50, 100, 250, 65, N/A\n" +
            "2, GPU-primary, 572.83, 99, 999, 999, 99, 999\n");

        Assert.HasCount(2, gpus);
        Assert.AreEqual("GPU-primary", gpus[0].DeviceId.Value);
        Assert.AreEqual("GPU-secondary", gpus[1].DeviceId.Value);
        Assert.AreEqual("572.83", gpus[0].DriverVersion?.Value);
        Assert.AreEqual(50m, gpus[0].UtilizationPercent);
        Assert.AreEqual(100m, gpus[0].VramUsedMebibytes);
        Assert.AreEqual(250m, gpus[0].VramTotalMebibytes);
        Assert.AreEqual(65m, gpus[0].TemperatureCelsius);
        Assert.IsNull(gpus[0].PowerWatts);
        Assert.IsNull(AssertSingle(NvidiaSmiGpuProbe.ParseCsv(
            "0, GPU-primary, N/A, 50, 100, 250, 65, 90")).DriverVersion);
        Assert.IsEmpty(NvidiaSmiGpuProbe.ParseCsv("malformed content"));
    }

    [TestMethod]
    public async Task WindowsHostProbeReturnsPhysicalMemoryAndMonotonicSystemCounters()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows-only runtime evidence.");
        }

        WindowsResourceProbe probe = new(new NvidiaSmiGpuProbe(executablePath: "missing-nvidia-smi.exe"));
        WindowsResourceSnapshot first = await probe.CaptureAsync(null, CancellationToken.None);
        await Task.Delay(20);
        WindowsResourceSnapshot second = await probe.CaptureAsync(null, CancellationToken.None);

        Assert.IsGreaterThan(0UL, first.TotalPhysicalMemoryBytes);
        Assert.IsLessThan(first.TotalPhysicalMemoryBytes, first.AvailablePhysicalMemoryBytes);
        Assert.IsGreaterThanOrEqualTo(first.KernelTimeTicks, second.KernelTimeTicks);
        Assert.IsGreaterThanOrEqualTo(first.UserTimeTicks, second.UserTimeTicks);
    }

    [TestMethod]
    public void ListenerOwnerResolverUsesExactPidAndProcessStartTime()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        TechnicalProcessAssociation? association = new WindowsBackendProcessResolver()
            .Resolve(new Uri($"http://127.0.0.1:{port}/"));

        Assert.IsNotNull(association);
        Assert.AreEqual(Environment.ProcessId, association.ProcessId);
        Assert.IsGreaterThan(association.ProcessStartedAt, DateTimeOffset.UtcNow);
        StringAssert.Contains(association.SourceVersion, "listener-owner");
    }

    [TestMethod]
    public void ResourcePresenterMakesRequestStageAndUnavailableAssociationExplicit()
    {
        TechnicalResourceSampleRecord sample = new(
            Guid.NewGuid(),
            null,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Exact(25, MetricUnit.Percent, MetricSource.WindowsApi),
            Exact(50, MetricUnit.Percent, MetricSource.WindowsApi))
        {
            RequestId = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"),
            Stage = RequestStageValue.ProtocolObserved(RequestStage.PromptProcessing, "test-stage-v1"),
            ClientToBackendBytes = Exact(10, MetricUnit.Bytes, MetricSource.GatewayTraffic),
            BackendToClientBytes = Exact(20, MetricUnit.Bytes, MetricSource.GatewayTraffic),
            GpuDeviceId = Id("GPU-primary"),
            GpuUtilizationPercent = Exact(25, MetricUnit.Percent, MetricSource.NvidiaSmi),
        };
        TechnicalResourceSampleRecord secondary = sample with
        {
            SampleId = Guid.NewGuid(),
            CpuPercent = MetricValue.Unavailable(
                MetricUnit.Percent,
                MetricSource.WindowsApi,
                "resource-test-v1"),
            MemoryPercent = MetricValue.Unavailable(
                MetricUnit.Percent,
                MetricSource.WindowsApi,
                "resource-test-v1"),
            GpuDeviceId = Id("GPU-secondary"),
            GpuUtilizationPercent = Exact(75, MetricUnit.Percent, MetricSource.NvidiaSmi),
        };

        string text = App.ResourceTelemetryTextPresenter.FormatLatest([sample, secondary]);

        StringAssert.Contains(text, "0123456789abcdef0123456789abcdef");
        StringAssert.Contains(text, "stage=PromptProcessing");
        StringAssert.Contains(text, "Related process=unavailable");
        StringAssert.Contains(text, "10 B [exact]/20 B [exact]");
        StringAssert.Contains(text, "Detected GPU devices=2");
        StringAssert.Contains(text, "GPU-primary");
        StringAssert.Contains(text, "GPU-secondary");
        StringAssert.Contains(text, "workload attribution=unavailable");
        StringAssert.Contains(App.ResourceTelemetryTextPresenter.Format(null), "correlation: unavailable");
    }

    private static WindowsResourceSnapshot Snapshot(
        DateTimeOffset at,
        ulong idle,
        ulong kernel,
        ulong user,
        int processCpuMs,
        ulong read,
        ulong write) =>
        new(
            at,
            idle,
            kernel,
            user,
            1_000,
            at.Second == 0 ? 400UL : 300UL,
            new ProcessResourceSnapshot(TimeSpan.FromMilliseconds(processCpuMs), 250, read, write),
            [
                new GpuResourceSnapshot(Id("GPU-primary"), Id("572.83"), 50, 100, 250, 80, 125.5m),
                new GpuResourceSnapshot(Id("GPU-secondary"), Id("572.83"), 75, 200, 300, 70, 90),
            ]);

    private static MetricValue Exact(decimal value, MetricUnit unit, MetricSource source) =>
        MetricValue.Exact(value, unit, source, "resource-test-v1");

    private static TechnicalIdentifier Id(string value) =>
        TechnicalIdentifier.FromBackend(value) ?? throw new InvalidOperationException("Invalid fixture identifier.");

    private static T AssertSingle<T>(IReadOnlyList<T> items)
    {
        Assert.HasCount(1, items);
        return items[0];
    }

    private sealed class FakeProbe(params WindowsResourceSnapshot[] snapshots) : IWindowsResourceProbe
    {
        private int _index;

        public TaskCompletionSource FirstCapture { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource SecondCapture { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<WindowsResourceSnapshot> CaptureAsync(
            TechnicalProcessAssociation? process,
            CancellationToken cancellationToken)
        {
            int index = Interlocked.Increment(ref _index) - 1;
            FirstCapture.TrySetResult();
            if (index >= 1)
            {
                SecondCapture.TrySetResult();
            }
            return ValueTask.FromResult(snapshots[Math.Min(index, snapshots.Length - 1)]);
        }
    }

    private sealed class ThrowingProbe : IWindowsResourceProbe
    {
        public ValueTask<WindowsResourceSnapshot> CaptureAsync(
            TechnicalProcessAssociation? process,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<WindowsResourceSnapshot>(new InvalidOperationException("Synthetic failure."));
    }

    private sealed class FixedProcessResolver(TechnicalProcessAssociation? association) : IBackendProcessResolver
    {
        public TechnicalProcessAssociation? Resolve(Uri backendBaseAddress) => association;
    }

    private sealed class CountingProbe : IWindowsResourceProbe
    {
        public int CaptureCount { get; private set; }

        public ValueTask<WindowsResourceSnapshot> CaptureAsync(
            TechnicalProcessAssociation? process,
            CancellationToken cancellationToken)
        {
            CaptureCount++;
            return ValueTask.FromException<WindowsResourceSnapshot>(new InvalidOperationException());
        }
    }

    private sealed class CountingProcessResolver : IBackendProcessResolver
    {
        public int ResolveCount { get; private set; }

        public TechnicalProcessAssociation? Resolve(Uri backendBaseAddress)
        {
            ResolveCount++;
            return null;
        }
    }
}
