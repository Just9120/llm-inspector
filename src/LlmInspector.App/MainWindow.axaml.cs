using Avalonia.Controls;
using Avalonia.Threading;
using LlmInspector.Application;
using LlmInspector.Diagnostics;
using LlmInspector.Domain;
using LlmInspector.Gateway;

namespace LlmInspector.App;

public partial class MainWindow : Window
{
    private readonly ILiveRequestSnapshotSource? _liveRequestState;
    private readonly IProxyObservationSnapshotSource? _observationSource;
    private readonly IResourceTelemetrySnapshotSource? _resourceSource;
    private readonly ITechnicalHistoryStore? _history;
    private readonly AppRuntimeStatus _runtimeStatus;
    private readonly string _historyState;
    private readonly BackgroundSettingsService? _backgroundSettings;
    private readonly BackgroundLifetimeController? _backgroundLifetime;
    private readonly Action<MonitoringPerformanceProfile>? _applyMonitoringProfile;
    private readonly IReadOnlyDictionary<BackendKind, BackendLifecycleManager> _lifecycleManagers;
    private readonly DiagnosticSnapshotService? _snapshotService;
    private readonly AnalyticsExportService? _analyticsExportService;
    private readonly DispatcherTimer? _liveRefreshTimer;
    private readonly DispatcherTimer? _lifecycleRefreshTimer;
    private HistoryClearPreview? _clearPreview;
    private DiagnosticSnapshotArtifact? _snapshotPreview;
    private AnalyticsExportArtifact? _analyticsExportPreview;

    public MainWindow()
        : this(AppRuntimeStatus.NotStarted, null, null, null, null, "Technical history is not composed.", null, null)
    {
    }

