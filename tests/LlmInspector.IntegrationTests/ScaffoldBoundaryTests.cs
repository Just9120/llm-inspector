namespace LlmInspector.IntegrationTests;

[TestClass]
public sealed class ScaffoldBoundaryTests
{
    [TestMethod]
    public void IntegrationModulesAreIndependentlyLoadable()
    {
        Assert.AreEqual("LlmInspector.Gateway", typeof(Gateway.ModuleMarker).Assembly.GetName().Name);
        Assert.AreEqual("LlmInspector.Telemetry", typeof(Telemetry.ModuleMarker).Assembly.GetName().Name);
    }
}
