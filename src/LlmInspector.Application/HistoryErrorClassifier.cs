using LlmInspector.Domain;

namespace LlmInspector.Application;

public static class HistoryErrorClassifier
{
    public static HistoryErrorType From(ProxyObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        return observation.ErrorType switch
        {
            ProxyErrorType.ConnectionRefused => HistoryErrorType.ConnectionRefused,
            ProxyErrorType.ModelLoading => HistoryErrorType.ModelLoading,
            ProxyErrorType.HttpApiError => HistoryErrorType.HttpApiError,
            ProxyErrorType.Timeout => HistoryErrorType.Timeout,
            ProxyErrorType.ContextOverflow => HistoryErrorType.ContextOverflow,
            ProxyErrorType.ClientCancellation => HistoryErrorType.ClientCancelled,
            ProxyErrorType.BackendCrash => HistoryErrorType.BackendCrash,
            ProxyErrorType.BackendUnavailable => HistoryErrorType.BackendUnavailable,
            ProxyErrorType.RelayFailure => HistoryErrorType.RelayFailed,
            ProxyErrorType.InspectorFailure => HistoryErrorType.RelayFailed,
            ProxyErrorType.None => FromLegacyOutcome(observation.Outcome),
            _ => throw new ArgumentOutOfRangeException(nameof(observation)),
        };
    }

    private static HistoryErrorType FromLegacyOutcome(ProxyOutcome outcome) => outcome switch
    {
        ProxyOutcome.Completed => HistoryErrorType.None,
        ProxyOutcome.BackendUnavailable => HistoryErrorType.BackendUnavailable,
        ProxyOutcome.ClientCancelled => HistoryErrorType.ClientCancelled,
        ProxyOutcome.RelayFailed => HistoryErrorType.RelayFailed,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
    };

    public static HistoryErrorOrigin OriginFrom(ProxyObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        return observation.ErrorType switch
        {
            ProxyErrorType.None when observation.Outcome == ProxyOutcome.Completed =>
                HistoryErrorOrigin.NotApplicable,
            ProxyErrorType.ClientCancellation => HistoryErrorOrigin.Client,
            ProxyErrorType.ModelLoading or ProxyErrorType.ContextOverflow => HistoryErrorOrigin.Model,
            ProxyErrorType.ConnectionRefused or
            ProxyErrorType.HttpApiError or
            ProxyErrorType.Timeout or
            ProxyErrorType.BackendCrash or
            ProxyErrorType.BackendUnavailable => HistoryErrorOrigin.Backend,
            ProxyErrorType.InspectorFailure => HistoryErrorOrigin.Inspector,
            ProxyErrorType.RelayFailure or ProxyErrorType.None => HistoryErrorOrigin.Unknown,
            _ => HistoryErrorOrigin.Unknown,
        };
    }
}
