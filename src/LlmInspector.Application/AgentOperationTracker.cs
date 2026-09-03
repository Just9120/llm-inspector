using LlmInspector.Domain;

namespace LlmInspector.Application;

public sealed class AgentOperationTracker
{
    private const int MaximumTrackedOperations = 1_024;
    private const string SourceVersion = "agent-operation-tracker-v1";

    private readonly object _gate = new();
    private readonly Dictionary<Guid, OperationState> _operations = [];
    private long _accessSequence;

    public TechnicalOperationGraph? Observe(ProxyObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        RequestCorrelation? correlation = observation.Correlation;
        if (correlation?.OperationId is not Guid operationId)
        {
            return null;
        }

        lock (_gate)
        {
            long access = ++_accessSequence;
            if (!_operations.TryGetValue(operationId, out OperationState? state))
            {
                if (correlation.TurnSequence != 1 || observation.AgentTurn.ToolResultCount is > 0)
                {
                    return null;
                }

                EvictIfNeeded();
                state = new OperationState(
                    operationId,
                    correlation.SessionId,
                    observation.Client,
                    observation.BackendTelemetry.Backend,
                    observation.StartedAt,
                    access);
                _operations.Add(operationId, state);
            }
            else if (state.IsTerminal ||
                     state.SessionId != correlation.SessionId ||
                     state.Client != observation.Client ||
                     state.Backend != observation.BackendTelemetry.Backend ||
                     correlation.TurnSequence != state.LastTurnSequence + 1 ||
                     state.TurnIds.Contains(correlation.TurnId))
            {
                state.LastAccess = access;
                return null;
            }

            int resultCount = observation.AgentTurn.ToolResultCount ?? -1;
            if (state.PendingToolIndexes.Count != 0)
            {
                if (resultCount != state.PendingToolIndexes.Count)
                {
                    state.LastAccess = access;
                    return null;
                }

                CompletePendingTools(state, observation.StartedAt);
            }
            else if (resultCount > 0)
            {
                state.LastAccess = access;
                return null;
            }

            HistoryErrorType error = MapErrorType(observation.Outcome);
            state.Turns.Add(new TechnicalTurnRecord(
                correlation.TurnId,
                operationId,
                correlation.TurnSequence,
                observation.RequestId,
                observation.StartedAt,
                observation.Duration,
                observation.Outcome,
                error)
            {
                AvailableToolCount = observation.AgentTurn.AvailableToolCount,
                InvokedToolCount = observation.AgentTurn.InvokedToolCount,
            });
            state.TurnIds.Add(correlation.TurnId);
            state.LastTurnSequence = correlation.TurnSequence;
            state.LastAccess = access;
            state.Model = observation.BackendTelemetry.Model ?? state.Model;

            DateTimeOffset responseCompletedAt = observation.StartedAt + observation.Duration;
            if (observation.AgentTurn.ToolDetailsComplete)
            {
                foreach (AgentToolCall tool in observation.AgentTurn.ToolCalls.OrderBy(item => item.Sequence))
                {
                    TechnicalToolEventRecord record = new(
                        CreateToolEventId(operationId, correlation.TurnId, tool.Sequence),
                        operationId,
                        correlation.TurnSequence,
                        tool.Sequence,
                        tool.ToolName,
                        responseCompletedAt,
                        TimeSpan.Zero,
                        TechnicalToolStatus.Started,
                        HistoryErrorType.None);
                    state.PendingToolIndexes.Add(state.Tools.Count);
                    state.Tools.Add(record);
                }
            }

            ApplyTerminalState(state, observation, responseCompletedAt);
            return state.Snapshot();
        }
    }

    private static void CompletePendingTools(OperationState state, DateTimeOffset nextTurnStartedAt)
    {
        foreach (int index in state.PendingToolIndexes)
        {
            TechnicalToolEventRecord pending = state.Tools[index];
            TimeSpan duration = nextTurnStartedAt > pending.StartedAt
                ? nextTurnStartedAt - pending.StartedAt
                : TimeSpan.Zero;
            state.Tools[index] = pending with
            {
                Duration = duration,
                Status = TechnicalToolStatus.Completed,
                ErrorType = HistoryErrorType.None,
                DurationMetric = MetricValue.Calculated(
                    (decimal)duration.TotalMilliseconds,
                    MetricUnit.Milliseconds,
                    MetricSource.Inspector,
                    SourceVersion,
                    "tool-call-to-result-turn-wall-duration-v1"),
            };
        }

        state.PendingToolIndexes.Clear();
    }

