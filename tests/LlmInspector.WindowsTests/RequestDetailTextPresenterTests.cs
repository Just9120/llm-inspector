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
            ContextUsageTokens = Exact(120, MetricUnit.TokenCount, MetricSource.OpenAiUsage),
            ContextLimitTokens = Exact(4096, MetricUnit.TokenCount, MetricSource.BackendExtension),
            ContextHistoryTokens = Exact(60, MetricUnit.TokenCount, MetricSource.BackendExtension),
            ContextToolTokens = Exact(20, MetricUnit.TokenCount, MetricSource.BackendExtension),
            PromptTokensPerSecond = Exact(20, MetricUnit.TokensPerSecond, MetricSource.BackendExtension),
            CompletionTokensPerSecond = Exact(40, MetricUnit.TokensPerSecond, MetricSource.BackendExtension),
            ModelLoadTime = Exact(200, MetricUnit.Milliseconds, MetricSource.BackendExtension),
            QueueTime = Exact(10, MetricUnit.Milliseconds, MetricSource.BackendExtension),
        };
        ProxyObservation observation = new(
            Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"),
            new DateTimeOffset(2026, 9, 3, 0, 0, 0, TimeSpan.Zero),
            TimeSpan.FromMilliseconds(1250),
            200,
            ProxyOutcome.Completed,
            ClientKind.Cline,
            telemetry,
            MetricValue.Calculated(
                250,
                MetricUnit.Milliseconds,
                MetricSource.Inspector,
                "gateway-streaming-ttft-v1",
                "first-nonempty-chat-content-delta-v1"))
        {
            Correlation = new RequestCorrelation(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                2),
            ContextChangeTokens = MetricValue.Calculated(
                -25,
                MetricUnit.TokenDelta,
                MetricSource.Inspector,
                "inspector-correlation-headers-v1",
                "adjacent-context-delta-v1"),
        };

        string text = App.RequestDetailTextPresenter.Format(observation);

        StringAssert.Contains(text, "Latest request 01234567 | Backend: llama.cpp | Model: fixture-model");
        StringAssert.Contains(text, "Input: 120 tokens [exact]");
        StringAssert.Contains(text, "Output: 30 tokens [exact]");
        StringAssert.Contains(text, "Cached input: 80 tokens [exact]");
        StringAssert.Contains(text, "Reasoning: 12 tokens [exact]");
        StringAssert.Contains(text, "Context | Usage: 120 tokens [exact] | Limit: 4096 tokens [exact]");
        StringAssert.Contains(text, "Change vs previous session turn: -25 tokens [calculated]");
        StringAssert.Contains(text, "History: 60 tokens [exact] | Tools: 20 tokens [exact] | Cache: 80 tokens [exact]");
        StringAssert.Contains(text, "Prompt/prefill: 20 tokens/s [exact]");
        StringAssert.Contains(text, "Generation: 40 tokens/s [exact]");
        StringAssert.Contains(text, "TTFT: 250 ms [calculated]");
        StringAssert.Contains(text, "Model load: 200 ms [exact]");
        StringAssert.Contains(text, "Queue: 10 ms [exact]");
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
