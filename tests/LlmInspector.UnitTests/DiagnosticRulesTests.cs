using LlmInspector.Application;
using LlmInspector.Diagnostics;
using LlmInspector.Domain;

namespace LlmInspector.UnitTests;

[TestClass]
public sealed class DiagnosticRulesTests
{
    private const string SourceVersion = "diagnostic-test-v1";

    [TestMethod]
    public void VersionedThresholdBoundariesProduceFactsAndExplicitCpuOffloadHypothesis()
    {
        Guid requestId = Guid.NewGuid();
        BackendResponseTelemetry telemetry = BackendResponseTelemetry.Unavailable(
            BackendKind.Ollama,
            SourceVersion) with
        {
            PromptTokens = Exact(8_192, MetricUnit.TokenCount),
            CompletionTokensPerSecond = Exact(10, MetricUnit.TokensPerSecond),
            ContextUsageTokens = Exact(900, MetricUnit.TokenCount),
            ContextLimitTokens = Exact(1_000, MetricUnit.TokenCount),
            ModelLoadTime = Exact(1_000, MetricUnit.Milliseconds),
            QueueTime = Exact(1_000, MetricUnit.Milliseconds),
            ModelLoadDisposition = ModelLoadDisposition.Cold,
        };
        ProxyObservation observation = Observation(requestId, telemetry);
        TechnicalResourceSampleRecord resource = Resource(requestId) with
        {
            ProcessCpuPercent = Exact(60, MetricUnit.Percent),
            GpuUtilizationPercent = Exact(20, MetricUnit.Percent),
            GpuVramUsedBytes = Exact(90, MetricUnit.Bytes),
            GpuVramTotalBytes = Exact(100, MetricUnit.Bytes),
        };

        IReadOnlyList<DiagnosticConclusion> conclusions = DiagnosticRuleset.Default.Evaluate(
            new DiagnosticInput(observation, resource, null));

        AssertConclusion(conclusions, DiagnosticRuleId.LargePrompt, DiagnosticConclusionKind.Fact);
        AssertConclusion(conclusions, DiagnosticRuleId.SlowGeneration, DiagnosticConclusionKind.Fact);
        AssertConclusion(conclusions, DiagnosticRuleId.CpuOffload, DiagnosticConclusionKind.Hypothesis);
        AssertConclusion(conclusions, DiagnosticRuleId.VramPressure, DiagnosticConclusionKind.Fact);
        AssertConclusion(conclusions, DiagnosticRuleId.ModelLoadingLatency, DiagnosticConclusionKind.Fact);
        AssertConclusion(conclusions, DiagnosticRuleId.QueueWaitingLatency, DiagnosticConclusionKind.Fact);
        AssertConclusion(conclusions, DiagnosticRuleId.HighContextUsage, DiagnosticConclusionKind.Fact);
        Assert.IsTrue(conclusions.All(item => item.RuleVersion == DiagnosticRuleOptions.Version1));
        Assert.IsTrue(conclusions.Where(item => item.Kind != DiagnosticConclusionKind.InsufficientData)
            .All(item => item.Evidence.Count > 0 && !string.IsNullOrWhiteSpace(item.Explanation)));
    }

