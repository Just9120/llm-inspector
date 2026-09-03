using System.Text.Json.Serialization;

namespace LlmInspector.Domain;

public enum BackendKind
{
    Ollama,
    LlamaCpp,
    LmStudio,
}

public enum ClientKind
{
    GenericUnknown,
    OpenCodeDesktop,
    HermesDesktop,
    Cline,
    OpenWebUi,
}

public enum MetricQuality
{
    Exact,
    Calculated,
    Estimated,
    Unavailable,
}

public enum MetricUnit
{
    TokenCount,
    TokenDelta,
    Nanoseconds,
    Milliseconds,
    TokensPerSecond,
    Percent,
}

public enum MetricSource
{
    OpenAiUsage,
    BackendExtension,
    Inspector,
}

public enum BackendMetricKey
{
    LlamaCppCachedPromptTokens,
    LlamaCppEvaluatedPromptTokens,
    LlamaCppPredictedTokens,
    LlamaCppPromptMilliseconds,
    LlamaCppPredictedMilliseconds,
    LlamaCppPromptTokensPerSecond,
    LlamaCppPredictedTokensPerSecond,
}

public sealed record TechnicalIdentifier
{
    private const int MaximumLength = 128;

    private TechnicalIdentifier(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static TechnicalIdentifier? FromBackend(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumLength)
        {
            return null;
        }

        foreach (char character in value)
        {
            if (!IsAllowed(character))
            {
                return null;
            }
        }

        return new TechnicalIdentifier(value);
    }

    public override string ToString() => Value;

    private static bool IsAllowed(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-' or ':' or '/' or '@' or '+';
}

public sealed record MetricValue
{
    [JsonConstructor]
    public MetricValue(
        decimal? value,
        MetricUnit unit,
        MetricQuality quality,
        MetricSource source,
        string sourceVersion,
        string? derivationVersion = null)
    {
        if (string.IsNullOrWhiteSpace(sourceVersion))
        {
            throw new ArgumentException("Metric source version is required.", nameof(sourceVersion));
        }

        if ((quality == MetricQuality.Unavailable) != (value is null))
        {
            throw new ArgumentException(
                "Unavailable metrics must have no value, and available metrics must have a value.",
                nameof(value));
        }

        if (value is < 0 && unit != MetricUnit.TokenDelta)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Telemetry metrics cannot be negative.");
        }

        if (unit is MetricUnit.TokenCount or MetricUnit.TokenDelta &&
            value is decimal tokenValue &&
            tokenValue != decimal.Truncate(tokenValue))
        {
            throw new ArgumentException("Token counts must be whole numbers.", nameof(value));
        }

        if (unit == MetricUnit.Percent && value is > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Percent metrics cannot exceed 100.");
        }

        if (quality is MetricQuality.Calculated or MetricQuality.Estimated &&
            string.IsNullOrWhiteSpace(derivationVersion))
        {
            throw new ArgumentException(
                "Calculated and estimated metrics require a versioned derivation.",
                nameof(derivationVersion));
        }

        if (quality is MetricQuality.Exact or MetricQuality.Unavailable && derivationVersion is not null)
        {
            throw new ArgumentException(
                "Exact and unavailable metrics cannot claim a derivation.",
                nameof(derivationVersion));
        }

        Value = value;
        Unit = unit;
        Quality = quality;
        Source = source;
        SourceVersion = sourceVersion;
        DerivationVersion = derivationVersion;
    }

    public decimal? Value { get; }

    public MetricUnit Unit { get; }

    public MetricQuality Quality { get; }

    public MetricSource Source { get; }

    public string SourceVersion { get; }

    public string? DerivationVersion { get; }

    public static MetricValue Exact(
        decimal value,
        MetricUnit unit,
        MetricSource source,
        string sourceVersion) =>
        new(value, unit, MetricQuality.Exact, source, sourceVersion);

    public static MetricValue Calculated(
        decimal value,
        MetricUnit unit,
        MetricSource source,
        string sourceVersion,
        string derivationVersion) =>
        new(value, unit, MetricQuality.Calculated, source, sourceVersion, derivationVersion);

    public static MetricValue Estimated(
        decimal value,
        MetricUnit unit,
        MetricSource source,
        string sourceVersion,
        string derivationVersion) =>
        new(value, unit, MetricQuality.Estimated, source, sourceVersion, derivationVersion);

    public static MetricValue Unavailable(
        MetricUnit unit,
        MetricSource source,
        string sourceVersion) =>
        new(null, unit, MetricQuality.Unavailable, source, sourceVersion);
}

public sealed record BackendMetric(
    BackendMetricKey Key,
    TechnicalIdentifier NativeName,
    MetricValue Metric);

public sealed record BackendResponseTelemetry(
    BackendKind Backend,
    TechnicalIdentifier? Model,
    MetricValue PromptTokens,
    MetricValue CompletionTokens,
    MetricValue TotalTokens,
    MetricValue CachedPromptTokens,
    MetricValue ReasoningTokens,
    MetricValue ContextUsageTokens,
    MetricValue ContextLimitTokens,
    MetricValue ContextHistoryTokens,
    MetricValue ContextToolTokens,
    MetricValue PromptTokensPerSecond,
    MetricValue CompletionTokensPerSecond,
    MetricValue ModelLoadTime,
    MetricValue QueueTime,
    IReadOnlyList<BackendMetric> BackendSpecificMetrics)
{
    public static BackendResponseTelemetry Unavailable(BackendKind backend, string sourceVersion) =>
        new(
            backend,
            null,
            MetricValue.Unavailable(MetricUnit.TokenCount, MetricSource.OpenAiUsage, sourceVersion),
            MetricValue.Unavailable(MetricUnit.TokenCount, MetricSource.OpenAiUsage, sourceVersion),
            MetricValue.Unavailable(MetricUnit.TokenCount, MetricSource.OpenAiUsage, sourceVersion),
            MetricValue.Unavailable(MetricUnit.TokenCount, MetricSource.OpenAiUsage, sourceVersion),
            MetricValue.Unavailable(MetricUnit.TokenCount, MetricSource.OpenAiUsage, sourceVersion),
            MetricValue.Unavailable(MetricUnit.TokenCount, MetricSource.OpenAiUsage, sourceVersion),
            MetricValue.Unavailable(MetricUnit.TokenCount, MetricSource.BackendExtension, sourceVersion),
            MetricValue.Unavailable(MetricUnit.TokenCount, MetricSource.BackendExtension, sourceVersion),
            MetricValue.Unavailable(MetricUnit.TokenCount, MetricSource.BackendExtension, sourceVersion),
            MetricValue.Unavailable(MetricUnit.TokensPerSecond, MetricSource.BackendExtension, sourceVersion),
            MetricValue.Unavailable(MetricUnit.TokensPerSecond, MetricSource.BackendExtension, sourceVersion),
            MetricValue.Unavailable(MetricUnit.Milliseconds, MetricSource.BackendExtension, sourceVersion),
            MetricValue.Unavailable(MetricUnit.Milliseconds, MetricSource.BackendExtension, sourceVersion),
            Array.Empty<BackendMetric>());
}

public interface IBackendTelemetryAdapter
{
    BackendKind Backend { get; }

    string FixtureVersion { get; }

    IBackendTelemetrySession CreateSession(string? responseMediaType);

    BackendResponseTelemetry CreateUnavailable();
}

public interface IBackendTelemetrySession
{
    bool HasObservedOutputContent { get; }

    void Observe(ReadOnlySpan<byte> responseBytes);

    BackendResponseTelemetry Complete();
}
