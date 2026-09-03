using System.Collections.Concurrent;
using System.Runtime.Versioning;
using LlmInspector.Application;
using LlmInspector.Domain;
using LlmInspector.Storage.Sqlite;

namespace LlmInspector.WindowsTests;

[TestClass]
public sealed class BackgroundBehaviorTests
{
    private const string SourceVersion = "background-test-v1";
    private static readonly bool[] ExpectedAutostartChanges = [true, false];
    private static readonly bool[] ExpectedTrayOpenTargets = [false, true];

    [TestMethod]
    public void WindowCloseHidesUntilExplicitTrayExit()
    {
        App.BackgroundLifetimeController lifetime = new(backgroundAvailable: true);

        Assert.AreEqual(App.BackgroundCloseAction.HideAndContinue, lifetime.OnWindowClosing());
        Assert.IsFalse(lifetime.IsExitRequested);

        lifetime.RequestExit();

        Assert.IsTrue(lifetime.IsExitRequested);
        Assert.AreEqual(App.BackgroundCloseAction.ExitProcess, lifetime.OnWindowClosing());
    }

    [TestMethod]
    public void TrayCommandsOpenApplicationSettingsPauseAndExit()
    {
        List<bool> opened = [];
        int toggles = 0;
        int exits = 0;
        App.TrayCommandRouter router = new(opened.Add, () => toggles++, () => exits++);

        router.Execute(App.TrayCommand.OpenApplication);
        router.Execute(App.TrayCommand.OpenNotificationSettings);
        router.Execute(App.TrayCommand.ToggleNotifications);
        router.Execute(App.TrayCommand.Exit);

        CollectionAssert.AreEqual(ExpectedTrayOpenTargets, opened);
        Assert.AreEqual(1, toggles);
        Assert.AreEqual(1, exits);
    }

    [TestMethod]
    public async Task HistoryAcceptsNewRecordsAfterWindowCloseBecomesBackgroundHide()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"llm-inspector-background-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            App.BackgroundLifetimeController lifetime = new(backgroundAvailable: true);
            Assert.AreEqual(App.BackgroundCloseAction.HideAndContinue, lifetime.OnWindowClosing());

            await using (SqliteTechnicalHistoryStore store = new(Path.Combine(directory, "history.db")))
            {
                await store.InitializeAsync();
                ProxyObservation observation = Observation(
                    Guid.NewGuid(),
                    TimeSpan.FromSeconds(2),
                    ProxyOutcome.Completed,
                    ProxyErrorType.None,
                    BackendResponseTelemetry.Unavailable(BackendKind.Ollama, SourceVersion));

                await store.RecordAsync(observation, CancellationToken.None);

                RequestHistoryItem stored = AssertSingle(await store.QueryRequestsAsync(new HistoryFilter()));
                Assert.AreEqual(observation.RequestId, stored.RequestId);
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task AutostartCanBeEnabledAndDisabledThroughAtomicSettingsService()
    {
        InMemorySettingsStore store = new(BackgroundSettingsWithAutostart(enabled: true));
        FakeAutostartRegistration autostart = new(enabled: false);
        App.BackgroundSettingsService service = new(store, autostart);
        await service.InitializeAsync();
        Assert.IsFalse(service.Current.AutostartEnabled, "Actual HKCU-equivalent state owns the displayed value.");

        await service.SaveAsync(BackgroundSettingsWithAutostart(enabled: true));
        Assert.IsTrue(autostart.Enabled);
        Assert.IsTrue(service.Current.AutostartEnabled);

        await service.SaveAsync(BackgroundSettingsWithAutostart(enabled: false));
        Assert.IsFalse(autostart.Enabled);
        Assert.IsFalse(service.Current.AutostartEnabled);
        CollectionAssert.AreEqual(ExpectedAutostartChanges, autostart.Changes.ToArray());
    }

    [TestMethod]
    public async Task JsonSettingsRoundTripAndRejectUnknownFields()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"llm-inspector-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.json");
        try
        {
            App.JsonBackgroundSettingsStore store = new(path);
            App.BackgroundSettings settings = new()
            {
                AutostartEnabled = true,
                Notifications = new App.NotificationSettings
                {
                    BackendUnavailable = true,
                    LongOperationCompleted = true,
                    RecurringError = false,
                    HighContextUsage = true,
                    SilentMode = false,
                },
            };

            await store.SaveAsync(settings);
            Assert.AreEqual(settings, await store.LoadAsync());
            Assert.IsFalse(Directory.EnumerateFiles(directory, "*.tmp").Any());

            await File.WriteAllTextAsync(
                path,
                """
                {"schema_version":1,"autostart_enabled":false,"notifications":{},"unknown_security_key":true}
                """);
            _ = await Assert.ThrowsExactlyAsync<InvalidDataException>(async () => await store.LoadAsync());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task SettingsFailureRollsBackAutostartState()
    {
        FakeAutostartRegistration autostart = new(enabled: false);
        App.BackgroundSettingsService service = new(
            new FailingSettingsStore(BackgroundSettingsWithAutostart(enabled: false)),
            autostart);
        await service.InitializeAsync();

        _ = await Assert.ThrowsExactlyAsync<IOException>(async () =>
            await service.SaveAsync(BackgroundSettingsWithAutostart(enabled: true)));

        Assert.IsFalse(service.Current.AutostartEnabled);
        Assert.IsFalse(autostart.Enabled);
        CollectionAssert.AreEqual(ExpectedAutostartChanges, autostart.Changes.ToArray());
    }

    [TestMethod]
    [SupportedOSPlatform("windows")]
    public void WindowsAutostartCommandUsesQuotedExecutableAndBackgroundFlag()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows autostart command is a Windows-only boundary.");
        }

