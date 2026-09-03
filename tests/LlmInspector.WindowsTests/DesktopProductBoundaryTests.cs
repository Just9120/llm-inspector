using System.Xml.Linq;
using LlmInspector.Domain;

namespace LlmInspector.WindowsTests;

[TestClass]
public sealed class DesktopProductBoundaryTests
{
    [TestMethod]
    public void MainWindowContainsMonitoringAnalyticsAndDiagnosticsSurfaces()
    {
        XDocument xaml = XDocument.Load(Path.Combine(
            FindRepositoryRoot().FullName,
            "src",
            "LlmInspector.App",
            "MainWindow.axaml"));

        string[] headings = xaml
            .Descendants()
            .Select(element => element.Attribute("Text")?.Value)
            .Where(value => value is not null)
            .Select(value => value!)
            .ToArray();

        CollectionAssert.Contains(headings, "Live requests");
        CollectionAssert.Contains(headings, "History and analytics");
        CollectionAssert.Contains(headings, "Diagnostics");

        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        string[] namedControls = xaml
            .Descendants()
            .Select(element => element.Attribute(x + "Name")?.Value)
            .Where(value => value is not null)
            .Select(value => value!)
            .ToArray();
        CollectionAssert.Contains(namedControls, "LiveRequestsText");
        CollectionAssert.Contains(namedControls, "AnalyticsOutputText");
        CollectionAssert.Contains(namedControls, "DiagnosticsSummaryText");
    }

    [TestMethod]
    public void EveryAcceptedBackendEndpointIsRestrictedToThisPc()
    {
        foreach (string backend in new[] { "ollama", "llama-cpp", "lm-studio" })
        {
            App.AppLaunchConfiguration configuration = App.AppLaunchConfiguration.Parse(
                [$"--backend={backend}"]);

            Assert.IsTrue(IsLiteralLoopback(configuration.BackendBaseAddress.Host));
            Assert.IsTrue(IsLiteralLoopback(configuration.CreateProxyOptions().BackendBaseAddress.Host));
        }

        App.AppLaunchConfiguration ipv6 = App.AppLaunchConfiguration.Parse(
            ["--backend-url=http://[::1]:1234/"]);
        Assert.IsTrue(IsLiteralLoopback(ipv6.BackendBaseAddress.Host));
    }

    [TestMethod]
    [DataRow("--start-backend")]
    [DataRow("--stop-backend")]
    [DataRow("--restart-backend")]
    [DataRow("--load-model=model-a")]
    [DataRow("--set-runtime-parameter=threads:8")]
    public void BackendLifecycleAndRuntimeMutationCommandsAreRejected(string argument)
    {
        _ = Assert.ThrowsExactly<ArgumentException>(() =>
            App.AppLaunchConfiguration.Parse([argument]));
    }

    [TestMethod]
    public void DiagnosticsSurfaceReportsTechnicalStateWithoutContentFields()
    {
        ProxyObservation observation = new(
            Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"),
            DateTimeOffset.UnixEpoch,
            TimeSpan.FromMilliseconds(125),
            503,
            ProxyOutcome.BackendUnavailable,
            ClientKind.Cline,
            BackendResponseTelemetry.Unavailable(BackendKind.Ollama, "desktop-boundary-test-v1"));

        string summary = App.DiagnosticsSummaryTextPresenter.Format(
            App.AppRuntimeStatus.Running(
                new Uri("http://127.0.0.1:5117/"),
                new Uri("http://127.0.0.1:11434/"),
                BackendKind.Ollama),
            observation,
            "Technical history is available.");

        StringAssert.Contains(summary, "Gateway: available");
        StringAssert.Contains(summary, "01234567");
        StringAssert.Contains(summary, "outcome=BackendUnavailable");
        StringAssert.Contains(summary, "HTTP=503");
        StringAssert.Contains(summary, "125 ms [calculated]");
        Assert.IsFalse(summary.Contains("prompt", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(summary.Contains("response", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(summary.Contains("reasoning", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(summary.Contains("tool", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsLiteralLoopback(string host) =>
        System.Net.IPAddress.TryParse(host.Trim('[', ']'), out System.Net.IPAddress? address) &&
        System.Net.IPAddress.IsLoopback(address);

    private static DirectoryInfo FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "LlmInspector.slnx")))
            {
                return current;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root containing LlmInspector.slnx was not found.");
    }
}
