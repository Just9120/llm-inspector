namespace LlmInspector.Domain;

public enum ProxyOutcome
{
    Completed,
    BackendUnavailable,
    ClientCancelled,
    RelayFailed,
}

public enum ProxyErrorType
{
    None,
    ConnectionRefused,
    ModelLoading,
    HttpApiError,
    Timeout,
    ContextOverflow,
    ClientCancellation,
    BackendCrash,
    BackendUnavailable,
    RelayFailure,
}

public sealed record RequestCorrelation
{
    public RequestCorrelation(Guid sessionId, Guid turnId, int turnSequence)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("Session identifier cannot be empty.", nameof(sessionId));
        }

        if (turnId == Guid.Empty)
        {
            throw new ArgumentException("Turn identifier cannot be empty.", nameof(turnId));
        }

        if (turnSequence < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(turnSequence), "Turn sequence must be positive.");
        }

        SessionId = sessionId;
        TurnId = turnId;
        TurnSequence = turnSequence;
    }

    public Guid SessionId { get; }

    public Guid TurnId { get; }

    public int TurnSequence { get; }

    public Guid? OperationId { get; init; }
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
    private const string CorrelationSourceVersion = "inspector-correlation-headers-v1";

    public RequestCorrelation? Correlation { get; init; }

    public MetricValue ContextChangeTokens { get; init; } = MetricValue.Unavailable(
        MetricUnit.TokenDelta,
        MetricSource.Inspector,
        CorrelationSourceVersion);

    public AgentTurnTelemetry AgentTurn { get; init; } = AgentTurnTelemetry.Unavailable;

    public ProxyErrorType ErrorType { get; init; }

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
