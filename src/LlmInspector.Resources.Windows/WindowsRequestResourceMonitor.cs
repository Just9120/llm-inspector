using LlmInspector.Application;
using LlmInspector.Domain;

namespace LlmInspector.Resources.Windows;

public sealed class WindowsRequestResourceMonitor : IRequestResourceMonitor, IResourceTelemetrySnapshotSource
{
    public const int MaximumSamplesPerRequest = 2_048;
    public static readonly TimeSpan DefaultSamplingInterval = TimeSpan.FromSeconds(1);

    private const string WindowsSourceVersion = "windows-resource-api-v1";
    private const string CpuDerivationVersion = "system-time-delta-v1";
    private const string ProcessCpuDerivationVersion = "process-time-wall-delta-v1";
    private const string CounterDeltaDerivationVersion = "cumulative-counter-delta-v1";
    private const string MemoryDerivationVersion = "total-minus-available-v1";
    private const string NvidiaSourceVersion = "nvidia-smi-query-v1";
    private const string MebibytesDerivationVersion = "mebibytes-to-bytes-v1";
    private const string TrafficSourceVersion = "gateway-relayed-byte-counter-v1";
    private const string RemoteUnavailableSourceVersion = "remote-backend-resource-unavailable-v1";

    private readonly IWindowsResourceProbe _probe;
    private readonly IBackendProcessResolver _processResolver;
    private long _samplingIntervalTicks;
    private TechnicalResourceSampleRecord? _latest;
    private TechnicalResourceSampleRecord[] _latestSamples = [];

    public WindowsRequestResourceMonitor(
        IWindowsResourceProbe? probe = null,
        IBackendProcessResolver? processResolver = null,
        TimeSpan? samplingInterval = null)
    {
        _probe = probe ?? new WindowsResourceProbe();
        _processResolver = processResolver ?? new WindowsBackendProcessResolver();
        TimeSpan initialInterval = samplingInterval ?? DefaultSamplingInterval;
        if (initialInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(samplingInterval));
        }

