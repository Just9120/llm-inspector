using LlmInspector.Application;
using LlmInspector.Domain;

namespace LlmInspector.UnitTests;

[TestClass]
public sealed class RemoteBackendMonitorTests
{
    [TestMethod]
    public async Task AvailabilityAndConnectLatencyAreSeparateTypedState()
    {
        Uri destination = new("https://backend.example-tailnet.ts.net/");
        using RemoteBackendMonitor monitor = new(
            destination,
            new FixtureProbe(RemoteBackendProbeResult.Success(TimeSpan.FromMilliseconds(12.5))));

        RemoteBackendStatus result = await monitor.ProbeAsync();

        Assert.AreEqual(RemoteBackendAvailability.Available, result.Availability);
        Assert.AreEqual(destination, result.Destination);
        Assert.AreEqual(MetricQuality.Calculated, result.NetworkConnectLatency.Quality);
        Assert.AreEqual(MetricUnit.Milliseconds, result.NetworkConnectLatency.Unit);
        Assert.AreEqual(12.5m, result.NetworkConnectLatency.Value);
        Assert.IsTrue(result.Message.Contains("not inference latency", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task FailedProbeDoesNotFabricateLatency()
    {
        using RemoteBackendMonitor monitor = new(
            new Uri("https://backend.example-tailnet.ts.net/"),
            new FixtureProbe(RemoteBackendProbeResult.Failure("connect-failed")));

        RemoteBackendStatus result = await monitor.ProbeAsync();

        Assert.AreEqual(RemoteBackendAvailability.Unavailable, result.Availability);
        Assert.AreEqual(MetricQuality.Unavailable, result.NetworkConnectLatency.Quality);
        Assert.IsNull(result.NetworkConnectLatency.Value);
        Assert.IsTrue(result.Message.Contains("connect-failed", StringComparison.Ordinal));
    }

    private sealed class FixtureProbe(RemoteBackendProbeResult result) : IRemoteBackendProbe
    {
        public ValueTask<RemoteBackendProbeResult> ProbeAsync(
            Uri destination,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(result);
    }
}
