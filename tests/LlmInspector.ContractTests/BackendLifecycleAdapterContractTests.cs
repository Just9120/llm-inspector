using System.Reflection;
using System.Text.Json;
using LlmInspector.Adapters;
using LlmInspector.Application;
using LlmInspector.Domain;

namespace LlmInspector.ContractTests;

[TestClass]
public sealed class BackendLifecycleAdapterContractTests
{
    private static readonly string[] OllamaServeArguments = ["serve"];
    private static readonly string[] OllamaEnvironmentKeys =
        ["OLLAMA_HOST", "OLLAMA_CONTEXT_LENGTH", "OLLAMA_KEEP_ALIVE", "OLLAMA_NUM_PARALLEL", "OLLAMA_MAX_LOADED_MODELS", "OLLAMA_MAX_QUEUE"];
    private static readonly string[] LlamaArguments =
    [
        "--host", "127.0.0.1", "--port", "8080", "--model", @"C:\models\qwen.gguf",
        "--ctx-size", "8192", "--n-gpu-layers", "0", "--threads", "8", "--parallel", "4",
    ];
    private static readonly string[] LmStudioStartArguments =
        ["server", "start", "--port", "1234", "--bind", "127.0.0.1"];
    private static readonly string[] LmStudioLoadArguments =
        ["load", "model-a", "--gpu", "0.75", "--context-length", "8192", "--ttl", "600"];
    private static readonly string[] LmStudioPsArguments = ["ps"];
    private static readonly string[] LmStudioStopArguments = ["server", "stop"];

    [TestMethod]
    public void BuiltInsExposeOnlyTheApprovedParameterAllowlists()
    {
        Dictionary<BackendKind, string[]> expected = new()
        {
            [BackendKind.Ollama] = ["context", "keep-alive", "local-port", "max-loaded", "max-queue", "parallel"],
            [BackendKind.LlamaCpp] = ["context", "cpu-threads", "gpu-layers", "local-port", "parallel"],
            [BackendKind.LmStudio] = ["context", "gpu-offload", "local-port", "model-id", "model-ttl"],
        };

        foreach (IBackendLifecycleAdapter adapter in BackendLifecycleAdapters.CreateAll())
        {
            string[] actual = adapter.Profile.Parameters.Select(parameter => parameter.Key).Order().ToArray();
            CollectionAssert.AreEqual(expected[adapter.Profile.Backend], actual);
            Assert.AreEqual("local-port", adapter.Profile.Parameters.Single(parameter => parameter.Key == "local-port").Key);
        }
    }

    [TestMethod]
    public void CompatibilityCatalogKeepsVerifiedAndPendingTargetsDistinct()
    {
        IBackendLifecycleAdapter ollama = BackendLifecycleAdapters.Create(BackendKind.Ollama);
        IBackendLifecycleAdapter llama = BackendLifecycleAdapters.Create(BackendKind.LlamaCpp);
        IBackendLifecycleAdapter lmStudio = BackendLifecycleAdapters.Create(BackendKind.LmStudio);

        Assert.AreEqual(BackendCompatibilityStatus.Verified, ollama.ClassifyVersion("ollama version 0.33.2"));
        Assert.AreEqual(BackendCompatibilityStatus.Compatible, llama.ClassifyVersion("llama b10516"));
        Assert.AreEqual(BackendCompatibilityStatus.Compatible, lmStudio.ClassifyVersion("lms 0.0.47"));
        Assert.AreEqual(BackendCompatibilityStatus.Compatible, ollama.ClassifyVersion("ollama version 99.0.0"));
        Assert.AreEqual("Проверено", ollama.GetCompatibilityLabel(BackendCompatibilityStatus.Verified));
        Assert.AreEqual("Совместимо", ollama.GetCompatibilityLabel(BackendCompatibilityStatus.Compatible));
        Assert.AreEqual("Только наблюдение", ollama.GetCompatibilityLabel(BackendCompatibilityStatus.ObservationOnly));
        Assert.AreEqual("Не поддерживается", ollama.GetCompatibilityLabel(BackendCompatibilityStatus.Unsupported));
    }

