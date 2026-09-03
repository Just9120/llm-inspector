using System.Threading.Channels;
using LlmInspector.Application;
using LlmInspector.Domain;

namespace LlmInspector.Storage.Sqlite;

public sealed class BufferedTechnicalHistorySink : IProxyObservationSink, ITechnicalOperationSink, IAsyncDisposable
{
    public const int DefaultCapacity = 256;

    private readonly ITechnicalHistoryStore _store;
    private readonly Channel<HistoryWrite> _channel;
    private readonly Task _worker;
    private long _droppedCount;
    private long _failedCount;
    private int _disposed;
    private string? _lastFailureType;

    public BufferedTechnicalHistorySink(
        ITechnicalHistoryStore store,
        int capacity = DefaultCapacity)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        _store = store;
        _channel = Channel.CreateBounded<HistoryWrite>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
        _worker = Task.Run(ProcessAsync);
    }

    public long DroppedCount => Interlocked.Read(ref _droppedCount);

    public long FailedCount => Interlocked.Read(ref _failedCount);

    public string? LastFailureType => Volatile.Read(ref _lastFailureType);

    public ValueTask RecordAsync(ProxyObservation observation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (Volatile.Read(ref _disposed) != 0 || !_channel.Writer.TryWrite(new HistoryWrite(observation, null)))
        {
            Interlocked.Increment(ref _droppedCount);
        }

        return ValueTask.CompletedTask;
    }

    public Task RecordOperationGraphAsync(
        TechnicalOperationGraph graph,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (Volatile.Read(ref _disposed) != 0 || !_channel.Writer.TryWrite(new HistoryWrite(null, graph)))
        {
            Interlocked.Increment(ref _droppedCount);
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _channel.Writer.TryComplete();
        await _worker.ConfigureAwait(false);
    }

    private async Task ProcessAsync()
    {
        await foreach (HistoryWrite write in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                if (write.Observation is not null)
                {
                    await _store.RecordAsync(write.Observation, CancellationToken.None).ConfigureAwait(false);
                }
                else if (write.Operation is not null)
                {
                    await _store.RecordOperationGraphAsync(write.Operation, CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            {
                Interlocked.Increment(ref _failedCount);
                Volatile.Write(ref _lastFailureType, exception.GetType().Name);
            }
        }
    }

    private sealed record HistoryWrite(
        ProxyObservation? Observation,
        TechnicalOperationGraph? Operation);
}
