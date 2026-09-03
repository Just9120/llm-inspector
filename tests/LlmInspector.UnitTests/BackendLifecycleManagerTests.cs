using LlmInspector.Application;
using LlmInspector.Domain;

namespace LlmInspector.UnitTests;

[TestClass]
public sealed class BackendLifecycleManagerTests
{
    [TestMethod]
    public async Task StartRequiresExactConfirmationAndIsIdempotent()
    {
        Fixture fixture = new();
        Assert.IsFalse((await fixture.Manager.StartAsync()).Succeeded);

        await fixture.DiscoverAndConfirmAsync();
        BackendLifecycleResult first = await fixture.Manager.StartAsync();
        BackendLifecycleResult second = await fixture.Manager.StartAsync();

        Assert.IsTrue(first.Succeeded);
        Assert.IsTrue(second.Succeeded);
        Assert.AreEqual(BackendLifecycleState.Running, second.Snapshot.State);
        Assert.AreEqual(1, fixture.Runtime.StartCalls);
    }

    [TestMethod]
    public async Task PortConflictIsReportedWithoutChangingForeignProcess()
    {
        Fixture fixture = new();
        await fixture.DiscoverAndConfirmAsync();
        fixture.Runtime.EndpointOwner = fixture.Runtime.Identity with { ProcessId = 90210 };

        BackendLifecycleResult result = await fixture.Manager.StartAsync();

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.Snapshot.Message, "90210");
        Assert.AreEqual(0, fixture.Runtime.StartCalls);
        Assert.AreEqual(0, fixture.Runtime.StopCalls);
    }

    [TestMethod]
    public async Task StopAndRestartAreBlockedWhileInspectorRequestsAreActive()
    {
        Fixture fixture = new();
        await fixture.DiscoverAndConfirmAsync();
        await fixture.Manager.StartAsync();
        fixture.Live.ActiveCount = 2;

        BackendLifecycleResult stop = await fixture.Manager.StopAsync();
        BackendLifecycleResult restart = await fixture.Manager.RestartAsync();

        Assert.IsFalse(stop.Succeeded);
        Assert.IsFalse(restart.Succeeded);
        StringAssert.Contains(restart.Snapshot.Message, "2");
        Assert.AreEqual(0, fixture.Runtime.StopCalls);
    }

    [TestMethod]
    public async Task RestartStopsExactOwnedProcessAndReusesLastConfiguration()
    {
        Fixture fixture = new();
        await fixture.DiscoverAndConfirmAsync();
        Assert.IsTrue(fixture.Manager.SetParameter("parallel", "4").Succeeded);
        await fixture.Manager.StartAsync();

        BackendLifecycleResult result = await fixture.Manager.RestartAsync();

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(1, fixture.Runtime.StopCalls);
        Assert.AreEqual(2, fixture.Runtime.StartCalls);
        Assert.AreEqual("4", fixture.Adapter.LastParameters!["parallel"]);
    }

    [TestMethod]
    public async Task ModelLoadRequiresNoActiveRequestsAndExactConfirmation()
    {
        Fixture fixture = new();
        await fixture.DiscoverAndConfirmAsync();
        await fixture.Manager.StartAsync();
        fixture.Live.ActiveCount = 1;

        Assert.IsFalse((await fixture.Manager.LoadModelAsync("model-a")).Succeeded);
        fixture.Live.ActiveCount = 0;
        BackendLifecycleResult loaded = await fixture.Manager.LoadModelAsync("model-a");

        Assert.IsTrue(loaded.Succeeded);
        Assert.AreEqual("model-a", loaded.Snapshot.Model);
        Assert.AreEqual(1, fixture.Adapter.LoadCalls);
        Assert.AreEqual(1, fixture.Adapter.ConfirmModelCalls);
    }

    [TestMethod]
    public async Task ParametersAreAllowlistedValidatedAndPortChangeRequiresReconfirmation()
    {
        Fixture fixture = new();
        await fixture.DiscoverAndConfirmAsync();

        Assert.IsFalse(fixture.Manager.SetParameter("arbitrary-args", "--danger").Succeeded);
        Assert.IsFalse(fixture.Manager.SetParameter("parallel", "0").Succeeded);
        Assert.IsTrue(fixture.Manager.SetParameter("parallel", "8").Succeeded);
        BackendLifecycleResult port = fixture.Manager.SetParameter("local-port", "12000");

        Assert.IsTrue(port.Succeeded);
        Assert.AreEqual(12000, port.Snapshot.Target!.Endpoint.Port);
        Assert.AreEqual(BackendLifecycleState.TargetPendingConfirmation, port.Snapshot.State);
        Assert.IsFalse((await fixture.Manager.StartAsync()).Succeeded);
        Assert.IsTrue(fixture.Manager.ConfirmTarget(port.Snapshot.Target.ConfirmationToken).Succeeded);
    }

    [TestMethod]
    public async Task FailedReadinessCleansOnlyTheCreatedOwnedProcess()
    {
        Fixture fixture = new();
        fixture.Adapter.Ready = false;
        await fixture.DiscoverAndConfirmAsync();

        BackendLifecycleResult result = await fixture.Manager.StartAsync();

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(BackendLifecycleState.Faulted, result.Snapshot.State);
        Assert.AreEqual(1, fixture.Runtime.StartCalls);
        Assert.AreEqual(1, fixture.Runtime.StopCalls);
        Assert.AreEqual(fixture.Runtime.Identity, fixture.Runtime.LastStopped);
    }

    [TestMethod]
    public async Task CrashIsTypedAndNeverTriggersAutomaticRestart()
    {
        Fixture fixture = new();
        await fixture.DiscoverAndConfirmAsync();
        await fixture.Manager.StartAsync();
        fixture.Runtime.ProcessAlive = false;

        BackendLifecycleSnapshot snapshot = await fixture.Manager.RefreshAsync();

        Assert.AreEqual(BackendLifecycleState.Crashed, snapshot.State);
        StringAssert.Contains(snapshot.Message, "Автоперезапуск отключён");
        Assert.AreEqual(1, fixture.Runtime.StartCalls);
    }

    [TestMethod]
    public async Task ConcurrentStartsAreSerializedPerBackend()
    {
        Fixture fixture = new();
        await fixture.DiscoverAndConfirmAsync();

        BackendLifecycleResult[] results = await Task.WhenAll(
            Enumerable.Range(0, 4).Select(_ => fixture.Manager.StartAsync().AsTask()));

        Assert.IsTrue(results.All(result => result.Succeeded));
        Assert.AreEqual(1, fixture.Runtime.StartCalls);
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            Manager = new BackendLifecycleManager(Adapter, Runtime, Live);
        }

        public FakeAdapter Adapter { get; } = new();

        public FakeRuntime Runtime { get; } = new();

        public FakeLiveRequests Live { get; } = new();

        public BackendLifecycleManager Manager { get; }

        public async Task DiscoverAndConfirmAsync()
        {
            BackendLifecycleResult discovery = await Manager.DiscoverAsync();
            Assert.IsTrue(discovery.Succeeded);
            Assert.IsTrue(Manager.ConfirmTarget(discovery.Snapshot.Target!.ConfirmationToken).Succeeded);
        }

        public void Dispose()
        {
            Manager.Dispose();
        }
    }

    private sealed class FakeAdapter : IBackendLifecycleAdapter
    {
        public BackendLifecycleProfile Profile { get; } = new(
            BackendKind.Ollama,
            "Fake",
            ["runtime.exe"],
            ["--version"],
            new Uri("http://127.0.0.1:11434/"),
            [
                new("local-port", "Порт", "Локальный порт.", BackendParameterKind.WholeNumber, "11434", 1024, 65535),
                new("parallel", "Параллельность", "Число requests.", BackendParameterKind.WholeNumber, null, 1, 64),
            ],
            BackendModelLoadMode.Command);

        public bool Ready { get; set; } = true;

        public int LoadCalls { get; private set; }

        public int ConfirmModelCalls { get; private set; }

        public IReadOnlyDictionary<string, string>? LastParameters { get; private set; }

        public BackendCompatibilityStatus ClassifyVersion(string version) => BackendCompatibilityStatus.Verified;

        public string GetCompatibilityLabel(BackendCompatibilityStatus status) => "Проверено";

        public BackendProcessStartPlan CreateStartPlan(
            BackendLifecycleTarget target,
            IReadOnlyDictionary<string, string> parameters,
            string? model)
        {
            LastParameters = parameters;
            return new BackendProcessStartPlan(
                target.ExecutablePath,
                ["serve"],
                new Dictionary<string, string>(),
                target.Endpoint,
                BackendStartOwnership.AttachedProcess,
                ["runtime.exe"]);
        }

        public BackendCommandPlan? CreateOfficialStopPlan(BackendLifecycleTarget target) => null;

        public ValueTask<bool> ConfirmReadyAsync(
            BackendLifecycleTarget target,
            IBackendLifecycleRuntime runtime,
            CancellationToken cancellationToken) => ValueTask.FromResult(Ready);

        public ValueTask<bool> LoadModelAsync(
            BackendLifecycleTarget target,
            string model,
            IReadOnlyDictionary<string, string> parameters,
            IBackendLifecycleRuntime runtime,
            CancellationToken cancellationToken)
        {
            LoadCalls++;
            return ValueTask.FromResult(true);
        }

        public ValueTask<bool> ConfirmModelAsync(
            BackendLifecycleTarget target,
            string model,
            IBackendLifecycleRuntime runtime,
            CancellationToken cancellationToken)
        {
            ConfirmModelCalls++;
            return ValueTask.FromResult(true);
        }
    }

    private sealed class FakeRuntime : IBackendLifecycleRuntime
    {
        public BackendProcessIdentity Identity { get; } = new(42, DateTimeOffset.UnixEpoch, @"C:\runtime.exe");

        public BackendProcessIdentity? EndpointOwner { get; set; }

        public bool ProcessAlive { get; set; }

        public int StartCalls { get; private set; }

        public int StopCalls { get; private set; }

        public BackendProcessIdentity? LastStopped { get; private set; }

        public ValueTask<string?> ResolveExecutableAsync(
            IReadOnlyList<string> candidates,
            string? manualPath,
            CancellationToken cancellationToken) => ValueTask.FromResult<string?>(@"C:\runtime.exe");

        public ValueTask<BackendCommandResult> ExecuteAsync(
            BackendCommandPlan command,
            CancellationToken cancellationToken) => ValueTask.FromResult(new BackendCommandResult(0, "runtime 1.0", string.Empty));

        public ValueTask<BackendProcessIdentity?> ResolveEndpointOwnerAsync(
            Uri endpoint,
            CancellationToken cancellationToken) => ValueTask.FromResult(EndpointOwner);

        public ValueTask<BackendProcessIdentity> StartAsync(
            BackendProcessStartPlan plan,
            CancellationToken cancellationToken)
        {
            StartCalls++;
            ProcessAlive = true;
            return ValueTask.FromResult(Identity);
        }

        public ValueTask<bool> IsSameProcessAliveAsync(
            BackendProcessIdentity identity,
            CancellationToken cancellationToken) => ValueTask.FromResult(ProcessAlive && identity == Identity);

        public ValueTask StopAsync(
            BackendProcessIdentity identity,
            BackendCommandPlan? officialStop,
            CancellationToken cancellationToken)
        {
            StopCalls++;
            LastStopped = identity;
            ProcessAlive = false;
            return ValueTask.CompletedTask;
        }

        public ValueTask<string> SendHttpAsync(
            HttpMethod method,
            Uri address,
            string? jsonBody,
            CancellationToken cancellationToken) => ValueTask.FromResult("{}");
    }

    private sealed class FakeLiveRequests : ILiveRequestSnapshotSource
    {
        public int ActiveCount { get; set; }

        public LiveRequestCollectionSnapshot GetSnapshot() => new(
            Enumerable.Range(0, ActiveCount)
                .Select(index => new LiveRequestSnapshot(
                    Guid.NewGuid(),
                    ClientKind.GenericUnknown,
                    RequestStageValue.ProtocolObserved(RequestStage.PromptProcessing, "test"),
                    DateTimeOffset.UnixEpoch,
                    MetricValue.Unavailable(MetricUnit.Milliseconds, MetricSource.Inspector, "test"),
                    MetricValue.Unavailable(MetricUnit.Percent, MetricSource.Inspector, "test"),
                    MetricValue.Unavailable(MetricUnit.Milliseconds, MetricSource.Inspector, "test")))
                .ToArray(),
            null);
    }
}
