using System.Text;
using LlmInspector.Adapters;
using LlmInspector.Domain;

namespace LlmInspector.ContractTests;

[TestClass]
public sealed class BackendAdapterContractTests
{
    [TestMethod]
    [DataRow(BackendKind.Ollama, "ollama-nonstreaming.json", "qwen3:8b", 11, 18, 29)]
    [DataRow(BackendKind.LlamaCpp, "llama-cpp-nonstreaming.json", "ggml-org/gemma-3", 44, 48, 92)]
    [DataRow(BackendKind.LmStudio, "lm-studio-nonstreaming.json", "lmstudio-community/qwen2.5", 223, 24, 247)]
    public void NonStreamingFixturesProduceCommonTelemetryWithIdenticalSemantics(
        BackendKind backend,
        string fixtureName,
        string expectedModel,
        int promptTokens,
        int completionTokens,
        int totalTokens)
    {
        BackendResponseTelemetry telemetry = ParseFixture(backend, fixtureName, "application/json");

        Assert.AreEqual(backend, telemetry.Backend);
        Assert.AreEqual(expectedModel, telemetry.Model?.Value);
        AssertCommonTokenMetric(telemetry.PromptTokens, promptTokens);
        AssertCommonTokenMetric(telemetry.CompletionTokens, completionTokens);
        AssertCommonTokenMetric(telemetry.TotalTokens, totalTokens);
    }

    [TestMethod]
    [DataRow(BackendKind.Ollama, "ollama-streaming.sse", 7, 3, 10)]
    [DataRow(BackendKind.LlamaCpp, "llama-cpp-streaming.sse", 19, 6, 25)]
    [DataRow(BackendKind.LmStudio, "lm-studio-streaming.sse", 31, 9, 40)]
    public void StreamingFixturesExtractFinalUsageAcrossArbitraryByteBoundaries(
        BackendKind backend,
        string fixtureName,
        int promptTokens,
        int completionTokens,
        int totalTokens)
    {
        BackendResponseTelemetry telemetry = ParseFixture(backend, fixtureName, "text/event-stream");

        AssertCommonTokenMetric(telemetry.PromptTokens, promptTokens);
        AssertCommonTokenMetric(telemetry.CompletionTokens, completionTokens);
        AssertCommonTokenMetric(telemetry.TotalTokens, totalTokens);
    }

    [TestMethod]
    public void StreamingSessionSignalsOnlyTheFirstNonEmptyGeneratedContentDelta()
    {
        IBackendTelemetrySession session = BackendTelemetryAdapters
            .Create(BackendKind.Ollama)
            .CreateSession("text/event-stream");

        session.Observe("data: {\"choices\":[{\"delta\":{\"role\":\"assistant\",\"content\":\"\"}}]}\n\n"u8);
        Assert.IsFalse(session.HasObservedOutputContent);

        session.Observe("data: {\"choices\":[{\"delta\":{\"content\":\"synthetic\"}}]}\n\n"u8);
        Assert.IsTrue(session.HasObservedOutputContent);
    }

    [TestMethod]
    public void NonStreamingResponseCannotClaimTokenArrivalTiming()
    {
        IBackendTelemetrySession session = BackendTelemetryAdapters
            .Create(BackendKind.Ollama)
            .CreateSession("application/json");

        session.Observe("{\"choices\":[{\"message\":{\"content\":\"synthetic\"}}]}"u8);
        _ = session.Complete();

        Assert.IsFalse(session.HasObservedOutputContent);
    }

