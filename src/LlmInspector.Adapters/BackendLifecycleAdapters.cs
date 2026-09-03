using System.Text.Json;
using LlmInspector.Application;
using LlmInspector.Domain;

namespace LlmInspector.Adapters;

public static class BackendLifecycleAdapters
{
    public static IReadOnlyList<IBackendLifecycleAdapter> CreateAll() =>
    [
        new OllamaLifecycleAdapter(),
        new LlamaCppLifecycleAdapter(),
        new LmStudioLifecycleAdapter(),
    ];

    public static IBackendLifecycleAdapter Create(BackendKind backend) => backend switch
    {
        BackendKind.Ollama => new OllamaLifecycleAdapter(),
        BackendKind.LlamaCpp => new LlamaCppLifecycleAdapter(),
        BackendKind.LmStudio => new LmStudioLifecycleAdapter(),
        _ => throw new ArgumentOutOfRangeException(nameof(backend)),
    };
}

internal abstract class BackendLifecycleAdapterBase : IBackendLifecycleAdapter
{
    private static readonly RuntimeCompatibilityCatalog CompatibilityCatalog = RuntimeCompatibilityCatalog.Load();

    public abstract BackendLifecycleProfile Profile { get; }

    public BackendCompatibilityStatus ClassifyVersion(string version) =>
        CompatibilityCatalog.Classify(Profile.Backend, version);

    public string GetCompatibilityLabel(BackendCompatibilityStatus status) => status switch
    {
        BackendCompatibilityStatus.Verified => "Проверено",
        BackendCompatibilityStatus.Compatible => "Совместимо",
        BackendCompatibilityStatus.ObservationOnly => "Только наблюдение",
        BackendCompatibilityStatus.Unsupported => "Не поддерживается",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public abstract BackendProcessStartPlan CreateStartPlan(
        BackendLifecycleTarget target,
        IReadOnlyDictionary<string, string> parameters,
        string? model);

    public virtual BackendCommandPlan? CreateOfficialStopPlan(BackendLifecycleTarget target) => null;

    public abstract ValueTask<bool> ConfirmReadyAsync(
        BackendLifecycleTarget target,
        IBackendLifecycleRuntime runtime,
        CancellationToken cancellationToken);

    public abstract ValueTask<bool> LoadModelAsync(
        BackendLifecycleTarget target,
        string model,
        IReadOnlyDictionary<string, string> parameters,
        IBackendLifecycleRuntime runtime,
        CancellationToken cancellationToken);

    public abstract ValueTask<bool> ConfirmModelAsync(
        BackendLifecycleTarget target,
        string model,
        IBackendLifecycleRuntime runtime,
        CancellationToken cancellationToken);

    protected static BackendParameterDefinition Integer(
        string key,
        string name,
        string description,
        long minimum,
        long maximum,
        string? defaultValue = null) =>
        new(key, name, description, BackendParameterKind.WholeNumber, defaultValue, minimum, maximum);

    protected static Uri Address(BackendLifecycleTarget target, string relative) =>
        new(target.Endpoint, relative);

    protected static string? Value(IReadOnlyDictionary<string, string> parameters, string key) =>
        parameters.TryGetValue(key, out string? value) ? value : null;

    protected static bool JsonModelsContain(string json, string model)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return EnumerateStrings(document.RootElement).Any(value => value.Equals(model, StringComparison.OrdinalIgnoreCase));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    protected static async ValueTask<string?> ProbeHttpAsync(
        Func<ValueTask<string>> probe,
        Func<string, bool> accept,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                string response = await probe().ConfigureAwait(false);
                if (accept(response))
                {
                    return response;
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or TimeoutException)
            {
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        }
        while (DateTimeOffset.UtcNow < deadline);

        return null;
    }

    private static IEnumerable<string> EnumerateStrings(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String && element.GetString() is string value)
        {
            yield return value;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                foreach (string nestedValue in EnumerateStrings(item))
                {
                    yield return nestedValue;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                foreach (string nestedValue in EnumerateStrings(property.Value))
                {
                    yield return nestedValue;
                }
            }
        }
    }
}

