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
        }
    }
}
