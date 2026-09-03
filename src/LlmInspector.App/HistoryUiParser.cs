using System.Globalization;
using LlmInspector.Application;
using LlmInspector.Domain;

namespace LlmInspector.App;

public sealed record HistoryComparisonFilters(
    HistoryFilter Baseline,
    HistoryFilter Candidate);

public sealed record HistoryRetentionChoice(
    HistoryRetention Value,
    string Label)
{
    public override string ToString() => Label;
}

public static class HistoryUiCatalog
{
    public static IReadOnlyList<HistoryRetentionChoice> RetentionChoices { get; } =
    [
        new(HistoryRetention.SevenDays, "7 days"),
        new(HistoryRetention.ThirtyDays, "30 days"),
        new(HistoryRetention.NinetyDays, "90 days"),
        new(HistoryRetention.Indefinite, "indefinite"),
    ];
}

public static class HistoryUiParser
{
    public static HistoryFilter CreateFilter(
        string? from,
        string? to,
        string? client,
        string? backend,
        string? model,
        string? session,
        string? status,
        string? error,
        int limit = 200) => new(
            ParseTime(from, "From"),
            ParseTime(to, "To"),
            ParseOptionalEnum<ClientKind>(client, "Client"),
            ParseOptionalEnum<BackendKind>(backend, "Backend"),
            ParseIdentifier(model, "Model"),
            ParseGuid(session, "Session"),
            ParseOptionalEnum<ProxyOutcome>(status, "Status"),
            ParseOptionalEnum<HistoryErrorType>(error, "Error"),
            limit);

    public static HistoryComparisonFilters CreateComparisonFilters(
        string dimension,
        string baseline,
        string candidate)
    {
        if (string.IsNullOrWhiteSpace(baseline) || string.IsNullOrWhiteSpace(candidate))
        {
            throw new ArgumentException("Both baseline and candidate selections are required.");
        }

        return dimension switch
        {
            "Period" => new(ParsePeriod(baseline), ParsePeriod(candidate)),
            "Model" => new(
                new HistoryFilter(Model: ParseIdentifier(baseline, "Baseline model")),
                new HistoryFilter(Model: ParseIdentifier(candidate, "Candidate model"))),
            "Backend" => new(
                new HistoryFilter(Backend: ParseRequiredEnum<BackendKind>(baseline, "Baseline backend")),
                new HistoryFilter(Backend: ParseRequiredEnum<BackendKind>(candidate, "Candidate backend"))),
            "Client" => new(
                new HistoryFilter(Client: ParseRequiredEnum<ClientKind>(baseline, "Baseline client")),
                new HistoryFilter(Client: ParseRequiredEnum<ClientKind>(candidate, "Candidate client"))),
            _ => throw new ArgumentException("Comparison dimension must be Period, Model, Backend or Client."),
        };
    }

    public static HistoryClearScope CreateClearScope(bool allHistory, string? from, string? to) =>
        new(allHistory, ParseTime(from, "Clear from"), ParseTime(to, "Clear to"));

    private static HistoryFilter ParsePeriod(string value)
    {
        string[] bounds = value.Split("..", StringSplitOptions.TrimEntries);
        if (bounds.Length != 2 ||
            ParseTime(bounds[0], "Period start") is not DateTimeOffset from ||
            ParseTime(bounds[1], "Period end") is not DateTimeOffset to)
        {
            throw new ArgumentException("Period must use the format <UTC start>..<UTC end>.");
        }

        return new HistoryFilter(From: from, To: to);
    }

    private static DateTimeOffset? ParseTime(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset parsed))
        {
            throw new ArgumentException($"{field} must be an ISO-8601 timestamp.");
        }

        return parsed;
    }

    private static TechnicalIdentifier? ParseIdentifier(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return TechnicalIdentifier.FromBackend(value) ??
            throw new ArgumentException($"{field} contains unsupported characters or is too long.");
    }

    private static Guid? ParseGuid(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Guid.TryParse(value, out Guid parsed)
            ? parsed
            : throw new ArgumentException($"{field} must be a GUID.");
    }

    private static T? ParseOptionalEnum<T>(string? value, string field)
        where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("Any", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return ParseRequiredEnum<T>(value, field);
    }

    private static T ParseRequiredEnum<T>(string value, string field)
        where T : struct, Enum => Enum.TryParse(value, ignoreCase: true, out T parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new ArgumentException($"{field} has an unsupported value.");
}
