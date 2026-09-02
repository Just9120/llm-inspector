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

    private static void AssertCommonTokenMetric(MetricValue metric, decimal expected)
    {
        Assert.AreEqual(expected, metric.Value);
        Assert.AreEqual(MetricUnit.TokenCount, metric.Unit);
        Assert.AreEqual(MetricQuality.Exact, metric.Quality);
        Assert.AreEqual(MetricSource.OpenAiUsage, metric.Source);
    }
}
