using LlmInspector.Application;
using LlmInspector.Domain;

namespace LlmInspector.UnitTests;

[TestClass]
public sealed class HistoryErrorClassifierTests
{
    [TestMethod]
    [DataRow(ProxyErrorType.InspectorFailure, HistoryErrorOrigin.Inspector)]
    [DataRow(ProxyErrorType.ClientCancellation, HistoryErrorOrigin.Client)]
    [DataRow(ProxyErrorType.ConnectionRefused, HistoryErrorOrigin.Backend)]
    [DataRow(ProxyErrorType.HttpApiError, HistoryErrorOrigin.Backend)]
    [DataRow(ProxyErrorType.ModelLoading, HistoryErrorOrigin.Model)]
    [DataRow(ProxyErrorType.ContextOverflow, HistoryErrorOrigin.Model)]
    [DataRow(ProxyErrorType.RelayFailure, HistoryErrorOrigin.Unknown)]
    public void TypedErrorEvidenceMapsToExplicitOrigin(
        ProxyErrorType errorType,
        HistoryErrorOrigin expected)
    {
        ProxyObservation observation = Observation(errorType, ProxyOutcome.RelayFailed);

        Assert.AreEqual(expected, HistoryErrorClassifier.OriginFrom(observation));
    }

    [TestMethod]
    public void SuccessfulRequestIsNotApplicableAndAmbiguousLegacyFailureStaysUnknown()
    {
        Assert.AreEqual(
            HistoryErrorOrigin.NotApplicable,
            HistoryErrorClassifier.OriginFrom(Observation(ProxyErrorType.None, ProxyOutcome.Completed)));
        Assert.AreEqual(
            HistoryErrorOrigin.Unknown,
            HistoryErrorClassifier.OriginFrom(Observation(ProxyErrorType.None, ProxyOutcome.RelayFailed)));
    }

    private static ProxyObservation Observation(ProxyErrorType errorType, ProxyOutcome outcome) => new(
        Guid.NewGuid(),
        DateTimeOffset.UtcNow,
        TimeSpan.FromMilliseconds(1),
        outcome == ProxyOutcome.Completed ? 200 : 502,
        outcome,
        ClientKind.GenericUnknown,
        BackendResponseTelemetry.Unavailable(BackendKind.Ollama, "error-origin-test-v1"))
    {
        ErrorType = errorType,
    };
}
