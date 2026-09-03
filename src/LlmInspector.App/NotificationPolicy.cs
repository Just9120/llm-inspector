using System.Globalization;
using LlmInspector.Application;
using LlmInspector.Domain;

namespace LlmInspector.App;

public enum NotificationEventType
{
    BackendUnavailable,
    LongOperationCompleted,
    RecurringError,
    HighContextUsage,
}

public sealed record NotificationCandidate(
    NotificationEventType EventType,
    string EventKey)
{
    public Guid? RequestId { get; init; }

    public HistoryErrorType ErrorType { get; init; }

    public int? Occurrences { get; init; }

    public decimal? DurationSeconds { get; init; }

    public decimal? ContextUsagePercent { get; init; }
}

public sealed record DesktopNotification(
    NotificationEventType EventType,
    string Title,
    string Body,
    bool Silent);

public interface IDesktopNotificationPublisher
{
    void Publish(DesktopNotification notification);
}

public static class NotificationTextPresenter
{
    public static DesktopNotification Format(NotificationCandidate candidate, bool silent)
    {
        ValidateCandidate(candidate);
        string request = candidate.RequestId?.ToString("N", CultureInfo.InvariantCulture)[..8] ?? "unknown";
        (string Title, string Body) text = candidate.EventType switch
        {
            NotificationEventType.BackendUnavailable => (
                "LLM backend unavailable",
                $"Request {request} ended with {candidate.ErrorType}."),
            NotificationEventType.LongOperationCompleted => (
                "Long LLM operation completed",
                $"Request {request} completed after {Format(candidate.DurationSeconds)} seconds."),
            NotificationEventType.RecurringError => (
                "Recurring LLM error",
                $"{candidate.ErrorType} occurred {candidate.Occurrences?.ToString(CultureInfo.InvariantCulture)} times in this application session."),
            NotificationEventType.HighContextUsage => (
                "High LLM context usage",
                $"Request {request} used {Format(candidate.ContextUsagePercent)}% of the reported context limit."),
            _ => throw new ArgumentOutOfRangeException(nameof(candidate)),
        };
        return new DesktopNotification(candidate.EventType, text.Title, text.Body, silent);
    }

    public static void ValidateCandidate(NotificationCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (string.IsNullOrWhiteSpace(candidate.EventKey) || candidate.EventKey.Length > 128 ||
            candidate.EventKey.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
        {
            throw new ArgumentException("A bounded technical notification key is required.", nameof(candidate));
        }

        bool valid = candidate.EventType switch
        {
            NotificationEventType.BackendUnavailable =>
                candidate.RequestId is not null && candidate.ErrorType != HistoryErrorType.None,
            NotificationEventType.LongOperationCompleted =>
                candidate.RequestId is not null && candidate.DurationSeconds is >= 0,
            NotificationEventType.RecurringError =>
                candidate.ErrorType != HistoryErrorType.None && candidate.Occurrences is >= 2,
            NotificationEventType.HighContextUsage =>
                candidate.RequestId is not null && candidate.ContextUsagePercent is >= 0 and <= 100,
            _ => false,
        };
        if (!valid)
        {
            throw new ArgumentException("Notification technical evidence does not match its event type.", nameof(candidate));
        }
    }

    private static string Format(decimal? value) =>
        value?.ToString("0.#", CultureInfo.InvariantCulture) ?? "unavailable";
}

public sealed record NotificationPolicyOptions
{
    public const string Version1 = "notification-policy-v1";

    public string Version { get; init; } = Version1;

    public TimeSpan DuplicateWindow { get; init; } = TimeSpan.FromMinutes(15);

    public TimeSpan GlobalRateWindow { get; init; } = TimeSpan.FromMinutes(10);

    public int GlobalRateLimit { get; init; } = 3;

    public TimeSpan LongOperationThreshold { get; init; } = TimeSpan.FromMinutes(1);

    public decimal HighContextUsagePercent { get; init; } = 90m;

    public int RecurringErrorMinimumOccurrences { get; init; } = HistoryPolicies.RecurringErrorMinimumOccurrences;

    public static void Validate(NotificationPolicyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.Version) || options.Version.Length > 128 ||
            options.DuplicateWindow <= TimeSpan.Zero ||
            options.GlobalRateWindow <= TimeSpan.Zero ||
            options.GlobalRateLimit < 1 ||
            options.LongOperationThreshold <= TimeSpan.Zero ||
            options.HighContextUsagePercent is <= 0 or > 100 ||
            options.RecurringErrorMinimumOccurrences < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Notification policy values are invalid.");
        }
    }
}

public enum NotificationDispatchResult
{
    Published,
    Disabled,
    Paused,
    DuplicateSuppressed,
    RateLimited,
}

public sealed record NotificationDispatchDecision(
    NotificationCandidate Candidate,
    NotificationDispatchResult Result,
    string PolicyVersion);

public sealed class NotificationDispatcher
{
    private readonly object _sync = new();
    private readonly IDesktopNotificationPublisher _publisher;
    private readonly NotificationPolicyOptions _policy;
    private readonly Dictionary<string, DateTimeOffset> _publishedByKey = new(StringComparer.Ordinal);
    private readonly Queue<DateTimeOffset> _published = new();
    private bool _paused;

    public NotificationDispatcher(
        IDesktopNotificationPublisher publisher,
        NotificationPolicyOptions? policy = null)
    {
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _policy = policy ?? new NotificationPolicyOptions();
        NotificationPolicyOptions.Validate(_policy);
    }

    public bool IsPaused
    {
        get
        {
            lock (_sync)
            {
                return _paused;
            }
        }
    }

