using System.Globalization;
using LlmInspector.Diagnostics;

namespace LlmInspector.App;

public static class AnalyticsExportUi
{
    public static AnalyticsExportSelection CreateSelection(string? from, string? to) =>
        AnalyticsExportSelection.ForTimeRange(
            ParseRequiredTime(from, "Analytics export start"),
            ParseRequiredTime(to, "Analytics export end"));

    public static string CreateDefaultLocalPath(DateTimeOffset now)
    {
        string directory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }

        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new IOException("A local analytics export directory is unavailable.");
        }

        return Path.Combine(
            directory,
            $"llm-inspector-analytics-{now.ToUniversalTime():yyyyMMdd-HHmmss}.json");
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
}
