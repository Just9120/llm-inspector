using LlmInspector.Domain;

namespace LlmInspector.UnitTests;

[TestClass]
public sealed class BackendTelemetryContractTests
{
    [TestMethod]
    public void UnavailableMetricCannotCarryFabricatedNumericValue()
    {
        _ = Assert.ThrowsExactly<ArgumentException>(() => new MetricValue(
            0,
            MetricUnit.TokenCount,
            MetricQuality.Unavailable,
            MetricSource.OpenAiUsage,
            "fixture-v1"));

        MetricValue unavailable = MetricValue.Unavailable(
            MetricUnit.TokenCount,
            MetricSource.OpenAiUsage,
            "fixture-v1");

        Assert.IsNull(unavailable.Value);
        Assert.AreEqual(MetricQuality.Unavailable, unavailable.Quality);
    }

    [TestMethod]
    public void AvailableMetricRequiresValueAndVersionedDerivationWhenNotExact()
    {
        _ = Assert.ThrowsExactly<ArgumentException>(() => new MetricValue(
            null,
            MetricUnit.TokenCount,
            MetricQuality.Exact,
            MetricSource.OpenAiUsage,
            "fixture-v1"));
        _ = Assert.ThrowsExactly<ArgumentException>(() => MetricValue.Exact(
            1.5m,
            MetricUnit.TokenCount,
            MetricSource.OpenAiUsage,
            "fixture-v1"));
        _ = Assert.ThrowsExactly<ArgumentException>(() => new MetricValue(
            1,
            MetricUnit.TokenCount,
            MetricQuality.Calculated,
            MetricSource.Inspector,
            "fixture-v1"));

        MetricValue calculated = MetricValue.Calculated(
            3,
            MetricUnit.TokenCount,
            MetricSource.Inspector,
            "fixture-v1",
            "sum-prompt-completion-v1");

        Assert.AreEqual(3, calculated.Value);
        Assert.AreEqual(MetricQuality.Calculated, calculated.Quality);
        Assert.AreEqual("sum-prompt-completion-v1", calculated.DerivationVersion);
    }

    [TestMethod]
    public void ContextDeltaAllowsSignedWholeTokensWithoutWeakeningTokenCounts()
    {
        MetricValue shrink = MetricValue.Calculated(
            -25,
            MetricUnit.TokenDelta,
            MetricSource.Inspector,
            "inspector-correlation-headers-v1",
            "adjacent-context-delta-v1");

        Assert.AreEqual(-25, shrink.Value);
        Assert.AreEqual(MetricUnit.TokenDelta, shrink.Unit);
        _ = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => MetricValue.Exact(
            -1,
            MetricUnit.TokenCount,
            MetricSource.Inspector,
            "fixture-v1"));
        _ = Assert.ThrowsExactly<ArgumentException>(() => MetricValue.Calculated(
            1.5m,
            MetricUnit.TokenDelta,
            MetricSource.Inspector,
            "fixture-v1",
            "fixture-derivation-v1"));
    }

    [TestMethod]
    public void ModelIdentifierRejectsFreeFormOrOversizedValues()
    {
        Assert.IsNull(TechnicalIdentifier.FromBackend("model id with prompt-like text"));
        Assert.IsNull(TechnicalIdentifier.FromBackend(new string('a', 129)));
        Assert.AreEqual(
            "lmstudio-community/qwen2.5-7b-instruct",
            TechnicalIdentifier.FromBackend("lmstudio-community/qwen2.5-7b-instruct")?.Value);
    }

    [TestMethod]
    public void BackendUnavailableProjectionUsesCommonTokenUnits()
    {
        foreach (BackendKind backend in Enum.GetValues<BackendKind>())
        {
            BackendResponseTelemetry telemetry = BackendResponseTelemetry.Unavailable(backend, "fixture-v1");

            Assert.AreEqual(MetricUnit.TokenCount, telemetry.PromptTokens.Unit);
            Assert.AreEqual(MetricUnit.TokenCount, telemetry.CompletionTokens.Unit);
            Assert.AreEqual(MetricUnit.TokenCount, telemetry.TotalTokens.Unit);
            Assert.AreEqual(MetricQuality.Unavailable, telemetry.PromptTokens.Quality);
            Assert.AreEqual(MetricQuality.Unavailable, telemetry.CompletionTokens.Quality);
            Assert.AreEqual(MetricQuality.Unavailable, telemetry.TotalTokens.Quality);
            Assert.AreEqual(MetricQuality.Unavailable, telemetry.CachedPromptTokens.Quality);
            Assert.AreEqual(MetricQuality.Unavailable, telemetry.ReasoningTokens.Quality);
            Assert.AreEqual(MetricQuality.Unavailable, telemetry.ContextUsageTokens.Quality);
            Assert.AreEqual(MetricQuality.Unavailable, telemetry.ContextLimitTokens.Quality);
            Assert.AreEqual(MetricQuality.Unavailable, telemetry.ContextHistoryTokens.Quality);
            Assert.AreEqual(MetricQuality.Unavailable, telemetry.ContextToolTokens.Quality);
            Assert.AreEqual(MetricQuality.Unavailable, telemetry.PromptTokensPerSecond.Quality);
            Assert.AreEqual(MetricQuality.Unavailable, telemetry.CompletionTokensPerSecond.Quality);
            Assert.AreEqual(MetricQuality.Unavailable, telemetry.ModelLoadTime.Quality);
            Assert.AreEqual(MetricQuality.Unavailable, telemetry.QueueTime.Quality);
        }
    }

    [TestMethod]
    public async Task LatestObservationStoreRetainsOnlyTheLatestAllowlistedRecord()
    {
        LlmInspector.Application.LatestProxyObservationStore store = new();
        BackendResponseTelemetry telemetry = BackendResponseTelemetry.Unavailable(
            BackendKind.Ollama,
            "fixture-v1");
        ProxyObservation first = new(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            TimeSpan.FromMilliseconds(1),
            200,
            ProxyOutcome.Completed,
            ClientKind.GenericUnknown,
            telemetry);
        ProxyObservation second = first with { RequestId = Guid.NewGuid() };

        await store.RecordAsync(first, CancellationToken.None);
        await store.RecordAsync(second, CancellationToken.None);

        Assert.AreEqual(2, store.AcceptedCount);
        Assert.AreSame(second, store.Latest);
    }
}
