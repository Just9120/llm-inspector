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

        Assert.IsGreaterThanOrEqualTo(3, samples.Count);
        TechnicalResourceSampleRecord sample = samples[^1];
        Assert.AreEqual(requestId, sample.RequestId);
        Assert.AreEqual(operationId, sample.OperationId);
        Assert.AreEqual(RequestStage.ReasoningGeneration, sample.Stage?.Stage);
        Assert.AreEqual(started.AddSeconds(1), samples[1].CapturedAt);
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
    public void NvidiaCsvSelectsLowestIndexAndTreatsUnsupportedFieldsAsUnavailable()
    {
        GpuResourceSnapshot? gpu = NvidiaSmiGpuProbe.ParseCsv(
            "1, GPU-secondary, 572.83, 80, 200, 300, 70, 90\n" +
            "0, GPU-primary, 572.83, 50, 100, 250, 65, N/A\n");

        Assert.IsNotNull(gpu);
        Assert.AreEqual("GPU-primary", gpu.DeviceId.Value);
        Assert.AreEqual("572.83", gpu.DriverVersion?.Value);
        Assert.AreEqual(50m, gpu.UtilizationPercent);
        Assert.AreEqual(100m, gpu.VramUsedMebibytes);
        Assert.AreEqual(250m, gpu.VramTotalMebibytes);
        Assert.AreEqual(65m, gpu.TemperatureCelsius);
        Assert.IsNull(gpu.PowerWatts);
        Assert.IsNull(NvidiaSmiGpuProbe.ParseCsv("0, GPU-primary, N/A, 50, 100, 250, 65, 90")?.DriverVersion);
        Assert.IsNull(NvidiaSmiGpuProbe.ParseCsv("malformed content"));
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
        };

        string text = App.ResourceTelemetryTextPresenter.Format(sample);

        StringAssert.Contains(text, "0123456789abcdef0123456789abcdef");
        StringAssert.Contains(text, "stage=PromptProcessing");
        StringAssert.Contains(text, "Related process=unavailable");
        StringAssert.Contains(text, "10 B [exact]/20 B [exact]");
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
            new GpuResourceSnapshot(Id("GPU-primary"), Id("572.83"), 50, 100, 250, 80, 125.5m));

    private static MetricValue Exact(decimal value, MetricUnit unit, MetricSource source) =>
        MetricValue.Exact(value, unit, source, "resource-test-v1");

    private static TechnicalIdentifier Id(string value) =>
        TechnicalIdentifier.FromBackend(value) ?? throw new InvalidOperationException("Invalid fixture identifier.");

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
}
