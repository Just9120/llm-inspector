namespace LlmInspector.ContractTests;

[TestClass]
public sealed class ScaffoldBoundaryTests
{
    [TestMethod]
    public void ContractModulesAreIndependentlyLoadable()
    {
        Assert.AreEqual("LlmInspector.Domain", typeof(Domain.ModuleMarker).Assembly.GetName().Name);
        Assert.AreEqual("LlmInspector.Adapters", typeof(Adapters.ModuleMarker).Assembly.GetName().Name);
    }
}
