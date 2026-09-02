using LlmInspector.Domain;
using LlmInspector.Telemetry;

namespace LlmInspector.UnitTests;

[TestClass]
public sealed class LiveRequestTrackerTests
{
    [TestMethod]
    public void ActiveSnapshotHasOneStageAndCalculatedElapsedWithoutFabricatedProgress()
    {
        ManualTimeProvider time = new();
        LiveRequestTracker tracker = new(time);
        Guid requestId = Guid.NewGuid();
        tracker.RequestStarted(requestId, time.GetUtcNow(), ClientKind.Cline);
        time.Advance(TimeSpan.FromMilliseconds(1250));

        LiveRequestSnapshot snapshot = tracker.GetSnapshot().ActiveRequests.Single();

        Assert.AreEqual(requestId, snapshot.RequestId);
        Assert.AreEqual(ClientKind.Cline, snapshot.Client);
        Assert.AreEqual(RequestStage.QueueWaiting, snapshot.Stage.Stage);
        Assert.AreEqual(RequestStageEvidence.ProtocolObserved, snapshot.Stage.Evidence);
        Assert.AreEqual(1250m, snapshot.Elapsed.Value);
        Assert.AreEqual(MetricQuality.Calculated, snapshot.Elapsed.Quality);
        Assert.AreEqual(MetricQuality.Unavailable, snapshot.Progress.Quality);
        Assert.AreEqual(MetricQuality.Unavailable, snapshot.Eta.Quality);
    }

    [TestMethod]
    [DataRow(RequestStage.ModelLoading)]
    [DataRow(RequestStage.QueueWaiting)]
    [DataRow(RequestStage.PromptProcessing)]
    [DataRow(RequestStage.ReasoningGeneration)]
    [DataRow(RequestStage.ToolWait)]
    public void EveryNonTerminalStageCanBeTheSingleCurrentStage(RequestStage stage)
    {
        ManualTimeProvider time = new();
        LiveRequestTracker tracker = new(time);
        Guid requestId = Guid.NewGuid();
        tracker.RequestStarted(requestId, time.GetUtcNow(), ClientKind.GenericUnknown);

        tracker.StageChanged(
            requestId,
            RequestStageValue.BackendReported(stage, "backend-events-v1"));

        LiveRequestSnapshot snapshot = tracker.GetSnapshot().ActiveRequests.Single();
        Assert.AreEqual(stage, snapshot.Stage.Stage);
        Assert.AreEqual(RequestStageEvidence.BackendReported, snapshot.Stage.Evidence);
        Assert.AreEqual(MetricQuality.Unavailable, snapshot.Progress.Quality);
    }

    [TestMethod]
    [DataRow(ProxyOutcome.Completed, RequestStage.Completed)]
    [DataRow(ProxyOutcome.ClientCancelled, RequestStage.Cancelled)]
    [DataRow(ProxyOutcome.BackendUnavailable, RequestStage.Error)]
    [DataRow(ProxyOutcome.RelayFailed, RequestStage.Error)]
    public void TerminalOutcomeRemovesActiveRequestAndPreservesTerminalStage(
        ProxyOutcome outcome,
        RequestStage expectedStage)
    {
        ManualTimeProvider time = new();
        LiveRequestTracker tracker = new(time);
        Guid requestId = Guid.NewGuid();
        tracker.RequestStarted(requestId, time.GetUtcNow(), ClientKind.HermesDesktop);
        time.Advance(TimeSpan.FromMilliseconds(750));

        tracker.RequestFinished(requestId, outcome);

        LiveRequestCollectionSnapshot collection = tracker.GetSnapshot();
        Assert.IsEmpty(collection.ActiveRequests);
        Assert.IsNotNull(collection.LatestTerminalRequest);
        Assert.AreEqual(expectedStage, collection.LatestTerminalRequest.Stage.Stage);
        Assert.AreEqual(750m, collection.LatestTerminalRequest.Elapsed.Value);
        Assert.AreEqual(MetricQuality.Unavailable, collection.LatestTerminalRequest.Eta.Quality);
    }

