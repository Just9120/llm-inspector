using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LlmInspector.Application;
using LlmInspector.Domain;

namespace LlmInspector.Diagnostics;

public enum DiagnosticSnapshotScope
{
    TimeRange,
    Operation,
}

public enum DiagnosticFactAvailability
{
    Available,
    Unavailable,
}

public sealed record DiagnosticTechnicalFact(
    DiagnosticFactAvailability Availability,
    string? Value,
    string SourceVersion)
{
    public static DiagnosticTechnicalFact Available(string value, string sourceVersion) =>
        new(DiagnosticFactAvailability.Available, value, sourceVersion);

    public static DiagnosticTechnicalFact Unavailable(string sourceVersion) =>
        new(DiagnosticFactAvailability.Unavailable, null, sourceVersion);

    public static void Validate(DiagnosticTechnicalFact fact)
    {
        ArgumentNullException.ThrowIfNull(fact);
        if (string.IsNullOrWhiteSpace(fact.SourceVersion) || fact.SourceVersion.Length > 128 ||
            fact.SourceVersion.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
        {
            throw new ArgumentException("A bounded technical fact source version is required.", nameof(fact));
        }

        bool hasValidValue = !string.IsNullOrWhiteSpace(fact.Value) &&
                             fact.Value.Length <= 256 &&
                             !fact.Value.Any(char.IsControl);
        if ((fact.Availability == DiagnosticFactAvailability.Available) != hasValidValue)
        {
            throw new ArgumentException(
                "Available technical facts require a bounded value; unavailable facts cannot carry a value.",
                nameof(fact));
        }
    }
}

public sealed record DiagnosticEnvironmentFacts(
    DiagnosticTechnicalFact OperatingSystemVersion,
    DiagnosticTechnicalFact GpuDriverVersion,
    DiagnosticTechnicalFact BackendVersion,
    DiagnosticTechnicalFact ClientVersion,
    DiagnosticTechnicalFact ApplicationVersion,
    DiagnosticTechnicalFact FrameworkVersion)
{
    private const string LocalRuntimeSource = "local-runtime-facts-v1";
    private const string NotCollectedSource = "not-collected-v1";

    public static DiagnosticEnvironmentFacts CaptureLocal()
    {
        string? applicationVersion = typeof(DiagnosticSnapshotService).Assembly.GetName().Version?
            .ToString(fieldCount: 3);
        return new DiagnosticEnvironmentFacts(
            DiagnosticTechnicalFact.Available(RuntimeInformation.OSDescription, LocalRuntimeSource),
            DiagnosticTechnicalFact.Unavailable(NotCollectedSource),
            DiagnosticTechnicalFact.Unavailable(NotCollectedSource),
            DiagnosticTechnicalFact.Unavailable(NotCollectedSource),
            string.IsNullOrWhiteSpace(applicationVersion)
                ? DiagnosticTechnicalFact.Unavailable(NotCollectedSource)
                : DiagnosticTechnicalFact.Available(applicationVersion, LocalRuntimeSource),
            DiagnosticTechnicalFact.Available(RuntimeInformation.FrameworkDescription, LocalRuntimeSource));
    }

    public static void Validate(DiagnosticEnvironmentFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        DiagnosticTechnicalFact.Validate(facts.OperatingSystemVersion);
        DiagnosticTechnicalFact.Validate(facts.GpuDriverVersion);
        DiagnosticTechnicalFact.Validate(facts.BackendVersion);
        DiagnosticTechnicalFact.Validate(facts.ClientVersion);
        DiagnosticTechnicalFact.Validate(facts.ApplicationVersion);
        DiagnosticTechnicalFact.Validate(facts.FrameworkVersion);
    }
}

public sealed record DiagnosticSnapshotSelection(
    DiagnosticSnapshotScope Scope,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    Guid? OperationId)
{
    public static DiagnosticSnapshotSelection ForTimeRange(DateTimeOffset fromUtc, DateTimeOffset toUtc) =>
        new(DiagnosticSnapshotScope.TimeRange, fromUtc.ToUniversalTime(), toUtc.ToUniversalTime(), null);

    public static DiagnosticSnapshotSelection ForOperation(Guid operationId) =>
        new(DiagnosticSnapshotScope.Operation, null, null, operationId);

    public static void Validate(DiagnosticSnapshotSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        bool valid = selection.Scope switch
        {
            DiagnosticSnapshotScope.TimeRange =>
                selection.FromUtc is not null &&
                selection.ToUtc is not null &&
                selection.FromUtc <= selection.ToUtc &&
                selection.OperationId is null,
            DiagnosticSnapshotScope.Operation =>
                selection.FromUtc is null &&
                selection.ToUtc is null &&
                selection.OperationId is Guid operationId && operationId != Guid.Empty,
            _ => false,
        };
        if (!valid)
        {
            throw new ArgumentException("Diagnostic snapshot selection is invalid.", nameof(selection));
        }
    }
}

public sealed record DiagnosticMetricEntry(
    string Key,
    decimal? Value,
    MetricUnit Unit,
    MetricQuality Quality,
    MetricSource Source,
    string SourceVersion,
    string? DerivationVersion);

public sealed record DiagnosticRequestEntry(
    Guid RequestId,
    Guid? OperationId,
    DateTimeOffset StartedAtUtc,
    int? HttpStatusCode,
    ProxyOutcome Outcome,
    HistoryErrorType ErrorType,
    ClientKind Client,
    BackendKind Backend,
    DiagnosticTechnicalFact Model,
    ModelLoadDisposition ModelLoadDisposition,
    IReadOnlyList<DiagnosticMetricEntry> RuntimeMetrics);

public sealed record DiagnosticResourceSampleEntry(
    Guid SampleId,
    Guid? RequestId,
    Guid? OperationId,
    DateTimeOffset CapturedAtUtc,
    string Stage,
    string StageEvidence,
    string? GpuDeviceId,
    int DroppedSampleCount,
    IReadOnlyList<DiagnosticMetricEntry> SystemMetrics);

public sealed record DiagnosticSnapshotTruncation(
    bool RequestsTruncated,
    bool ResourceSamplesTruncated);

public sealed record DiagnosticSnapshotDocument(
    string SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    DiagnosticSnapshotSelection Selection,
    DiagnosticEnvironmentFacts Environment,
    IReadOnlyList<DiagnosticRequestEntry> Requests,
    IReadOnlyList<DiagnosticResourceSampleEntry> ResourceSamples,
    DiagnosticSnapshotTruncation Truncation);

public sealed record DiagnosticSnapshotArtifact(
    DiagnosticSnapshotDocument Document,
    string Json,
    string Sha256);

public static class DiagnosticSnapshotContract
{
    public const string SchemaVersion1 = "diagnostic-snapshot-v1";
    public const int MaximumRequests = TechnicalHistorySnapshotPolicy.MaximumRequests;
    public const int MaximumResourceSamples = TechnicalHistorySnapshotPolicy.MaximumResourceSamples;

    public static IReadOnlyList<string> RootFieldAllowlist { get; } = Array.AsReadOnly(
        new[]
        {
            "schema_version",
            "generated_at_utc",
            "selection",
            "environment",
            "requests",
            "resource_samples",
            "truncation",
        });

    public static IReadOnlyList<string> SelectionFieldAllowlist { get; } = Array.AsReadOnly(
        new[]
        {
            "scope",
            "from_utc",
            "to_utc",
            "operation_id",
        });

    public static IReadOnlyList<string> EnvironmentFieldAllowlist { get; } = Array.AsReadOnly(
        new[]
        {
            "operating_system_version",
            "gpu_driver_version",
            "backend_version",
            "client_version",
            "application_version",
            "framework_version",
        });

    public static IReadOnlyList<string> TechnicalFactFieldAllowlist { get; } = Array.AsReadOnly(
        new[]
        {
            "availability",
            "value",
            "source_version",
        });

    public static IReadOnlyList<string> RequestFieldAllowlist { get; } = Array.AsReadOnly(
        new[]
        {
            "request_id",
            "operation_id",
            "started_at_utc",
            "http_status_code",
            "outcome",
            "error_type",
            "client",
            "backend",
            "model",
            "model_load_disposition",
            "runtime_metrics",
        });

    public static IReadOnlyList<string> ResourceSampleFieldAllowlist { get; } = Array.AsReadOnly(
        new[]
        {
            "sample_id",
            "request_id",
            "operation_id",
            "captured_at_utc",
            "stage",
            "stage_evidence",
            "gpu_device_id",
            "dropped_sample_count",
            "system_metrics",
        });

    public static IReadOnlyList<string> MetricFieldAllowlist { get; } = Array.AsReadOnly(
        new[]
        {
            "key",
            "value",
            "unit",
            "quality",
            "source",
            "source_version",
            "derivation_version",
        });

    public static IReadOnlyList<string> TruncationFieldAllowlist { get; } = Array.AsReadOnly(
        new[]
        {
            "requests_truncated",
            "resource_samples_truncated",
        });
}

public sealed class DiagnosticSnapshotService
{
    private const string UnavailableModelSource = "history-model-unavailable-v1";
    private const string HistoryModelSource = "history-model-identifier-v1";
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private readonly ITechnicalHistoryStore _history;
    private readonly TimeProvider _timeProvider;

    public DiagnosticSnapshotService(
        ITechnicalHistoryStore history,
        TimeProvider? timeProvider = null)
    {
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<DiagnosticSnapshotArtifact> CreateAsync(
        DiagnosticSnapshotSelection selection,
        DiagnosticEnvironmentFacts environment,
        CancellationToken cancellationToken = default)
    {
        DiagnosticSnapshotSelection.Validate(selection);
        DiagnosticEnvironmentFacts.Validate(environment);
        HistoryFilter filter = selection.Scope == DiagnosticSnapshotScope.TimeRange
            ? new HistoryFilter(From: selection.FromUtc, To: selection.ToUtc)
            : new HistoryFilter();
        TechnicalHistorySlice slice = await _history.QuerySnapshotSliceAsync(
            filter,
            selection.OperationId,
            cancellationToken).ConfigureAwait(false);
        if (slice.Requests.Count > DiagnosticSnapshotContract.MaximumRequests ||
            slice.ResourceSamples.Count > DiagnosticSnapshotContract.MaximumResourceSamples)
        {
            throw new InvalidDataException("The snapshot source exceeded its bounded contract.");
        }

        DiagnosticSnapshotDocument document = new(
            DiagnosticSnapshotContract.SchemaVersion1,
            _timeProvider.GetUtcNow(),
            selection,
            environment,
            slice.Requests
                .OrderBy(request => request.StartedAt)
                .ThenBy(request => request.RequestId)
                .Select(CreateRequest)
                .ToArray(),
            slice.ResourceSamples
                .OrderBy(sample => sample.CapturedAt)
                .ThenBy(sample => sample.SampleId)
                .Select(CreateResourceSample)
                .ToArray(),
            new DiagnosticSnapshotTruncation(
                slice.RequestsTruncated,
                slice.ResourceSamplesTruncated));
        string json = JsonSerializer.Serialize(document, SerializerOptions);
        string hash = Convert.ToHexString(SHA256.HashData(Utf8WithoutBom.GetBytes(json)))
            .ToLowerInvariant();
        return new DiagnosticSnapshotArtifact(document, json, hash);
    }

    public static async Task SaveAsync(
        DiagnosticSnapshotArtifact artifact,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        await LocalJsonArtifactWriter.SaveAsync(
            artifact.Json,
            path,
            "Diagnostic snapshot",
            cancellationToken).ConfigureAwait(false);
    }

    private static DiagnosticRequestEntry CreateRequest(RequestHistoryItem request) => new(
        request.RequestId,
        request.OperationId,
        request.StartedAt.ToUniversalTime(),
        request.HttpStatusCode,
        request.Outcome,
        request.ErrorType,
        request.Client,
        request.Backend,
        request.Model is null
            ? DiagnosticTechnicalFact.Unavailable(UnavailableModelSource)
            : DiagnosticTechnicalFact.Available(request.Model.Value, HistoryModelSource),
        request.ModelLoadDisposition,
        request.Metrics
            .OrderBy(metric => metric.Key)
            .Select(metric => CreateMetric(metric.Key.ToString(), metric.Value))
            .ToArray());

    private static DiagnosticResourceSampleEntry CreateResourceSample(TechnicalResourceSampleRecord sample)
    {
        List<DiagnosticMetricEntry> metrics = new(capacity: 14);
        AddMetric(metrics, "cpu_percent", sample.CpuPercent);
        AddMetric(metrics, "memory_percent", sample.MemoryPercent);
        AddMetric(metrics, "memory_used_bytes", sample.MemoryUsedBytes);
        AddMetric(metrics, "process_cpu_percent", sample.ProcessCpuPercent);
        AddMetric(metrics, "process_memory_bytes", sample.ProcessMemoryBytes);
        AddMetric(metrics, "disk_read_bytes", sample.DiskReadBytes);
        AddMetric(metrics, "disk_write_bytes", sample.DiskWriteBytes);
        AddMetric(metrics, "client_to_backend_bytes", sample.ClientToBackendBytes);
        AddMetric(metrics, "backend_to_client_bytes", sample.BackendToClientBytes);
        AddMetric(metrics, "gpu_utilization_percent", sample.GpuUtilizationPercent);
        AddMetric(metrics, "gpu_vram_used_bytes", sample.GpuVramUsedBytes);
        AddMetric(metrics, "gpu_vram_total_bytes", sample.GpuVramTotalBytes);
        AddMetric(metrics, "gpu_temperature_celsius", sample.GpuTemperatureCelsius);
        AddMetric(metrics, "gpu_power_watts", sample.GpuPowerWatts);
        return new DiagnosticResourceSampleEntry(
            sample.SampleId,
            sample.RequestId,
            sample.OperationId,
            sample.CapturedAt.ToUniversalTime(),
            sample.Stage?.Stage.ToString() ?? "unavailable",
            sample.Stage?.Evidence.ToString() ?? "unavailable",
            sample.GpuDeviceId?.Value,
            sample.DroppedSampleCount,
            metrics);
    }

    private static void AddMetric(
        List<DiagnosticMetricEntry> target,
        string key,
        MetricValue metric) => target.Add(CreateMetric(key, metric));

    private static DiagnosticMetricEntry CreateMetric(string key, MetricValue metric) => new(
        key,
        metric.Value,
        metric.Unit,
        metric.Quality,
        metric.Source,
        metric.SourceVersion,
        metric.DerivationVersion);
}

internal static class LocalJsonArtifactWriter
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    public static async Task SaveAsync(
        string json,
        string path,
        string artifactName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactName);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException($"A local {artifactName.ToLowerInvariant()} path is required.", nameof(path));
        }

        string fullPath = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(fullPath), ".json", StringComparison.OrdinalIgnoreCase) ||
            fullPath.StartsWith(@"\\", StringComparison.Ordinal))
        {
            throw new ArgumentException($"{artifactName} output must be a local .json path.", nameof(path));
        }

        string? directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException($"{artifactName} output directory is unavailable.", nameof(path));
        }

        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                byte[] bytes = Utf8WithoutBom.GetBytes(json);
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