    [TestMethod]
    public void ValuesOutsideThresholdsDoNotCreateFalsePositiveConclusions()
    {
        Guid requestId = Guid.NewGuid();
        BackendResponseTelemetry telemetry = BackendResponseTelemetry.Unavailable(
            BackendKind.Ollama,
            SourceVersion) with
        {
            PromptTokens = Exact(8_191, MetricUnit.TokenCount),
            CompletionTokensPerSecond = Exact(10.01m, MetricUnit.TokensPerSecond),
            ContextUsageTokens = Exact(899, MetricUnit.TokenCount),
            ContextLimitTokens = Exact(1_000, MetricUnit.TokenCount),
            ModelLoadTime = Exact(999, MetricUnit.Milliseconds),
            QueueTime = Exact(999, MetricUnit.Milliseconds),
            ModelLoadDisposition = ModelLoadDisposition.Warm,
        };
        TechnicalResourceSampleRecord resource = Resource(requestId) with
        {
            ProcessCpuPercent = Exact(59.99m, MetricUnit.Percent),
            GpuUtilizationPercent = Exact(20.01m, MetricUnit.Percent),
            GpuVramUsedBytes = Exact(89, MetricUnit.Bytes),
            GpuVramTotalBytes = Exact(100, MetricUnit.Bytes),
        };

        IReadOnlyList<DiagnosticConclusion> conclusions = DiagnosticRuleset.Default.Evaluate(
            new DiagnosticInput(Observation(requestId, telemetry), resource, null));

        DiagnosticRuleId[] positiveRules = conclusions
            .Where(item => item.Kind is DiagnosticConclusionKind.Fact or DiagnosticConclusionKind.Hypothesis)
            .Select(item => item.Rule)
            .ToArray();
        Assert.IsFalse(positiveRules.Contains(DiagnosticRuleId.LargePrompt));
        Assert.IsFalse(positiveRules.Contains(DiagnosticRuleId.SlowGeneration));
        Assert.IsFalse(positiveRules.Contains(DiagnosticRuleId.CpuOffload));
        Assert.IsFalse(positiveRules.Contains(DiagnosticRuleId.VramPressure));
        Assert.IsFalse(positiveRules.Contains(DiagnosticRuleId.ModelLoadingLatency));
        Assert.IsFalse(positiveRules.Contains(DiagnosticRuleId.QueueWaitingLatency));
        Assert.IsFalse(positiveRules.Contains(DiagnosticRuleId.HighContextUsage));
    }

    [TestMethod]
    public void MissingOrMismatchedEvidenceIsInsufficientAndNeverBecomesFact()
    {
        ProxyObservation observation = Observation(
            Guid.NewGuid(),
            BackendResponseTelemetry.Unavailable(BackendKind.Ollama, SourceVersion));
        TechnicalResourceSampleRecord unrelated = Resource(Guid.NewGuid()) with
        {
            ProcessCpuPercent = Exact(100, MetricUnit.Percent),
            GpuUtilizationPercent = Exact(0, MetricUnit.Percent),
        };

        IReadOnlyList<DiagnosticConclusion> conclusions = DiagnosticRuleset.Default.Evaluate(
            new DiagnosticInput(observation, unrelated, null));

        DiagnosticRuleId[] insufficientRules = conclusions
            .Where(item => item.Kind == DiagnosticConclusionKind.InsufficientData)
            .Select(item => item.Rule)
            .ToArray();
        CollectionAssert.IsSubsetOf(
            new[]
            {
                DiagnosticRuleId.LargePrompt,
                DiagnosticRuleId.SlowGeneration,
                DiagnosticRuleId.CpuOffload,
                DiagnosticRuleId.VramPressure,
                DiagnosticRuleId.ModelLoadingLatency,
                DiagnosticRuleId.QueueWaitingLatency,
                DiagnosticRuleId.HighContextUsage,
            },
            insufficientRules);
        Assert.IsFalse(conclusions.Any(item => item.Kind == DiagnosticConclusionKind.Fact));
    }

    [TestMethod]
    public void EstimatedThresholdEvidenceProducesHypothesisInsteadOfFact()
    {
        BackendResponseTelemetry telemetry = BackendResponseTelemetry.Unavailable(
            BackendKind.Ollama,
            SourceVersion) with
        {
            PromptTokens = MetricValue.Estimated(
                9_000,
                MetricUnit.TokenCount,
                MetricSource.Inspector,
                SourceVersion,
                "estimated-token-count-v1"),
        };

        IReadOnlyList<DiagnosticConclusion> conclusions = DiagnosticRuleset.Default.Evaluate(
            new DiagnosticInput(Observation(Guid.NewGuid(), telemetry), null, null));

        AssertConclusion(conclusions, DiagnosticRuleId.LargePrompt, DiagnosticConclusionKind.Hypothesis);
        Assert.IsFalse(conclusions.Any(item =>
            item.Rule == DiagnosticRuleId.LargePrompt && item.Kind == DiagnosticConclusionKind.Fact));
    }