    [TestMethod]
    public void OllamaStartUsesOnlyDocumentedLoopbackEnvironment()
    {
        IBackendLifecycleAdapter adapter = BackendLifecycleAdapters.Create(BackendKind.Ollama);
        BackendProcessStartPlan plan = adapter.CreateStartPlan(
            Target(adapter, @"C:\Ollama\ollama.exe"),
            new Dictionary<string, string>
            {
                ["local-port"] = "11434",
                ["context"] = "8192",
                ["keep-alive"] = "300",
                ["parallel"] = "4",
                ["max-loaded"] = "2",
                ["max-queue"] = "64",
            },
            null);

        CollectionAssert.AreEqual(OllamaServeArguments, plan.Arguments.ToArray());
        Assert.AreEqual("127.0.0.1:11434", plan.Environment["OLLAMA_HOST"]);
        Assert.AreEqual("8192", plan.Environment["OLLAMA_CONTEXT_LENGTH"]);
        Assert.AreEqual("300s", plan.Environment["OLLAMA_KEEP_ALIVE"]);
        Assert.AreEqual("4", plan.Environment["OLLAMA_NUM_PARALLEL"]);
        CollectionAssert.AreEquivalent(
            OllamaEnvironmentKeys,
            plan.Environment.Keys.ToArray());
    }

    [TestMethod]
    public void LlamaCppStartUsesArgumentListAndLiteralLoopbackOnly()
    {
        IBackendLifecycleAdapter adapter = BackendLifecycleAdapters.Create(BackendKind.LlamaCpp);
        BackendProcessStartPlan plan = adapter.CreateStartPlan(
            Target(adapter, @"C:\llama\llama-server.exe"),
            new Dictionary<string, string>
            {
                ["context"] = "8192",
                ["gpu-layers"] = "off",
                ["cpu-threads"] = "8",
                ["parallel"] = "4",
            },
            @"C:\models\qwen.gguf");

        CollectionAssert.AreEqual(
            LlamaArguments,
            plan.Arguments.ToArray());
        Assert.AreEqual(BackendStartOwnership.AttachedProcess, plan.Ownership);
        Assert.AreEqual("127.0.0.1", plan.Endpoint.Host);
    }

    [TestMethod]
    public async Task LmStudioUsesOfficialServerAndTypedLoadCommands()
    {
        IBackendLifecycleAdapter adapter = BackendLifecycleAdapters.Create(BackendKind.LmStudio);
        BackendLifecycleTarget target = Target(adapter, @"C:\LM Studio\lms.exe");
        BackendProcessStartPlan start = adapter.CreateStartPlan(target, new Dictionary<string, string>(), null);
        RecordingRuntime runtime = new("model-a");

        bool loaded = await adapter.LoadModelAsync(
            target,
            "model-a",
            new Dictionary<string, string>
            {
                ["gpu-offload"] = "0.75",
                ["context"] = "8192",
                ["model-ttl"] = "600",
            },
            runtime,
            CancellationToken.None);
        bool confirmed = await adapter.ConfirmModelAsync(target, "model-a", runtime, CancellationToken.None);

        CollectionAssert.AreEqual(
            LmStudioStartArguments,
            start.Arguments.ToArray());
        Assert.AreEqual(BackendStartOwnership.DetachedListener, start.Ownership);
        CollectionAssert.AreEqual(
            LmStudioLoadArguments,
            runtime.Commands[0].Arguments.ToArray());
        CollectionAssert.AreEqual(LmStudioPsArguments, runtime.Commands[1].Arguments.ToArray());
        Assert.IsTrue(loaded);
        Assert.IsTrue(confirmed);
        CollectionAssert.AreEqual(
            LmStudioStopArguments,
            adapter.CreateOfficialStopPlan(target)!.Arguments.ToArray());
    }