    public MainWindow(
        AppRuntimeStatus runtimeStatus,
        ILiveRequestSnapshotSource? liveRequestState = null,
        IProxyObservationSnapshotSource? observationSource = null,
        IResourceTelemetrySnapshotSource? resourceSource = null,
        ITechnicalHistoryStore? history = null,
        string? historyState = null,
        BackgroundSettingsService? backgroundSettings = null,
        BackgroundLifetimeController? backgroundLifetime = null,
        Action<MonitoringPerformanceProfile>? applyMonitoringProfile = null,
        IReadOnlyList<BackendLifecycleManager>? lifecycleManagers = null)
    {
        InitializeComponent();
        _liveRequestState = liveRequestState;
        _observationSource = observationSource;
        _resourceSource = resourceSource;
        _history = history;
        _runtimeStatus = runtimeStatus;
        _historyState = historyState ?? "Technical history state is unavailable.";
        _backgroundSettings = backgroundSettings;
        _backgroundLifetime = backgroundLifetime;
        _applyMonitoringProfile = applyMonitoringProfile;
        _lifecycleManagers = (lifecycleManagers ?? [])
            .ToDictionary(manager => manager.Profile.Backend);
        _snapshotService = history is null ? null : new DiagnosticSnapshotService(history);
        _analyticsExportService = history is null ? null : new AnalyticsExportService(history);

        GatewayStateText.Text = runtimeStatus.State;
        ListenerText.Text = $"Listener: {runtimeStatus.Listener}";
        BackendText.Text = $"Backend: {runtimeStatus.Backend}";
        BackendKindText.Text = $"Backend adapter: {runtimeStatus.BackendType}";
        ClientEndpointsText.Text = CreateClientEndpointText(runtimeStatus);
        TechnicalDataText.Text = string.Join(
            Environment.NewLine,
            TechnicalDataDisclosure.CurrentCategories.Select(
                category => $"{category.Name}: {category.Fields}. Retention: {category.Retention}."));
        PersistentDataText.Text = TechnicalDataDisclosure.PersistentDataStatement;
        ForbiddenContentText.Text = TechnicalDataDisclosure.ForbiddenContentStatement;
        HistoryStateText.Text = _historyState;
        ConfigureHistoryControls();
        ConfigureSnapshotControls();
        ConfigureAnalyticsExportControls();
        ConfigureBackgroundControls();
        ConfigureLifecycleControls();
        RefreshLiveRequests();
        RefreshRequestDetail();
        RefreshResources();
        RefreshDiagnostics();

        if (_liveRequestState is not null || _observationSource is not null)
        {
            _liveRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250),
            };
            _liveRefreshTimer.Tick += (_, _) =>
            {
                RefreshLiveRequests();
                RefreshRequestDetail();
                RefreshResources();
                RefreshDiagnostics();
            };
            _liveRefreshTimer.Start();
            Closed += (_, _) => _liveRefreshTimer.Stop();
        }

        if (_lifecycleManagers.Count > 0)
        {
            _lifecycleRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _lifecycleRefreshTimer.Tick += async (_, _) => await RefreshLifecycleAsync();
            _lifecycleRefreshTimer.Start();
            Closed += (_, _) => _lifecycleRefreshTimer.Stop();
        }

        Opened += OnOpened;
        Closing += OnWindowClosing;
    }

    public void ShowFromTray(bool openNotificationSettings)
    {
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        _liveRefreshTimer?.Start();
        Activate();
        if (openNotificationSettings)
        {
            _ = AutostartCheckBox.Focus();
        }
    }

    public void HideToBackground()
    {
        _liveRefreshTimer?.Stop();
        Hide();
    }

    private void ConfigureLifecycleControls()
    {
        bool enabled = _lifecycleManagers.Count > 0;
        LifecycleBackendCombo.IsEnabled = enabled;
        LifecycleExecutablePathText.IsEnabled = enabled;
        DiscoverBackendButton.IsEnabled = enabled;
        ConfirmLifecycleTargetCheckBox.IsEnabled = false;
        SetLifecycleActionAvailability(false);
        if (!enabled)
        {
            LifecycleStateText.Text = "Lifecycle management недоступен на этой платформе.";
            return;
        }

        LifecycleBackendCombo.ItemsSource = _lifecycleManagers.Values
            .Select(manager => new BackendLifecycleUiChoice(manager.Profile.Backend, manager.Profile.DisplayName))
            .ToArray();
        _ = Enum.TryParse(_runtimeStatus.BackendType, out BackendKind configuredBackend);
        LifecycleBackendCombo.SelectedItem = LifecycleBackendCombo.ItemsSource
            .Cast<BackendLifecycleUiChoice>()
            .FirstOrDefault(choice => choice.Backend == configuredBackend) ??
            LifecycleBackendCombo.ItemsSource.Cast<BackendLifecycleUiChoice>().First();
        LifecycleBackendCombo.SelectionChanged += (_, _) => SelectLifecycleBackend();
        LifecycleParameterCombo.SelectionChanged += (_, _) => SelectLifecycleParameter();
        DiscoverBackendButton.Click += async (_, _) => await DiscoverLifecycleBackendAsync();
        ConfirmLifecycleTargetCheckBox.Click += (_, _) => ConfirmLifecycleTarget();
        StartBackendButton.Click += async (_, _) => await RunLifecycleOperationAsync(manager => manager.StartAsync());
        StopBackendButton.Click += async (_, _) => await RunLifecycleOperationAsync(manager => manager.StopAsync());
        RestartBackendButton.Click += async (_, _) => await RunLifecycleOperationAsync(manager => manager.RestartAsync());
        LoadLifecycleModelButton.Click += async (_, _) =>
            await RunLifecycleOperationAsync(manager => manager.LoadModelAsync(LifecycleModelText.Text ?? string.Empty));
        ApplyLifecycleParameterButton.Click += (_, _) => ApplyLifecycleParameter();
        ResetLifecycleParametersButton.Click += (_, _) => ResetLifecycleParameters();
        SelectLifecycleBackend();
    }

    private BackendLifecycleManager? SelectedLifecycleManager =>
        LifecycleBackendCombo.SelectedItem is BackendLifecycleUiChoice choice &&
        _lifecycleManagers.TryGetValue(choice.Backend, out BackendLifecycleManager? manager)
            ? manager
            : null;

    private void SelectLifecycleBackend()
    {
        BackendLifecycleManager? manager = SelectedLifecycleManager;
        if (manager is null)
        {
            return;
        }

        LifecycleParameterCombo.ItemsSource = manager.Profile.Parameters
            .Select(parameter => new BackendLifecycleParameterUiChoice(parameter))
            .ToArray();
        LifecycleParameterCombo.SelectedIndex = manager.Profile.Parameters.Count > 0 ? 0 : -1;
        ConfirmLifecycleTargetCheckBox.IsChecked = false;
        UpdateLifecycleSurface(manager.Snapshot);
        SelectLifecycleParameter();
    }

    private async Task DiscoverLifecycleBackendAsync()
    {
        BackendLifecycleManager? manager = SelectedLifecycleManager;
        if (manager is null)
        {
            return;
        }

        SetLifecycleActionAvailability(false);
        BackendLifecycleResult result = await manager.DiscoverAsync(LifecycleExecutablePathText.Text);
        ConfirmLifecycleTargetCheckBox.IsChecked = false;
        ConfirmLifecycleTargetCheckBox.IsEnabled = result.Succeeded;
        UpdateLifecycleSurface(result.Snapshot);
    }

    private void ConfirmLifecycleTarget()
    {
        BackendLifecycleManager? manager = SelectedLifecycleManager;
        if (manager?.Snapshot.Target is not BackendLifecycleTarget target ||
            ConfirmLifecycleTargetCheckBox.IsChecked != true)
        {
            SetLifecycleActionAvailability(false);
            return;
        }

        BackendLifecycleResult result = manager.ConfirmTarget(target.ConfirmationToken);
        ConfirmLifecycleTargetCheckBox.IsChecked = result.Succeeded;
        UpdateLifecycleSurface(result.Snapshot);
    }

    private void ApplyLifecycleParameter()
    {
        BackendLifecycleManager? manager = SelectedLifecycleManager;
        if (manager is null || LifecycleParameterCombo.SelectedItem is not BackendLifecycleParameterUiChoice choice)
        {
            return;
        }

        BackendLifecycleResult result = manager.SetParameter(choice.Definition.Key, LifecycleParameterValueText.Text);
        if (result.Snapshot.State == BackendLifecycleState.TargetPendingConfirmation)
        {
            ConfirmLifecycleTargetCheckBox.IsChecked = false;
            ConfirmLifecycleTargetCheckBox.IsEnabled = true;
        }

        UpdateLifecycleSurface(result.Snapshot);
        SelectLifecycleParameter();
    }

    private void ResetLifecycleParameters()
    {
        BackendLifecycleManager? manager = SelectedLifecycleManager;
        if (manager is null)
        {
            return;
        }

        BackendLifecycleResult result = manager.ResetParameters();
        UpdateLifecycleSurface(result.Snapshot);
        SelectLifecycleParameter();
    }

    private void SelectLifecycleParameter()
    {
        if (SelectedLifecycleManager is not BackendLifecycleManager manager ||
            LifecycleParameterCombo.SelectedItem is not BackendLifecycleParameterUiChoice choice)
        {
            LifecycleParameterHelpText.Text = string.Empty;
            return;
        }

        manager.Snapshot.Parameters.TryGetValue(choice.Definition.Key, out string? value);
        LifecycleParameterValueText.Text = value ?? string.Empty;
        LifecycleParameterHelpText.Text = choice.Definition.Description +
            (choice.Definition.DefaultValue is null
                ? " Native default не переопределён."
                : $" Backend default: {choice.Definition.DefaultValue}.");
    }

    private async Task RunLifecycleOperationAsync(
        Func<BackendLifecycleManager, ValueTask<BackendLifecycleResult>> operation)
    {
        BackendLifecycleManager? manager = SelectedLifecycleManager;
        if (manager is null || ConfirmLifecycleTargetCheckBox.IsChecked != true)
        {
            LifecycleStateText.Text = "Сначала подтвердите exact target.";
            return;
        }

        SetLifecycleActionAvailability(false);
        try
        {
            BackendLifecycleResult result = await operation(manager);
            UpdateLifecycleSurface(result.Snapshot);
        }
        catch (Exception exception) when (exception is
            IOException or InvalidOperationException or UnauthorizedAccessException or
            System.ComponentModel.Win32Exception or HttpRequestException or TimeoutException)
        {
            LifecycleStateText.Text = $"Операция не выполнена безопасно ({exception.GetType().Name}).";
        }
        finally
        {
            SetLifecycleActionAvailability(ConfirmLifecycleTargetCheckBox.IsChecked == true);
        }
    }

    private async Task RefreshLifecycleAsync()
    {
        BackendLifecycleManager? manager = SelectedLifecycleManager;
        if (manager is null)
        {
            return;
        }

        BackendLifecycleSnapshot snapshot = await manager.RefreshAsync();
        UpdateLifecycleSurface(snapshot);
    }

    private void UpdateLifecycleSurface(BackendLifecycleSnapshot snapshot)
    {
        LifecycleTargetText.Text = snapshot.Target is null
            ? "Target: не выбран."
            : $"Target: {snapshot.Target.ExecutablePath}{Environment.NewLine}" +
              $"Version: {snapshot.Target.Version}; endpoint: {snapshot.Target.Endpoint}; status: {snapshot.Target.CompatibilityLabel}.";
        LifecycleStateText.Text = $"State: {snapshot.State}. {snapshot.Message}";
        if (snapshot.Model is not null)
        {
            LifecycleModelText.Text = snapshot.Model;
        }

        bool confirmed = ConfirmLifecycleTargetCheckBox.IsChecked == true &&
            snapshot.State != BackendLifecycleState.TargetPendingConfirmation;
        SetLifecycleActionAvailability(confirmed);
    }

    private void SetLifecycleActionAvailability(bool enabled)
    {
        StartBackendButton.IsEnabled = enabled;
        StopBackendButton.IsEnabled = enabled;
        RestartBackendButton.IsEnabled = enabled;
        LifecycleParameterCombo.IsEnabled = enabled;
        LifecycleParameterValueText.IsEnabled = enabled;
        ApplyLifecycleParameterButton.IsEnabled = enabled;
        ResetLifecycleParametersButton.IsEnabled = enabled;
        LifecycleModelText.IsEnabled = enabled;
        LoadLifecycleModelButton.IsEnabled = enabled;
    }

    private sealed record BackendLifecycleUiChoice(BackendKind Backend, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    private sealed record BackendLifecycleParameterUiChoice(BackendParameterDefinition Definition)
    {
        public override string ToString() => Definition.DisplayName;
    }

    private async void OnOpened(object? sender, EventArgs eventArgs)
    {
        if (_history is null)
        {
            return;
        }

        await RunHistoryActionAsync(async () =>
        {
            HistoryRetention retention = await _history.GetRetentionAsync();
            RetentionCombo.SelectedItem = HistoryUiCatalog.RetentionChoices.Single(item => item.Value == retention);
            await LoadHistoryAsync();
        });
    }

    private void ConfigureHistoryControls()
    {
        FilterClientCombo.ItemsSource = WithAny<ClientKind>();
        FilterBackendCombo.ItemsSource = WithAny<BackendKind>();
        FilterStatusCombo.ItemsSource = WithAny<ProxyOutcome>();
        FilterErrorCombo.ItemsSource = WithAny<HistoryErrorType>();
        FilterClientCombo.SelectedIndex = 0;
        FilterBackendCombo.SelectedIndex = 0;
        FilterStatusCombo.SelectedIndex = 0;
        FilterErrorCombo.SelectedIndex = 0;
        ComparisonDimensionCombo.ItemsSource = new[] { "Period", "Model", "Backend", "Client" };
        ComparisonDimensionCombo.SelectedIndex = 0;
        ComparisonMetricCombo.ItemsSource = Enum.GetValues<HistoryMetric>();
        ComparisonMetricCombo.SelectedItem = HistoryMetric.TimeToFirstTokenMilliseconds;
        RetentionCombo.ItemsSource = HistoryUiCatalog.RetentionChoices;
        RetentionCombo.SelectedItem = HistoryUiCatalog.RetentionChoices.Single(
            item => item.Value == HistoryRetention.ThirtyDays);

        LoadHistoryButton.Click += async (_, _) => await RunHistoryActionAsync(LoadHistoryAsync);
        LoadAnalyticsButton.Click += async (_, _) => await RunHistoryActionAsync(LoadAnalyticsAsync);
        LoadOperationButton.Click += async (_, _) => await RunHistoryActionAsync(LoadOperationAsync);
        CompareButton.Click += async (_, _) => await RunHistoryActionAsync(CompareAsync);
        ApplyRetentionButton.Click += async (_, _) => await RunHistoryActionAsync(ApplyRetentionAsync);
        PreviewClearButton.Click += async (_, _) => await RunHistoryActionAsync(PreviewClearAsync);
        ConfirmClearButton.Click += async (_, _) => await RunHistoryActionAsync(ConfirmClearAsync);
        ClearAllCheckBox.IsCheckedChanged += (_, _) => InvalidateClearPreview();
        ClearFromText.TextChanged += (_, _) => InvalidateClearPreview();
        ClearToText.TextChanged += (_, _) => InvalidateClearPreview();

        bool enabled = _history is not null;
        LoadHistoryButton.IsEnabled = enabled;
        LoadAnalyticsButton.IsEnabled = enabled;
        LoadOperationButton.IsEnabled = enabled;
        CompareButton.IsEnabled = enabled;
        ApplyRetentionButton.IsEnabled = enabled;
        PreviewClearButton.IsEnabled = enabled;
    }

    private void ConfigureBackgroundControls()
    {
        bool enabled = _backgroundSettings is not null;
        AutostartCheckBox.IsEnabled = enabled;
        NotifyBackendUnavailableCheckBox.IsEnabled = enabled;
        NotifyLongOperationCheckBox.IsEnabled = enabled;
        NotifyRecurringErrorCheckBox.IsEnabled = enabled;
        NotifyHighContextCheckBox.IsEnabled = enabled;
        SilentNotificationsCheckBox.IsEnabled = enabled;
        PerformanceProfileCombo.IsEnabled = enabled;
        CustomSamplingIntervalText.IsEnabled = enabled;
        ResetPerformanceProfileButton.IsEnabled = enabled;
        SaveBackgroundSettingsButton.IsEnabled = enabled;
        if (_backgroundSettings is null)
        {
            BackgroundSettingsStateText.Text = "Background settings are unavailable.";
            return;
        }

        ApplyBackgroundSettings(_backgroundSettings.Current);
        BackgroundSettingsStateText.Text =
            $"Settings schema v{BackgroundSettings.CurrentSchemaVersion}; Windows autostart is " +
            (_backgroundSettings.Current.AutostartEnabled ? "enabled." : "disabled.");
        SaveBackgroundSettingsButton.Click += async (_, _) => await SaveBackgroundSettingsAsync();
        PerformanceProfileCombo.SelectionChanged += (_, _) => UpdatePerformanceProfileDescription();
        CustomSamplingIntervalText.TextChanged += (_, _) => UpdatePerformanceProfileDescription();
        ResetPerformanceProfileButton.Click += (_, _) =>
        {
            PerformanceProfileCombo.SelectedItem = PerformanceProfileUi.Choices.Single(
                choice => choice.Id == MonitoringPerformanceProfileId.Balanced);
            CustomSamplingIntervalText.Text = "1000";
            UpdatePerformanceProfileDescription();
        };
    }

    private void ConfigureSnapshotControls()
    {
        SnapshotScopeCombo.ItemsSource = DiagnosticSnapshotUi.ScopeChoices;
        SnapshotScopeCombo.SelectedItem = DiagnosticSnapshotUi.TimeRangeScope;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        SnapshotFromText.Text = now.AddHours(-1).ToString("O");
        SnapshotToText.Text = now.ToString("O");
        try
        {
            SnapshotPathText.Text = DiagnosticSnapshotUi.CreateDefaultLocalPath(now);
        }
        catch (IOException)
        {
            SnapshotPathText.Text = string.Empty;
        }

        bool enabled = _snapshotService is not null;
        SnapshotScopeCombo.IsEnabled = enabled;
        SnapshotFromText.IsEnabled = enabled;
        SnapshotToText.IsEnabled = enabled;
        SnapshotOperationIdText.IsEnabled = enabled;
        SnapshotPathText.IsEnabled = enabled;
        PreviewSnapshotButton.IsEnabled = enabled;
        SaveSnapshotButton.IsEnabled = false;
        SnapshotStateText.Text = enabled
            ? "Choose a UTC range or operation, then inspect the generated local JSON preview."
            : "Diagnostic snapshot is unavailable while technical history is unavailable.";

        SnapshotScopeCombo.SelectionChanged += (_, _) => InvalidateSnapshotPreview();
        SnapshotFromText.TextChanged += (_, _) => InvalidateSnapshotPreview();
        SnapshotToText.TextChanged += (_, _) => InvalidateSnapshotPreview();
        SnapshotOperationIdText.TextChanged += (_, _) => InvalidateSnapshotPreview();
        PreviewSnapshotButton.Click += async (_, _) => await RunSnapshotActionAsync(PreviewSnapshotAsync);
        SaveSnapshotButton.Click += async (_, _) => await RunSnapshotActionAsync(SaveSnapshotAsync);
    }

    private async Task PreviewSnapshotAsync()
    {
        DiagnosticSnapshotService service = _snapshotService ??
            throw new InvalidOperationException("Diagnostic snapshot history source is unavailable.");
        InvalidateSnapshotPreview();
        DiagnosticSnapshotSelection selection = DiagnosticSnapshotUi.CreateSelection(
            SnapshotScopeCombo.SelectedItem?.ToString(),
            SnapshotFromText.Text,
            SnapshotToText.Text,
            SnapshotOperationIdText.Text);
        DiagnosticSnapshotArtifact preview = await service.CreateAsync(
            selection,
            DiagnosticEnvironmentFacts.CaptureLocal());
        _snapshotPreview = preview;
        SnapshotPreviewText.Text = preview.Json;
        SaveSnapshotButton.IsEnabled = true;
        SnapshotStateText.Text =
            $"Local preview ready: schema {preview.Document.SchemaVersion}; " +
            $"requests {preview.Document.Requests.Count}; resource samples {preview.Document.ResourceSamples.Count}; " +
            $"SHA-256 {preview.Sha256}. Nothing was uploaded.";
    }

    private async Task SaveSnapshotAsync()
    {
        DiagnosticSnapshotArtifact preview = _snapshotPreview ??
            throw new InvalidOperationException("Create and inspect a preview before saving.");
        await DiagnosticSnapshotService.SaveAsync(preview, SnapshotPathText.Text ?? string.Empty);
        SnapshotStateText.Text =
            $"Exact preview saved locally to {Path.GetFullPath(SnapshotPathText.Text!)}; " +
            $"SHA-256 {preview.Sha256}. No upload was performed.";
    }

    private async Task RunSnapshotActionAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            InvalidOperationException or
            InvalidDataException or
            IOException or
            UnauthorizedAccessException or
            System.Security.SecurityException or
            Microsoft.Data.Sqlite.SqliteException)
        {
            SnapshotStateText.Text = $"Diagnostic snapshot action failed: {exception.Message}";
        }
    }

    private void InvalidateSnapshotPreview()
    {
        _snapshotPreview = null;
        SnapshotPreviewText.Text = string.Empty;
        SaveSnapshotButton.IsEnabled = false;
    }

    private void ConfigureAnalyticsExportControls()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ExportFromText.Text = now.AddHours(-1).ToString("O");
        ExportToText.Text = now.ToString("O");
        try
        {
            ExportPathText.Text = AnalyticsExportUi.CreateDefaultLocalPath(now);
        }
        catch (IOException)
        {
            ExportPathText.Text = string.Empty;
        }

        bool enabled = _analyticsExportService is not null;
        ExportFromText.IsEnabled = enabled;
        ExportToText.IsEnabled = enabled;
        ExportPathText.IsEnabled = enabled;
        PreviewExportButton.IsEnabled = enabled;
        SaveExportButton.IsEnabled = false;
        ExportStateText.Text = enabled
            ? "Choose a UTC range, then inspect the generated local analytics JSON preview."
            : "Analytics export is unavailable while technical history is unavailable.";

        ExportFromText.TextChanged += (_, _) => InvalidateAnalyticsExportPreview();
        ExportToText.TextChanged += (_, _) => InvalidateAnalyticsExportPreview();
        PreviewExportButton.Click += async (_, _) =>
            await RunAnalyticsExportActionAsync(PreviewAnalyticsExportAsync);
        SaveExportButton.Click += async (_, _) =>
            await RunAnalyticsExportActionAsync(SaveAnalyticsExportAsync);
    }

    private async Task PreviewAnalyticsExportAsync()
    {
        AnalyticsExportService service = _analyticsExportService ??
            throw new InvalidOperationException("Analytics export history source is unavailable.");
        InvalidateAnalyticsExportPreview();
        AnalyticsExportSelection selection = AnalyticsExportUi.CreateSelection(
            ExportFromText.Text,
            ExportToText.Text);
        AnalyticsExportArtifact preview = await service.CreateAsync(selection);
        _analyticsExportPreview = preview;
        ExportPreviewText.Text = preview.Json;
        SaveExportButton.IsEnabled = true;
        ExportStateText.Text =
            $"Local preview ready: schema {preview.Document.SchemaVersion}; " +
            $"requests {preview.Document.History.Requests.Count}; " +
            $"resource samples {preview.Document.History.ResourceSamples.Count}; " +
            $"aggregate days {preview.Document.AggregateMetrics.Count}; " +
            $"SHA-256 {preview.Sha256}. Nothing was uploaded.";
    }

    private async Task SaveAnalyticsExportAsync()
    {
        AnalyticsExportArtifact preview = _analyticsExportPreview ??
            throw new InvalidOperationException("Create and inspect an analytics export preview before saving.");
        await AnalyticsExportService.SaveAsync(preview, ExportPathText.Text ?? string.Empty);
        ExportStateText.Text =
            $"Exact analytics preview saved locally to {Path.GetFullPath(ExportPathText.Text!)}; " +
            $"SHA-256 {preview.Sha256}. No upload was performed.";
    }

    private async Task RunAnalyticsExportActionAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            InvalidOperationException or
            InvalidDataException or
            IOException or
            UnauthorizedAccessException or
            System.Security.SecurityException or
            Microsoft.Data.Sqlite.SqliteException)
        {
            ExportStateText.Text = $"Analytics export action failed: {exception.Message}";
        }
    }

    private void InvalidateAnalyticsExportPreview()
    {
        _analyticsExportPreview = null;
        ExportPreviewText.Text = string.Empty;
        SaveExportButton.IsEnabled = false;
    }

    private async Task SaveBackgroundSettingsAsync()
    {
        if (_backgroundSettings is null)
        {
            return;
        }

        try
        {
            BackgroundSettings settings = new()
            {
                AutostartEnabled = AutostartCheckBox.IsChecked == true,
                Notifications = new NotificationSettings
                {
                    BackendUnavailable = NotifyBackendUnavailableCheckBox.IsChecked == true,
                    LongOperationCompleted = NotifyLongOperationCheckBox.IsChecked == true,
                    RecurringError = NotifyRecurringErrorCheckBox.IsChecked == true,
                    HighContextUsage = NotifyHighContextCheckBox.IsChecked == true,
                    SilentMode = SilentNotificationsCheckBox.IsChecked == true,
                },
                Monitoring = CreateMonitoringSettingsFromControls(),
            };
            await _backgroundSettings.SaveAsync(settings);
            _applyMonitoringProfile?.Invoke(settings.Monitoring.Resolve());
            ApplyBackgroundSettings(_backgroundSettings.Current);
            BackgroundSettingsStateText.Text =
                $"Background settings saved atomically; Windows autostart is " +
                (_backgroundSettings.Current.AutostartEnabled ? "enabled." : "disabled.");
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            System.Security.SecurityException or
            InvalidDataException or
            InvalidOperationException or
            PlatformNotSupportedException)
        {
            ApplyBackgroundSettings(_backgroundSettings.Current);
            BackgroundSettingsStateText.Text =
                $"Background settings were not changed ({exception.GetType().Name}).";
        }
    }

    private void ApplyBackgroundSettings(BackgroundSettings settings)
    {
        AutostartCheckBox.IsChecked = settings.AutostartEnabled;
        NotifyBackendUnavailableCheckBox.IsChecked = settings.Notifications.BackendUnavailable;
        NotifyLongOperationCheckBox.IsChecked = settings.Notifications.LongOperationCompleted;
        NotifyRecurringErrorCheckBox.IsChecked = settings.Notifications.RecurringError;
        NotifyHighContextCheckBox.IsChecked = settings.Notifications.HighContextUsage;
        SilentNotificationsCheckBox.IsChecked = settings.Notifications.SilentMode;
        PerformanceProfileCombo.ItemsSource = PerformanceProfileUi.Choices;
        PerformanceProfileCombo.SelectedItem = PerformanceProfileUi.Choices.Single(
            choice => choice.Id == settings.Monitoring.Profile);
        CustomSamplingIntervalText.Text = settings.Monitoring.CustomSamplingIntervalMilliseconds.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        UpdatePerformanceProfileDescription();
    }

    private MonitoringProfileSettings CreateMonitoringSettingsFromControls()
    {
        if (PerformanceProfileCombo.SelectedItem is not MonitoringPerformanceProfileChoice choice)
        {
            throw new InvalidDataException("Выберите профиль производительности мониторинга.");
        }

        return PerformanceProfileUi.CreateSettings(choice.Id, CustomSamplingIntervalText.Text);
    }

    private void UpdatePerformanceProfileDescription()
    {
        bool custom = PerformanceProfileCombo.SelectedItem is MonitoringPerformanceProfileChoice
        {
            Id: MonitoringPerformanceProfileId.Custom,
        };
        CustomSamplingIntervalText.IsEnabled = _backgroundSettings is not null && custom;
        try
        {
            MonitoringProfileSettings settings = CreateMonitoringSettingsFromControls();
            MonitoringPerformanceProfileChoice? choice = PerformanceProfileCombo.SelectedItem as
                MonitoringPerformanceProfileChoice;
            PerformanceProfileStateText.Text = $"{choice?.Description} {PerformanceProfileUi.Describe(settings)}";
        }
        catch (InvalidDataException exception)
        {
            PerformanceProfileStateText.Text = exception.Message;
        }
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs eventArgs)
    {
        if (_backgroundLifetime?.OnWindowClosing() != BackgroundCloseAction.HideAndContinue)
        {
            return;
        }

        eventArgs.Cancel = true;
        HideToBackground();
        BackgroundSettingsStateText.Text = "Monitoring continues in the Windows system tray.";
    }

    private async Task LoadHistoryAsync()
    {
        HistoryFilter filter = CreateCurrentFilter();
        IReadOnlyList<RequestHistoryItem> requests = await RequireHistory().QueryRequestsAsync(filter);
        HistoryOutputText.Text = HistoryTextPresenter.FormatRequests(requests);
    }

    private async Task LoadAnalyticsAsync()
    {
        PeriodAnalytics analytics = await RequireHistory().AnalyzePeriodAsync(CreateCurrentFilter());
        AnalyticsOutputText.Text = HistoryTextPresenter.FormatAnalytics(analytics);
    }

    private async Task LoadOperationAsync()
    {
        if (!Guid.TryParse(OperationIdText.Text, out Guid operationId))
        {
            throw new ArgumentException("Operation must be a GUID from the technical history list.");
        }

        TechnicalOperationDetail? detail = await RequireHistory().GetOperationDetailAsync(operationId);
        OperationOutputText.Text = HistoryTextPresenter.FormatOperation(detail);
    }

    private async Task CompareAsync()
    {
        string dimension = ComparisonDimensionCombo.SelectedItem?.ToString() ?? string.Empty;
        HistoryComparisonFilters filters = HistoryUiParser.CreateComparisonFilters(
            dimension,
            ComparisonBaselineText.Text ?? string.Empty,
            ComparisonCandidateText.Text ?? string.Empty);
        if (ComparisonMetricCombo.SelectedItem is not HistoryMetric metric)
        {
            throw new ArgumentException("Select a comparison metric.");
        }

        AnalyticsComparison comparison = await RequireHistory().CompareAsync(
            filters.Baseline,
            filters.Candidate,
            metric);
        ComparisonOutputText.Text = HistoryTextPresenter.FormatComparison(comparison);
    }

    private async Task ApplyRetentionAsync()
    {
        if (RetentionCombo.SelectedItem is not HistoryRetentionChoice choice)
        {
            throw new ArgumentException("Select a retention option.");
        }

        ITechnicalHistoryStore history = RequireHistory();
        HistoryRetention retention = choice.Value;
        await history.SetRetentionAsync(retention);
        int deleted = await history.ApplyRetentionAsync(retention, DateTimeOffset.UtcNow);
        RetentionOutputText.Text = $"Retention {retention} saved and applied; deleted {deleted} old technical record(s).";
        await LoadHistoryAsync();
    }

    private async Task PreviewClearAsync()
    {
        HistoryClearScope scope = HistoryUiParser.CreateClearScope(
            ClearAllCheckBox.IsChecked == true,
            ClearFromText.Text,
            ClearToText.Text);
        _clearPreview = await RequireHistory().PreviewClearAsync(scope);
        ClearOutputText.Text = HistoryTextPresenter.FormatClearPreview(_clearPreview);
        ConfirmClearButton.IsEnabled = true;
    }

    private async Task ConfirmClearAsync()
    {
        HistoryClearPreview preview = _clearPreview ??
            throw new InvalidOperationException("Preview the exact clear scope before confirmation.");
        HistoryClearPreview cleared = await RequireHistory().ClearAsync(preview, confirmed: true);
        ClearOutputText.Text = $"Cleared the confirmed scope ({cleared.TotalRecords} technical record(s)).";
        _clearPreview = null;
        ConfirmClearButton.IsEnabled = false;
        await LoadHistoryAsync();
    }

    private HistoryFilter CreateCurrentFilter() => HistoryUiParser.CreateFilter(
        FilterFromText.Text,
        FilterToText.Text,
        FilterClientCombo.SelectedItem?.ToString(),
        FilterBackendCombo.SelectedItem?.ToString(),
        FilterModelText.Text,
        FilterSessionText.Text,
        FilterStatusCombo.SelectedItem?.ToString(),
        FilterErrorCombo.SelectedItem?.ToString());

    private async Task RunHistoryActionAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            InvalidOperationException or
            IOException or
            UnauthorizedAccessException or
            Microsoft.Data.Sqlite.SqliteException)
        {
            HistoryStateText.Text = $"History action failed: {exception.Message}";
        }
    }

    private void InvalidateClearPreview()
    {
        _clearPreview = null;
        ConfirmClearButton.IsEnabled = false;
    }

    private ITechnicalHistoryStore RequireHistory() =>
        _history ?? throw new InvalidOperationException("Technical history is unavailable.");

    private static string[] WithAny<T>()
        where T : struct, Enum => ["Any", .. Enum.GetNames<T>()];

    private void RefreshLiveRequests()
    {
        LiveRequestsText.Text = _liveRequestState is null
            ? "Active requests: unavailable while live tracking is not composed."
            : LiveRequestTextPresenter.Format(_liveRequestState.GetSnapshot());
    }

    private void RefreshRequestDetail()
    {
        RequestDetailText.Text = RequestDetailTextPresenter.Format(_observationSource?.Latest);
    }

    private void RefreshDiagnostics()
    {
        DiagnosticsSummaryText.Text = DiagnosticsSummaryTextPresenter.Format(
            _runtimeStatus,
            _observationSource?.Latest,
            _historyState,
            _resourceSource?.Latest,
            _liveRequestState?.GetSnapshot());
    }

    private void RefreshResources()
    {
        ResourceTelemetryText.Text = ResourceTelemetryTextPresenter.FormatLatest(_resourceSource?.LatestSamples);
    }

    private static string CreateClientEndpointText(AppRuntimeStatus runtimeStatus)
    {
        if (!runtimeStatus.ProxyRunning ||
            !Uri.TryCreate(runtimeStatus.Listener, UriKind.Absolute, out Uri? listener))
        {
            return "Client base URLs: unavailable while the gateway is not running.";
        }

        string origin = listener.GetLeftPart(UriPartial.Authority);
        IEnumerable<string> endpoints =
        [
            $"Generic/Unknown: {origin}{ClientEndpointCatalog.GenericBasePath}",
            .. ClientEndpointCatalog.KnownClients.Select(endpoint =>
                $"{endpoint.DisplayName}: {origin}{endpoint.BasePath}"),
        ];
        return "Client base URLs:" + Environment.NewLine + string.Join(Environment.NewLine, endpoints);
    }
}
