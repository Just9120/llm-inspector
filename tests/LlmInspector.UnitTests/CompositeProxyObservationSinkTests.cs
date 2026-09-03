using LlmInspector.Application;
using LlmInspector.Domain;

namespace LlmInspector.UnitTests;

[TestClass]
public sealed class CompositeProxyObservationSinkTests
{
    [TestMethod]
    public async Task LaterSinksStillReceiveObservationWhenEarlierSinkFails()
    {
        LatestProxyObservationStore latest = new();
        CompositeProxyObservationSink composite = new(new ThrowingSink(), latest);
        ProxyObservation observation = new(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            TimeSpan.FromMilliseconds(1),
            200,
            ProxyOutcome.Completed,
            ClientKind.Cline,
            BackendResponseTelemetry.Unavailable(BackendKind.Ollama, "composite-test-v1"));

        _ = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await composite.RecordAsync(observation, CancellationToken.None));

        Assert.AreSame(observation, latest.Latest);
    }

    private sealed class ThrowingSink : IProxyObservationSink
    {
        public ValueTask RecordAsync(ProxyObservation observation, CancellationToken cancellationToken) =>
            ValueTask.FromException(new IOException("Expected test failure."));
    }
}