    [TestMethod]
    public async Task OllamaModelLoadUsesNativeApiAndRequiresExactIdentityConfirmation()
    {
        IBackendLifecycleAdapter adapter = BackendLifecycleAdapters.Create(BackendKind.Ollama);
        BackendLifecycleTarget target = Target(adapter, @"C:\Ollama\ollama.exe");
        RecordingRuntime runtime = new("model-a");

        Assert.IsTrue(await adapter.LoadModelAsync(
            target,
            "model-a",
            new Dictionary<string, string>(),
            runtime,
            CancellationToken.None));
        Assert.IsTrue(await adapter.ConfirmModelAsync(target, "model-a", runtime, CancellationToken.None));

        Assert.AreEqual(HttpMethod.Post, runtime.HttpCalls[0].Method);
        Assert.AreEqual("/api/generate", runtime.HttpCalls[0].Address.AbsolutePath);
        StringAssert.Contains(runtime.HttpCalls[0].Body!, "\"model\":\"model-a\"");
        Assert.AreEqual("/api/tags", runtime.HttpCalls[1].Address.AbsolutePath);
    }

    [TestMethod]
    public void EmbeddedCompatibilityMatrixCarriesRequiredEvidenceFields()
    {
        Assembly assembly = typeof(BackendLifecycleAdapters).Assembly;
        string resource = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith("runtime-compatibility.json", StringComparison.Ordinal));
        using Stream stream = assembly.GetManifestResourceStream(resource)!;
        using JsonDocument document = JsonDocument.Parse(stream);

        Assert.AreEqual(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        JsonElement[] entries = document.RootElement.GetProperty("entries").EnumerateArray().ToArray();
        Assert.AreEqual(3, entries.Length);
        foreach (JsonElement entry in entries)
        {
            Assert.IsTrue(entry.GetProperty("capabilities").GetArrayLength() > 0);
            Assert.IsTrue(entry.GetProperty("windows").GetArrayLength() > 0);
            Assert.IsTrue(entry.TryGetProperty("verifiedAtUtc", out _));
            Assert.IsFalse(string.IsNullOrWhiteSpace(entry.GetProperty("inspectorRevision").GetString()));
            Assert.IsTrue(entry.GetProperty("evidence").GetArrayLength() > 0);
            Assert.IsTrue(entry.GetProperty("limitations").GetArrayLength() > 0);
        }
    }

    private static BackendLifecycleTarget Target(IBackendLifecycleAdapter adapter, string path) => new(
        adapter.Profile.Backend,
        path,
        "test-version",
        adapter.Profile.DefaultEndpoint,
        BackendCompatibilityStatus.Compatible,
        "Совместимо",
        "token");

    private sealed class RecordingRuntime(string model) : IBackendLifecycleRuntime
    {
        public List<BackendCommandPlan> Commands { get; } = [];

        public List<HttpCall> HttpCalls { get; } = [];

        public ValueTask<string?> ResolveExecutableAsync(
            IReadOnlyList<string> candidates,
            string? manualPath,
            CancellationToken cancellationToken) => ValueTask.FromResult<string?>(null);

        public ValueTask<BackendCommandResult> ExecuteAsync(
            BackendCommandPlan command,
            CancellationToken cancellationToken)
        {
            Commands.Add(command);
            string output = command.Arguments.SequenceEqual(["ps"]) ? model : string.Empty;
            return ValueTask.FromResult(new BackendCommandResult(0, output, string.Empty));
        }

        public ValueTask<BackendProcessIdentity?> ResolveEndpointOwnerAsync(
            Uri endpoint,
            CancellationToken cancellationToken) => ValueTask.FromResult<BackendProcessIdentity?>(null);

        public ValueTask<BackendProcessIdentity> StartAsync(
            BackendProcessStartPlan plan,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<bool> IsSameProcessAliveAsync(
            BackendProcessIdentity identity,
            CancellationToken cancellationToken) => ValueTask.FromResult(false);

        public ValueTask StopAsync(
            BackendProcessIdentity identity,
            BackendCommandPlan? officialStop,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask<string> SendHttpAsync(
            HttpMethod method,
            Uri address,
            string? jsonBody,
            CancellationToken cancellationToken)
        {
            HttpCalls.Add(new HttpCall(method, address, jsonBody));
            return ValueTask.FromResult($"{{\"models\":[{{\"name\":\"{model}\"}}]}}");
        }
    }

    private sealed record HttpCall(HttpMethod Method, Uri Address, string? Body);
}