    [TestMethod]
    public void ExplicitBackendActivityDistinguishesConfirmedStallFromActiveLifecycle()
    {
        Guid requestId = Guid.NewGuid();
        LiveRequestSnapshot request = new(
            requestId,
            ClientKind.Cline,
            RequestStageValue.BackendReported(RequestStage.PromptProcessing, SourceVersion),
            DateTimeOffset.UnixEpoch,
            Calculated(30_000, MetricUnit.Milliseconds),
            Exact(50, MetricUnit.Percent),
            Unavailable(MetricUnit.Milliseconds));
        LiveRequestCollectionSnapshot live = new([request], null);

        IReadOnlyList<DiagnosticConclusion> withoutActivity = DiagnosticRuleset.Default.Evaluate(
            new DiagnosticInput(null, null, live));
        AssertConclusion(withoutActivity, DiagnosticRuleId.ActiveWork, DiagnosticConclusionKind.Fact);
        AssertConclusion(withoutActivity, DiagnosticRuleId.ConfirmedStall, DiagnosticConclusionKind.InsufficientData);
        Assert.IsFalse(withoutActivity.Any(item =>
            item.Rule == DiagnosticRuleId.ConfirmedStall && item.Kind == DiagnosticConclusionKind.Fact));

        BackendActivitySignal signal = new(
            requestId,
            BackendActivityState.Stalled,
            DateTimeOffset.UnixEpoch.AddSeconds(30),
            SourceVersion);
        IReadOnlyList<DiagnosticConclusion> confirmed = DiagnosticRuleset.Default.Evaluate(
            new DiagnosticInput(null, null, live, [signal]));
        AssertConclusion(confirmed, DiagnosticRuleId.ConfirmedStall, DiagnosticConclusionKind.Fact);
        Assert.IsFalse(confirmed.Any(item => item.Rule == DiagnosticRuleId.ActiveWork));
    }

    [TestMethod]
    [DataRow(ProxyErrorType.ConnectionRefused, DiagnosticRuleId.BackendUnavailable)]
    [DataRow(ProxyErrorType.ModelLoading, DiagnosticRuleId.RequestError)]
    [DataRow(ProxyErrorType.HttpApiError, DiagnosticRuleId.RequestError)]
    [DataRow(ProxyErrorType.Timeout, DiagnosticRuleId.BackendUnavailable)]
    [DataRow(ProxyErrorType.ContextOverflow, DiagnosticRuleId.RequestError)]
    [DataRow(ProxyErrorType.ClientCancellation, DiagnosticRuleId.RequestError)]
    [DataRow(ProxyErrorType.BackendCrash, DiagnosticRuleId.BackendUnavailable)]
    public void TypedErrorsHaveHumanReadableEvidenceBackedConclusions(
        ProxyErrorType error,
        DiagnosticRuleId expectedRule)
    {
        ProxyObservation observation = Observation(
            Guid.NewGuid(),
            BackendResponseTelemetry.Unavailable(BackendKind.Ollama, SourceVersion)) with
        {
            ErrorType = error,
        };

        DiagnosticConclusion conclusion = AssertConclusion(
            DiagnosticRuleset.Default.Evaluate(new DiagnosticInput(observation, null, null)),
            expectedRule,
            DiagnosticConclusionKind.Fact);

        Assert.IsFalse(string.IsNullOrWhiteSpace(conclusion.Explanation));
        Assert.IsTrue(conclusion.Evidence.Any(item => item.Error == error));
    }

    private static DiagnosticConclusion AssertConclusion(
        IReadOnlyList<DiagnosticConclusion> conclusions,
        DiagnosticRuleId rule,
        DiagnosticConclusionKind kind) =>
        conclusions.Single(item => item.Rule == rule && item.Kind == kind);

    private static ProxyObservation Observation(Guid requestId, BackendResponseTelemetry telemetry) => new(
        requestId,
        DateTimeOffset.UnixEpoch,
        TimeSpan.FromSeconds(1),
        200,
        ProxyOutcome.Completed,
        ClientKind.Cline,
        telemetry);

    private static TechnicalResourceSampleRecord Resource(Guid requestId) => new(
        Guid.NewGuid(),
        null,
        DateTimeOffset.UnixEpoch,
        Unavailable(MetricUnit.Percent),
        Unavailable(MetricUnit.Percent))
    {
        RequestId = requestId,
    };

    private static MetricValue Exact(decimal value, MetricUnit unit) =>
        MetricValue.Exact(value, unit, MetricSource.BackendExtension, SourceVersion);

    private static MetricValue Calculated(decimal value, MetricUnit unit) =>
        MetricValue.Calculated(value, unit, MetricSource.Inspector, SourceVersion, "diagnostic-test-calculation-v1");

    private static MetricValue Unavailable(MetricUnit unit) =>
        MetricValue.Unavailable(unit, MetricSource.Inspector, SourceVersion);
}
