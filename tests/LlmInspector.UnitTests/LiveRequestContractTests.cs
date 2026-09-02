using LlmInspector.Domain;

namespace LlmInspector.UnitTests;

[TestClass]
public sealed class LiveRequestContractTests
{
    [TestMethod]
    public void StateContractDistinguishesEveryCanonicalStage()
    {
        RequestStage[] expected =
        [
            RequestStage.ModelLoading,
            RequestStage.QueueWaiting,
            RequestStage.PromptProcessing,
            RequestStage.ReasoningGeneration,
            RequestStage.ToolWait,
            RequestStage.Completed,
            RequestStage.Cancelled,
            RequestStage.Error,
        ];

        CollectionAssert.AreEquivalent(expected, Enum.GetValues<RequestStage>());
        Assert.IsFalse(RequestStageValue.ProtocolObserved(
            RequestStage.ReasoningGeneration,
            "gateway-protocol-v1").IsTerminal);
        Assert.IsTrue(RequestStageValue.BackendReported(
            RequestStage.Completed,
            "backend-events-v1").IsTerminal);
    }

    [TestMethod]
    public void BackendProgressIsExactBoundedAndBackendSourced()
    {
        BackendProgressSignal signal = new(42.5m, "backend-progress-v1");

        MetricValue metric = signal.ToMetric();

        Assert.AreEqual(42.5m, metric.Value);
        Assert.AreEqual(MetricUnit.Percent, metric.Unit);
        Assert.AreEqual(MetricQuality.Exact, metric.Quality);
        Assert.AreEqual(MetricSource.BackendExtension, metric.Source);
        _ = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new BackendProgressSignal(-0.1m, "backend-progress-v1"));
        _ = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new BackendProgressSignal(100.1m, "backend-progress-v1"));
        _ = Assert.ThrowsExactly<ArgumentException>(() => new BackendProgressSignal(50m, ""));
    }

    [TestMethod]
    public void GenericPercentMetricCannotExceedOneHundred()
    {
        _ = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => MetricValue.Exact(
            101m,
            MetricUnit.Percent,
            MetricSource.BackendExtension,
            "backend-progress-v1"));
    }
}
