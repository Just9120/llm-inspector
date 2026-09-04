using System.Runtime.InteropServices;
using LlmInspector.Domain;
using LlmInspector.Gateway;

namespace LlmInspector.WindowsTests;

[TestClass]
public sealed class ScaffoldBoundaryTests
{
    [TestMethod]
    public void WindowsCompositionModulesLoadOnAWindowsHost()
    {
        Assert.IsTrue(RuntimeInformation.IsOSPlatform(OSPlatform.Windows));
        Assert.AreEqual("LlmInspector.App", typeof(App.App).Assembly.GetName().Name);
        Assert.AreEqual("LlmInspector.Resources.Windows", typeof(Resources.Windows.ModuleMarker).Assembly.GetName().Name);
    }

    [TestMethod]
    public void RuntimeStatusUsesOnlyValidatedEndpointSummaries()
    {
        Uri listener = new("http://127.0.0.1:5117/");
        Uri backend = new("http://127.0.0.1:11434/");

        App.AppRuntimeStatus running = App.AppRuntimeStatus.Running(listener, backend, BackendKind.Ollama);
        App.AppRuntimeStatus unavailable = App.AppRuntimeStatus.ListenerUnavailable(
            backend,
            listener.Port,
            BackendKind.Ollama);

        Assert.IsTrue(running.ProxyRunning);
        Assert.AreEqual(listener.ToString(), running.Listener);
        Assert.AreEqual(backend.ToString(), running.Backend);
        Assert.AreEqual(nameof(BackendKind.Ollama), running.BackendType);
        Assert.IsFalse(unavailable.ProxyRunning);
        StringAssert.Contains(unavailable.State, listener.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [TestMethod]
    [DataRow("ollama", BackendKind.Ollama, 11434)]
    [DataRow("llama-cpp", BackendKind.LlamaCpp, 8080)]
    [DataRow("lm-studio", BackendKind.LmStudio, 1234)]
    public void LaunchConfigurationSelectsBackendAndSafeDefaultEndpoint(
        string option,
        BackendKind expectedBackend,
        int expectedPort)
    {
        App.AppLaunchConfiguration configuration = App.AppLaunchConfiguration.Parse([$"--backend={option}"]);

        Assert.AreEqual(expectedBackend, configuration.Backend);
        Assert.AreEqual(expectedPort, configuration.BackendBaseAddress.Port);
        Assert.IsTrue(System.Net.IPAddress.IsLoopback(
            System.Net.IPAddress.Parse(configuration.BackendBaseAddress.Host.Trim('[', ']'))));
    }

    [TestMethod]
    public void LaunchConfigurationAcceptsExplicitLoopbackAndRejectsUnsafeOrAmbiguousInput()
    {
        App.AppLaunchConfiguration configuration = App.AppLaunchConfiguration.Parse(
        [
            "--backend=lm-studio",
            "--backend-url=http://127.0.0.1:4321/",
            "--listener-port=5118",
        ]);

        Assert.AreEqual(new Uri("http://127.0.0.1:4321/"), configuration.BackendBaseAddress);
        Assert.AreEqual(5118, configuration.ListenerPort);
        _ = Assert.ThrowsExactly<ArgumentException>(() => App.AppLaunchConfiguration.Parse(
            ["--backend-url=https://example.com/"]));
        _ = Assert.ThrowsExactly<ArgumentException>(() => App.AppLaunchConfiguration.Parse(
            ["--backend=ollama", "--backend=llama-cpp"]));
        _ = Assert.ThrowsExactly<ArgumentException>(() => App.AppLaunchConfiguration.Parse(
            ["--unknown=unsafe"]));
    }

    [TestMethod]
    public void LaunchConfigurationRequiresExplicitRemoteOptionAndTailscaleHttpsName()
    {
        App.AppLaunchConfiguration remote = App.AppLaunchConfiguration.Parse(
        [
            "--backend=ollama",
            "--remote-backend-url=https://backend.example-tailnet.ts.net/",
        ]);

        Assert.AreEqual(BackendConnectionScope.TailscaleHttps, remote.BackendConnectionScope);
        Assert.AreEqual(
            BackendConnectionScope.TailscaleHttps,
            remote.CreateProxyOptions().BackendConnectionScope);
        _ = Assert.ThrowsExactly<ArgumentException>(() => App.AppLaunchConfiguration.Parse(
            ["--remote-backend-url=http://backend.example-tailnet.ts.net/"]));
        _ = Assert.ThrowsExactly<ArgumentException>(() => App.AppLaunchConfiguration.Parse(
            [
                "--backend-url=http://127.0.0.1:11434/",
                "--remote-backend-url=https://backend.example-tailnet.ts.net/",
            ]));
    }
}
