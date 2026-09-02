namespace LlmInspector.PrivacyTests;

[TestClass]
public sealed class ScaffoldBoundaryTests
{
    [TestMethod]
    public void PrivacySensitiveModulesAreIndependentlyLoadable()
    {
        Assert.AreEqual("LlmInspector.Telemetry", typeof(Telemetry.ModuleMarker).Assembly.GetName().Name);
        Assert.AreEqual("LlmInspector.Storage.Sqlite", typeof(Storage.Sqlite.ModuleMarker).Assembly.GetName().Name);
        Assert.AreEqual("LlmInspector.Diagnostics", typeof(Diagnostics.ModuleMarker).Assembly.GetName().Name);
    }
}
