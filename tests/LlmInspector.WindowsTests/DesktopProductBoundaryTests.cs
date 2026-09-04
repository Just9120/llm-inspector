using System.Reflection;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using LlmInspector.Domain;

namespace LlmInspector.WindowsTests;

[TestClass]
public sealed class DesktopProductBoundaryTests
{
    [TestMethod]
    public void WindowsTrayUsesTheExportedUnicodeShellNotifyIconEntryPoint()
    {
        const string EntryPointName = "Shell_NotifyIconW";
        MethodInfo? method = typeof(App.WindowsTrayHost).GetMethod(
            "ShellNotifyIcon",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method);

        DllImportAttribute? import = method.GetCustomAttribute<DllImportAttribute>();
        Assert.IsNotNull(import);
        Assert.AreEqual("shell32.dll", import.Value, ignoreCase: true);
        Assert.AreEqual(EntryPointName, import.EntryPoint);
        Assert.AreEqual(CharSet.Unicode, import.CharSet);
        Assert.IsTrue(import.ExactSpelling);

        IntPtr shell32 = NativeLibrary.Load("shell32.dll");
        try
        {
            Assert.IsTrue(NativeLibrary.TryGetExport(shell32, EntryPointName, out IntPtr entryPoint));
            Assert.AreNotEqual(IntPtr.Zero, entryPoint);
        }
        finally
        {
            NativeLibrary.Free(shell32);
        }
    }

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
        CollectionAssert.Contains(headings, "Anonymized diagnostic snapshot");
        CollectionAssert.Contains(headings, "Anonymized analytics export");
        CollectionAssert.Contains(headings, "Background and notifications");
        CollectionAssert.Contains(headings, "Производительность мониторинга");
        CollectionAssert.Contains(headings, "Управление локальным backend");
        CollectionAssert.Contains(headings, "Защищённый remote access");
        CollectionAssert.Contains(headings, "Remote backend");

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
        CollectionAssert.Contains(namedControls, "SnapshotScopeCombo");
        CollectionAssert.Contains(namedControls, "SnapshotFromText");
        CollectionAssert.Contains(namedControls, "SnapshotToText");
        CollectionAssert.Contains(namedControls, "SnapshotOperationIdText");
        CollectionAssert.Contains(namedControls, "SnapshotPreviewText");
        CollectionAssert.Contains(namedControls, "PreviewSnapshotButton");
        CollectionAssert.Contains(namedControls, "SaveSnapshotButton");
        CollectionAssert.Contains(namedControls, "ExportFromText");
        CollectionAssert.Contains(namedControls, "ExportToText");
        CollectionAssert.Contains(namedControls, "ExportPreviewText");
        CollectionAssert.Contains(namedControls, "PreviewExportButton");
        CollectionAssert.Contains(namedControls, "SaveExportButton");
        CollectionAssert.Contains(namedControls, "AutostartCheckBox");
        CollectionAssert.Contains(namedControls, "NotifyBackendUnavailableCheckBox");
        CollectionAssert.Contains(namedControls, "NotifyLongOperationCheckBox");
        CollectionAssert.Contains(namedControls, "NotifyRecurringErrorCheckBox");
        CollectionAssert.Contains(namedControls, "NotifyHighContextCheckBox");
        CollectionAssert.Contains(namedControls, "SilentNotificationsCheckBox");
        CollectionAssert.Contains(namedControls, "PerformanceProfileCombo");
        CollectionAssert.Contains(namedControls, "CustomSamplingIntervalText");
        CollectionAssert.Contains(namedControls, "ResetPerformanceProfileButton");
        CollectionAssert.Contains(namedControls, "PerformanceProfileStateText");
        CollectionAssert.Contains(namedControls, "LifecycleBackendCombo");
        CollectionAssert.Contains(namedControls, "LifecycleExecutablePathText");
        CollectionAssert.Contains(namedControls, "DiscoverBackendButton");
        CollectionAssert.Contains(namedControls, "ConfirmLifecycleTargetCheckBox");
        CollectionAssert.Contains(namedControls, "StartBackendButton");
        CollectionAssert.Contains(namedControls, "StopBackendButton");
        CollectionAssert.Contains(namedControls, "RestartBackendButton");
        CollectionAssert.Contains(namedControls, "LifecycleParameterCombo");
        CollectionAssert.Contains(namedControls, "ApplyLifecycleParameterButton");
        CollectionAssert.Contains(namedControls, "ResetLifecycleParametersButton");
        CollectionAssert.Contains(namedControls, "LifecycleModelText");
        CollectionAssert.Contains(namedControls, "LoadLifecycleModelButton");
        CollectionAssert.Contains(namedControls, "RemoteBoundaryConfirmationCheckBox");
        CollectionAssert.Contains(namedControls, "EnableRemoteAccessButton");
        CollectionAssert.Contains(namedControls, "RotateRemoteTokenButton");
        CollectionAssert.Contains(namedControls, "DisableRemoteAccessButton");
        CollectionAssert.Contains(namedControls, "RemoteOneTimeTokenText");
        CollectionAssert.Contains(namedControls, "ProbeRemoteBackendButton");
        CollectionAssert.Contains(namedControls, "RemoteBackendStateText");

        XElement saveSnapshot = xaml.Descendants()
            .Single(element => element.Attribute(x + "Name")?.Value == "SaveSnapshotButton");
        Assert.AreEqual("False", saveSnapshot.Attribute("IsEnabled")?.Value);
        XElement saveExport = xaml.Descendants()
            .Single(element => element.Attribute(x + "Name")?.Value == "SaveExportButton");
        Assert.AreEqual("False", saveExport.Attribute("IsEnabled")?.Value);
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
        StringAssert.Contains(summary, "FACT | BackendUnavailable");
        StringAssert.Contains(summary, "Evidence:");
        Assert.IsFalse(summary.Contains("private-content-canary", StringComparison.Ordinal));
    }

    [TestMethod]
    public void DiagnosticsSurfaceDoesNotCallLongRunningActiveStageAConfirmedStall()
    {
        Guid requestId = Guid.NewGuid();
        LiveRequestSnapshot active = new(
            requestId,
            ClientKind.Cline,
            RequestStageValue.ProtocolObserved(RequestStage.ReasoningGeneration, "ui-stall-test-v1"),
            DateTimeOffset.UnixEpoch,
            MetricValue.Calculated(
                30_000,
                MetricUnit.Milliseconds,
                MetricSource.Inspector,
                "ui-stall-test-v1",
                "elapsed-v1"),
            MetricValue.Unavailable(MetricUnit.Percent, MetricSource.Inspector, "ui-stall-test-v1"),
            MetricValue.Unavailable(MetricUnit.Milliseconds, MetricSource.Inspector, "ui-stall-test-v1"));

        string summary = App.DiagnosticsSummaryTextPresenter.Format(
            App.AppRuntimeStatus.Running(
                new Uri("http://127.0.0.1:5117/"),
                new Uri("http://127.0.0.1:11434/"),
                BackendKind.Ollama),
            null,
            "Technical history is available.",
            liveRequests: new LiveRequestCollectionSnapshot([active], null));

        StringAssert.Contains(summary, "FACT | ActiveWork");
        StringAssert.Contains(summary, "INSUFFICIENTDATA | ConfirmedStall");
        Assert.IsFalse(summary.Contains("FACT | ConfirmedStall", StringComparison.Ordinal));
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
