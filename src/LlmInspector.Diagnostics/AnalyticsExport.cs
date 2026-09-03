using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LlmInspector.Application;
using LlmInspector.Domain;

namespace LlmInspector.Diagnostics;

public sealed record AnalyticsExportSelection(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc)
{
    public static AnalyticsExportSelection ForTimeRange(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc) => new(fromUtc.ToUniversalTime(), toUtc.ToUniversalTime());

    public static void Validate(AnalyticsExportSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.FromUtc > selection.ToUtc)
        {
            throw new ArgumentException("Analytics export start cannot be after its end.", nameof(selection));
        }
    }
}

public sealed record AnalyticsExportHistory(
    IReadOnlyList<DiagnosticRequestEntry> Requests,
    IReadOnlyList<DiagnosticResourceSampleEntry> ResourceSamples);

public sealed record AnalyticsExportMetricEntry(
    string Category,
    string Key,
    MetricUnit Unit,
    int SampleCount,
    bool IsStatisticallySufficient,
    decimal? ArithmeticMean,
    decimal? Median,
    decimal? P95);

public sealed record AnalyticsExportTrendEntry(
    DateOnly Day,
    IReadOnlyList<AnalyticsExportMetricEntry> Metrics);

public sealed record AnalyticsExportModelLoads(
    int ColdRequests,
    int WarmRequests,
    int UnavailableRequests);

public sealed record AnalyticsExportDocument(
    string SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    AnalyticsExportSelection Selection,
    AnalyticsExportHistory History,
    IReadOnlyList<AnalyticsExportTrendEntry> AggregateMetrics,
    AnalyticsExportModelLoads ModelLoads);

public sealed record AnalyticsExportArtifact(
    AnalyticsExportDocument Document,
    string Json,
    string Sha256);

public static class AnalyticsExportContract
{
    public const string SchemaVersion1 = "analytics-export-v1";

    public static IReadOnlyList<string> RootFieldAllowlist { get; } = Array.AsReadOnly(
        new[]
        {
            "schema_version",
            "generated_at_utc",
            "selection",
            "history",
            "aggregate_metrics",
            "model_loads",
        });

    public static IReadOnlyList<string> SelectionFieldAllowlist { get; } = Array.AsReadOnly(
        new[]
        {
            "from_utc",
            "to_utc",
        });

    public static IReadOnlyList<string> HistoryFieldAllowlist { get; } = Array.AsReadOnly(
        new[]
        {
            "requests",
            "resource_samples",
        });

    public static IReadOnlyList<string> TrendFieldAllowlist { get; } = Array.AsReadOnly(
        new[]
        {
            "day",
            "metrics",
        });

    public static IReadOnlyList<string> MetricFieldAllowlist { get; } = Array.AsReadOnly(
        new[]
        {
            "key",
            "category",
            "unit",
            "sample_count",
            "is_statistically_sufficient",
            "arithmetic_mean",
            "median",
            "p95",
        });

    public static IReadOnlyList<string> ModelLoadsFieldAllowlist { get; } = Array.AsReadOnly(
        new[]
        {
            "cold_requests",
            "warm_requests",
            "unavailable_requests",
        });
}

public sealed class AnalyticsExportService
{
    private const string RequestMetricCategory = "request";
    private const string ResourceMetricCategory = "resource";
    private const string ErrorRateMetricKey = "error_rate_percent";
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private readonly DiagnosticSnapshotService _snapshotService;

    public AnalyticsExportService(
        ITechnicalHistoryStore history,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(history);
        _snapshotService = new DiagnosticSnapshotService(history, timeProvider);
    }

    public async Task<AnalyticsExportArtifact> CreateAsync(
        AnalyticsExportSelection selection,
        CancellationToken cancellationToken = default)
    {
        AnalyticsExportSelection.Validate(selection);
        selection = AnalyticsExportSelection.ForTimeRange(selection.FromUtc, selection.ToUtc);
        DiagnosticSnapshotArtifact snapshot = await _snapshotService.CreateAsync(
            DiagnosticSnapshotSelection.ForTimeRange(selection.FromUtc, selection.ToUtc),
            DiagnosticEnvironmentFacts.CaptureLocal(),
            cancellationToken).ConfigureAwait(false);
        if (snapshot.Document.Truncation.RequestsTruncated ||
            snapshot.Document.Truncation.ResourceSamplesTruncated)
        {
            throw new InvalidDataException(
                "The selected range exceeds bounded export capacity; narrow the range before exporting.");
        }

        AnalyticsExportDocument document = new(
            AnalyticsExportContract.SchemaVersion1,
            snapshot.Document.GeneratedAtUtc,
            selection,
            new AnalyticsExportHistory(
                snapshot.Document.Requests,
                snapshot.Document.ResourceSamples),
            CreateAggregates(snapshot.Document.Requests, snapshot.Document.ResourceSamples),
            new AnalyticsExportModelLoads(
                snapshot.Document.Requests.Count(request =>
                    request.ModelLoadDisposition == ModelLoadDisposition.Cold),
                snapshot.Document.Requests.Count(request =>
                    request.ModelLoadDisposition == ModelLoadDisposition.Warm),
                snapshot.Document.Requests.Count(request =>
                    request.ModelLoadDisposition == ModelLoadDisposition.Unavailable)));
        string json = JsonSerializer.Serialize(document, SerializerOptions);
        string hash = Convert.ToHexString(SHA256.HashData(Utf8WithoutBom.GetBytes(json)))
            .ToLowerInvariant();
        return new AnalyticsExportArtifact(document, json, hash);
    }