        _samplingIntervalTicks = initialInterval.Ticks;
    }

    public TimeSpan SamplingInterval => TimeSpan.FromTicks(Interlocked.Read(ref _samplingIntervalTicks));

    public void ApplyProfile(MonitoringPerformanceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        MonitoringPerformanceProfile resolved = MonitoringPerformanceProfiles.Resolve(
            profile.Id,
            checked((int)profile.SamplingInterval.TotalMilliseconds));
        if (resolved.SamplingInterval != profile.SamplingInterval)
        {
            throw new ArgumentException("The monitoring profile does not match its canonical sampling interval.", nameof(profile));
        }

        Interlocked.Exchange(ref _samplingIntervalTicks, profile.SamplingInterval.Ticks);
    }

    public TechnicalResourceSampleRecord? Latest => Volatile.Read(ref _latest);

    public IReadOnlyList<TechnicalResourceSampleRecord> LatestSamples =>
        Volatile.Read(ref _latestSamples);

    public IRequestResourceSession Start(RequestResourceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.RequestId == Guid.Empty)
        {
            throw new ArgumentException("A generated request identifier is required.", nameof(context));
        }

        bool collectLocalResources = IsLoopbackDestination(context.BackendBaseAddress);
        TechnicalProcessAssociation? association = null;
        if (collectLocalResources)
        {
            try
            {
                association = _processResolver.Resolve(context.BackendBaseAddress);
            }
            catch (Exception)
            {
                association = null;
            }
        }

        return new Session(this, context, association, collectLocalResources);
    }

    private void PublishLatest(TechnicalResourceSampleRecord[] samples)
    {
        Volatile.Write(ref _latestSamples, samples);
        Volatile.Write(ref _latest, samples.Length == 0 ? null : samples[0]);
    }

    private sealed class Session : IRequestResourceSession
    {
        private readonly WindowsRequestResourceMonitor _owner;
        private readonly RequestResourceContext _context;
        private readonly TechnicalProcessAssociation? _process;
        private readonly bool _collectLocalResources;
        private readonly TimeSpan _samplingInterval;
        private readonly CancellationTokenSource _stop = new();
        private readonly SemaphoreSlim _captureLock = new(1, 1);
        private readonly object _samplesLock = new();
        private readonly List<TechnicalResourceSampleRecord> _samples = [];
        private readonly Task _samplingTask;
        private TechnicalResourceSampleRecord[] _latestCapture = [];
        private RequestStageValue _stage = RequestStageValue.ProtocolObserved(
            RequestStage.QueueWaiting,
            "gateway-resource-stage-v1");
        private WindowsResourceSnapshot? _previous;
        private long _clientToBackendBytes;
        private long _backendToClientBytes;
        private int _droppedSamples;
        private int _completed;
        private int _lastStoredGroupStart = -1;
        private int _lastStoredGroupLength;

        public Session(
            WindowsRequestResourceMonitor owner,
            RequestResourceContext context,
            TechnicalProcessAssociation? process,
            bool collectLocalResources)
        {
            _owner = owner;
            _context = context;
            _process = process;
            _collectLocalResources = collectLocalResources;
            _samplingInterval = owner.SamplingInterval;
            _samplingTask = Task.Run(SampleLoopAsync);
        }

        public void StageChanged(RequestStageValue stage)
        {
            ArgumentNullException.ThrowIfNull(stage);
            Volatile.Write(ref _stage, stage);
        }

        public void AddClientToBackendBytes(int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            Interlocked.Add(ref _clientToBackendBytes, count);
        }

        public void AddBackendToClientBytes(int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            Interlocked.Add(ref _backendToClientBytes, count);
        }

        public async Task<IReadOnlyList<TechnicalResourceSampleRecord>> CompleteAsync(
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _completed, 1) == 0)
            {
                _stop.Cancel();
                await _samplingTask.ConfigureAwait(false);
                AddTerminalProjection();
            }

            lock (_samplesLock)
            {
                return _samples.ToArray();
            }
        }

        public async ValueTask DisposeAsync()
        {
            _ = await CompleteAsync().ConfigureAwait(false);
            _captureLock.Dispose();
            _stop.Dispose();
        }

        private async Task SampleLoopAsync()
        {
            try
            {
                await CaptureOnceAsync(_stop.Token).ConfigureAwait(false);
                using PeriodicTimer timer = new(_samplingInterval);
                while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
                {
                    await CaptureOnceAsync(_stop.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
            }
        }

        private void AddTerminalProjection()
        {
            TechnicalResourceSampleRecord[] projected;
            lock (_samplesLock)
            {
                TechnicalResourceSampleRecord[] previous = _latestCapture;
                projected = previous.Length == 0
                    ? CreateSamples(null)
                    : previous.Select((sample, index) => sample with
                    {
                        SampleId = Guid.NewGuid(),
                        CapturedAt = DateTimeOffset.UtcNow,
                        Stage = Volatile.Read(ref _stage),
                        DroppedSampleCount = Volatile.Read(ref _droppedSamples),
                        ClientToBackendBytes = index == 0
                            ? ExactTraffic(Interlocked.Read(ref _clientToBackendBytes))
                            : Unavailable(MetricUnit.Bytes, TrafficSourceVersion, MetricSource.GatewayTraffic),
                        BackendToClientBytes = index == 0
                            ? ExactTraffic(Interlocked.Read(ref _backendToClientBytes))
                            : Unavailable(MetricUnit.Bytes, TrafficSourceVersion, MetricSource.GatewayTraffic),
                    }).ToArray();
                if (_samples.Count + projected.Length <= MaximumSamplesPerRequest)
                {
                    _lastStoredGroupStart = _samples.Count;
                    _lastStoredGroupLength = projected.Length;
                    _samples.AddRange(projected);
                }
                else
                {
                    int removeStart = _lastStoredGroupStart >= 0 &&
                                      _lastStoredGroupStart + _lastStoredGroupLength == _samples.Count
                        ? _lastStoredGroupStart
                        : Math.Max(0, _samples.Count - Math.Min(_samples.Count, projected.Length));
                    int removed = _samples.Count - removeStart;
                    _samples.RemoveRange(removeStart, removed);
                    Interlocked.Add(ref _droppedSamples, removed);
                    projected = projected
                        .Select(sample => sample with
                        {
                            DroppedSampleCount = Volatile.Read(ref _droppedSamples),
                        })
                        .ToArray();
                    _lastStoredGroupStart = _samples.Count;
                    _lastStoredGroupLength = projected.Length;
                    _samples.AddRange(projected);
                }

                _latestCapture = projected;
            }

            _owner.PublishLatest(projected);
        }

        private async Task CaptureOnceAsync(CancellationToken cancellationToken)
        {
            await _captureLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                WindowsResourceSnapshot? current = null;
                if (_collectLocalResources)
                {
                    try
                    {
                        current = await _owner._probe.CaptureAsync(_process, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                    }
                }

                TechnicalResourceSampleRecord[] samples = CreateSamples(current);
                lock (_samplesLock)
                {
                    if (_samples.Count + samples.Length <= MaximumSamplesPerRequest)
                    {
                        _lastStoredGroupStart = _samples.Count;
                        _lastStoredGroupLength = samples.Length;
                        _samples.AddRange(samples);
                    }
                    else
                    {
                        Interlocked.Add(ref _droppedSamples, samples.Length);
                        samples = samples
                            .Select(sample => sample with
                            {
                                DroppedSampleCount = Volatile.Read(ref _droppedSamples),
                            })
                            .ToArray();
                    }

                    _latestCapture = samples;
                }

                _owner.PublishLatest(samples);
                if (current is not null)
                {
                    _previous = current;
                }
            }
            finally
            {
                _captureLock.Release();
            }
        }

        private TechnicalResourceSampleRecord[] CreateSamples(WindowsResourceSnapshot? current)
        {
            IReadOnlyList<GpuResourceSnapshot> gpus = current?.Gpus ?? [];
            return gpus.Count == 0
                ? [CreateSample(current, null, includeHostMetrics: true)]
                : gpus.Select((gpu, index) =>
                    CreateSample(current, gpu, includeHostMetrics: index == 0)).ToArray();
        }

        private TechnicalResourceSampleRecord CreateSample(
            WindowsResourceSnapshot? current,
            GpuResourceSnapshot? gpu,
            bool includeHostMetrics)
        {
            string unavailableSource = _collectLocalResources
                ? WindowsSourceVersion
                : RemoteUnavailableSourceVersion;
            MetricSource unavailableMetricSource = _collectLocalResources
                ? MetricSource.WindowsApi
                : MetricSource.Inspector;
            MetricValue unavailablePercent = Unavailable(
                MetricUnit.Percent,
                unavailableSource,
                unavailableMetricSource);
            MetricValue unavailableBytes = Unavailable(
                MetricUnit.Bytes,
                unavailableSource,
                unavailableMetricSource);
            MetricValue cpu = unavailablePercent;
            MetricValue memory = unavailablePercent;
            MetricValue memoryUsed = unavailableBytes;
            MetricValue processCpu = unavailablePercent;
            MetricValue processMemory = unavailableBytes;
            MetricValue diskRead = unavailableBytes;
            MetricValue diskWrite = unavailableBytes;
            TechnicalIdentifier? gpuDevice = gpu?.DeviceId;
            MetricValue gpuUtilization = Unavailable(MetricUnit.Percent, NvidiaSourceVersion, MetricSource.NvidiaSmi);
            MetricValue gpuUsed = Unavailable(MetricUnit.Bytes, NvidiaSourceVersion, MetricSource.NvidiaSmi);
            MetricValue gpuTotal = Unavailable(MetricUnit.Bytes, NvidiaSourceVersion, MetricSource.NvidiaSmi);
            MetricValue gpuTemperature = Unavailable(MetricUnit.Celsius, NvidiaSourceVersion, MetricSource.NvidiaSmi);
            MetricValue gpuPower = Unavailable(MetricUnit.Watts, NvidiaSourceVersion, MetricSource.NvidiaSmi);
            DateTimeOffset capturedAt = current?.CapturedAt ?? DateTimeOffset.UtcNow;

            if (includeHostMetrics &&
                current is not null &&
                current.TotalPhysicalMemoryBytes > 0 &&
                current.AvailablePhysicalMemoryBytes <= current.TotalPhysicalMemoryBytes)
            {
                memory = ExactPercent(100m * (current.TotalPhysicalMemoryBytes - current.AvailablePhysicalMemoryBytes) /
                    current.TotalPhysicalMemoryBytes);
                memoryUsed = MetricValue.Calculated(
                    current.TotalPhysicalMemoryBytes - current.AvailablePhysicalMemoryBytes,
                    MetricUnit.Bytes,
                    MetricSource.WindowsApi,
                    WindowsSourceVersion,
                    MemoryDerivationVersion);
                processMemory = current.Process is null
                    ? unavailableBytes
                    : MetricValue.Exact(
                        current.Process.WorkingSetBytes,
                        MetricUnit.Bytes,
                        MetricSource.WindowsApi,
                        WindowsSourceVersion);

                if (_previous is WindowsResourceSnapshot previous)
                {
                    cpu = CalculateSystemCpu(previous, current);
                    processCpu = CalculateProcessCpu(previous, current);
                    diskRead = CalculateCounterDelta(
                        previous.Process?.ReadTransferBytes,
                        current.Process?.ReadTransferBytes);
                    diskWrite = CalculateCounterDelta(
                        previous.Process?.WriteTransferBytes,
                        current.Process?.WriteTransferBytes);
                }

            }

            if (gpu is not null)
            {
                gpuUtilization = OptionalExact(gpu.UtilizationPercent, MetricUnit.Percent);
                gpuUsed = OptionalMebibytes(gpu.VramUsedMebibytes);
                gpuTotal = OptionalMebibytes(gpu.VramTotalMebibytes);
                gpuTemperature = OptionalExact(gpu.TemperatureCelsius, MetricUnit.Celsius);
                gpuPower = OptionalExact(gpu.PowerWatts, MetricUnit.Watts);
            }

            return new TechnicalResourceSampleRecord(
                Guid.NewGuid(),
                _context.OperationId,
                capturedAt,
                cpu,
                memory)
            {
                RequestId = _context.RequestId,
                Stage = Volatile.Read(ref _stage),
                RelatedProcess = includeHostMetrics ? _process : null,
                GpuDeviceId = gpuDevice,
                GpuDriverVersion = gpu?.DriverVersion,
                DroppedSampleCount = Volatile.Read(ref _droppedSamples),
                MemoryUsedBytes = memoryUsed,
                ProcessCpuPercent = processCpu,
                ProcessMemoryBytes = processMemory,
                DiskReadBytes = diskRead,
                DiskWriteBytes = diskWrite,
                ClientToBackendBytes = includeHostMetrics
                    ? ExactTraffic(Interlocked.Read(ref _clientToBackendBytes))
                    : Unavailable(MetricUnit.Bytes, TrafficSourceVersion, MetricSource.GatewayTraffic),
                BackendToClientBytes = includeHostMetrics
                    ? ExactTraffic(Interlocked.Read(ref _backendToClientBytes))
                    : Unavailable(MetricUnit.Bytes, TrafficSourceVersion, MetricSource.GatewayTraffic),
                GpuUtilizationPercent = gpuUtilization,
                GpuVramUsedBytes = gpuUsed,
                GpuVramTotalBytes = gpuTotal,
                GpuTemperatureCelsius = gpuTemperature,
                GpuPowerWatts = gpuPower,
            };
        }

        private static MetricValue CalculateSystemCpu(
            WindowsResourceSnapshot previous,
            WindowsResourceSnapshot current)
        {
            ulong previousTotal = previous.KernelTimeTicks + previous.UserTimeTicks;
            ulong currentTotal = current.KernelTimeTicks + current.UserTimeTicks;
            if (currentTotal <= previousTotal || current.IdleTimeTicks < previous.IdleTimeTicks)
            {
                return Unavailable(MetricUnit.Percent, WindowsSourceVersion);
            }

            ulong totalDelta = currentTotal - previousTotal;
            ulong idleDelta = current.IdleTimeTicks - previous.IdleTimeTicks;
            if (idleDelta > totalDelta)
            {
                return Unavailable(MetricUnit.Percent, WindowsSourceVersion);
            }

            decimal value = 100m * (totalDelta - idleDelta) / totalDelta;
            return MetricValue.Calculated(
                value,
                MetricUnit.Percent,
                MetricSource.WindowsApi,
                WindowsSourceVersion,
                CpuDerivationVersion);
        }

        private static MetricValue CalculateProcessCpu(
            WindowsResourceSnapshot previous,
            WindowsResourceSnapshot current)
        {
            if (previous.Process is null || current.Process is null || current.CapturedAt <= previous.CapturedAt)
            {
                return Unavailable(MetricUnit.Percent, WindowsSourceVersion);
            }

            TimeSpan processor = current.Process.TotalProcessorTime - previous.Process.TotalProcessorTime;
            TimeSpan wall = current.CapturedAt - previous.CapturedAt;
            if (processor < TimeSpan.Zero || wall <= TimeSpan.Zero)
            {
                return Unavailable(MetricUnit.Percent, WindowsSourceVersion);
            }

            decimal value = Math.Min(
                100m,
                100m * (decimal)processor.TotalMilliseconds /
                ((decimal)wall.TotalMilliseconds * Environment.ProcessorCount));
            return MetricValue.Calculated(
                value,
                MetricUnit.Percent,
                MetricSource.WindowsApi,
                WindowsSourceVersion,
                ProcessCpuDerivationVersion);
        }

        private static MetricValue CalculateCounterDelta(ulong? previous, ulong? current)
        {
            if (previous is null || current is null || current < previous)
            {
                return Unavailable(MetricUnit.Bytes, WindowsSourceVersion);
            }

            return MetricValue.Calculated(
                current.Value - previous.Value,
                MetricUnit.Bytes,
                MetricSource.WindowsApi,
                WindowsSourceVersion,
                CounterDeltaDerivationVersion);
        }

        private static MetricValue ExactPercent(decimal value) =>
            MetricValue.Exact(
                Math.Clamp(value, 0m, 100m),
                MetricUnit.Percent,
                MetricSource.WindowsApi,
                WindowsSourceVersion);

        private static MetricValue ExactTraffic(long value) =>
            MetricValue.Exact(
                value,
                MetricUnit.Bytes,
                MetricSource.GatewayTraffic,
                TrafficSourceVersion);

        private static MetricValue OptionalExact(decimal? value, MetricUnit unit) => value is decimal exact
            ? MetricValue.Exact(exact, unit, MetricSource.NvidiaSmi, NvidiaSourceVersion)
            : Unavailable(unit, NvidiaSourceVersion, MetricSource.NvidiaSmi);

        private static MetricValue OptionalMebibytes(decimal? value) => value is decimal exact
            ? MetricValue.Calculated(
                exact * 1_048_576m,
                MetricUnit.Bytes,
                MetricSource.NvidiaSmi,
                NvidiaSourceVersion,
                MebibytesDerivationVersion)
            : Unavailable(MetricUnit.Bytes, NvidiaSourceVersion, MetricSource.NvidiaSmi);

        private static MetricValue Unavailable(
            MetricUnit unit,
            string sourceVersion,
            MetricSource source = MetricSource.WindowsApi) =>
            MetricValue.Unavailable(unit, source, sourceVersion);
    }

    private static bool IsLoopbackDestination(Uri address)
    {
        string host = address.Host.Trim('[', ']');
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            (System.Net.IPAddress.TryParse(host, out System.Net.IPAddress? ipAddress) &&
             System.Net.IPAddress.IsLoopback(ipAddress));
    }
}
