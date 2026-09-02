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
}
