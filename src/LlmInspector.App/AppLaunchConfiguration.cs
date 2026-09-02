using System.Globalization;
using LlmInspector.Domain;
using LlmInspector.Gateway;

namespace LlmInspector.App;

public sealed record AppLaunchConfiguration(
    BackendKind Backend,
    Uri BackendBaseAddress,
    int ListenerPort)
{
    public const int SchemaVersion = 1;

    private const string BackendPrefix = "--backend=";
    private const string BackendUrlPrefix = "--backend-url=";
    private const string ListenerPortPrefix = "--listener-port=";

    public static AppLaunchConfiguration Parse(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        BackendKind backend = BackendKind.Ollama;
        Uri? backendAddress = null;
        int listenerPort = ProxyGatewayOptions.DefaultListenerPort;
        HashSet<string> seenOptions = new(StringComparer.OrdinalIgnoreCase);

        foreach (string argument in arguments)
        {
            if (argument.StartsWith(BackendPrefix, StringComparison.OrdinalIgnoreCase))
            {
                EnsureSingle(seenOptions, BackendPrefix);
                backend = ParseBackend(argument[BackendPrefix.Length..]);
            }
            else if (argument.StartsWith(BackendUrlPrefix, StringComparison.OrdinalIgnoreCase))
            {
                EnsureSingle(seenOptions, BackendUrlPrefix);
                if (!Uri.TryCreate(argument[BackendUrlPrefix.Length..], UriKind.Absolute, out backendAddress))
                {
                    throw new ArgumentException("Backend URL must be an absolute URI.", nameof(arguments));
                }
            }
            else if (argument.StartsWith(ListenerPortPrefix, StringComparison.OrdinalIgnoreCase))
            {
                EnsureSingle(seenOptions, ListenerPortPrefix);
                if (!int.TryParse(
                        argument[ListenerPortPrefix.Length..],
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out listenerPort))
                {
                    throw new ArgumentException("Listener port must be an integer.", nameof(arguments));
                }
            }
            else
            {
                throw new ArgumentException("Unknown application option.", nameof(arguments));
            }
        }

        backendAddress ??= ProxyGatewayOptions.GetDefaultBackendBaseAddress(backend);
        _ = ProxyGatewayOptions.Create(listenerPort, backendAddress, backend);
        return new AppLaunchConfiguration(backend, backendAddress, listenerPort);
    }

    public ProxyGatewayOptions CreateProxyOptions() =>
        ProxyGatewayOptions.Create(ListenerPort, BackendBaseAddress, Backend);

    private static BackendKind ParseBackend(string value) => value.ToLowerInvariant() switch
    {
        "ollama" => BackendKind.Ollama,
        "llama-cpp" or "llama.cpp" or "llamacpp" => BackendKind.LlamaCpp,
        "lm-studio" or "lmstudio" => BackendKind.LmStudio,
        _ => throw new ArgumentException("Backend must be ollama, llama-cpp or lm-studio."),
    };

    private static void EnsureSingle(HashSet<string> seenOptions, string option)
    {
        if (!seenOptions.Add(option))
        {
            throw new ArgumentException("Application options cannot be repeated.");
        }
    }
}