    public static Task SaveAsync(
        AnalyticsExportArtifact artifact,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        return LocalJsonArtifactWriter.SaveAsync(
            artifact.Json,
            path,
            "Analytics export",
            cancellationToken);
    }

    private static AnalyticsExportTrendEntry[] CreateAggregates(
        IReadOnlyList<DiagnosticRequestEntry> requests,
        IReadOnlyList<DiagnosticResourceSampleEntry> resources)
    {
        Dictionary<DateOnly, Dictionary<(string Category, string Key, MetricUnit Unit), List<decimal>>> buckets = new();
        foreach (DiagnosticRequestEntry request in requests)
        {
            DateOnly day = DateOnly.FromDateTime(request.StartedAtUtc.UtcDateTime);
            Dictionary<(string Category, string Key, MetricUnit Unit), List<decimal>> bucket = GetBucket(buckets, day);
            foreach (DiagnosticMetricEntry metric in request.RuntimeMetrics)
            {
                AddMetric(bucket, RequestMetricCategory, metric);
            }

            AddSample(
                bucket,
                RequestMetricCategory,
                ErrorRateMetricKey,
                MetricUnit.Percent,
                request.ErrorType == HistoryErrorType.None ? 0 : 100);
        }

        foreach (DiagnosticResourceSampleEntry resource in resources)
        {
            DateOnly day = DateOnly.FromDateTime(resource.CapturedAtUtc.UtcDateTime);
            Dictionary<(string Category, string Key, MetricUnit Unit), List<decimal>> bucket = GetBucket(buckets, day);
            foreach (DiagnosticMetricEntry metric in resource.SystemMetrics)
            {
                AddMetric(bucket, ResourceMetricCategory, metric);
            }
        }

        return buckets
            .OrderBy(item => item.Key)
            .Select(item => new AnalyticsExportTrendEntry(
                item.Key,
                item.Value
                    .OrderBy(metric => metric.Key.Category, StringComparer.Ordinal)
                    .ThenBy(metric => metric.Key.Key, StringComparer.Ordinal)
                    .ThenBy(metric => metric.Key.Unit)
                    .Select(metric => CreateAggregate(metric.Key, metric.Value))
                    .ToArray()))
            .ToArray();
    }

    private static Dictionary<(string Category, string Key, MetricUnit Unit), List<decimal>> GetBucket(
        Dictionary<DateOnly, Dictionary<(string Category, string Key, MetricUnit Unit), List<decimal>>> buckets,
        DateOnly day)
    {
        if (!buckets.TryGetValue(
                day,
                out Dictionary<(string Category, string Key, MetricUnit Unit), List<decimal>>? bucket))
        {
            bucket = new Dictionary<(string Category, string Key, MetricUnit Unit), List<decimal>>();
            buckets.Add(day, bucket);
        }

        return bucket;
    }

    private static void AddMetric(
        Dictionary<(string Category, string Key, MetricUnit Unit), List<decimal>> bucket,
        string category,
        DiagnosticMetricEntry metric)
    {
        if (metric.Value is decimal value)
        {
            AddSample(
                bucket,
                category,
                JsonNamingPolicy.SnakeCaseLower.ConvertName(metric.Key),
                metric.Unit,
                value);
        }
    }

    private static void AddSample(
        Dictionary<(string Category, string Key, MetricUnit Unit), List<decimal>> bucket,
        string category,
        string key,
        MetricUnit unit,
        decimal value)
    {
        if (!bucket.TryGetValue((category, key, unit), out List<decimal>? samples))
        {
            samples = [];
            bucket.Add((category, key, unit), samples);
        }

        samples.Add(value);
    }

    private static AnalyticsExportMetricEntry CreateAggregate(
        (string Category, string Key, MetricUnit Unit) identity,
        IReadOnlyList<decimal> samples)
    {
        MetricAggregate aggregate = HistoryStatistics.Calculate(samples);
        return new AnalyticsExportMetricEntry(
            identity.Category,
            identity.Key,
            identity.Unit,
            aggregate.SampleCount,
            aggregate.IsStatisticallySufficient,
            aggregate.ArithmeticMean,
            aggregate.Median,
            aggregate.P95);
    }
}
