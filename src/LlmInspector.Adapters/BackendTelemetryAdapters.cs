using LlmInspector.Domain;

namespace LlmInspector.Adapters;

public static class BackendTelemetryAdapters
{
    public static IBackendTelemetryAdapter Create(BackendKind backend) => backend switch
    {
        BackendKind.Ollama => new OpenAiBackendTelemetryAdapter(
            backend,
            "openai-chat-fixtures-v2/ollama"),
        BackendKind.LlamaCpp => new OpenAiBackendTelemetryAdapter(
            backend,
            "openai-chat-fixtures-v2/llama-cpp"),
        BackendKind.LmStudio => new OpenAiBackendTelemetryAdapter(
            backend,
            "openai-chat-fixtures-v2/lm-studio"),
        _ => throw new ArgumentOutOfRangeException(nameof(backend)),
    };

    public static IBackendTelemetryAdapter CreateLmStudioNative() =>
        new LmStudioNativeTelemetryAdapter("lm-studio-native-chat-v1");
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
