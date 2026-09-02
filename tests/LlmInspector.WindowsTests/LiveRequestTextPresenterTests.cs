using LlmInspector.Domain;

namespace LlmInspector.WindowsTests;

[TestClass]
public sealed class LiveRequestTextPresenterTests
{
    [TestMethod]
    public void MissingBackendSignalShowsStageAndQualityWithoutPercentageOrEta()
    {
        LiveRequestSnapshot request = CreateRequest(
            RequestStage.PromptProcessing,
            MetricValue.Unavailable(
                MetricUnit.Percent,
                MetricSource.BackendExtension,
                "no-backend-progress-v1"),
            MetricValue.Unavailable(MetricUnit.Milliseconds, MetricSource.Inspector, "live-eta-v1"));

        string text = App.LiveRequestTextPresenter.Format(
            new LiveRequestCollectionSnapshot([request], null));

        StringAssert.Contains(text, "Stage: Prompt processing [protocol observed]");
        StringAssert.Contains(text, "Elapsed: 1250 ms [calculated]");
        StringAssert.Contains(text, "Progress: unavailable [unavailable]");
        Assert.DoesNotContain(" %", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ETA:", text, StringComparison.Ordinal);
    }

    [TestMethod]
    public void CredibleBackendProgressAndQualifiedEstimateAreExplicitlyLabelled()
    {
        LiveRequestSnapshot request = CreateRequest(
            RequestStage.ReasoningGeneration,
            MetricValue.Exact(
                40m,
                MetricUnit.Percent,
                MetricSource.BackendExtension,
                "backend-progress-v1"),
            MetricValue.Estimated(
                3000m,
                MetricUnit.Milliseconds,
                MetricSource.Inspector,
                "live-eta-v1",
                "linear-backend-progress-v1"));

        string text = App.LiveRequestTextPresenter.Format(
            new LiveRequestCollectionSnapshot([request], null));

        StringAssert.Contains(text, "Progress: 40 % [exact]");
        StringAssert.Contains(text, "ETA: 3000 ms [estimated]");
    }

    [TestMethod]
    [DataRow(RequestStage.ModelLoading, "Model loading")]
    [DataRow(RequestStage.QueueWaiting, "Queue / waiting")]
    [DataRow(RequestStage.PromptProcessing, "Prompt processing")]
    [DataRow(RequestStage.ReasoningGeneration, "Reasoning / generation")]
    [DataRow(RequestStage.ToolWait, "Tool wait")]
    [DataRow(RequestStage.Completed, "Completed")]
    [DataRow(RequestStage.Cancelled, "Cancelled")]
    [DataRow(RequestStage.Error, "Error")]
    public void EveryCanonicalStageHasAnExplicitUiLabel(RequestStage stage, string expected)
    {
        Assert.AreEqual(expected, App.LiveRequestTextPresenter.GetStageLabel(stage));
    }

    [TestMethod]
    public void EveryConcurrentActiveRequestIsRendered()
    {
        LiveRequestSnapshot first = CreateRequest(
            RequestStage.QueueWaiting,
            MetricValue.Unavailable(
                MetricUnit.Percent,
                MetricSource.BackendExtension,
                "no-backend-progress-v1"),
            MetricValue.Unavailable(MetricUnit.Milliseconds, MetricSource.Inspector, "live-eta-v1"));
        LiveRequestSnapshot second = first with { RequestId = Guid.NewGuid() };

        string text = App.LiveRequestTextPresenter.Format(
            new LiveRequestCollectionSnapshot([first, second], null));

        StringAssert.Contains(text, "Active requests: 2.");
        StringAssert.Contains(text, first.RequestId.ToString("N")[..8]);
        StringAssert.Contains(text, second.RequestId.ToString("N")[..8]);
    }

    private static LiveRequestSnapshot CreateRequest(
        RequestStage stage,
        MetricValue progress,
        MetricValue eta) =>
        new(
            Guid.NewGuid(),
            ClientKind.Cline,
            RequestStageValue.ProtocolObserved(stage, "gateway-openai-lifecycle-v1"),
            new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero),
            MetricValue.Calculated(
                1250m,
                MetricUnit.Milliseconds,
                MetricSource.Inspector,
                "monotonic-clock-v1",
                "monotonic-elapsed-v1"),
            progress,
            eta);
}
