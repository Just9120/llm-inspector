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
        nameof(ProxyObservation.TimeToFirstToken),
        nameof(ProxyObservation.Duration),
        nameof(ProxyObservation.HttpStatusCode),
        nameof(ProxyObservation.Outcome),
        nameof(ProxyObservation.ErrorType),
        nameof(ProxyObservation.Client),
        nameof(ProxyObservation.BackendTelemetry),
        nameof(ProxyObservation.Correlation),
        nameof(ProxyObservation.ContextChangeTokens),
        nameof(ProxyObservation.AgentTurn),
        nameof(ProxyObservation.RuntimeFacts),
    ];

    private static readonly string[] AllowedCorrelationProperties =
    [
        nameof(RequestCorrelation.SessionId),
        nameof(RequestCorrelation.TurnId),
        nameof(RequestCorrelation.TurnSequence),
        nameof(RequestCorrelation.OperationId),
    ];

    private static readonly string[] AllowedRuntimeFactProperties =
    [
        "BackendVersion",
        "ClientVersion",
        "ConfigurationId",
        "FrameworkVersion",
        "GpuDriverVersion",
        "InspectorVersion",
        "ModelVersion",
        "OperatingSystemVersion",
        "TelemetryContractVersion",
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

        string[] runtimeFactProperties = typeof(TechnicalRuntimeFacts)
            .GetProperties()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        CollectionAssert.AreEqual(
            AllowedRuntimeFactProperties,
            runtimeFactProperties);
        Assert.IsTrue(typeof(TechnicalRuntimeFacts).GetProperties().All(property =>
            property.PropertyType == typeof(TechnicalIdentifier)));

        string[] correlationProperties = typeof(RequestCorrelation)
            .GetProperties()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        CollectionAssert.AreEqual(
            AllowedCorrelationProperties.Order(StringComparer.Ordinal).ToArray(),
            correlationProperties);
        Assert.IsFalse(typeof(RequestCorrelation).GetProperties().Any(property => property.PropertyType == typeof(string)));

        string[] agentProperties = typeof(AgentTurnTelemetry)
            .GetProperties()
            .Select(property => property.Name)
            .Concat(typeof(AgentToolCall).GetProperties().Select(property => property.Name))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] allowedAgentProperties =
        [
            "AvailableToolCount",
            "Completion",
            "InvokedToolCount",
            "Sequence",
            "ToolCalls",
            "ToolDetailsComplete",
            "ToolName",
            "ToolResultCount",
            "Unavailable",
        ];
        CollectionAssert.AreEqual(allowedAgentProperties, agentProperties);
        Assert.IsFalse(
            typeof(AgentTurnTelemetry).GetProperties().Any(property => property.PropertyType == typeof(string)));

        string[] telemetryPropertyNames = typeof(BackendResponseTelemetry)
            .GetProperties()
            .Select(property => property.Name)
            .Concat(typeof(MetricValue).GetProperties().Select(property => property.Name))
            .Concat(typeof(BackendMetric).GetProperties().Select(property => property.Name))
            .Concat(typeof(TechnicalIdentifier).GetProperties().Select(property => property.Name))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] allowedTelemetryPropertyNames =
        [
            "Backend",
            "BackendSpecificMetrics",
            "CachedPromptTokens",
            "CompletionTokens",
            "CompletionTokensPerSecond",
            "ContextHistoryTokens",
            "ContextLimitTokens",
            "ContextToolTokens",
            "ContextUsageTokens",
            "DerivationVersion",
            "Key",
            "Metric",
            "Model",
            "ModelLoadDisposition",
            "ModelLoadTime",
            "NativeName",
            "PromptTokens",
            "PromptTokensPerSecond",
            "Quality",
            "QueueTime",
            "ReasoningTokens",
            "Source",
            "SourceVersion",
            "TotalTokens",
            "Unit",
            "Value",
        ];
        CollectionAssert.AreEqual(allowedTelemetryPropertyNames, telemetryPropertyNames);
    }

    [TestMethod]
    public void TechnicalDataDisclosureListsFieldsAndRetention()
    {
        Assert.HasCount(5, TechnicalDataDisclosure.CurrentCategories);

        foreach (TechnicalDataCategory category in TechnicalDataDisclosure.CurrentCategories)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(category.Name));
            Assert.IsFalse(string.IsNullOrWhiteSpace(category.Fields));
            Assert.IsFalse(string.IsNullOrWhiteSpace(category.Retention));
        }

        StringAssert.Contains(TechnicalDataDisclosure.CurrentCategories[0].Retention, "Process lifetime", StringComparison.Ordinal);
        StringAssert.Contains(TechnicalDataDisclosure.CurrentCategories[1].Retention, "30 days (default)", StringComparison.Ordinal);
        StringAssert.Contains(TechnicalDataDisclosure.CurrentCategories[2].Fields, "autostart", StringComparison.OrdinalIgnoreCase);
        StringAssert.Contains(TechnicalDataDisclosure.CurrentCategories[2].Fields, "monitoring performance profile", StringComparison.OrdinalIgnoreCase);
        StringAssert.Contains(TechnicalDataDisclosure.CurrentCategories[2].Retention, "settings file", StringComparison.Ordinal);
        StringAssert.Contains(TechnicalDataDisclosure.CurrentCategories[3].Fields, "typed errors", StringComparison.Ordinal);
        StringAssert.Contains(TechnicalDataDisclosure.CurrentCategories[3].Retention, "user deletes", StringComparison.Ordinal);
        StringAssert.Contains(TechnicalDataDisclosure.CurrentCategories[4].Fields, "aggregate", StringComparison.Ordinal);
        StringAssert.Contains(TechnicalDataDisclosure.CurrentCategories[4].Retention, "user deletes", StringComparison.Ordinal);
        StringAssert.Contains(TechnicalDataDisclosure.PersistentDataStatement, "%LOCALAPPDATA%", StringComparison.Ordinal);
        StringAssert.Contains(TechnicalDataDisclosure.PersistentDataStatement, "30 days", StringComparison.Ordinal);
        StringAssert.Contains(TechnicalDataDisclosure.PersistentDataStatement, "settings.json", StringComparison.Ordinal);
        StringAssert.Contains(TechnicalDataDisclosure.PersistentDataStatement, "not uploaded", StringComparison.Ordinal);
        StringAssert.Contains(TechnicalDataDisclosure.ForbiddenContentStatement, "never retained", StringComparison.Ordinal);
    }

    [TestMethod]
    public void ResourceTelemetryContractContainsOnlyTechnicalAllowlistedFields()
    {
        string[] actual = typeof(TechnicalResourceSampleRecord)
            .GetProperties()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] expected =
        [
            "BackendToClientBytes",
            "CapturedAt",
            "ClientToBackendBytes",
            "CpuPercent",
            "DiskReadBytes",
            "DiskWriteBytes",
            "DroppedSampleCount",
            "GpuDeviceId",
            "GpuDriverVersion",
            "GpuPowerWatts",
            "GpuTemperatureCelsius",
            "GpuUtilizationPercent",
            "GpuVramTotalBytes",
            "GpuVramUsedBytes",
            "MemoryPercent",
            "MemoryUsedBytes",
            "OperationId",
            "ProcessCpuPercent",
            "ProcessMemoryBytes",
            "RelatedProcess",
            "RequestId",
            "SampleId",
            "Stage",
        ];

        CollectionAssert.AreEqual(expected, actual);
        string[] forbiddenFragments = ["Content", "Prompt", "Response", "Argument", "Result", "Header"];
        Assert.IsFalse(actual.Any(name => forbiddenFragments.Any(
            fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase))));
    }
}