    [TestMethod]
    public void LlamaCppFixturePreservesNativeTimingsWithoutRenamingThemAsCommonMetrics()
    {
        BackendResponseTelemetry telemetry = ParseFixture(
            BackendKind.LlamaCpp,
            "llama-cpp-nonstreaming.json",
            "application/json");

        Dictionary<BackendMetricKey, MetricValue> metrics = telemetry.BackendSpecificMetrics
            .ToDictionary(item => item.Key, item => item.Metric);
        Assert.HasCount(7, metrics);
        Assert.AreEqual(
            "cache_n",
            telemetry.BackendSpecificMetrics.Single(
                item => item.Key == BackendMetricKey.LlamaCppCachedPromptTokens).NativeName.Value);
        Assert.AreEqual(
            "predicted_per_second",
            telemetry.BackendSpecificMetrics.Single(
                item => item.Key == BackendMetricKey.LlamaCppPredictedTokensPerSecond).NativeName.Value);
        Assert.AreEqual(236, metrics[BackendMetricKey.LlamaCppCachedPromptTokens].Value);
        Assert.AreEqual(MetricUnit.TokenCount, metrics[BackendMetricKey.LlamaCppCachedPromptTokens].Unit);
        Assert.AreEqual(30.958m, metrics[BackendMetricKey.LlamaCppPromptMilliseconds].Value);
        Assert.AreEqual(MetricUnit.Milliseconds, metrics[BackendMetricKey.LlamaCppPromptMilliseconds].Unit);
        Assert.AreEqual(52.94494935437416m, metrics[BackendMetricKey.LlamaCppPredictedTokensPerSecond].Value);
        Assert.AreEqual(
            MetricUnit.TokensPerSecond,
            metrics[BackendMetricKey.LlamaCppPredictedTokensPerSecond].Unit);
        Assert.IsTrue(metrics.Values.All(metric => metric.Source == MetricSource.BackendExtension));
        AssertCommonBackendMetric(telemetry.CachedPromptTokens, 236, MetricUnit.TokenCount);
        AssertCommonBackendMetric(
            telemetry.PromptTokensPerSecond,
            32.301828283480845m,
            MetricUnit.TokensPerSecond);
        AssertCommonBackendMetric(
            telemetry.CompletionTokensPerSecond,
            52.94494935437416m,
            MetricUnit.TokensPerSecond);
    }

