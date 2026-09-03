using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LlmInspector.Application;
using LlmInspector.Diagnostics;
using LlmInspector.Domain;

namespace LlmInspector.UnitTests;

[TestClass]
public sealed class AnalyticsExportTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task SelectedRangeExportsAnonymizedHistoryAndAggregateMetrics()
    {
        RequestHistoryItem[] requests =
        [
            Request(10, HistoryErrorType.None, ModelLoadDisposition.Cold),
            Request(20, HistoryErrorType.Timeout, ModelLoadDisposition.Warm),
            Request(30, HistoryErrorType.None, ModelLoadDisposition.Unavailable),
        ];
        TechnicalResourceSampleRecord resource = new(
            Guid.NewGuid(),
            null,
            StartedAt.AddMinutes(1),
            Exact(25, MetricUnit.Percent),
            Exact(50, MetricUnit.Percent));
        ExportHistoryStore history = new(new TechnicalHistorySlice(requests, [resource], false, false));
        AnalyticsExportService service = new(history, new FixedTimeProvider(StartedAt.AddHours(1)));
        AnalyticsExportSelection selection = AnalyticsExportSelection.ForTimeRange(
            StartedAt.AddMinutes(-1),
            StartedAt.AddMinutes(2));

        AnalyticsExportArtifact artifact = await service.CreateAsync(selection);

        Assert.AreEqual(AnalyticsExportContract.SchemaVersion1, artifact.Document.SchemaVersion);
        Assert.AreEqual(StartedAt.AddHours(1), artifact.Document.GeneratedAtUtc);
        Assert.AreEqual(selection, artifact.Document.Selection);
        Assert.AreEqual(selection.FromUtc, history.LastFilter?.From);
        Assert.AreEqual(selection.ToUtc, history.LastFilter?.To);
        Assert.HasCount(3, artifact.Document.History.Requests);
        Assert.HasCount(1, artifact.Document.History.ResourceSamples);
        Assert.AreEqual(new AnalyticsExportModelLoads(1, 1, 1), artifact.Document.ModelLoads);

        AnalyticsExportTrendEntry day = AssertSingle(artifact.Document.AggregateMetrics);
        AnalyticsExportMetricEntry input = day.Metrics.Single(metric =>
            metric.Category == "request" && metric.Key == "input_tokens");
        Assert.AreEqual(MetricUnit.TokenCount, input.Unit);
        Assert.AreEqual(3, input.SampleCount);
        Assert.IsTrue(input.IsStatisticallySufficient);
        Assert.AreEqual(20, input.ArithmeticMean);
        Assert.AreEqual(20, input.Median);
        Assert.AreEqual(30, input.P95);
        AnalyticsExportMetricEntry errorRate = day.Metrics.Single(metric =>
            metric.Category == "request" && metric.Key == "error_rate_percent");
        Assert.AreEqual(3, errorRate.SampleCount);
        Assert.AreEqual(100m / 3m, errorRate.ArithmeticMean);

        using JsonDocument json = JsonDocument.Parse(artifact.Json);
        CollectionAssert.AreEquivalent(
            AnalyticsExportContract.RootFieldAllowlist.ToArray(),
            json.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
        CollectionAssert.AreEquivalent(
            AnalyticsExportContract.SelectionFieldAllowlist.ToArray(),
            json.RootElement.GetProperty("selection").EnumerateObject().Select(property => property.Name).ToArray());
        CollectionAssert.AreEquivalent(
            AnalyticsExportContract.HistoryFieldAllowlist.ToArray(),
            json.RootElement.GetProperty("history").EnumerateObject().Select(property => property.Name).ToArray());
        CollectionAssert.AreEquivalent(
            AnalyticsExportContract.TrendFieldAllowlist.ToArray(),
            json.RootElement.GetProperty("aggregate_metrics")[0]
                .EnumerateObject().Select(property => property.Name).ToArray());
        CollectionAssert.AreEquivalent(
            AnalyticsExportContract.MetricFieldAllowlist.ToArray(),
            json.RootElement.GetProperty("aggregate_metrics")[0].GetProperty("metrics")[0]
                .EnumerateObject().Select(property => property.Name).ToArray());
        CollectionAssert.AreEquivalent(
            AnalyticsExportContract.ModelLoadsFieldAllowlist.ToArray(),
            json.RootElement.GetProperty("model_loads")
                .EnumerateObject().Select(property => property.Name).ToArray());
        CollectionAssert.AreEquivalent(
            DiagnosticSnapshotContract.RequestFieldAllowlist.ToArray(),
            json.RootElement.GetProperty("history").GetProperty("requests")[0]
                .EnumerateObject().Select(property => property.Name).ToArray());
        CollectionAssert.AreEquivalent(
            DiagnosticSnapshotContract.ResourceSampleFieldAllowlist.ToArray(),
            json.RootElement.GetProperty("history").GetProperty("resource_samples")[0]
                .EnumerateObject().Select(property => property.Name).ToArray());
        Assert.AreEqual(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(artifact.Json))).ToLowerInvariant(),
            artifact.Sha256);
    }

    [TestMethod]
    public async Task ExportSaveIsAtomicAndOversizedOrInvalidRangesFailClosed()
    {
        ExportHistoryStore history = new(new TechnicalHistorySlice([], [], false, false));
        AnalyticsExportService service = new(history, new FixedTimeProvider(StartedAt));
        AnalyticsExportArtifact artifact = await service.CreateAsync(
            AnalyticsExportSelection.ForTimeRange(StartedAt, StartedAt.AddMinutes(1)));
        string directory = Path.Combine(Path.GetTempPath(), $"llm-inspector-export-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "analytics.json");
        try
        {
            await AnalyticsExportService.SaveAsync(artifact, path);

            Assert.AreEqual(artifact.Json, await File.ReadAllTextAsync(path));
            Assert.IsFalse(Directory.EnumerateFiles(directory, "*.tmp").Any());
            _ = await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
                await AnalyticsExportService.SaveAsync(artifact, Path.Combine(directory, "analytics.txt")));
            _ = await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
                await service.CreateAsync(AnalyticsExportSelection.ForTimeRange(
                    StartedAt.AddMinutes(1),
                    StartedAt)));

            AnalyticsExportService oversized = new(new ExportHistoryStore(
                new TechnicalHistorySlice([], [], true, false)));
            _ = await Assert.ThrowsExactlyAsync<InvalidDataException>(async () =>
                await oversized.CreateAsync(AnalyticsExportSelection.ForTimeRange(
                    StartedAt,
                    StartedAt.AddMinutes(1))));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static RequestHistoryItem Request(
        decimal inputTokens,
        HistoryErrorType errorType,
        ModelLoadDisposition modelLoad) => new(
        Guid.NewGuid(),
        null,
        null,
        StartedAt,
        errorType == HistoryErrorType.None ? 200 : 504,
        errorType == HistoryErrorType.None ? ProxyOutcome.Completed : ProxyOutcome.RelayFailed,
        errorType,
        ClientKind.Cline,
        BackendKind.Ollama,
        Id("model-a"),
        new Dictionary<HistoryMetric, MetricValue>
        {
            [HistoryMetric.InputTokens] = Exact(inputTokens, MetricUnit.TokenCount),
        },
        modelLoad);

    private static MetricValue Exact(decimal value, MetricUnit unit) =>
        MetricValue.Exact(value, unit, MetricSource.Inspector, "analytics-export-test-v1");

    private static TechnicalIdentifier Id(string value) =>
        TechnicalIdentifier.FromBackend(value) ?? throw new InvalidOperationException("Invalid test identifier.");

    private static T AssertSingle<T>(IReadOnlyList<T> items)
    {
        Assert.HasCount(1, items);
        return items[0];
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class ExportHistoryStore(TechnicalHistorySlice slice) : ITechnicalHistoryStore
    {
        public HistoryFilter? LastFilter { get; private set; }

        public Task<TechnicalHistorySlice> QuerySnapshotSliceAsync(
            HistoryFilter filter,
            Guid? operationId,
            CancellationToken cancellationToken = default)
        {
            LastFilter = filter;
            Assert.IsNull(operationId);
            return Task.FromResult(slice);
        }

        public ValueTask RecordAsync(ProxyObservation observation, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RecordOperationGraphAsync(
            TechnicalOperationGraph graph,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task RecordResourceSamplesAsync(
            IReadOnlyList<TechnicalResourceSampleRecord> samples,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<RequestHistoryItem>> QueryRequestsAsync(
            HistoryFilter filter,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<TechnicalOperationDetail?> GetOperationDetailAsync(
            Guid operationId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PeriodAnalytics> AnalyzePeriodAsync(
            HistoryFilter filter,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AnalyticsComparison> CompareAsync(
            HistoryFilter baseline,
            HistoryFilter candidate,
            HistoryMetric metric,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<HistoryRetention> GetRetentionAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SetRetentionAsync(
            HistoryRetention retention,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<int> ApplyRetentionAsync(
            HistoryRetention retention,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<HistoryClearPreview> PreviewClearAsync(
            HistoryClearScope scope,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<HistoryClearPreview> ClearAsync(
            HistoryClearPreview preview,
            bool confirmed,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
