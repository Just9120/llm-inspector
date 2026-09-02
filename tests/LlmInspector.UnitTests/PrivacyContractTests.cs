using LlmInspector.Application;
using LlmInspector.Domain;

namespace LlmInspector.UnitTests;

[TestClass]
public sealed class PrivacyContractTests
{
    private static readonly string[] AllowedObservationProperties =
    [
        nameof(ProxyObservation.RequestId),
        nameof(ProxyObservation.StartedAt),
        nameof(ProxyObservation.Duration),
        nameof(ProxyObservation.HttpStatusCode),
        nameof(ProxyObservation.Outcome),
    ];

    [TestMethod]
    public void ProxyObservationExposesOnlyTheTechnicalAllowlist()
    {
        string[] actualProperties = typeof(ProxyObservation)
            .GetProperties()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] expectedProperties = AllowedObservationProperties
            .Order(StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(expectedProperties, actualProperties);
        Assert.IsFalse(
            typeof(ProxyObservation).GetProperties().Any(property => property.PropertyType == typeof(string)),
            "A free-form string would allow request or response content to enter the observation contract.");
    }

    [TestMethod]
    public void TechnicalDataDisclosureListsFieldsAndRetention()
    {
        Assert.HasCount(1, TechnicalDataDisclosure.CurrentCategories);

        TechnicalDataCategory category = TechnicalDataDisclosure.CurrentCategories[0];
        Assert.IsFalse(string.IsNullOrWhiteSpace(category.Name));
        Assert.IsFalse(string.IsNullOrWhiteSpace(category.Fields));
        StringAssert.Contains(category.Retention, "Process lifetime", StringComparison.Ordinal);
        StringAssert.Contains(TechnicalDataDisclosure.PersistentDataStatement, "none", StringComparison.OrdinalIgnoreCase);
        StringAssert.Contains(TechnicalDataDisclosure.ForbiddenContentStatement, "never retained", StringComparison.Ordinal);
    }
}
