using Avalonia.Controls;
using LlmInspector.Application;

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
        TechnicalDataText.Text = string.Join(
            Environment.NewLine,
            TechnicalDataDisclosure.CurrentCategories.Select(
                category => $"{category.Name}: {category.Fields}. Retention: {category.Retention}."));
        PersistentDataText.Text = TechnicalDataDisclosure.PersistentDataStatement;
        ForbiddenContentText.Text = TechnicalDataDisclosure.ForbiddenContentStatement;
    }
}
