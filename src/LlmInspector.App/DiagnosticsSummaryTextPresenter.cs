using System.Globalization;
using System.Text;
using LlmInspector.Domain;

namespace LlmInspector.App;

public static class DiagnosticsSummaryTextPresenter
{
    public static string Format(
        AppRuntimeStatus runtimeStatus,
        ProxyObservation? latestObservation,
        string historyState)
    {
        ArgumentNullException.ThrowIfNull(runtimeStatus);

        StringBuilder text = new();
        text.Append("Gateway: ");
        text.Append(runtimeStatus.ProxyRunning ? "available" : "unavailable");
        text.Append(". ");
        text.Append(runtimeStatus.State);

        text.AppendLine();
        text.Append("Technical history: ");
        text.Append(string.IsNullOrWhiteSpace(historyState) ? "state unavailable." : historyState.Trim());

        text.AppendLine();
        text.Append("Latest completed request: ");
        if (latestObservation is null)
        {
            text.Append("none; send a request through an Inspector base URL to collect technical diagnostics.");
            return text.ToString();
        }

        text.Append(latestObservation.RequestId.ToString("N", CultureInfo.InvariantCulture)[..8]);
        text.Append(" | outcome=");
        text.Append(latestObservation.Outcome);
        text.Append(" | HTTP=");
        text.Append(latestObservation.HttpStatusCode?.ToString(CultureInfo.InvariantCulture) ?? "unavailable");
        text.Append(" | duration=");
        text.Append(latestObservation.Duration.TotalMilliseconds.ToString("0.##", CultureInfo.InvariantCulture));
        text.Append(" ms [calculated]");
        return text.ToString();
    }
}
