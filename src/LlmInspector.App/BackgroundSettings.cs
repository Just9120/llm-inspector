using System.Text.Json;
using System.Text.Json.Serialization;
using LlmInspector.Domain;

namespace LlmInspector.App;

public sealed record NotificationSettings
{
    public bool BackendUnavailable { get; init; }

    public bool LongOperationCompleted { get; init; }

    public bool RecurringError { get; init; }

    public bool HighContextUsage { get; init; }

    public bool SilentMode { get; init; } = true;

    public bool IsEnabled(NotificationEventType eventType) => eventType switch
    {
        NotificationEventType.BackendUnavailable => BackendUnavailable,
        NotificationEventType.LongOperationCompleted => LongOperationCompleted,
        NotificationEventType.RecurringError => RecurringError,
        NotificationEventType.HighContextUsage => HighContextUsage,
        _ => throw new ArgumentOutOfRangeException(nameof(eventType)),
    };
}

public sealed record BackgroundSettings
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public bool AutostartEnabled { get; init; }

    public NotificationSettings Notifications { get; init; } = new();

    public MonitoringProfileSettings Monitoring { get; init; } = new();

    public static BackgroundSettings Default { get; } = new();

    public static void Validate(BackgroundSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException("The background settings schema version is unsupported.");
        }

        if (settings.Notifications is null)
        {
            throw new InvalidDataException("Notification settings are required.");
        }

        if (settings.Monitoring is null)
        {
            throw new InvalidDataException("Monitoring profile settings are required.");
        }

        settings.Monitoring.Validate();
    }
}

public sealed record MonitoringProfileSettings
{
    public MonitoringPerformanceProfileId Profile { get; init; } = MonitoringPerformanceProfileId.Balanced;

    public int CustomSamplingIntervalMilliseconds { get; init; } = 1_000;

    public MonitoringPerformanceProfile Resolve()
    {
        Validate();
        return MonitoringPerformanceProfiles.Resolve(Profile, CustomSamplingIntervalMilliseconds);
    }

    public void Validate()
    {
        if (!Enum.IsDefined(Profile))
        {
            throw new InvalidDataException("The monitoring performance profile is unsupported.");
        }

        try
        {
            _ = MonitoringPerformanceProfiles.CreateCustom(CustomSamplingIntervalMilliseconds);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException("The custom monitoring sampling interval is invalid.", exception);
        }
    }
}

public interface IBackgroundSettingsStore
{
    ValueTask<BackgroundSettings> LoadAsync(CancellationToken cancellationToken = default);

    ValueTask SaveAsync(BackgroundSettings settings, CancellationToken cancellationToken = default);
}

public sealed class JsonBackgroundSettingsStore : IBackgroundSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        {
            new JsonStringEnumConverter<MonitoringPerformanceProfileId>(JsonNamingPolicy.SnakeCaseLower),
        },
    };

    private readonly string _path;

    public JsonBackgroundSettingsStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A settings file path is required.", nameof(path));
        }

        _path = Path.GetFullPath(path);
    }

    public async ValueTask<BackgroundSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return BackgroundSettings.Default;
        }

        try
        {
            await using FileStream stream = new(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using JsonDocument document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!document.RootElement.TryGetProperty("schema_version", out JsonElement schemaElement) ||
                !schemaElement.TryGetInt32(out int schemaVersion))
            {
                throw new InvalidDataException("The background settings schema version is missing.");
            }

            BackgroundSettings settings = schemaVersion switch
            {
                1 => MigrateFromV1(document.RootElement),
                BackgroundSettings.CurrentSchemaVersion => document.RootElement.Deserialize<BackgroundSettings>(
                    SerializerOptions) ?? throw new InvalidDataException("The background settings document is empty."),
                _ => throw new InvalidDataException("The background settings schema version is unsupported."),
            };
            BackgroundSettings.Validate(settings);
            return settings;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The background settings document is invalid.", exception);
        }
    }

    public async ValueTask SaveAsync(
        BackgroundSettings settings,
        CancellationToken cancellationToken = default)
    {
        BackgroundSettings.Validate(settings);
        string? directory = Path.GetDirectoryName(_path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new IOException("The settings directory is unavailable.");
        }

        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    settings,
                    SerializerOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static BackgroundSettings MigrateFromV1(JsonElement root)
    {
        BackgroundSettingsV1 legacy = root.Deserialize<BackgroundSettingsV1>(SerializerOptions) ??
            throw new InvalidDataException("The background settings document is empty.");
        if (legacy.SchemaVersion != 1 || legacy.Notifications is null)
        {
            throw new InvalidDataException("The background settings v1 document is invalid.");
        }

        return new BackgroundSettings
        {
            AutostartEnabled = legacy.AutostartEnabled,
            Notifications = legacy.Notifications,
            Monitoring = new MonitoringProfileSettings(),
        };
    }

    private sealed record BackgroundSettingsV1
    {
        public int SchemaVersion { get; init; }

        public bool AutostartEnabled { get; init; }

        public NotificationSettings? Notifications { get; init; }
    }
}

public interface IAutostartRegistration
{
    bool IsEnabled();

    void SetEnabled(bool enabled);
}

public sealed class BackgroundSettingsService
{
    private readonly IBackgroundSettingsStore _store;
    private readonly IAutostartRegistration _autostart;
    private BackgroundSettings _current = BackgroundSettings.Default;

    public BackgroundSettingsService(
        IBackgroundSettingsStore store,
        IAutostartRegistration autostart)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _autostart = autostart ?? throw new ArgumentNullException(nameof(autostart));
    }

    public BackgroundSettings Current => Volatile.Read(ref _current);

    public async ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        BackgroundSettings stored = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        BackgroundSettings current = stored with { AutostartEnabled = _autostart.IsEnabled() };
        Volatile.Write(ref _current, current);
    }

    public void InitializeFromAutostartState()
    {
        Volatile.Write(
            ref _current,
            BackgroundSettings.Default with { AutostartEnabled = _autostart.IsEnabled() });
    }

    public async ValueTask SaveAsync(
        BackgroundSettings settings,
        CancellationToken cancellationToken = default)
    {
        BackgroundSettings.Validate(settings);
        BackgroundSettings previous = Current;
        bool changedAutostart = previous.AutostartEnabled != settings.AutostartEnabled;
        if (changedAutostart)
        {
            _autostart.SetEnabled(settings.AutostartEnabled);
        }

        try
        {
            await _store.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (changedAutostart)
            {
                _autostart.SetEnabled(previous.AutostartEnabled);
            }

            throw;
        }

        Volatile.Write(ref _current, settings);
    }
}
