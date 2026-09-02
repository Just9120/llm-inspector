using LlmInspector.Domain;

namespace LlmInspector.WindowsTests;

[TestClass]
public sealed class RequestDetailTextPresenterTests
{
    [TestMethod]
    public void LatestRequestShowsNormalizedTokensContextTimingsAndQuality()
    {
        BackendResponseTelemetry telemetry = BackendResponseTelemetry.Unavailable(
            BackendKind.LlamaCpp,
            "openai-chat-fixtures-v2/llama-cpp") with
        {
            Model = TechnicalIdentifier.FromBackend("fixture-model"),
            PromptTokens = Exact(120, MetricUnit.TokenCount, MetricSource.OpenAiUsage),
            CompletionTokens = Exact(30, MetricUnit.TokenCount, MetricSource.OpenAiUsage),
            CachedPromptTokens = Exact(80, MetricUnit.TokenCount, MetricSource.OpenAiUsage),
            ReasoningTokens = Exact(12, MetricUnit.TokenCount, MetricSource.OpenAiUsage),
            PromptTokensPerSecond = Exact(20, MetricUnit.TokensPerSecond, MetricSource.BackendExtension),
            CompletionTokensPerSecond = Exact(40, MetricUnit.TokensPerSecond, MetricSource.BackendExtension),
        };
        ProxyObservation observation = new(
            Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"),
            new DateTimeOffset(2026, 9, 3, 0, 0, 0, TimeSpan.Zero),
            TimeSpan.FromMilliseconds(1250),
            200,
            ProxyOutcome.Completed,
            ClientKind.Cline,
            telemetry);

        string text = App.RequestDetailTextPresenter.Format(observation);

        StringAssert.Contains(text, "Latest request 01234567 | Backend: llama.cpp | Model: fixture-model");
        StringAssert.Contains(text, "Input: 120 tokens [exact]");
        StringAssert.Contains(text, "Output: 30 tokens [exact]");
        StringAssert.Contains(text, "Cached input: 80 tokens [exact]");
        StringAssert.Contains(text, "Reasoning: 12 tokens [exact]");
        StringAssert.Contains(text, "Context | Usage: 120 tokens [exact] | Limit: unavailable [unavailable]");
        StringAssert.Contains(text, "History: unavailable [unavailable] | Tools: unavailable [unavailable] | Cache: 80 tokens [exact]");
        StringAssert.Contains(text, "Prompt/prefill: 20 tokens/s [exact]");
        StringAssert.Contains(text, "Generation: 40 tokens/s [exact]");
        StringAssert.Contains(text, "TTFT: unavailable [unavailable]");
        StringAssert.Contains(text, "Model load: unavailable [unavailable]");
        StringAssert.Contains(text, "Queue: unavailable [unavailable]");
        StringAssert.Contains(text, "Total: 1250 ms [calculated]");
        StringAssert.Contains(text, "Cold/warm classification: unavailable [unavailable]");
    }

    [TestMethod]
    public void MissingObservationOrSourceMetricDoesNotFabricateZero()
    {
        Assert.AreEqual("Latest request: none.", App.RequestDetailTextPresenter.Format(null));

        ProxyObservation observation = new(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            TimeSpan.FromMilliseconds(1),
            502,
            ProxyOutcome.BackendUnavailable,
            ClientKind.GenericUnknown,
            BackendResponseTelemetry.Unavailable(BackendKind.Ollama, "openai-chat-fixtures-v2/ollama"));

        string text = App.RequestDetailTextPresenter.Format(observation);

        StringAssert.Contains(text, "Input: unavailable [unavailable]");
        Assert.DoesNotContain("Input: 0 tokens", text, StringComparison.Ordinal);
    }

    private static MetricValue Exact(decimal value, MetricUnit unit, MetricSource source) =>
        MetricValue.Exact(value, unit, source, "fixture-v1");
}