internal sealed class OllamaLifecycleAdapter : BackendLifecycleAdapterBase
{
    public override BackendLifecycleProfile Profile { get; } = new(
        BackendKind.Ollama,
        "Ollama",
        DiscoveryCandidates("ollama.exe", Path.Combine("Ollama", "ollama.exe")),
        ["--version"],
        new Uri("http://127.0.0.1:11434/"),
        [
            Integer("local-port", "Локальный порт", "Loopback endpoint Ollama.", 1024, 65535, "11434"),
            Integer("context", "Контекст", "Максимальный размер контекста.", 128, 1048576),
            new("keep-alive", "Keep-alive", "Время хранения модели в памяти, секунды.", BackendParameterKind.DurationSeconds, null, 0, 604800),
            Integer("parallel", "Параллельные запросы", "Одновременные model requests.", 1, 64),
            Integer("max-loaded", "Максимум загруженных моделей", "Лимит моделей в памяти.", 1, 64),
            Integer("max-queue", "Максимальная очередь", "Лимит ожидающих requests.", 1, 65536),
        ],
        BackendModelLoadMode.HttpRequest);

    public override BackendProcessStartPlan CreateStartPlan(
        BackendLifecycleTarget target,
        IReadOnlyDictionary<string, string> parameters,
        string? model)
    {
        Dictionary<string, string> environment = new(StringComparer.Ordinal)
        {
            ["OLLAMA_HOST"] = $"127.0.0.1:{target.Endpoint.Port}",
        };
        AddEnvironment(environment, parameters, "context", "OLLAMA_CONTEXT_LENGTH");
        AddEnvironment(environment, parameters, "parallel", "OLLAMA_NUM_PARALLEL");
        AddEnvironment(environment, parameters, "max-loaded", "OLLAMA_MAX_LOADED_MODELS");
        AddEnvironment(environment, parameters, "max-queue", "OLLAMA_MAX_QUEUE");
        if (Value(parameters, "keep-alive") is string keepAlive)
        {
            environment["OLLAMA_KEEP_ALIVE"] = $"{keepAlive}s";
        }

        return new BackendProcessStartPlan(
            target.ExecutablePath,
            ["serve"],
            environment,
            target.Endpoint,
            BackendStartOwnership.AttachedProcess,
            ["ollama.exe"]);
    }

    public override async ValueTask<bool> ConfirmReadyAsync(
        BackendLifecycleTarget target,
        IBackendLifecycleRuntime runtime,
        CancellationToken cancellationToken)
    {
        string? response = await ProbeHttpAsync(
            () => runtime.SendHttpAsync(HttpMethod.Get, Address(target, "api/tags"), null, cancellationToken),
            value => value.Length > 0,
            cancellationToken).ConfigureAwait(false);
        return response is not null;
    }

    public override async ValueTask<bool> LoadModelAsync(
        BackendLifecycleTarget target,
        string model,
        IReadOnlyDictionary<string, string> parameters,
        IBackendLifecycleRuntime runtime,
        CancellationToken cancellationToken)
    {
        string body = JsonSerializer.Serialize(new { model, prompt = string.Empty, keep_alive = "5m" });
        _ = await runtime.SendHttpAsync(
            HttpMethod.Post,
            Address(target, "api/generate"),
            body,
            cancellationToken).ConfigureAwait(false);
        return true;
    }

    public override async ValueTask<bool> ConfirmModelAsync(
        BackendLifecycleTarget target,
        string model,
        IBackendLifecycleRuntime runtime,
        CancellationToken cancellationToken)
    {
        string response = await runtime.SendHttpAsync(
            HttpMethod.Get,
            Address(target, "api/tags"),
            null,
            cancellationToken).ConfigureAwait(false);
        return JsonModelsContain(response, model);
    }

