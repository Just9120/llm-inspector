using LlmInspector.Domain;

namespace LlmInspector.Application;

public sealed class CompositeProxyObservationSink : IProxyObservationSink
{
    private readonly IProxyObservationSink[] _sinks;

    public CompositeProxyObservationSink(params IProxyObservationSink[] sinks)
    {
        ArgumentNullException.ThrowIfNull(sinks);
        if (sinks.Length == 0 || sinks.Any(sink => sink is null))
        {
            throw new ArgumentException("At least one non-null observation sink is required.", nameof(sinks));
        }

        _sinks = [.. sinks];
    }

    public async ValueTask RecordAsync(ProxyObservation observation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(observation);
        Exception? firstFailure = null;
        foreach (IProxyObservationSink sink in _sinks)
        {
            try
            {
                await sink.RecordAsync(observation, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                firstFailure ??= exception;
            }
        }

        if (firstFailure is not null)
        {
            throw new InvalidOperationException("One or more observation sinks rejected the record.", firstFailure);
        }
    }
}