    [TestMethod]
    public void EtaAppearsOnlyAfterEnoughMonotonicBackendProgressEvidence()
    {
        ManualTimeProvider time = new();
        LiveRequestTracker tracker = new(time);
        Guid requestId = Guid.NewGuid();
        tracker.RequestStarted(requestId, time.GetUtcNow(), ClientKind.OpenWebUi);

        tracker.BackendProgressChanged(requestId, new BackendProgressSignal(10m, "backend-progress-v1"));
        Assert.AreEqual(MetricQuality.Unavailable, tracker.GetSnapshot().ActiveRequests.Single().Eta.Quality);
        time.Advance(TimeSpan.FromSeconds(1));
        tracker.BackendProgressChanged(requestId, new BackendProgressSignal(20m, "backend-progress-v1"));
        Assert.AreEqual(MetricQuality.Unavailable, tracker.GetSnapshot().ActiveRequests.Single().Eta.Quality);
        time.Advance(TimeSpan.FromSeconds(1));
        tracker.BackendProgressChanged(requestId, new BackendProgressSignal(30m, "backend-progress-v1"));

        LiveRequestSnapshot snapshot = tracker.GetSnapshot().ActiveRequests.Single();
        Assert.AreEqual(30m, snapshot.Progress.Value);
        Assert.AreEqual(MetricQuality.Exact, snapshot.Progress.Quality);
        Assert.AreEqual(MetricQuality.Estimated, snapshot.Eta.Quality);
        Assert.AreEqual(7000m, snapshot.Eta.Value);
    }

    [TestMethod]
    public void RegressedOrChangedSourceProgressResetsEtaEvidence()
    {
        ManualTimeProvider time = new();
        LiveRequestTracker tracker = new(time);
        Guid requestId = Guid.NewGuid();
        tracker.RequestStarted(requestId, time.GetUtcNow(), ClientKind.OpenCodeDesktop);

        AddProgressSample(tracker, time, requestId, 10m, "source-v1");
        AddProgressSample(tracker, time, requestId, 20m, "source-v1");
        AddProgressSample(tracker, time, requestId, 30m, "source-v1");
        Assert.AreEqual(MetricQuality.Estimated, tracker.GetSnapshot().ActiveRequests.Single().Eta.Quality);

        AddProgressSample(tracker, time, requestId, 25m, "source-v1");
        Assert.AreEqual(MetricQuality.Unavailable, tracker.GetSnapshot().ActiveRequests.Single().Eta.Quality);
        AddProgressSample(tracker, time, requestId, 40m, "source-v2");

        LiveRequestSnapshot snapshot = tracker.GetSnapshot().ActiveRequests.Single();
        Assert.AreEqual(40m, snapshot.Progress.Value);
        Assert.AreEqual(MetricQuality.Exact, snapshot.Progress.Quality);
        Assert.AreEqual(MetricQuality.Unavailable, snapshot.Eta.Quality);
    }

    [TestMethod]
    public void ConcurrentRequestsRetainIndependentCurrentAndTerminalState()
    {
        ManualTimeProvider time = new();
        LiveRequestTracker tracker = new(time);
        Guid firstId = Guid.NewGuid();
        Guid secondId = Guid.NewGuid();
        tracker.RequestStarted(firstId, time.GetUtcNow(), ClientKind.Cline);
        tracker.RequestStarted(secondId, time.GetUtcNow(), ClientKind.OpenWebUi);
        tracker.StageChanged(
            firstId,
            RequestStageValue.BackendReported(RequestStage.ModelLoading, "backend-events-v1"));
        tracker.StageChanged(
            secondId,
            RequestStageValue.BackendReported(RequestStage.ToolWait, "backend-events-v1"));

        LiveRequestCollectionSnapshot active = tracker.GetSnapshot();
        Assert.HasCount(2, active.ActiveRequests);
        Assert.AreEqual(
            RequestStage.ModelLoading,
            active.ActiveRequests.Single(item => item.RequestId == firstId).Stage.Stage);
        Assert.AreEqual(
            RequestStage.ToolWait,
            active.ActiveRequests.Single(item => item.RequestId == secondId).Stage.Stage);

        tracker.RequestFinished(firstId, ProxyOutcome.Completed);

        LiveRequestCollectionSnapshot afterCompletion = tracker.GetSnapshot();
        Assert.AreEqual(secondId, afterCompletion.ActiveRequests.Single().RequestId);
        Assert.AreEqual(firstId, afterCompletion.LatestTerminalRequest?.RequestId);
        Assert.AreEqual(RequestStage.Completed, afterCompletion.LatestTerminalRequest?.Stage.Stage);
    }

    private static void AddProgressSample(
        LiveRequestTracker tracker,
        ManualTimeProvider time,
        Guid requestId,
        decimal percentage,
        string sourceVersion)
    {
        tracker.BackendProgressChanged(requestId, new BackendProgressSignal(percentage, sourceVersion));
        time.Advance(TimeSpan.FromSeconds(1));
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = new(2026, 9, 2, 0, 0, 0, TimeSpan.Zero);
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan duration)
        {
            _utcNow += duration;
            _timestamp += duration.Ticks;
        }
    }
}