    private static void AddEnvironment(
        Dictionary<string, string> environment,
        IReadOnlyDictionary<string, string> parameters,
        string key,
        string variable)
    {
        if (Value(parameters, key) is string value)
        {
            environment[variable] = value;
        }
    }

    private static List<string> DiscoveryCandidates(string fileName, string localRelativePath)
    {
        List<string> candidates = [fileName];
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(local))
        {
            candidates.Add(Path.Combine(local, "Programs", localRelativePath));
        }

        return candidates;
    }
}

internal sealed class LlamaCppLifecycleAdapter : BackendLifecycleAdapterBase
{
    public override BackendLifecycleProfile Profile { get; } = new(
        BackendKind.LlamaCpp,
        "llama.cpp",
        ["llama-server.exe"],
        ["--version"],
        new Uri("http://127.0.0.1:8080/"),
        [
            Integer("local-port", "Локальный порт", "Loopback endpoint llama-server.", 1024, 65535, "8080"),
            Integer("context", "Контекст", "Размер runtime context.", 128, 1048576),
            new("gpu-layers", "GPU layers", "auto, off, all или точное число слоёв.", BackendParameterKind.GpuLayers),
            Integer("cpu-threads", "CPU threads", "Количество CPU worker threads.", 1, 512),
            Integer("parallel", "Параллельные slots", "Количество simultaneous slots.", 1, 64),
        ],
        BackendModelLoadMode.RestartProcess);

    public override BackendProcessStartPlan CreateStartPlan(
        BackendLifecycleTarget target,
        IReadOnlyDictionary<string, string> parameters,
        string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new InvalidOperationException("Для запуска llama.cpp сначала выберите .gguf model.");
        }

        List<string> arguments = ["--host", "127.0.0.1", "--port", target.Endpoint.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), "--model", model];
        Add(arguments, parameters, "context", "--ctx-size");
        Add(arguments, parameters, "gpu-layers", "--n-gpu-layers", value => value == "off" ? "0" : value);
        Add(arguments, parameters, "cpu-threads", "--threads");
        Add(arguments, parameters, "parallel", "--parallel");
        return new BackendProcessStartPlan(
            target.ExecutablePath,
            arguments,
            new Dictionary<string, string>(),
            target.Endpoint,
            BackendStartOwnership.AttachedProcess,
            ["llama-server.exe"]);
    }

    public override async ValueTask<bool> ConfirmReadyAsync(
        BackendLifecycleTarget target,
        IBackendLifecycleRuntime runtime,
        CancellationToken cancellationToken)
    {
        string? response = await ProbeHttpAsync(
            () => runtime.SendHttpAsync(HttpMethod.Get, Address(target, "health"), null, cancellationToken),
            value => value.Contains("ok", StringComparison.OrdinalIgnoreCase),
            cancellationToken).ConfigureAwait(false);
        return response is not null;
    }

    public override ValueTask<bool> LoadModelAsync(
        BackendLifecycleTarget target,
        string model,
        IReadOnlyDictionary<string, string> parameters,
        IBackendLifecycleRuntime runtime,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(true);

    public override async ValueTask<bool> ConfirmModelAsync(
        BackendLifecycleTarget target,
        string model,
        IBackendLifecycleRuntime runtime,
        CancellationToken cancellationToken)
    {
        string response = await runtime.SendHttpAsync(
            HttpMethod.Get,
            Address(target, "v1/models"),
            null,
            cancellationToken).ConfigureAwait(false);
        string fileName = Path.GetFileNameWithoutExtension(model);
        return JsonModelsContain(response, model) || JsonModelsContain(response, fileName);
    }

    private static void Add(
        List<string> arguments,
        IReadOnlyDictionary<string, string> parameters,
        string key,
        string flag,
        Func<string, string>? map = null)
    {
        if (Value(parameters, key) is string value)
        {
            arguments.Add(flag);
            arguments.Add(map?.Invoke(value) ?? value);
        }
    }
}

