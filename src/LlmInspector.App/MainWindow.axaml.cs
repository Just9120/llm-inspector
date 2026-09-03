using Avalonia.Controls;
using Avalonia.Threading;
using LlmInspector.Application;
using LlmInspector.Domain;
using LlmInspector.Gateway;

namespace LlmInspector.App;

public partial class MainWindow : Window
{
    private readonly ILiveRequestSnapshotSource? _liveRequestState;
    private readonly IProxyObservationSnapshotSource? _observationSource;
    private readonly ITechnicalHistoryStore? _history;
    private readonly AppRuntimeStatus _runtimeStatus;
    private readonly string _historyState;
    private readonly DispatcherTimer? _liveRefreshTimer;
    private HistoryClearPreview? _clearPreview;

    public MainWindow()
        : this(AppRuntimeStatus.NotStarted, null, null, null, "Technical history is not composed.")
    {
    }

    public MainWindow(
        AppRuntimeStatus runtimeStatus,
        ILiveRequestSnapshotSource? liveRequestState = null,
        IProxyObservationSnapshotSource? observationSource = null,
        ITechnicalHistoryStore? history = null,
        string? historyState = null)
    {
        InitializeComponent();
        _liveRequestState = liveRequestState;
        _observationSource = observationSource;
        _history = history;
        _runtimeStatus = runtimeStatus;
        _historyState = historyState ?? "Technical history state is unavailable.";

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
        RefreshLiveRequests();
        RefreshRequestDetail();
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
                RefreshDiagnostics();
            };
            _liveRefreshTimer.Start();
            Closed += (_, _) => _liveRefreshTimer.Stop();
        }

        Opened += OnOpened;
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
            _historyState);
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
