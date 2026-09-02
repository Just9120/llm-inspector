namespace LlmInspector.Domain;

public enum ProxyOutcome
{
    Completed,
    BackendUnavailable,
    ClientCancelled,
    RelayFailed,
}

public sealed record ProxyObservation(
    Guid RequestId,
    DateTimeOffset StartedAt,
    TimeSpan Duration,
    int? HttpStatusCode,
    ProxyOutcome Outcome,
    ClientKind Client,
    BackendResponseTelemetry BackendTelemetry);