internal sealed class LmStudioLifecycleAdapter : BackendLifecycleAdapterBase
{
    public override BackendLifecycleProfile Profile { get; } = new(
        BackendKind.LmStudio,
        "LM Studio",
        ["lms.exe"],
        ["--version"],
        new Uri("http://127.0.0.1:1234/"),
        [
            Integer("local-port", "Локальный порт", "Loopback endpoint LM Studio.", 1024, 65535, "1234"),
            Integer("context", "Контекст", "Размер context для загружаемой модели.", 128, 1048576),
            new("gpu-offload", "GPU offload", "auto, off, max или доля 0..1.", BackendParameterKind.GpuOffload),
            new("model-ttl", "Model TTL", "Время хранения модели, секунды.", BackendParameterKind.DurationSeconds, null, 0, 604800),
            new("model-id", "Model ID", "Exact model key из lms ls.", BackendParameterKind.ModelIdentifier),
        ],
        BackendModelLoadMode.Command);

    public override BackendProcessStartPlan CreateStartPlan(
        BackendLifecycleTarget target,
        IReadOnlyDictionary<string, string> parameters,
        string? model) =>
        new(
            target.ExecutablePath,
            ["server", "start", "--port", target.Endpoint.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), "--bind", "127.0.0.1"],
            new Dictionary<string, string>(),
            target.Endpoint,
            BackendStartOwnership.DetachedListener,
            ["LM Studio.exe", "lms.exe"]);

    public override BackendCommandPlan? CreateOfficialStopPlan(BackendLifecycleTarget target) =>
        new(target.ExecutablePath, ["server", "stop"], TimeSpan.FromSeconds(15));

    public override async ValueTask<bool> ConfirmReadyAsync(
        BackendLifecycleTarget target,
        IBackendLifecycleRuntime runtime,
        CancellationToken cancellationToken)
    {
        string? response = await ProbeHttpAsync(
            () => runtime.SendHttpAsync(HttpMethod.Get, Address(target, "v1/models"), null, cancellationToken),
            value => value.Length > 0,
            cancellationToken).ConfigureAwait(false);
        return response is not null;
    }

    public override async ValueTask<bool> LoadModelAsync(
        BackendLifecycleTarget target,
        string model,
        IReadOnlyDictionary<string, string> parameters,
        IBackendLifecycleRuntime runtime,
        CancellationToken cancellationToken)
    {
        List<string> arguments = ["load", model];
        Add(arguments, parameters, "gpu-offload", "--gpu");
        Add(arguments, parameters, "context", "--context-length");
        Add(arguments, parameters, "model-ttl", "--ttl");
        BackendCommandResult result = await runtime.ExecuteAsync(
            new BackendCommandPlan(target.ExecutablePath, arguments, TimeSpan.FromMinutes(5)),
            cancellationToken).ConfigureAwait(false);
        return result.Succeeded;
    }

    public override async ValueTask<bool> ConfirmModelAsync(
        BackendLifecycleTarget target,
        string model,
        IBackendLifecycleRuntime runtime,
        CancellationToken cancellationToken)
    {
        BackendCommandResult result = await runtime.ExecuteAsync(
            new BackendCommandPlan(target.ExecutablePath, ["ps"], TimeSpan.FromSeconds(15)),
            cancellationToken).ConfigureAwait(false);
        return result.Succeeded && result.StandardOutput.Contains(model, StringComparison.OrdinalIgnoreCase);
    }

    private static void Add(
        List<string> arguments,
        IReadOnlyDictionary<string, string> parameters,
        string key,
        string flag)
    {
        if (Value(parameters, key) is string value)
        {
            arguments.Add(flag);
            arguments.Add(value);
        }
    }
}

internal sealed class RuntimeCompatibilityCatalog
{
    private readonly IReadOnlyList<Entry> _entries;

    private RuntimeCompatibilityCatalog(IReadOnlyList<Entry> entries)
    {
        _entries = entries;
    }

