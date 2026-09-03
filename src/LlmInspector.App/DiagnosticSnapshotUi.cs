using System.Globalization;
using LlmInspector.Diagnostics;

namespace LlmInspector.App;

public static class DiagnosticSnapshotUi
{
    public const string TimeRangeScope = "Time range";
    public const string OperationScope = "Operation";

    public static IReadOnlyList<string> ScopeChoices { get; } =
        Array.AsReadOnly(new[] { TimeRangeScope, OperationScope });

    public static DiagnosticSnapshotSelection CreateSelection(
        string? scope,
        string? from,
        string? to,
        string? operationId)
    {
        return scope switch
        {
            TimeRangeScope => DiagnosticSnapshotSelection.ForTimeRange(
                ParseRequiredTime(from, "Snapshot start"),
                ParseRequiredTime(to, "Snapshot end")),
            OperationScope => DiagnosticSnapshotSelection.ForOperation(
                ParseRequiredGuid(operationId, "Snapshot operation")),
            _ => throw new ArgumentException("Select Time range or Operation snapshot scope.", nameof(scope)),
        };
    }

    public static string CreateDefaultLocalPath(DateTimeOffset now)
    {
        string directory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }

        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new IOException("A local snapshot directory is unavailable.");
        }

        return Path.Combine(
            directory,
            $"llm-inspector-diagnostic-{now.ToUniversalTime():yyyyMMdd-HHmmss}.json");
    }

    private static DateTimeOffset ParseRequiredTime(string? value, string field)
    {
        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset parsed))
        {
            throw new ArgumentException($"{field} must be an ISO-8601 UTC timestamp.");
        }

        return parsed;
    }

    private static Guid ParseRequiredGuid(string? value, string field) =>
        Guid.TryParse(value, out Guid parsed) && parsed != Guid.Empty
            ? parsed
            : throw new ArgumentException($"{field} must be a non-empty GUID.");
}
