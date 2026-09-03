using System.Globalization;
using System.Text;
using LlmInspector.Application;
using LlmInspector.Domain;

namespace LlmInspector.App;

public static class ResourceTelemetryTextPresenter
{
    public static string Format(TechnicalResourceSampleRecord? sample) =>
        sample is null ? FormatLatest([]) : FormatLatest([sample]);

    public static string FormatLatest(IReadOnlyList<TechnicalResourceSampleRecord>? samples)
    {
        if (samples is null || samples.Count == 0)
        {
            return "Resource correlation: unavailable; no request sample has been captured.";
        }

        TechnicalResourceSampleRecord sample = samples[0];
        StringBuilder text = new();
        text.Append("Request ").Append(sample.RequestId?.ToString("N") ?? "unavailable")
            .Append(" | operation=").Append(sample.OperationId?.ToString("N") ?? "unavailable")
            .Append(" | stage=").Append(sample.Stage?.Stage.ToString() ?? "unavailable")
            .Append(" | captured=").Append(sample.CapturedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))
            .Append(" | dropped samples=").Append(sample.DroppedSampleCount.ToString(CultureInfo.InvariantCulture))
            .AppendLine();
        text.Append("System CPU=").Append(Format(sample.CpuPercent))
            .Append(" | RAM=").Append(Format(sample.MemoryPercent))
            .Append(" / ").Append(Format(sample.MemoryUsedBytes))
            .AppendLine();
        text.Append("Related process=")
            .Append(sample.RelatedProcess is null
                ? "unavailable"
                : $"{sample.RelatedProcess.ImageName.Value} PID {sample.RelatedProcess.ProcessId}")
            .Append(" | CPU=").Append(Format(sample.ProcessCpuPercent))
            .Append(" | memory=").Append(Format(sample.ProcessMemoryBytes))
            .Append(" | disk read/write=").Append(Format(sample.DiskReadBytes))
            .Append('/').Append(Format(sample.DiskWriteBytes))
            .AppendLine();
        text.Append("Gateway traffic client→backend/backend→client=")
            .Append(Format(sample.ClientToBackendBytes))
            .Append('/').Append(Format(sample.BackendToClientBytes))
            .AppendLine();
        TechnicalResourceSampleRecord[] gpuSamples = samples
            .Where(item => item.GpuDeviceId is not null)
            .DistinctBy(item => item.GpuDeviceId!.Value, StringComparer.Ordinal)
            .ToArray();
        if (gpuSamples.Length == 0)
        {
            text.Append("GPU devices=unavailable | workload attribution=unavailable");
            return text.ToString();
        }

        text.Append("Detected GPU devices=").Append(gpuSamples.Length.ToString(CultureInfo.InvariantCulture));
        foreach (TechnicalResourceSampleRecord gpu in gpuSamples)
        {
            text.AppendLine();
            AppendGpu(text, gpu);
        }

        return text.ToString();
    }

    private static void AppendGpu(StringBuilder text, TechnicalResourceSampleRecord sample)
    {
        text.Append("GPU=").Append(sample.GpuDeviceId!.Value)
            .Append(" | driver=").Append(sample.GpuDriverVersion?.Value ?? "unavailable")
            .Append(" | utilization=").Append(Format(sample.GpuUtilizationPercent))
            .Append(" | VRAM used/total=").Append(Format(sample.GpuVramUsedBytes))
            .Append('/').Append(Format(sample.GpuVramTotalBytes))
            .Append(" | temperature=").Append(Format(sample.GpuTemperatureCelsius))
            .Append(" | power=").Append(Format(sample.GpuPowerWatts))
            .Append(" | workload attribution=unavailable (device-wide source)");
    }

    private static string Format(MetricValue metric)
    {
        if (metric.Value is not decimal value)
        {
            return "unavailable";
        }

        string unit = metric.Unit switch
        {
            MetricUnit.Percent => "%",
            MetricUnit.Bytes => " B",
            MetricUnit.Celsius => " °C",
            MetricUnit.Watts => " W",
            _ => $" {metric.Unit}",
        };
        return value.ToString("0.###", CultureInfo.InvariantCulture) + unit +
            $" [{metric.Quality.ToString().ToLowerInvariant()}]";
    }
}
