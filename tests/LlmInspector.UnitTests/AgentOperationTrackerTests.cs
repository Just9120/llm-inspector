using LlmInspector.Application;
using LlmInspector.Domain;

namespace LlmInspector.UnitTests;

[TestClass]
public sealed class AgentOperationTrackerTests
{
    [TestMethod]
    public void AdjacentTurnsBecomeOneOrderedOperationWithToolLifecycle()
    {
        AgentOperationTracker tracker = new();
        Guid operationId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        DateTimeOffset startedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        TechnicalOperationGraph? first = tracker.Observe(CreateObservation(
            operationId,
            sessionId,
            Guid.NewGuid(),
            1,
            startedAt,
            TimeSpan.FromMilliseconds(100),
            availableTools: 3,
            toolResults: 0,
            toolNames: ["read_file", "list_files"],
            AgentCompletionDisposition.ToolCalls));
        TechnicalOperationGraph? second = tracker.Observe(CreateObservation(
            operationId,
            sessionId,
            Guid.NewGuid(),
            2,
            startedAt.AddSeconds(2),
            TimeSpan.FromMilliseconds(200),
            availableTools: 2,
            toolResults: 2,
            toolNames: [],
            AgentCompletionDisposition.Final));

        Assert.IsNotNull(first);
        Assert.AreEqual(TechnicalOperationStatus.Running, first.Operation.Status);
        Assert.IsNotNull(second);
        Assert.AreEqual(TechnicalOperationStatus.Completed, second.Operation.Status);
        Assert.HasCount(2, second.Turns);
        Assert.AreEqual(1, second.Turns[0].Sequence);
        Assert.AreEqual(2, second.Turns[1].Sequence);
        Assert.AreEqual(3m, second.Turns[0].AvailableToolCount.Value);
        Assert.AreEqual(2m, second.Turns[0].InvokedToolCount.Value);
        Assert.AreEqual(0m, second.Turns[1].InvokedToolCount.Value);
        Assert.HasCount(2, second.ToolEvents);
        Assert.IsTrue(second.ToolEvents.All(tool => tool.Status == TechnicalToolStatus.Completed));
        Assert.IsTrue(second.ToolEvents.All(tool => tool.ErrorType == HistoryErrorType.None));
        Assert.IsTrue(second.ToolEvents.All(tool => tool.Duration == TimeSpan.FromMilliseconds(1_900)));
        Assert.IsTrue(second.ToolEvents.All(tool => tool.DurationMetric.Quality == MetricQuality.Calculated));
    }

    [TestMethod]
    public void SeparateOperationsNeverMixConcurrentClientsOrSessions()
    {
        AgentOperationTracker tracker = new();
        Guid firstOperation = Guid.NewGuid();
        Guid secondOperation = Guid.NewGuid();
        Guid firstSession = Guid.NewGuid();
        Guid secondSession = Guid.NewGuid();

        TechnicalOperationGraph? first = tracker.Observe(CreateObservation(
            firstOperation, firstSession, Guid.NewGuid(), 1, DateTimeOffset.UnixEpoch,
            TimeSpan.FromMilliseconds(20), 1, 0, [], AgentCompletionDisposition.Final,
            ClientKind.Cline));
        TechnicalOperationGraph? second = tracker.Observe(CreateObservation(
            secondOperation, secondSession, Guid.NewGuid(), 1, DateTimeOffset.UnixEpoch,
            TimeSpan.FromMilliseconds(10), 4, 0, [], AgentCompletionDisposition.Final,
            ClientKind.OpenWebUi));

        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        Assert.AreEqual(firstOperation, first.Operation.OperationId);
        Assert.AreEqual(firstSession, first.Operation.SessionId);
        Assert.AreEqual(ClientKind.Cline, first.Operation.Client);
        Assert.AreEqual(secondOperation, second.Operation.OperationId);
        Assert.AreEqual(secondSession, second.Operation.SessionId);
        Assert.AreEqual(ClientKind.OpenWebUi, second.Operation.Client);
    }

    [TestMethod]
    public void AmbiguousCorrelationIsRejectedInsteadOfJoiningAnOperation()
    {
        AgentOperationTracker tracker = new();
        Guid operationId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        _ = tracker.Observe(CreateObservation(
            operationId, sessionId, Guid.NewGuid(), 1, DateTimeOffset.UnixEpoch,
            TimeSpan.FromMilliseconds(10), 1, 0, ["tool_a"], AgentCompletionDisposition.ToolCalls));

        Assert.IsNull(tracker.Observe(CreateObservation(
            operationId, sessionId, Guid.NewGuid(), 3, DateTimeOffset.UnixEpoch.AddSeconds(1),
            TimeSpan.FromMilliseconds(10), 1, 1, [], AgentCompletionDisposition.Final)));
        Assert.IsNull(tracker.Observe(CreateObservation(
            operationId, Guid.NewGuid(), Guid.NewGuid(), 2, DateTimeOffset.UnixEpoch.AddSeconds(1),
            TimeSpan.FromMilliseconds(10), 1, 1, [], AgentCompletionDisposition.Final)));
        Assert.IsNull(tracker.Observe(CreateObservation(
            operationId, sessionId, Guid.NewGuid(), 2, DateTimeOffset.UnixEpoch.AddSeconds(1),
            TimeSpan.FromMilliseconds(10), 1, 0, [], AgentCompletionDisposition.Final)));
    }

    [TestMethod]
    public void MissingOperationIdRemainsUnavailable()
    {
        AgentOperationTracker tracker = new();
        ProxyObservation observation = CreateObservation(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, DateTimeOffset.UnixEpoch,
            TimeSpan.FromMilliseconds(10), 0, 0, [], AgentCompletionDisposition.Final);
        observation = observation with
        {
            Correlation = new RequestCorrelation(
                observation.Correlation!.SessionId,
                observation.Correlation.TurnId,
                observation.Correlation.TurnSequence),
        };

        Assert.IsNull(tracker.Observe(observation));
    }

    private static ProxyObservation CreateObservation(
        Guid operationId,
        Guid sessionId,
        Guid turnId,
        int turnSequence,
        DateTimeOffset startedAt,
        TimeSpan duration,
        int availableTools,
        int toolResults,
        IReadOnlyList<string> toolNames,
        AgentCompletionDisposition completion,
        ClientKind client = ClientKind.Cline)
    {
        AgentToolCall[] tools = toolNames
            .Select((name, index) => new AgentToolCall(index, Id(name)))
            .ToArray();
        return new ProxyObservation(
            Guid.NewGuid(),
            startedAt,
            duration,
            200,
            ProxyOutcome.Completed,
            client,
            BackendResponseTelemetry.Unavailable(BackendKind.Ollama, "agent-operation-test-v1"))
        {
            Correlation = new RequestCorrelation(sessionId, turnId, turnSequence)
            {
                OperationId = operationId,
            },
            AgentTurn = new AgentTurnTelemetry(
                Count(availableTools),
                Count(tools.Length),
                toolResults,
                tools,
                true,
                completion),
        };
    }

    private static MetricValue Count(int value) =>
        MetricValue.Exact(value, MetricUnit.Count, MetricSource.Inspector, "openai-agent-metadata-v1");

    private static TechnicalIdentifier Id(string value) =>
        TechnicalIdentifier.FromBackend(value) ?? throw new InvalidOperationException("Invalid test identifier.");
}
