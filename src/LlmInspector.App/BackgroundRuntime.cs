using System.Threading.Channels;
using LlmInspector.Application;
using LlmInspector.Domain;

namespace LlmInspector.App;

public enum BackgroundCloseAction
{
    HideAndContinue,
    ExitProcess,
}

public sealed class BackgroundLifetimeController
{
    private readonly bool _backgroundAvailable;
    private int _exitRequested;

    public BackgroundLifetimeController(bool backgroundAvailable = true)
    {
        _backgroundAvailable = backgroundAvailable;
    }

    public bool IsExitRequested => Volatile.Read(ref _exitRequested) != 0;

    public BackgroundCloseAction OnWindowClosing() => IsExitRequested || !_backgroundAvailable
        ? BackgroundCloseAction.ExitProcess
        : BackgroundCloseAction.HideAndContinue;

    public void RequestExit() => Interlocked.Exchange(ref _exitRequested, 1);
}

public sealed class TrayCommandRouter
{
    private readonly Action<bool> _showApplication;
    private readonly Action _toggleNotifications;
    private readonly Action _exit;

    public TrayCommandRouter(
        Action<bool> showApplication,
        Action toggleNotifications,
        Action exit)
    {
        _showApplication = showApplication ?? throw new ArgumentNullException(nameof(showApplication));
        _toggleNotifications = toggleNotifications ?? throw new ArgumentNullException(nameof(toggleNotifications));
        _exit = exit ?? throw new ArgumentNullException(nameof(exit));
    }

    public void Execute(TrayCommand command)
    {
        switch (command)
        {
            case TrayCommand.OpenApplication:
                _showApplication(false);
                break;
            case TrayCommand.OpenNotificationSettings:
                _showApplication(true);
                break;
            case TrayCommand.ToggleNotifications:
                _toggleNotifications();
                break;
            case TrayCommand.Exit:
                _exit();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command));
        }
    }
}

public sealed class NotificationObservationBuffer : IProxyObservationSink
{
    public const int DefaultCapacity = 256;
    private readonly Channel<ProxyObservation> _channel;
    private long _droppedCount;

    public NotificationObservationBuffer(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _channel = Channel.CreateBounded<ProxyObservation>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false,
        });
    }

    public long DroppedCount => Interlocked.Read(ref _droppedCount);

    public ValueTask RecordAsync(ProxyObservation observation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(observation);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_channel.Writer.TryWrite(observation))
        {
            Interlocked.Increment(ref _droppedCount);
        }

        return ValueTask.CompletedTask;
    }

    public IAsyncEnumerable<ProxyObservation> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    public void Complete() => _channel.Writer.TryComplete();
}

public sealed class BackgroundNotificationMonitor : IAsyncDisposable
{
    private readonly NotificationObservationBuffer _observations;
    private readonly BackgroundSettingsService _settings;
    private readonly NotificationRuleEngine _rules;
    private readonly NotificationDispatcher _dispatcher;
    private readonly CancellationTokenSource _stop = new();
    private readonly Dictionary<HistoryErrorType, int> _errorCounts = [];
    private Task? _worker;

    public BackgroundNotificationMonitor(
        NotificationObservationBuffer observations,
        BackgroundSettingsService settings,
        NotificationRuleEngine rules,
        NotificationDispatcher dispatcher)
    {
        _observations = observations ?? throw new ArgumentNullException(nameof(observations));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public void Start()
    {
        if (Volatile.Read(ref _worker) is not null)
        {
            throw new InvalidOperationException("The background notification monitor is already running.");
        }

        Volatile.Write(ref _worker, Task.Run(ProcessAsync));
    }

    public async ValueTask DisposeAsync()
    {
        _observations.Complete();
        await _stop.CancelAsync().ConfigureAwait(false);
        Task? worker = Volatile.Read(ref _worker);
        if (worker is not null)
        {
            try
            {
                await worker.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
            }
        }

        _stop.Dispose();
    }

    private async Task ProcessAsync()
    {
        await foreach (ProxyObservation observation in _observations.ReadAllAsync(_stop.Token).ConfigureAwait(false))
        {
            try
            {
                HistoryErrorType error = HistoryErrorClassifier.From(observation);
                int occurrenceCount = error == HistoryErrorType.None
                    ? 0
                    : IncrementErrorCount(error);
                IReadOnlyList<NotificationCandidate> candidates = _rules.Evaluate(observation, occurrenceCount);
                _ = _dispatcher.Dispatch(candidates, _settings.Current.Notifications, DateTimeOffset.UtcNow);
            }
            catch (Exception exception) when (exception is
                ArgumentException or
                InvalidOperationException or
                IOException or
                UnauthorizedAccessException)
            {
                // Notification policy and native delivery are isolated from request forwarding.
            }
        }
    }

    private int IncrementErrorCount(HistoryErrorType error)
    {
        int count = _errorCounts.GetValueOrDefault(error) + 1;
        _errorCounts[error] = count;
        return count;
    }
}