    [TestMethod]
    public void OpenAiUsageDetailsExposeOnlyCachedAndReasoningTokenCounters()
    {
        byte[] fixture = File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "openai-chat",
            "v2",
            "usage-details.json"));
        IBackendTelemetrySession session = BackendTelemetryAdapters
            .Create(BackendKind.Ollama)
            .CreateSession("application/json");
        session.Observe(fixture);

        BackendResponseTelemetry telemetry = session.Complete();

        AssertCommonTokenMetric(telemetry.CachedPromptTokens, 80);
        AssertCommonTokenMetric(telemetry.ReasoningTokens, 12);
        string serialized = System.Text.Json.JsonSerializer.Serialize(telemetry);
        Assert.DoesNotContain("FORBIDDEN_REASONING_SENTINEL", serialized, StringComparison.Ordinal);
    }

    [TestMethod]
    public void MissingUsageRemainsUnavailableAndDoesNotFabricateZero()
    {
        IBackendTelemetryAdapter adapter = BackendTelemetryAdapters.Create(BackendKind.LmStudio);
        IBackendTelemetrySession session = adapter.CreateSession("application/json");
        session.Observe("{\"model\":\"fixture-model\",\"choices\":[]}"u8);

        BackendResponseTelemetry telemetry = session.Complete();

        Assert.IsNull(telemetry.PromptTokens.Value);
        Assert.IsNull(telemetry.CompletionTokens.Value);
        Assert.IsNull(telemetry.TotalTokens.Value);
        Assert.AreEqual(MetricQuality.Unavailable, telemetry.PromptTokens.Quality);
        Assert.AreEqual(MetricQuality.Unavailable, telemetry.CompletionTokens.Quality);
        Assert.AreEqual(MetricQuality.Unavailable, telemetry.TotalTokens.Quality);
        Assert.AreEqual(MetricQuality.Unavailable, telemetry.CachedPromptTokens.Quality);
        Assert.AreEqual(MetricQuality.Unavailable, telemetry.ReasoningTokens.Quality);
        Assert.AreEqual(MetricQuality.Unavailable, telemetry.PromptTokensPerSecond.Quality);
        Assert.AreEqual(MetricQuality.Unavailable, telemetry.CompletionTokensPerSecond.Quality);
    }

    [TestMethod]
    public void MissingTotalIsCalculatedOnlyFromTwoExactTokenInputs()
    {
        IBackendTelemetryAdapter adapter = BackendTelemetryAdapters.Create(BackendKind.Ollama);
        IBackendTelemetrySession session = adapter.CreateSession("application/json");
        session.Observe("{\"usage\":{\"prompt_tokens\":5,\"completion_tokens\":8}}"u8);

        BackendResponseTelemetry telemetry = session.Complete();

        Assert.AreEqual(13, telemetry.TotalTokens.Value);
        Assert.AreEqual(MetricQuality.Calculated, telemetry.TotalTokens.Quality);
        Assert.AreEqual("sum-prompt-completion-v1", telemetry.TotalTokens.DerivationVersion);
    }

    [TestMethod]
    public void VeryLargeContentStringIsDiscardedWhileTrailingUsageStillParses()
    {
        IBackendTelemetryAdapter adapter = BackendTelemetryAdapters.Create(BackendKind.Ollama);
        IBackendTelemetrySession session = adapter.CreateSession("application/json");
        session.Observe("{\"choices\":[{\"message\":{\"content\":\""u8);
        byte[] content = Encoding.UTF8.GetBytes(new string('x', 1024 * 1024));
        for (int offset = 0; offset < content.Length; offset += 8191)
        {
            session.Observe(content.AsSpan(offset, Math.Min(8191, content.Length - offset)));
        }

        session.Observe("\"}}],\"usage\":{\"prompt_tokens\":2,\"completion_tokens\":3,\"total_tokens\":5}}"u8);

        BackendResponseTelemetry telemetry = session.Complete();

        AssertCommonTokenMetric(telemetry.TotalTokens, 5);
    }

    [TestMethod]
    public void MalformedOrMisplacedMetricsCannotBecomeExactTelemetry()
    {
        string oversizedProperty = new('a', 300);
        string[] invalidDocuments =
        [
            $"{{\"{oversizedProperty}\":{{\"usage\":{{\"prompt_tokens\":1,\"completion_tokens\":2,\"total_tokens\":3}}}}}}",
            "{\"usage\":{\"prompt_tokens\":1.5,\"completion_tokens\":2,\"total_tokens\":3.5}}",
            "{\"usage\":{\"prompt_tokens\":1,\"completion_tokens\":2,}}",
            "{\"a\":" + new string('[', 65) + new string(']', 65) + "}",
        ];

        foreach (string document in invalidDocuments)
        {
            IBackendTelemetrySession session = BackendTelemetryAdapters
                .Create(BackendKind.Ollama)
                .CreateSession("application/json");
            session.Observe(Encoding.UTF8.GetBytes(document));

            BackendResponseTelemetry telemetry = session.Complete();

            Assert.AreEqual(MetricQuality.Unavailable, telemetry.PromptTokens.Quality, document[..Math.Min(40, document.Length)]);
            Assert.AreEqual(MetricQuality.Unavailable, telemetry.CompletionTokens.Quality);
            Assert.AreEqual(MetricQuality.Unavailable, telemetry.TotalTokens.Quality);
        }
    }

    [TestMethod]
    [DataRow("cold-nonstreaming.json", ModelLoadDisposition.Cold, 2656, 646, 586)]
    [DataRow("warm-nonstreaming.json", ModelLoadDisposition.Warm, 0, 700, 40)]
    public void LmStudioNativeNonStreamingFixturesExposeExactColdWarmEvidence(
        string fixtureName,
        ModelLoadDisposition expectedDisposition,
        double expectedLoadMilliseconds,
        int expectedInput,
        int expectedOutput)
    {
        BackendResponseTelemetry telemetry = ParseLmStudioNativeFixture(fixtureName, "application/json");

        Assert.AreEqual(expectedDisposition, telemetry.ModelLoadDisposition);
        Assert.AreEqual((decimal)expectedLoadMilliseconds, telemetry.ModelLoadTime.Value);
        Assert.AreEqual(MetricQuality.Exact, telemetry.ModelLoadTime.Quality);
        Assert.AreEqual(MetricSource.BackendExtension, telemetry.ModelLoadTime.Source);
        Assert.AreEqual(expectedInput, telemetry.PromptTokens.Value);
        Assert.AreEqual(expectedOutput, telemetry.CompletionTokens.Value);
        Assert.AreEqual(expectedInput + expectedOutput, telemetry.TotalTokens.Value);
        Assert.AreEqual(MetricQuality.Calculated, telemetry.TotalTokens.Quality);
        Assert.AreEqual("lmstudio-community/qwen2.5", telemetry.Model?.Value);
        Assert.DoesNotContain(
            "FORBIDDEN_NATIVE_CONTENT_SENTINEL",
            System.Text.Json.JsonSerializer.Serialize(telemetry),
            StringComparison.Ordinal);
    }

    [TestMethod]
    [DataRow("cold-streaming.sse", ModelLoadDisposition.Cold, 3250, 329, 268)]
    [DataRow("warm-streaming.sse", ModelLoadDisposition.Warm, 0, 350, 20)]
    public void LmStudioNativeStreamingFixturesExposeExactColdWarmEvidenceAcrossFragments(
        string fixtureName,
        ModelLoadDisposition expectedDisposition,
        double expectedLoadMilliseconds,
        int expectedInput,
        int expectedOutput)
    {
        BackendResponseTelemetry telemetry = ParseLmStudioNativeFixture(fixtureName, "text/event-stream");

        Assert.AreEqual(expectedDisposition, telemetry.ModelLoadDisposition);
        Assert.AreEqual((decimal)expectedLoadMilliseconds, telemetry.ModelLoadTime.Value);
        Assert.AreEqual(expectedInput, telemetry.PromptTokens.Value);
        Assert.AreEqual(expectedOutput, telemetry.CompletionTokens.Value);
        Assert.AreEqual(MetricQuality.Exact, telemetry.CompletionTokensPerSecond.Quality);
        Assert.DoesNotContain(
            "FORBIDDEN_STREAM_CONTENT_SENTINEL",
            System.Text.Json.JsonSerializer.Serialize(telemetry),
            StringComparison.Ordinal);
    }

    [TestMethod]
    public void LmStudioNativeStreamingSessionSignalsOnlyMessageContentAndFailsClosedWithoutTerminalStats()
    {
        IBackendTelemetrySession session = BackendTelemetryAdapters
            .CreateLmStudioNative()
            .CreateSession("text/event-stream");
        session.Observe("event: reasoning.delta\ndata: {\"type\":\"reasoning.delta\",\"content\":\"opaque\"}\n\n"u8);
        Assert.IsFalse(session.HasObservedOutputContent);
        session.Observe("event: message.delta\ndata: {\"type\":\"message.delta\",\"content\":\"synthetic\"}\n\n"u8);
        Assert.IsTrue(session.HasObservedOutputContent);

        BackendResponseTelemetry telemetry = session.Complete();

        Assert.AreEqual(ModelLoadDisposition.Unavailable, telemetry.ModelLoadDisposition);
        Assert.AreEqual(MetricQuality.Unavailable, telemetry.ModelLoadTime.Quality);
        Assert.AreEqual(MetricQuality.Unavailable, telemetry.PromptTokens.Quality);
    }

    private static BackendResponseTelemetry ParseFixture(
        BackendKind backend,
        string fixtureName,
        string mediaType)
    {
        byte[] fixture = File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "epic02",
            "v1",
            fixtureName));
        IBackendTelemetrySession session = BackendTelemetryAdapters.Create(backend).CreateSession(mediaType);

        int offset = 0;
        while (offset < fixture.Length)
        {
            int length = Math.Min(1 + (offset % 23), fixture.Length - offset);
            session.Observe(fixture.AsSpan(offset, length));
            offset += length;
        }

        return session.Complete();
    }

    private static BackendResponseTelemetry ParseLmStudioNativeFixture(string fixtureName, string mediaType)
    {
        byte[] fixture = File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "epic04",
            "lm-studio-native-v1",
            fixtureName));
        IBackendTelemetrySession session = BackendTelemetryAdapters.CreateLmStudioNative().CreateSession(mediaType);

        int offset = 0;
        while (offset < fixture.Length)
        {
            int length = Math.Min(1 + (offset % 19), fixture.Length - offset);
            session.Observe(fixture.AsSpan(offset, length));
            offset += length;
        }

        return session.Complete();
    }

    private static void AssertCommonTokenMetric(MetricValue metric, decimal expected)
    {
        Assert.AreEqual(expected, metric.Value);
        Assert.AreEqual(MetricUnit.TokenCount, metric.Unit);
        Assert.AreEqual(MetricQuality.Exact, metric.Quality);
        Assert.AreEqual(MetricSource.OpenAiUsage, metric.Source);
    }

    private static void AssertCommonBackendMetric(
        MetricValue metric,
        decimal expected,
        MetricUnit unit)
    {
        Assert.AreEqual(expected, metric.Value);
        Assert.AreEqual(unit, metric.Unit);
        Assert.AreEqual(MetricQuality.Exact, metric.Quality);
        Assert.AreEqual(MetricSource.BackendExtension, metric.Source);
    }
}
