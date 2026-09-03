using System.Globalization;
using LlmInspector.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace LlmInspector.Gateway;

public static class InspectorCorrelationHeaders
{
    public const string SessionId = "X-LLM-Inspector-Session-Id";
    public const string TurnId = "X-LLM-Inspector-Turn-Id";
    public const string TurnSequence = "X-LLM-Inspector-Turn-Sequence";

    public static IReadOnlySet<string> Names { get; } = new HashSet<string>(
        [SessionId, TurnId, TurnSequence],
        StringComparer.OrdinalIgnoreCase);
}

internal static class RequestCorrelationHeaderReader
{
    public static RequestCorrelation? Read(IHeaderDictionary headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        if (!TryReadSingle(headers, InspectorCorrelationHeaders.SessionId, out string? sessionValue) ||
            !TryReadSingle(headers, InspectorCorrelationHeaders.TurnId, out string? turnValue) ||
            !TryReadSingle(headers, InspectorCorrelationHeaders.TurnSequence, out string? sequenceValue) ||
            !Guid.TryParseExact(sessionValue, "N", out Guid sessionId) ||
            !Guid.TryParseExact(turnValue, "N", out Guid turnId) ||
            !int.TryParse(sequenceValue, NumberStyles.None, CultureInfo.InvariantCulture, out int turnSequence) ||
            sessionId == Guid.Empty ||
            turnId == Guid.Empty ||
            turnSequence < 1)
        {
            return null;
        }

        return new RequestCorrelation(sessionId, turnId, turnSequence);
    }

    private static bool TryReadSingle(IHeaderDictionary headers, string name, out string? value)
    {
        value = null;
        if (!headers.TryGetValue(name, out StringValues values) || values.Count != 1)
        {
            return false;
        }

        value = values[0];
        return !string.IsNullOrWhiteSpace(value);
    }
}

internal sealed class RequestCorrelationTracker
{
    private const int MaximumTrackedSessions = 1_024;
    private const string SourceVersion = "inspector-correlation-headers-v1";
    private const string DerivationVersion = "adjacent-context-delta-v1";

    private readonly object _gate = new();
    private readonly Dictionary<Guid, SessionState> _sessions = [];
    private long _accessSequence;

    public MetricValue Observe(RequestCorrelation? correlation, MetricValue contextUsage)
    {
        MetricValue unavailable = MetricValue.Unavailable(
            MetricUnit.TokenDelta,
            MetricSource.Inspector,
            SourceVersion);
        if (correlation is null ||
            contextUsage.Value is not decimal currentValue ||
            contextUsage.Unit != MetricUnit.TokenCount ||
            contextUsage.Quality != MetricQuality.Exact)
        {
            return unavailable;
        }

        lock (_gate)
        {
            long access = ++_accessSequence;
            if (!_sessions.TryGetValue(correlation.SessionId, out SessionState? previous))
            {
                EvictIfNeeded();
                _sessions[correlation.SessionId] = new SessionState(
                    correlation.TurnId,
                    correlation.TurnSequence,
                    currentValue,
                    access);
                return unavailable;
            }

            if (correlation.TurnSequence <= previous.TurnSequence || correlation.TurnId == previous.TurnId)
            {
                previous.LastAccess = access;
                return unavailable;
            }

            bool adjacent = correlation.TurnSequence == previous.TurnSequence + 1;
            decimal delta = currentValue - previous.ContextUsage;
            _sessions[correlation.SessionId] = new SessionState(
                correlation.TurnId,
                correlation.TurnSequence,
                currentValue,
                access);
            return adjacent
                ? MetricValue.Calculated(
                    delta,
                    MetricUnit.TokenDelta,
                    MetricSource.Inspector,
                    SourceVersion,
                    DerivationVersion)
                : unavailable;
        }
    }

    private void EvictIfNeeded()
    {
        if (_sessions.Count < MaximumTrackedSessions)
        {
            return;
        }

        Guid oldest = _sessions.MinBy(item => item.Value.LastAccess).Key;
        _sessions.Remove(oldest);
    }

    private sealed class SessionState(
        Guid turnId,
        int turnSequence,
        decimal contextUsage,
        long lastAccess)
    {
        public Guid TurnId { get; } = turnId;

        public int TurnSequence { get; } = turnSequence;

        public decimal ContextUsage { get; } = contextUsage;

        public long LastAccess { get; set; } = lastAccess;
    }
}
