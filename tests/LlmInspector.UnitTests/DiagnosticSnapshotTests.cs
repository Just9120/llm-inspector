using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LlmInspector.Application;
using LlmInspector.Diagnostics;
using LlmInspector.Domain;

namespace LlmInspector.UnitTests;

[TestClass]
public sealed class DiagnosticSnapshotTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task TimeRangeSnapshotUsesVersionedAllowlistAndTypedTechnicalData()
    {
        Guid requestId = Guid.NewGuid();
        Guid operationId = Guid.NewGuid();
        RequestHistoryItem request = new(
            requestId,
            Guid.NewGuid(),
            operationId,
            StartedAt,
            503,
            ProxyOutcome.BackendUnavailable,
            HistoryErrorType.ModelLoading,
            ClientKind.Cline,
            BackendKind.Ollama,
            Id("model-v1"),
            new Dictionary<HistoryMetric, MetricValue>
            {
                [HistoryMetric.InputTokens] = Exact(12, MetricUnit.TokenCount),
                [HistoryMetric.TotalDurationMilliseconds] = Calculated(250, MetricUnit.Milliseconds),
            },
            ModelLoadDisposition.Cold);
        TechnicalResourceSampleRecord resource = new(
            Guid.NewGuid(),
            operationId,
            StartedAt.AddSeconds(1),
            Exact(25, MetricUnit.Percent),
            Exact(50, MetricUnit.Percent))
        {
            RequestId = requestId,
            Stage = RequestStageValue.ProtocolObserved(RequestStage.ModelLoading, "snapshot-test-v1"),
            GpuDeviceId = Id("GPU-0"),
            GpuUtilizationPercent = Exact(75, MetricUnit.Percent),
        };
        SnapshotHistoryStore history = new(new TechnicalHistorySlice([request], [resource], false, false));
        DiagnosticSnapshotService service = new(history, new FixedTimeProvider(StartedAt.AddMinutes(5)));
        DiagnosticSnapshotSelection selection = DiagnosticSnapshotSelection.ForTimeRange(
            StartedAt.AddMinutes(-1),
            StartedAt.AddMinutes(1));

        DiagnosticSnapshotArtifact artifact = await service.CreateAsync(selection, EnvironmentFacts());

        Assert.AreEqual(DiagnosticSnapshotContract.SchemaVersion1, artifact.Document.SchemaVersion);
        Assert.AreEqual(StartedAt.AddMinutes(5), artifact.Document.GeneratedAtUtc);
        Assert.AreEqual(selection, artifact.Document.Selection);
        Assert.AreEqual(selection.FromUtc, history.LastFilter?.From);
        Assert.AreEqual(selection.ToUtc, history.LastFilter?.To);
        Assert.IsNull(history.LastOperationId);
        DiagnosticRequestEntry actualRequest = AssertSingle(artifact.Document.Requests);
        Assert.AreEqual("model-v1", actualRequest.Model.Value);
        Assert.AreEqual(HistoryErrorType.ModelLoading, actualRequest.ErrorType);
        Assert.AreEqual(503, actualRequest.HttpStatusCode);
        Assert.HasCount(2, actualRequest.RuntimeMetrics);
        DiagnosticResourceSampleEntry actualResource = AssertSingle(artifact.Document.ResourceSamples);
        Assert.AreEqual("ModelLoading", actualResource.Stage);
        Assert.AreEqual("GPU-0", actualResource.GpuDeviceId);
        Assert.IsTrue(actualResource.SystemMetrics.Any(metric =>
            metric.Key == "gpu_utilization_percent" && metric.Value == 75));
        Assert.AreEqual(DiagnosticFactAvailability.Unavailable, artifact.Document.Environment.GpuDriverVersion.Availability);
        Assert.IsNull(artifact.Document.Environment.GpuDriverVersion.Value);

        using JsonDocument json = JsonDocument.Parse(artifact.Json);
        string[] rootFields = json.RootElement.EnumerateObject()
            .Select(property => property.Name)
            .ToArray();
        CollectionAssert.AreEquivalent(DiagnosticSnapshotContract.RootFieldAllowlist.ToArray(), rootFields);
        Assert.AreEqual(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(artifact.Json))).ToLowerInvariant(),
            artifact.Sha256);
    }

    [TestMethod]
    public async Task OperationSnapshotPassesOnlySelectedOperationToHistoryBoundary()
    {
        Guid operationId = Guid.NewGuid();
        SnapshotHistoryStore history = new(new TechnicalHistorySlice([], [], false, false));
        DiagnosticSnapshotService service = new(history, new FixedTimeProvider(StartedAt));

        DiagnosticSnapshotArtifact artifact = await service.CreateAsync(
            DiagnosticSnapshotSelection.ForOperation(operationId),
            EnvironmentFacts());

        Assert.AreEqual(operationId, history.LastOperationId);
        Assert.IsNull(history.LastFilter?.From);
        Assert.IsNull(history.LastFilter?.To);
        Assert.AreEqual(DiagnosticSnapshotScope.Operation, artifact.Document.Selection.Scope);
    }

    [TestMethod]
    public async Task SnapshotSaveWritesExactPreviewAtomicallyToLocalJson()
    {
        SnapshotHistoryStore history = new(new TechnicalHistorySlice([], [], false, false));
        DiagnosticSnapshotService service = new(history, new FixedTimeProvider(StartedAt));
        DiagnosticSnapshotArtifact artifact = await service.CreateAsync(
            DiagnosticSnapshotSelection.ForTimeRange(StartedAt, StartedAt.AddMinutes(1)),
            EnvironmentFacts());
        string directory = Path.Combine(Path.GetTempPath(), $"llm-inspector-snapshot-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "snapshot.json");
        try
        {
            await DiagnosticSnapshotService.SaveAsync(artifact, path);

            Assert.AreEqual(artifact.Json, await File.ReadAllTextAsync(path));
            Assert.IsFalse(Directory.EnumerateFiles(directory, "*.tmp").Any());
            await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
                await DiagnosticSnapshotService.SaveAsync(artifact, Path.Combine(directory, "snapshot.txt")));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task InvalidSelectionAndOversizedSourceFailClosed()
    {
        DiagnosticSnapshotSelection invalid = new(
            DiagnosticSnapshotScope.TimeRange,
            StartedAt.AddMinutes(1),
            StartedAt,
            null);
        SnapshotHistoryStore normal = new(new TechnicalHistorySlice([], [], false, false));
        DiagnosticSnapshotService normalService = new(normal);
        _ = await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await normalService.CreateAsync(invalid, EnvironmentFacts()));

        RequestHistoryItem repeated = Request();
        SnapshotHistoryStore oversized = new(new TechnicalHistorySlice(
            Enumerable.Repeat(repeated, DiagnosticSnapshotContract.MaximumRequests + 1).ToArray(),
            [],
            true,
            false));
        DiagnosticSnapshotService oversizedService = new(oversized);
        _ = await Assert.ThrowsExactlyAsync<InvalidDataException>(async () =>
            await oversizedService.CreateAsync(
                DiagnosticSnapshotSelection.ForTimeRange(StartedAt, StartedAt.AddMinutes(1)),
                EnvironmentFacts()));
    }

    private static RequestHistoryItem Request() => new(
        Guid.NewGuid(),
        null,
        null,
        StartedAt,
        200,
        ProxyOutcome.Completed,
        HistoryErrorType.None,
        ClientKind.GenericUnknown,
        BackendKind.Ollama,
        null,
        new Dictionary<HistoryMetric, MetricValue>());

    private static DiagnosticEnvironmentFacts EnvironmentFacts() => new(
        DiagnosticTechnicalFact.Available("Windows 11 25H2", "snapshot-test-v1"),
        DiagnosticTechnicalFact.Unavailable("snapshot-test-v1"),
        DiagnosticTechnicalFact.Unavailable("snapshot-test-v1"),
        DiagnosticTechnicalFact.Unavailable("snapshot-test-v1"),
        DiagnosticTechnicalFact.Available("1.0.0", "snapshot-test-v1"),
        DiagnosticTechnicalFact.Available(".NET 10.0", "snapshot-test-v1"));

    private static MetricValue Exact(decimal value, MetricUnit unit) =>
        MetricValue.Exact(value, unit, MetricSource.Inspector, "snapshot-test-v1");

    private static MetricValue Calculated(decimal value, MetricUnit unit) =>
        MetricValue.Calculated(
            value,
            unit,
            MetricSource.Inspector,
            "snapshot-test-v1",
            "snapshot-derivation-v1");

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

    private sealed class SnapshotHistoryStore(TechnicalHistorySlice slice) : ITechnicalHistoryStore
    {
        public HistoryFilter? LastFilter { get; private set; }

        public Guid? LastOperationId { get; private set; }

        public Task<TechnicalHistorySlice> QuerySnapshotSliceAsync(
            HistoryFilter filter,
            Guid? operationId,
            CancellationToken cancellationToken = default)
        {
            LastFilter = filter;
            LastOperationId = operationId;
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
