using System.Runtime.InteropServices;

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

        App.AppRuntimeStatus running = App.AppRuntimeStatus.Running(listener, backend);
        App.AppRuntimeStatus unavailable = App.AppRuntimeStatus.ListenerUnavailable(backend, listener.Port);

        Assert.IsTrue(running.ProxyRunning);
        Assert.AreEqual(listener.ToString(), running.Listener);
        Assert.AreEqual(backend.ToString(), running.Backend);
        Assert.IsFalse(unavailable.ProxyRunning);
        StringAssert.Contains(unavailable.State, listener.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
