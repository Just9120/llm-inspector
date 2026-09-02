using LlmInspector.Domain;

namespace LlmInspector.Application;

public interface IProxyObservationSink
{
    ValueTask RecordAsync(ProxyObservation observation, CancellationToken cancellationToken);
}

public sealed class NullProxyObservationSink : IProxyObservationSink
{
    public static NullProxyObservationSink Instance { get; } = new();

    private NullProxyObservationSink()
    {
    }

    public ValueTask RecordAsync(ProxyObservation observation, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}