    public static RuntimeCompatibilityCatalog Load()
    {
        string resource = typeof(RuntimeCompatibilityCatalog).Assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith("runtime-compatibility.json", StringComparison.Ordinal));
        using Stream stream = typeof(RuntimeCompatibilityCatalog).Assembly.GetManifestResourceStream(resource)!;
        using JsonDocument document = JsonDocument.Parse(stream);
        if (document.RootElement.GetProperty("schemaVersion").GetInt32() != 1)
        {
            throw new InvalidDataException("Unsupported runtime compatibility schema.");
        }

        List<Entry> entries = [];
        foreach (JsonElement item in document.RootElement.GetProperty("entries").EnumerateArray())
        {
            if (!Enum.TryParse(item.GetProperty("backend").GetString(), out BackendKind backend))
            {
                throw new InvalidDataException("Runtime compatibility backend is invalid.");
            }

            string match = item.GetProperty("versionMatch").GetString() ?? string.Empty;
            string status = item.GetProperty("status").GetString() ?? string.Empty;
            string revision = item.GetProperty("inspectorRevision").GetString() ?? string.Empty;
            bool supportedStatus = status is "verified" or "compatible" or "observation-only" or "unsupported";
            bool validRevision = revision.Length == 40 && revision.All(Uri.IsHexDigit);
            bool requiredArraysPresent = RequiredArray(item, "capabilities") &&
                RequiredArray(item, "windows") &&
                RequiredArray(item, "evidence") &&
                RequiredArray(item, "limitations");
            bool verifiedDatePresent = status != "verified" || item.GetProperty("verifiedAtUtc").ValueKind == JsonValueKind.String;
            if (string.IsNullOrWhiteSpace(match) || !supportedStatus || !validRevision ||
                !requiredArraysPresent || !verifiedDatePresent ||
                entries.Any(entry => entry.Backend == backend && entry.VersionMatch == match))
            {
                throw new InvalidDataException("Runtime compatibility entry is incomplete or ambiguous.");
            }

            entries.Add(new Entry(backend, match, status));
        }

        if (entries.Select(entry => entry.Backend).Distinct().Count() != Enum.GetValues<BackendKind>().Length)
        {
            throw new InvalidDataException("Runtime compatibility matrix does not cover every built-in backend.");
        }

        return new RuntimeCompatibilityCatalog(entries);
    }

    public BackendCompatibilityStatus Classify(BackendKind backend, string version)
    {
        Entry? exact = _entries.FirstOrDefault(entry =>
            entry.Backend == backend && ContainsVersionToken(version, entry.VersionMatch));
        if (exact is null)
        {
            return BackendCompatibilityStatus.Compatible;
        }

        return exact.Status switch
        {
            "verified" => BackendCompatibilityStatus.Verified,
            "compatible" => BackendCompatibilityStatus.Compatible,
            "observation-only" => BackendCompatibilityStatus.ObservationOnly,
            _ => BackendCompatibilityStatus.Unsupported,
        };
    }

    private static bool RequiredArray(JsonElement item, string propertyName) =>
        item.TryGetProperty(propertyName, out JsonElement value) &&
        value.ValueKind == JsonValueKind.Array &&
        value.GetArrayLength() > 0;

    private static bool ContainsVersionToken(string value, string token)
    {
        int index = value.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            int beforeIndex = index - 1;
            int afterIndex = index + token.Length;
            bool validBefore = beforeIndex < 0 || !IsVersionCharacter(value[beforeIndex]);
            bool validAfter = afterIndex >= value.Length || !IsVersionCharacter(value[afterIndex]);
            if (validBefore && validAfter)
            {
                return true;
            }

            index = value.IndexOf(token, index + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool IsVersionCharacter(char value) => char.IsAsciiDigit(value) || value == '.';

    private sealed record Entry(BackendKind Backend, string VersionMatch, string Status);
}
