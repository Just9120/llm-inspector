using Avalonia.Controls;
using LlmInspector.Application;
using LlmInspector.Gateway;

namespace LlmInspector.App;

public partial class MainWindow : Window
{
    public MainWindow()
        : this(AppRuntimeStatus.NotStarted)
    {
    }

    public MainWindow(AppRuntimeStatus runtimeStatus)
    {
        InitializeComponent();

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