    private static void ApplyTerminalState(
        OperationState state,
        ProxyObservation observation,
        DateTimeOffset completedAt)
    {
        switch (observation.Outcome)
        {
            case ProxyOutcome.ClientCancelled:
                state.Status = TechnicalOperationStatus.Cancelled;
                state.ErrorType = HistoryErrorType.ClientCancelled;
                state.EndedAt = completedAt;
                state.IsTerminal = true;
                FailPendingTools(state, HistoryErrorType.ClientCancelled, completedAt);
                break;
            case ProxyOutcome.BackendUnavailable:
            case ProxyOutcome.RelayFailed:
                state.Status = TechnicalOperationStatus.Error;
                state.ErrorType = MapErrorType(observation.Outcome);
                state.EndedAt = completedAt;
                state.IsTerminal = true;
                FailPendingTools(state, state.ErrorType, completedAt);
                break;
            case ProxyOutcome.Completed
                when observation.AgentTurn.Completion == AgentCompletionDisposition.Final:
                state.Status = TechnicalOperationStatus.Completed;
                state.ErrorType = HistoryErrorType.None;
                state.EndedAt = completedAt;
                state.IsTerminal = true;
                break;
            case ProxyOutcome.Completed:
                state.Status = TechnicalOperationStatus.Running;
                state.ErrorType = HistoryErrorType.None;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(observation));
        }
    }

    private static void FailPendingTools(
        OperationState state,
        HistoryErrorType error,
        DateTimeOffset completedAt)
    {
        foreach (int index in state.PendingToolIndexes)
        {
            TechnicalToolEventRecord pending = state.Tools[index];
            TimeSpan duration = completedAt > pending.StartedAt
                ? completedAt - pending.StartedAt
                : TimeSpan.Zero;
            state.Tools[index] = pending with
            {
                Duration = duration,
                Status = TechnicalToolStatus.Error,
                ErrorType = error,
                DurationMetric = MetricValue.Calculated(
                    (decimal)duration.TotalMilliseconds,
                    MetricUnit.Milliseconds,
                    MetricSource.Inspector,
                    SourceVersion,
                    "tool-call-failure-wall-duration-v1"),
            };
        }

        state.PendingToolIndexes.Clear();
    }

    private void EvictIfNeeded()
    {
        if (_operations.Count < MaximumTrackedOperations)
        {
            return;
        }

        Guid oldest = _operations.MinBy(item => item.Value.LastAccess).Key;
        _operations.Remove(oldest);
    }

    private static Guid CreateToolEventId(Guid operationId, Guid turnId, int sequence)
    {
        Span<byte> bytes = stackalloc byte[16];
        operationId.TryWriteBytes(bytes);
        Span<byte> turnBytes = stackalloc byte[16];
        turnId.TryWriteBytes(turnBytes);
        for (int index = 0; index < bytes.Length; index++)
        {
            bytes[index] ^= turnBytes[index];
        }

        BitConverter.TryWriteBytes(bytes[12..], sequence);
        return new Guid(bytes);
    }

    private static HistoryErrorType MapErrorType(ProxyOutcome outcome) => outcome switch
    {
        ProxyOutcome.Completed => HistoryErrorType.None,
        ProxyOutcome.BackendUnavailable => HistoryErrorType.BackendUnavailable,
        ProxyOutcome.ClientCancelled => HistoryErrorType.ClientCancelled,
        ProxyOutcome.RelayFailed => HistoryErrorType.RelayFailed,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
    };

    private sealed class OperationState(
        Guid operationId,
        Guid sessionId,
        ClientKind client,
        BackendKind backend,
        DateTimeOffset startedAt,
        long lastAccess)
    {
        public Guid OperationId { get; } = operationId;

        public Guid SessionId { get; } = sessionId;

        public ClientKind Client { get; } = client;

        public BackendKind Backend { get; } = backend;

        public DateTimeOffset StartedAt { get; } = startedAt;

        public DateTimeOffset? EndedAt { get; set; }

        public TechnicalIdentifier? Model { get; set; }

        public TechnicalOperationStatus Status { get; set; } = TechnicalOperationStatus.Running;

        public HistoryErrorType ErrorType { get; set; } = HistoryErrorType.None;

        public int LastTurnSequence { get; set; }

        public bool IsTerminal { get; set; }

        public long LastAccess { get; set; } = lastAccess;

        public HashSet<Guid> TurnIds { get; } = [];

        public List<TechnicalTurnRecord> Turns { get; } = [];

        public List<TechnicalToolEventRecord> Tools { get; } = [];

        public List<int> PendingToolIndexes { get; } = [];

        public TechnicalOperationGraph Snapshot() => new(
            new TechnicalSessionRecord(SessionId, StartedAt, EndedAt, Client, Backend, Model),
            new TechnicalOperationRecord(
                OperationId,
                SessionId,
                StartedAt,
                EndedAt,
                Client,
                Backend,
                Model,
                Status,
                ErrorType),
            Turns.ToArray(),
            Tools.ToArray(),
            []);
    }
}
