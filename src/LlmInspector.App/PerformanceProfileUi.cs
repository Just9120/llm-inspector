using System.Globalization;
using LlmInspector.Domain;

namespace LlmInspector.App;

public sealed record MonitoringPerformanceProfileChoice(
    MonitoringPerformanceProfileId Id,
    string Label,
    string Description)
{
    public override string ToString() => Label;
}

public static class PerformanceProfileUi
{
    public static IReadOnlyList<MonitoringPerformanceProfileChoice> Choices { get; } =
    [
        new(
            MonitoringPerformanceProfileId.Saver,
            "Бережный · 2 с",
            "Минимальная нагрузка Inspector, обновление технических метрик раз в 2 секунды."),
        new(
            MonitoringPerformanceProfileId.Balanced,
            "Сбалансированный · 1 с (рекомендуется)",
            "Баланс детализации и нагрузки; используется по умолчанию."),
        new(
            MonitoringPerformanceProfileId.Detailed,
            "Детальный · 500 мс",
            "Более частая телеметрия для диагностики с повышенной допустимой нагрузкой."),
        new(
            MonitoringPerformanceProfileId.Custom,
            "Свой профиль",
            $"Интервал {MonitoringPerformanceProfiles.MinimumCustomSamplingMilliseconds}–" +
            $"{MonitoringPerformanceProfiles.MaximumCustomSamplingMilliseconds} мс. " +
            "Не используется как release-performance evidence."),
    ];

    public static MonitoringProfileSettings CreateSettings(
        MonitoringPerformanceProfileId id,
        string? customSamplingIntervalText)
    {
        if (!int.TryParse(
                customSamplingIntervalText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int customSamplingIntervalMilliseconds))
        {
            throw new InvalidDataException("Укажите целый интервал своего профиля в миллисекундах.");
        }

        MonitoringProfileSettings settings = new()
        {
            Profile = id,
            CustomSamplingIntervalMilliseconds = customSamplingIntervalMilliseconds,
        };
        settings.Validate();
        return settings;
    }

    public static string Describe(MonitoringProfileSettings settings)
    {
        MonitoringPerformanceProfile resolved = settings.Resolve();
        string releaseStatus = resolved.IsBuiltIn
            ? "Профиль участвует в release-performance gate после контрольных замеров."
            : "Свой профиль не участвует в release-performance gate.";
        return $"Активен профиль «{resolved.DisplayName}»: интервал " +
            $"{resolved.SamplingInterval.TotalMilliseconds:0} мс. {releaseStatus}";
    }
}
