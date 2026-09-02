using LlmInspector.Domain;

namespace LlmInspector.Gateway;

public sealed record ClientEndpoint(
    ClientKind Client,
    string DisplayName,
    string BasePath,
    string ChatCompletionsPath,
    string ModelsPath);

public static class ClientEndpointCatalog
{
    public const string GenericBasePath = "/v1";

    public static IReadOnlyList<ClientEndpoint> KnownClients { get; } = Array.AsReadOnly(
    [
        Create(ClientKind.OpenCodeDesktop, "OpenCode Desktop", "/clients/opencode/v1"),
        Create(ClientKind.HermesDesktop, "Hermes Desktop", "/clients/hermes/v1"),
        Create(ClientKind.Cline, "Cline", "/clients/cline/v1"),
        Create(ClientKind.OpenWebUi, "Open WebUI", "/clients/open-webui/v1"),
    ]);

    public static string GenericChatCompletionsPath => GenericBasePath + "/chat/completions";

    public static string GenericModelsPath => GenericBasePath + "/models";

    private static ClientEndpoint Create(ClientKind client, string displayName, string basePath) =>
        new(client, displayName, basePath, basePath + "/chat/completions", basePath + "/models");
}
