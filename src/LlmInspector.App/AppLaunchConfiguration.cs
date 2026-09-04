using System.Globalization;
using LlmInspector.Domain;
using LlmInspector.Gateway;

namespace LlmInspector.App;

public sealed record AppLaunchConfiguration(
    BackendKind Backend,
    Uri BackendBaseAddress,
    int ListenerPort)
{
    public const int SchemaVersion = 2;

    private const string BackendPrefix = "--backend=";
    private const string BackendUrlPrefix = "--backend-url=";
    private const string RemoteBackendUrlPrefix = "--remote-backend-url=";
    private const string ListenerPortPrefix = "--listener-port=";
    private const string BackgroundOption = "--background";

    public bool StartInBackground { get; init; }

    public BackendConnectionScope BackendConnectionScope { get; init; } = BackendConnectionScope.LocalLoopback;

    public static AppLaunchConfiguration Parse(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        BackendKind backend = BackendKind.Ollama;
        Uri? backendAddress = null;
        int listenerPort = ProxyGatewayOptions.DefaultListenerPort;
        bool startInBackground = false;
        BackendConnectionScope backendConnectionScope = BackendConnectionScope.LocalLoopback;
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
            else if (argument.StartsWith(RemoteBackendUrlPrefix, StringComparison.OrdinalIgnoreCase))
            {
                EnsureSingle(seenOptions, RemoteBackendUrlPrefix);
                if (seenOptions.Contains(BackendUrlPrefix))
                {
                    throw new ArgumentException("Local and remote backend URL options are mutually exclusive.", nameof(arguments));
                }

                if (!Uri.TryCreate(argument[RemoteBackendUrlPrefix.Length..], UriKind.Absolute, out backendAddress))
                {
                    throw new ArgumentException("Remote backend URL must be an absolute URI.", nameof(arguments));
                }

                backendConnectionScope = BackendConnectionScope.TailscaleHttps;
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
            else if (string.Equals(argument, BackgroundOption, StringComparison.OrdinalIgnoreCase))
            {
                EnsureSingle(seenOptions, BackgroundOption);
                startInBackground = true;
            }
            else
            {
                throw new ArgumentException("Unknown application option.", nameof(arguments));
            }
        }

        if (seenOptions.Contains(BackendUrlPrefix) && seenOptions.Contains(RemoteBackendUrlPrefix))
        {
            throw new ArgumentException("Local and remote backend URL options are mutually exclusive.", nameof(arguments));
        }

        backendAddress ??= ProxyGatewayOptions.GetDefaultBackendBaseAddress(backend);
        _ = backendConnectionScope == BackendConnectionScope.TailscaleHttps
            ? ProxyGatewayOptions.CreateTailscaleRemote(listenerPort, backendAddress, backend)
            : ProxyGatewayOptions.Create(listenerPort, backendAddress, backend);
        return new AppLaunchConfiguration(backend, backendAddress, listenerPort)
        {
            StartInBackground = startInBackground,
            BackendConnectionScope = backendConnectionScope,
        };
    }

    public ProxyGatewayOptions CreateProxyOptions() =>
        BackendConnectionScope == BackendConnectionScope.TailscaleHttps
            ? ProxyGatewayOptions.CreateTailscaleRemote(ListenerPort, BackendBaseAddress, Backend)
            : ProxyGatewayOptions.Create(ListenerPort, BackendBaseAddress, Backend);

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
