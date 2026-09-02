using LlmInspector.Domain;

namespace LlmInspector.Adapters;

public static class BackendTelemetryAdapters
{
    public static IBackendTelemetryAdapter Create(BackendKind backend) => backend switch
    {
        BackendKind.Ollama => new OpenAiBackendTelemetryAdapter(
            backend,
            "epic02-fixtures-v1/ollama-openai"),
        BackendKind.LlamaCpp => new OpenAiBackendTelemetryAdapter(
            backend,
            "epic02-fixtures-v1/llama-cpp-openai"),
        BackendKind.LmStudio => new OpenAiBackendTelemetryAdapter(
            backend,
            "epic02-fixtures-v1/lm-studio-openai"),
        _ => throw new ArgumentOutOfRangeException(nameof(backend)),
    };
}

internal sealed class OpenAiBackendTelemetryAdapter(
    BackendKind backend,
    string fixtureVersion) : IBackendTelemetryAdapter
{
    public BackendKind Backend { get; } = backend;

    public string FixtureVersion { get; } = fixtureVersion;

    public IBackendTelemetrySession CreateSession(string? responseMediaType) =>
        new OpenAiBackendTelemetrySession(
            Backend,
            FixtureVersion,
            responseMediaType?.StartsWith("text/event-stream", StringComparison.OrdinalIgnoreCase) == true);

    public BackendResponseTelemetry CreateUnavailable() =>
        BackendResponseTelemetry.Unavailable(Backend, FixtureVersion);
}
