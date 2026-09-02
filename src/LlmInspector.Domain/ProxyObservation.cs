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
    BackendResponseTelemetry BackendTelemetry,
    MetricValue TimeToFirstToken)
{
    public ProxyObservation(
        Guid requestId,
        DateTimeOffset startedAt,
        TimeSpan duration,
        int? httpStatusCode,
        ProxyOutcome outcome,
        ClientKind client,
        BackendResponseTelemetry backendTelemetry)
        : this(
            requestId,
            startedAt,
            duration,
            httpStatusCode,
            outcome,
            client,
            backendTelemetry,
            MetricValue.Unavailable(
                MetricUnit.Milliseconds,
                MetricSource.Inspector,
                "gateway-streaming-ttft-v1"))
    {
    }
}
