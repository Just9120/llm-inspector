using Avalonia.Controls;
using Avalonia.Threading;
using LlmInspector.Application;
using LlmInspector.Gateway;

namespace LlmInspector.App;

public partial class MainWindow : Window
{
    private readonly ILiveRequestSnapshotSource? _liveRequestState;
    private readonly IProxyObservationSnapshotSource? _observationSource;
    private readonly DispatcherTimer? _liveRefreshTimer;

    public MainWindow()
        : this(AppRuntimeStatus.NotStarted, null, null)
    {
    }

    public MainWindow(
        AppRuntimeStatus runtimeStatus,
        ILiveRequestSnapshotSource? liveRequestState = null,
        IProxyObservationSnapshotSource? observationSource = null)
    {
        InitializeComponent();
        _liveRequestState = liveRequestState;
        _observationSource = observationSource;

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
        RefreshLiveRequests();
        RefreshRequestDetail();

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
            };
            _liveRefreshTimer.Start();
            Closed += (_, _) => _liveRefreshTimer.Stop();
        }
    }

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