    public bool TogglePaused()
    {
        lock (_sync)
        {
            _paused = !_paused;
            return _paused;
        }
    }

    public IReadOnlyList<NotificationDispatchDecision> Dispatch(
        IReadOnlyList<NotificationCandidate> candidates,
        NotificationSettings settings,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(settings);
        List<NotificationDispatchDecision> decisions = [];
        lock (_sync)
        {
            RemoveExpired(now);
            foreach (NotificationCandidate candidate in candidates)
            {
                NotificationTextPresenter.ValidateCandidate(candidate);
                NotificationDispatchResult result = Classify(candidate, settings, now);
                if (result == NotificationDispatchResult.Published)
                {
                    _publisher.Publish(NotificationTextPresenter.Format(candidate, settings.SilentMode));
                    _publishedByKey[DeduplicationKey(candidate)] = now;
                    _published.Enqueue(now);
                }

                decisions.Add(new NotificationDispatchDecision(candidate, result, _policy.Version));
            }
        }

        return decisions;
    }

    private NotificationDispatchResult Classify(
        NotificationCandidate candidate,
        NotificationSettings settings,
        DateTimeOffset now)
    {
        if (!settings.IsEnabled(candidate.EventType))
        {
            return NotificationDispatchResult.Disabled;
        }

        if (_paused)
        {
            return NotificationDispatchResult.Paused;
        }

        string key = DeduplicationKey(candidate);
        if (_publishedByKey.TryGetValue(key, out DateTimeOffset last) &&
            now - last < _policy.DuplicateWindow)
        {
            return NotificationDispatchResult.DuplicateSuppressed;
        }

        return _published.Count >= _policy.GlobalRateLimit
            ? NotificationDispatchResult.RateLimited
            : NotificationDispatchResult.Published;
    }

    private void RemoveExpired(DateTimeOffset now)
    {
        while (_published.TryPeek(out DateTimeOffset publishedAt) &&
               now - publishedAt >= _policy.GlobalRateWindow)
        {
            _ = _published.Dequeue();
        }

        foreach (string key in _publishedByKey
                     .Where(pair => now - pair.Value >= _policy.DuplicateWindow)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            _publishedByKey.Remove(key);
        }
    }

    private static string DeduplicationKey(NotificationCandidate candidate) =>
        $"{candidate.EventType}:{candidate.EventKey}";
}

public sealed class NotificationRuleEngine
{
    private readonly NotificationPolicyOptions _policy;

    public NotificationRuleEngine(NotificationPolicyOptions? policy = null)
    {
        _policy = policy ?? new NotificationPolicyOptions();
        NotificationPolicyOptions.Validate(_policy);
    }

    public IReadOnlyList<NotificationCandidate> Evaluate(
        ProxyObservation observation,
        int errorOccurrenceCount)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentOutOfRangeException.ThrowIfNegative(errorOccurrenceCount);
        List<NotificationCandidate> candidates = [];
        HistoryErrorType error = HistoryErrorClassifier.From(observation);
        string requestKey = observation.RequestId.ToString("N");
        if (IsBackendUnavailable(error))
        {
            candidates.Add(new NotificationCandidate(
                NotificationEventType.BackendUnavailable,
                requestKey)
            {
                RequestId = observation.RequestId,
                ErrorType = error,
            });
        }

        if (error == HistoryErrorType.None &&
            observation.Outcome == ProxyOutcome.Completed &&
            observation.Duration >= _policy.LongOperationThreshold)
        {
            candidates.Add(new NotificationCandidate(
                NotificationEventType.LongOperationCompleted,
                requestKey)
            {
                RequestId = observation.RequestId,
                DurationSeconds = (decimal)observation.Duration.TotalSeconds,
            });
        }

        if (error != HistoryErrorType.None &&
            errorOccurrenceCount >= _policy.RecurringErrorMinimumOccurrences)
        {
            candidates.Add(new NotificationCandidate(
                NotificationEventType.RecurringError,
                error.ToString())
            {
                ErrorType = error,
                Occurrences = errorOccurrenceCount,
            });
        }

        if (IsHighContextUsage(observation.BackendTelemetry))
        {
            decimal used = observation.BackendTelemetry.ContextUsageTokens.Value!.Value;
            decimal limit = observation.BackendTelemetry.ContextLimitTokens.Value!.Value;
            decimal percent = 100m * used / limit;
            candidates.Add(new NotificationCandidate(
                NotificationEventType.HighContextUsage,
                requestKey)
            {
                RequestId = observation.RequestId,
                ContextUsagePercent = percent,
            });
        }

        return candidates;
    }

    private bool IsHighContextUsage(BackendResponseTelemetry telemetry)
    {
        MetricValue used = telemetry.ContextUsageTokens;
        MetricValue limit = telemetry.ContextLimitTokens;
        return used.Quality is MetricQuality.Exact or MetricQuality.Calculated &&
               limit.Quality is MetricQuality.Exact or MetricQuality.Calculated &&
               used.Value is decimal usedValue &&
               limit.Value is decimal limitValue &&
               limitValue > 0 &&
               usedValue <= limitValue &&
               100m * usedValue / limitValue >= _policy.HighContextUsagePercent;
    }

    private static bool IsBackendUnavailable(HistoryErrorType error) => error is
        HistoryErrorType.BackendUnavailable or
        HistoryErrorType.ConnectionRefused or
        HistoryErrorType.ModelLoading or
        HistoryErrorType.Timeout or
        HistoryErrorType.BackendCrash or
        HistoryErrorType.RelayFailed;
}