        string executable = Path.Combine(Path.GetTempPath(), "LLM Inspector", "LlmInspector.App.exe");
        string command = App.WindowsAutostartRegistration.CreateCommand(executable);

        Assert.AreEqual($"\"{Path.GetFullPath(executable)}\" --background", command);
        Assert.IsTrue(App.AppLaunchConfiguration.Parse(["--background"]).StartInBackground);
        _ = Assert.ThrowsExactly<ArgumentException>(() =>
            App.AppLaunchConfiguration.Parse(["--background", "--background"]));
    }

    [TestMethod]
    public void RuleEngineProducesFourContentFreeTechnicalEventTypes()
    {
        App.NotificationRuleEngine rules = new();
        ProxyObservation backendFailure = Observation(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            TimeSpan.FromSeconds(1),
            ProxyOutcome.BackendUnavailable,
            ProxyErrorType.ConnectionRefused,
            BackendResponseTelemetry.Unavailable(BackendKind.Ollama, SourceVersion));
        BackendResponseTelemetry highContext = BackendResponseTelemetry.Unavailable(
            BackendKind.Ollama,
            SourceVersion) with
        {
            ContextUsageTokens = Exact(900, MetricUnit.TokenCount),
            ContextLimitTokens = Exact(1_000, MetricUnit.TokenCount),
        };
        ProxyObservation successfulLong = Observation(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            TimeSpan.FromMinutes(1),
            ProxyOutcome.Completed,
            ProxyErrorType.None,
            highContext);

        App.NotificationEventType[] events = rules.Evaluate(backendFailure, errorOccurrenceCount: 2)
            .Concat(rules.Evaluate(successfulLong, errorOccurrenceCount: 0))
            .Select(candidate => candidate.EventType)
            .ToArray();

        CollectionAssert.AreEquivalent(Enum.GetValues<App.NotificationEventType>(), events);
        Assert.IsTrue(events.Length == 4);
    }

    [TestMethod]
    public void EventTogglesAndSilentModeAreAppliedIndependently()
    {
        RecordingPublisher publisher = new();
        App.NotificationDispatcher dispatcher = new(publisher);
        App.NotificationCandidate[] candidates = Enum.GetValues<App.NotificationEventType>()
            .Select((eventType, index) => Candidate(eventType, $"event-{index}"))
            .ToArray();
        App.NotificationSettings settings = new()
        {
            BackendUnavailable = true,
            LongOperationCompleted = false,
            RecurringError = true,
            HighContextUsage = false,
            SilentMode = true,
        };

        IReadOnlyList<App.NotificationDispatchDecision> decisions = dispatcher.Dispatch(
            candidates,
            settings,
            DateTimeOffset.UnixEpoch);

        CollectionAssert.AreEquivalent(
            new[] { App.NotificationEventType.BackendUnavailable, App.NotificationEventType.RecurringError },
            publisher.Published.Select(item => item.EventType).ToArray());
        Assert.IsTrue(publisher.Published.All(item => item.Silent));
        Assert.AreEqual(2, decisions.Count(item => item.Result == App.NotificationDispatchResult.Published));
        Assert.AreEqual(2, decisions.Count(item => item.Result == App.NotificationDispatchResult.Disabled));
    }

    [TestMethod]
    public void NotificationTextDoesNotRenderOpaqueDeduplicationKey()
    {
        string canary = $"privatecontent{Guid.NewGuid():N}";
        App.NotificationCandidate candidate = Candidate(App.NotificationEventType.BackendUnavailable, canary);

        App.DesktopNotification notification = App.NotificationTextPresenter.Format(candidate, silent: true);

        Assert.DoesNotContain(canary, notification.Title, StringComparison.Ordinal);
        Assert.DoesNotContain(canary, notification.Body, StringComparison.Ordinal);
        Assert.IsTrue(notification.Silent);
        Assert.IsFalse(typeof(App.NotificationCandidate).GetProperties().Any(property =>
            property.Name is "Title" or "Body"));
    }

    [TestMethod]
    public void TrayPauseControlSuppressesUntilResumed()
    {
        RecordingPublisher publisher = new();
        App.NotificationDispatcher dispatcher = new(publisher);
        App.NotificationSettings settings = new() { BackendUnavailable = true };
        App.NotificationCandidate candidate = Candidate(App.NotificationEventType.BackendUnavailable, "pause-test");

        Assert.IsTrue(dispatcher.TogglePaused());
        AssertDecision(dispatcher, candidate, settings, DateTimeOffset.UnixEpoch, App.NotificationDispatchResult.Paused);
        Assert.IsFalse(dispatcher.TogglePaused());
        AssertDecision(dispatcher, candidate, settings, DateTimeOffset.UnixEpoch, App.NotificationDispatchResult.Published);
    }

    [TestMethod]
    public async Task BackgroundMonitorDrainsEveryBufferedObservation()
    {
        App.NotificationObservationBuffer buffer = new();
        InMemorySettingsStore store = new(new App.BackgroundSettings
        {
            Notifications = new App.NotificationSettings
            {
                BackendUnavailable = true,
                RecurringError = true,
            },
        });
        App.BackgroundSettingsService settings = new(store, new FakeAutostartRegistration(enabled: false));
        await settings.InitializeAsync();
        RecordingPublisher publisher = new();
        App.NotificationDispatcher dispatcher = new(publisher);
        await using App.BackgroundNotificationMonitor monitor = new(
            buffer,
            settings,
            new App.NotificationRuleEngine(),
            dispatcher);
        monitor.Start();
        ProxyObservation first = Observation(
            Guid.NewGuid(), TimeSpan.Zero, ProxyOutcome.BackendUnavailable, ProxyErrorType.ConnectionRefused,
            BackendResponseTelemetry.Unavailable(BackendKind.Ollama, SourceVersion));
        ProxyObservation second = first with { RequestId = Guid.NewGuid() };

        await buffer.RecordAsync(first, CancellationToken.None);
        await buffer.RecordAsync(second, CancellationToken.None);
        await WaitUntilAsync(() => publisher.Published.Length == 3, TimeSpan.FromSeconds(5));

        Assert.AreEqual(2, publisher.Published.Count(item =>
            item.EventType == App.NotificationEventType.BackendUnavailable));
        Assert.AreEqual(1, publisher.Published.Count(item =>
            item.EventType == App.NotificationEventType.RecurringError));
    }

    [TestMethod]
    public void DuplicateAndGlobalRateLimitBoundariesAreDeterministic()
    {
        RecordingPublisher publisher = new();
        App.NotificationDispatcher dispatcher = new(publisher);
        App.NotificationSettings settings = new()
        {
            BackendUnavailable = true,
            LongOperationCompleted = true,
            RecurringError = true,
            HighContextUsage = true,
        };
        DateTimeOffset at = DateTimeOffset.UnixEpoch;
        App.NotificationCandidate duplicate = Candidate(App.NotificationEventType.BackendUnavailable, "same");

        AssertDecision(dispatcher, duplicate, settings, at, App.NotificationDispatchResult.Published);
        AssertDecision(dispatcher, duplicate, settings, at.AddMinutes(15).AddTicks(-1), App.NotificationDispatchResult.DuplicateSuppressed);
        AssertDecision(dispatcher, duplicate, settings, at.AddMinutes(15), App.NotificationDispatchResult.Published);

        NotificationPolicyOptionsWithLimit(out App.NotificationDispatcher limited, out RecordingPublisher limitedPublisher);
        AssertDecision(limited, Candidate(App.NotificationEventType.BackendUnavailable, "1"), settings, at, App.NotificationDispatchResult.Published);
        AssertDecision(limited, Candidate(App.NotificationEventType.LongOperationCompleted, "2"), settings, at.AddMinutes(1), App.NotificationDispatchResult.Published);
        AssertDecision(limited, Candidate(App.NotificationEventType.RecurringError, "3"), settings, at.AddMinutes(2), App.NotificationDispatchResult.Published);
        AssertDecision(limited, Candidate(App.NotificationEventType.HighContextUsage, "4"), settings, at.AddMinutes(9), App.NotificationDispatchResult.RateLimited);
        AssertDecision(limited, Candidate(App.NotificationEventType.HighContextUsage, "5"), settings, at.AddMinutes(10), App.NotificationDispatchResult.Published);
        Assert.HasCount(4, limitedPublisher.Published);
    }

    [TestMethod]
    public async Task ObservationBufferIsNonBlockingBoundedAndReportsDrops()
    {
        App.NotificationObservationBuffer buffer = new(capacity: 1);
        ProxyObservation first = Observation(
            Guid.NewGuid(), TimeSpan.Zero, ProxyOutcome.Completed, ProxyErrorType.None,
            BackendResponseTelemetry.Unavailable(BackendKind.Ollama, SourceVersion));
        ProxyObservation second = first with { RequestId = Guid.NewGuid() };

        await buffer.RecordAsync(first, CancellationToken.None);
        await buffer.RecordAsync(second, CancellationToken.None);

        Assert.AreEqual(1L, buffer.DroppedCount);
        buffer.Complete();
        List<ProxyObservation> read = [];
        await foreach (ProxyObservation observation in buffer.ReadAllAsync(CancellationToken.None))
        {
            read.Add(observation);
        }

        Assert.HasCount(1, read);
        Assert.AreEqual(first.RequestId, read[0].RequestId);
    }

    private static App.BackgroundSettings BackgroundSettingsWithAutostart(bool enabled) => new()
    {
        AutostartEnabled = enabled,
    };

    private static App.NotificationCandidate Candidate(App.NotificationEventType eventType, string key) =>
        eventType switch
        {
            App.NotificationEventType.BackendUnavailable => new(eventType, key)
            {
                RequestId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                ErrorType = HistoryErrorType.BackendUnavailable,
            },
            App.NotificationEventType.LongOperationCompleted => new(eventType, key)
            {
                RequestId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                DurationSeconds = 60,
            },
            App.NotificationEventType.RecurringError => new(eventType, key)
            {
                ErrorType = HistoryErrorType.Timeout,
                Occurrences = 2,
            },
            App.NotificationEventType.HighContextUsage => new(eventType, key)
            {
                RequestId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                ContextUsagePercent = 90,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(eventType)),
        };

    private static void AssertDecision(
        App.NotificationDispatcher dispatcher,
        App.NotificationCandidate candidate,
        App.NotificationSettings settings,
        DateTimeOffset at,
        App.NotificationDispatchResult expected)
    {
        App.NotificationDispatchDecision decision = AssertSingle(dispatcher.Dispatch([candidate], settings, at));
        Assert.AreEqual(expected, decision.Result);
        Assert.AreEqual(App.NotificationPolicyOptions.Version1, decision.PolicyVersion);
    }

    private static void NotificationPolicyOptionsWithLimit(
        out App.NotificationDispatcher dispatcher,
        out RecordingPublisher publisher)
    {
        publisher = new RecordingPublisher();
        dispatcher = new App.NotificationDispatcher(publisher, new App.NotificationPolicyOptions
        {
            DuplicateWindow = TimeSpan.FromMinutes(15),
            GlobalRateWindow = TimeSpan.FromMinutes(10),
            GlobalRateLimit = 3,
        });
    }

    private static ProxyObservation Observation(
        Guid requestId,
        TimeSpan duration,
        ProxyOutcome outcome,
        ProxyErrorType error,
        BackendResponseTelemetry telemetry) => new(
        requestId,
        DateTimeOffset.UnixEpoch,
        duration,
        outcome == ProxyOutcome.Completed ? 200 : 502,
        outcome,
        ClientKind.Cline,
        telemetry)
        {
            ErrorType = error,
        };

    private static MetricValue Exact(decimal value, MetricUnit unit) =>
        MetricValue.Exact(value, unit, MetricSource.BackendExtension, SourceVersion);

    private static T AssertSingle<T>(IEnumerable<T> items) => items.Single();

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                Assert.Fail("The bounded background operation did not complete in time.");
            }

            await Task.Delay(10);
        }
    }

    private sealed class RecordingPublisher : App.IDesktopNotificationPublisher
    {
        private readonly ConcurrentQueue<App.DesktopNotification> _published = new();

        public App.DesktopNotification[] Published => _published.ToArray();

        public void Publish(App.DesktopNotification notification) => _published.Enqueue(notification);
    }

    private sealed class FailingSettingsStore(App.BackgroundSettings initial) : App.IBackgroundSettingsStore
    {
        public ValueTask<App.BackgroundSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(initial);

        public ValueTask SaveAsync(
            App.BackgroundSettings settings,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException(new IOException("Synthetic settings write failure."));
    }

    private sealed class InMemorySettingsStore(App.BackgroundSettings initial) : App.IBackgroundSettingsStore
    {
        private App.BackgroundSettings _settings = initial;

        public ValueTask<App.BackgroundSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_settings);

        public ValueTask SaveAsync(
            App.BackgroundSettings settings,
            CancellationToken cancellationToken = default)
        {
            _settings = settings;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeAutostartRegistration(bool enabled) : App.IAutostartRegistration
    {
        public bool Enabled { get; private set; } = enabled;

        public List<bool> Changes { get; } = [];

        public bool IsEnabled() => Enabled;

        public void SetEnabled(bool value)
        {
            Enabled = value;
            Changes.Add(value);
        }
    }
}
