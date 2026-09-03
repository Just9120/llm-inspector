using System.Collections.ObjectModel;
using System.Globalization;
using LlmInspector.Domain;

namespace LlmInspector.Application;

public enum BackendLifecycleState
{
    NotConfigured,
    TargetPendingConfirmation,
    Stopped,
    Starting,
    Running,
    Stopping,
    Crashed,
    Faulted,
}

public enum BackendCompatibilityStatus
{
    Verified,
    Compatible,
    ObservationOnly,
    Unsupported,
}

public enum BackendParameterKind
{
    WholeNumber,
    DurationSeconds,
    ModelIdentifier,
    GpuLayers,
    GpuOffload,
}

public enum BackendStartOwnership
{
    AttachedProcess,
    DetachedListener,
}

public enum BackendModelLoadMode
{
    Command,
    HttpRequest,
    RestartProcess,
}

public sealed record BackendParameterDefinition(
    string Key,
    string DisplayName,
    string Description,
    BackendParameterKind Kind,
    string? DefaultValue = null,
    decimal? Minimum = null,
    decimal? Maximum = null)
{
    public string? Normalize(string? value)
    {
        string? trimmed = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (trimmed is null)
        {
            return DefaultValue;
        }

        return Kind switch
        {
            BackendParameterKind.WholeNumber or BackendParameterKind.DurationSeconds =>
                NormalizeInteger(trimmed),
            BackendParameterKind.ModelIdentifier => NormalizeModelIdentifier(trimmed),
            BackendParameterKind.GpuLayers => NormalizeGpuLayers(trimmed),
            BackendParameterKind.GpuOffload => NormalizeGpuOffload(trimmed),
            _ => throw new InvalidOperationException("Unsupported lifecycle parameter kind."),
        };
    }

    private string NormalizeInteger(string value)
    {
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long parsed) ||
            (Minimum is decimal minimum && parsed < minimum) ||
            (Maximum is decimal maximum && parsed > maximum))
        {
            throw new ArgumentOutOfRangeException(Key, $"{DisplayName}: введите целое значение в допустимом диапазоне.");
        }

        return parsed.ToString(CultureInfo.InvariantCulture);
    }

    private string NormalizeModelIdentifier(string value)
    {
        if (value.Length > 512 || value.Any(char.IsControl))
        {
            throw new ArgumentException($"{DisplayName}: значение недопустимо.", Key);
        }

        return value;
    }

    private string NormalizeGpuLayers(string value)
    {
        if (value.Equals("auto", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("off", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return value.ToLowerInvariant();
        }

        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int layers) &&
            layers is >= 0 and <= 999
                ? layers.ToString(CultureInfo.InvariantCulture)
                : throw new ArgumentException($"{DisplayName}: используйте auto, off, all или 0..999.", Key);
    }

    private string NormalizeGpuOffload(string value)
    {
        if (value.Equals("auto", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("off", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("max", StringComparison.OrdinalIgnoreCase))
        {
            return value.ToLowerInvariant();
        }

        return decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal ratio) &&
            ratio is >= 0 and <= 1
                ? ratio.ToString(CultureInfo.InvariantCulture)
                : throw new ArgumentException($"{DisplayName}: используйте auto, off, max или число 0..1.", Key);
    }
}

public sealed record BackendLifecycleProfile(
    BackendKind Backend,
    string DisplayName,
    IReadOnlyList<string> DiscoveryCandidates,
    IReadOnlyList<string> VersionArguments,
    Uri DefaultEndpoint,
    IReadOnlyList<BackendParameterDefinition> Parameters,
    BackendModelLoadMode ModelLoadMode);

public sealed record BackendLifecycleTarget(
    BackendKind Backend,
    string ExecutablePath,
    string Version,
    Uri Endpoint,
    BackendCompatibilityStatus Compatibility,
    string CompatibilityLabel,
    string ConfirmationToken);

public sealed record BackendProcessIdentity(
    int ProcessId,
    DateTimeOffset StartedAt,
    string ExecutablePath);

public sealed record BackendProcessStartPlan(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Environment,
    Uri Endpoint,
    BackendStartOwnership Ownership,
    IReadOnlyList<string> AllowedListenerImageNames);

public sealed record BackendCommandPlan(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    TimeSpan Timeout);

public sealed record BackendCommandResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}

public sealed record BackendLifecycleSnapshot(
    BackendLifecycleState State,
    BackendLifecycleTarget? Target,
    BackendProcessIdentity? OwnedProcess,
    IReadOnlyDictionary<string, string> Parameters,
    string? Model,
    string Message)
{
    public static BackendLifecycleSnapshot Empty { get; } = new(
        BackendLifecycleState.NotConfigured,
        null,
        null,
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>()),
        null,
        "Сначала найдите runtime и подтвердите точный путь, версию и endpoint.");
}

public sealed record BackendLifecycleResult(bool Succeeded, BackendLifecycleSnapshot Snapshot);

public interface IBackendLifecycleRuntime
{
    ValueTask<string?> ResolveExecutableAsync(
        IReadOnlyList<string> candidates,
        string? manualPath,
        CancellationToken cancellationToken);

    ValueTask<BackendCommandResult> ExecuteAsync(
        BackendCommandPlan command,
        CancellationToken cancellationToken);

    ValueTask<BackendProcessIdentity?> ResolveEndpointOwnerAsync(
        Uri endpoint,
        CancellationToken cancellationToken);

    ValueTask<BackendProcessIdentity> StartAsync(
        BackendProcessStartPlan plan,
        CancellationToken cancellationToken);

    ValueTask<bool> IsSameProcessAliveAsync(
        BackendProcessIdentity identity,
        CancellationToken cancellationToken);

    ValueTask StopAsync(
        BackendProcessIdentity identity,
        BackendCommandPlan? officialStop,
        CancellationToken cancellationToken);

    ValueTask<string> SendHttpAsync(
        HttpMethod method,
        Uri address,
        string? jsonBody,
        CancellationToken cancellationToken);
}

public interface IBackendLifecycleAdapter
{
    BackendLifecycleProfile Profile { get; }

    BackendCompatibilityStatus ClassifyVersion(string version);

    string GetCompatibilityLabel(BackendCompatibilityStatus status);

    BackendProcessStartPlan CreateStartPlan(
        BackendLifecycleTarget target,
        IReadOnlyDictionary<string, string> parameters,
        string? model);

    BackendCommandPlan? CreateOfficialStopPlan(BackendLifecycleTarget target);

    ValueTask<bool> ConfirmReadyAsync(
        BackendLifecycleTarget target,
        IBackendLifecycleRuntime runtime,
        CancellationToken cancellationToken);

    ValueTask<bool> LoadModelAsync(
        BackendLifecycleTarget target,
        string model,
        IReadOnlyDictionary<string, string> parameters,
        IBackendLifecycleRuntime runtime,
        CancellationToken cancellationToken);

    ValueTask<bool> ConfirmModelAsync(
        BackendLifecycleTarget target,
        string model,
        IBackendLifecycleRuntime runtime,
        CancellationToken cancellationToken);
}

public sealed class BackendLifecycleManager : IDisposable
{
    private static readonly TimeSpan VersionProbeTimeout = TimeSpan.FromSeconds(5);
    private readonly IBackendLifecycleAdapter _adapter;
    private readonly IBackendLifecycleRuntime _runtime;
    private readonly ILiveRequestSnapshotSource _liveRequests;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private BackendLifecycleSnapshot _snapshot = BackendLifecycleSnapshot.Empty;
    private string? _confirmedToken;

    public BackendLifecycleManager(
        IBackendLifecycleAdapter adapter,
        IBackendLifecycleRuntime runtime,
        ILiveRequestSnapshotSource liveRequests)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _liveRequests = liveRequests ?? throw new ArgumentNullException(nameof(liveRequests));
    }

    public BackendLifecycleProfile Profile => _adapter.Profile;

    public BackendLifecycleSnapshot Snapshot => _snapshot;

    public async ValueTask<BackendLifecycleResult> DiscoverAsync(
        string? manualPath = null,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_snapshot.OwnedProcess is BackendProcessIdentity owned &&
                await _runtime.IsSameProcessAliveAsync(owned, cancellationToken).ConfigureAwait(false))
            {
                return Fail("Нельзя менять target работающего owned backend. Сначала остановите его.");
            }

            string? executable = await _runtime.ResolveExecutableAsync(
                _adapter.Profile.DiscoveryCandidates,
                manualPath,
                cancellationToken).ConfigureAwait(false);
            if (executable is null)
            {
                return Fail("Runtime не найден. Укажите точный путь к executable вручную.");
            }

            BackendCommandResult versionResult = await _runtime.ExecuteAsync(
                new BackendCommandPlan(executable, _adapter.Profile.VersionArguments, VersionProbeTimeout),
                cancellationToken).ConfigureAwait(false);
            string version = FirstNonEmptyLine(versionResult.StandardOutput, versionResult.StandardError);
            if (!versionResult.Succeeded || string.IsNullOrWhiteSpace(version))
            {
                return Fail("Не удалось безопасно подтвердить версию выбранного runtime.");
            }

            BackendCompatibilityStatus compatibility = _adapter.ClassifyVersion(version);
            string token = CreateConfirmationToken(
                _adapter.Profile.Backend,
                executable,
                version,
                _adapter.Profile.DefaultEndpoint);
            BackendLifecycleTarget target = new(
                _adapter.Profile.Backend,
                executable,
                version,
                _adapter.Profile.DefaultEndpoint,
                compatibility,
                _adapter.GetCompatibilityLabel(compatibility),
                token);
            _confirmedToken = null;
            _snapshot = new BackendLifecycleSnapshot(
                BackendLifecycleState.TargetPendingConfirmation,
                target,
                null,
                CreateDefaults(),
                null,
                "Проверьте exact path, version и endpoint, затем подтвердите target.");
            return Success();
        }
        catch (Exception exception) when (IsExpectedLifecycleFailure(exception))
        {
            return Fail($"Runtime discovery не выполнен безопасно ({exception.GetType().Name}).");
        }
        finally
        {
            _gate.Release();
        }
    }

    public BackendLifecycleResult ConfirmTarget(string confirmationToken)
    {
        BackendLifecycleTarget? target = _snapshot.Target;
        if (target is null || !CryptographicEquals(target.ConfirmationToken, confirmationToken))
        {
            return Fail("Target не подтверждён: сведения изменились или confirmation token не совпал.");
        }

        if (target.Compatibility is BackendCompatibilityStatus.Unsupported or BackendCompatibilityStatus.ObservationOnly)
        {
            return Fail("Для этого runtime разрешено только наблюдение; lifecycle недоступен.");
        }

        _confirmedToken = target.ConfirmationToken;
        _snapshot = _snapshot with
        {
            State = BackendLifecycleState.Stopped,
            Message = "Exact target подтверждён. Можно запускать backend.",
        };
        return Success();
    }

    public BackendLifecycleResult SetParameter(string key, string? value)
    {
        if (_snapshot.Target is null || !IsTargetConfirmed())
        {
            return Fail("Сначала подтвердите exact target.");
        }

        BackendParameterDefinition? definition = _adapter.Profile.Parameters.SingleOrDefault(
            item => item.Key.Equals(key, StringComparison.Ordinal));
        if (definition is null)
        {
            return Fail("Параметр не входит в allowlist выбранного backend.");
        }

        try
        {
            string? normalized = definition.Normalize(value);
            Dictionary<string, string> updated = new(_snapshot.Parameters, StringComparer.Ordinal);
            if (normalized is null)
            {
                updated.Remove(key);
            }
            else
            {
                updated[key] = normalized;
            }

            BackendLifecycleTarget target = _snapshot.Target;
            BackendLifecycleState state = _snapshot.State;
            if (key.Equals("local-port", StringComparison.Ordinal) && normalized is string portValue)
            {
                if (_snapshot.State == BackendLifecycleState.Running)
                {
                    return Fail("Локальный порт нельзя менять у запущенного backend. Сначала остановите owned process.");
                }

                int port = int.Parse(portValue, NumberStyles.None, CultureInfo.InvariantCulture);
                UriBuilder endpoint = new(target.Endpoint) { Host = "127.0.0.1", Port = port };
                string token = CreateConfirmationToken(target.Backend, target.ExecutablePath, target.Version, endpoint.Uri);
                target = target with { Endpoint = endpoint.Uri, ConfirmationToken = token };
                _confirmedToken = null;
                state = BackendLifecycleState.TargetPendingConfirmation;
            }

            _snapshot = _snapshot with
            {
                State = state,
                Target = target,
                Parameters = new ReadOnlyDictionary<string, string>(updated),
                Model = key.Equals("model-id", StringComparison.Ordinal) ? normalized : _snapshot.Model,
                Message = key.Equals("local-port", StringComparison.Ordinal)
                    ? $"Параметр «{definition.DisplayName}» сохранён. Подтвердите обновлённый exact endpoint."
                    : $"Параметр «{definition.DisplayName}» сохранён. Он применится при следующей подходящей операции start/model load/restart.",
            };
            return Success();
        }
        catch (ArgumentException exception)
        {
            return Fail(exception.Message);
        }
    }

    public BackendLifecycleResult ResetParameters()
    {
        if (_snapshot.Target is null || !IsTargetConfirmed())
        {
            return Fail("Сначала подтвердите exact target.");
        }

        BackendLifecycleTarget target = _snapshot.Target;
        BackendLifecycleState state = _snapshot.State;
        int defaultPort = _adapter.Profile.DefaultEndpoint.Port;
        if (target.Endpoint.Port != defaultPort)
        {
            if (_snapshot.State == BackendLifecycleState.Running)
            {
                return Fail("Defaults меняют локальный порт. Сначала остановите owned backend.");
            }

            UriBuilder endpoint = new(target.Endpoint) { Host = "127.0.0.1", Port = defaultPort };
            target = target with
            {
                Endpoint = endpoint.Uri,
                ConfirmationToken = CreateConfirmationToken(
                    target.Backend,
                    target.ExecutablePath,
                    target.Version,
                    endpoint.Uri),
            };
            _confirmedToken = null;
            state = BackendLifecycleState.TargetPendingConfirmation;
        }

        _snapshot = _snapshot with
        {
            State = state,
            Target = target,
            Parameters = CreateDefaults(),
            Model = _adapter.Profile.Backend == BackendKind.LmStudio ? null : _snapshot.Model,
            Message = state == BackendLifecycleState.TargetPendingConfirmation
                ? "Параметры возвращены к native defaults. Подтвердите обновлённый exact endpoint."
                : "Параметры возвращены к native defaults backend.",
        };
        return Success();
    }

    public async ValueTask<BackendLifecycleResult> StartAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await StartCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<BackendLifecycleResult> StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            BackendLifecycleResult? gate = CheckDestructiveOperation("остановка");
            if (gate is not null)
            {
                return gate;
            }

            return await StopCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<BackendLifecycleResult> RestartAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            BackendLifecycleResult? gate = CheckDestructiveOperation("перезапуск");
            if (gate is not null)
            {
                return gate;
            }

            BackendLifecycleResult stopped = await StopCoreAsync(cancellationToken).ConfigureAwait(false);
            return stopped.Succeeded
                ? await StartCoreAsync(cancellationToken).ConfigureAwait(false)
                : stopped;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<BackendLifecycleResult> LoadModelAsync(
        string model,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            BackendLifecycleResult? gate = CheckDestructiveOperation("загрузка модели");
            if (gate is not null)
            {
                return gate;
            }

            if (string.IsNullOrWhiteSpace(model) || model.Length > 1024 || model.Any(char.IsControl))
            {
                return Fail("Укажите точный model ID или .gguf path.");
            }

            string normalizedModel = model.Trim();
            bool restartedForModel = false;
            if (_adapter.Profile.ModelLoadMode == BackendModelLoadMode.RestartProcess)
            {
                if (!Path.IsPathFullyQualified(normalizedModel) ||
                    !normalizedModel.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(normalizedModel))
                {
                    return Fail("Для llama.cpp выберите существующий .gguf по полному пути.");
                }

                BackendLifecycleResult stopped = await StopCoreAsync(cancellationToken).ConfigureAwait(false);
                if (!stopped.Succeeded)
                {
                    return stopped;
                }

                _snapshot = _snapshot with { Model = normalizedModel };
                BackendLifecycleResult started = await StartCoreAsync(cancellationToken).ConfigureAwait(false);
                if (!started.Succeeded)
                {
                    return started;
                }

                restartedForModel = true;
            }
            else
            {
                if (_snapshot.State != BackendLifecycleState.Running)
                {
                    return Fail("Сначала запустите подтверждённый backend.");
                }

                if (!await _adapter.LoadModelAsync(
                        _snapshot.Target!,
                        normalizedModel,
                        _snapshot.Parameters,
                        _runtime,
                        cancellationToken).ConfigureAwait(false))
                {
                    return Fail("Backend не подтвердил запуск model-load operation.");
                }
            }

            if (!await _adapter.ConfirmModelAsync(
                    _snapshot.Target!, normalizedModel, _runtime, cancellationToken).ConfigureAwait(false))
            {
                if (restartedForModel && _snapshot.OwnedProcess is BackendProcessIdentity unconfirmed)
                {
                    try
                    {
                        await _runtime.StopAsync(unconfirmed, null, cancellationToken).ConfigureAwait(false);
                        _snapshot = _snapshot with
                        {
                            State = BackendLifecycleState.Faulted,
                            OwnedProcess = null,
                            Message = "Exact model identity не подтверждён; созданный model process остановлен.",
                        };
                        return new BackendLifecycleResult(false, _snapshot);
                    }
                    catch (Exception cleanupException) when (IsExpectedLifecycleFailure(cleanupException))
                    {
                        return Fail("Exact model identity не подтверждён, а owned process требует ручной остановки.");
                    }
                }

                return Fail("Backend не подтвердил exact model identity после загрузки.");
            }

            _snapshot = _snapshot with
            {
                Model = normalizedModel,
                Message = $"Backend подтвердил загруженную модель: {normalizedModel}",
            };
            return Success();
        }
        catch (Exception exception) when (IsExpectedLifecycleFailure(exception))
        {
            return Fail($"Model load не выполнен безопасно ({exception.GetType().Name}).");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<BackendLifecycleSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_snapshot.State == BackendLifecycleState.Running &&
                _snapshot.OwnedProcess is BackendProcessIdentity process &&
                !await _runtime.IsSameProcessAliveAsync(process, cancellationToken).ConfigureAwait(false))
            {
                _snapshot = _snapshot with
                {
                    State = BackendLifecycleState.Crashed,
                    Message = "Owned backend process завершился. Автоперезапуск отключён; используйте «Перезапустить».",
                };
            }

            return _snapshot;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask<BackendLifecycleResult> StartCoreAsync(CancellationToken cancellationToken)
    {
        if (_snapshot.Target is null || !IsTargetConfirmed())
        {
            return Fail("Сначала подтвердите exact target.");
        }

        if (_snapshot.OwnedProcess is BackendProcessIdentity existing &&
            await _runtime.IsSameProcessAliveAsync(existing, cancellationToken).ConfigureAwait(false))
        {
            _snapshot = _snapshot with
            {
                State = BackendLifecycleState.Running,
                Message = "Backend уже запущен этим экземпляром Inspector.",
            };
            return Success();
        }

        BackendProcessIdentity? owner = await _runtime.ResolveEndpointOwnerAsync(
            _snapshot.Target.Endpoint,
            cancellationToken).ConfigureAwait(false);
        if (owner is not null)
        {
            return Fail($"Port {_snapshot.Target.Endpoint.Port} уже занят процессом PID {owner.ProcessId}; чужой процесс не изменён.");
        }

        _snapshot = _snapshot with { State = BackendLifecycleState.Starting, OwnedProcess = null, Message = "Запуск backend…" };
        BackendProcessIdentity? started = null;
        try
        {
            BackendProcessStartPlan plan = _adapter.CreateStartPlan(
                _snapshot.Target,
                _snapshot.Parameters,
                _snapshot.Model);
            started = await _runtime.StartAsync(plan, cancellationToken).ConfigureAwait(false);
            if (!await _adapter.ConfirmReadyAsync(
                    _snapshot.Target, _runtime, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("Readiness endpoint не подтвердил готовность backend.");
            }

            _snapshot = _snapshot with
            {
                State = BackendLifecycleState.Running,
                OwnedProcess = started,
                Message = $"Backend готов; owned PID {started.ProcessId}.",
            };
            return Success();
        }
        catch (Exception exception) when (IsExpectedLifecycleFailure(exception))
        {
            bool cleaned = started is null;
            if (started is not null)
            {
                try
                {
                    await _runtime.StopAsync(started, null, cancellationToken).ConfigureAwait(false);
                    cleaned = true;
                }
                catch (Exception cleanupException) when (IsExpectedLifecycleFailure(cleanupException))
                {
                    cleaned = false;
                }
            }

            _snapshot = _snapshot with
            {
                State = BackendLifecycleState.Faulted,
                OwnedProcess = cleaned ? null : started,
                Message = cleaned
                    ? $"Backend не запущен ({exception.GetType().Name}); созданный Inspector process очищен."
                    : $"Backend не готов ({exception.GetType().Name}); exact owned process не удалось остановить, доступна ручная остановка.",
            };
            return new BackendLifecycleResult(false, _snapshot);
        }
    }

    private async ValueTask<BackendLifecycleResult> StopCoreAsync(CancellationToken cancellationToken)
    {
        if (_snapshot.Target is null || !IsTargetConfirmed())
        {
            return Fail("Сначала подтвердите exact target.");
        }

        if (_snapshot.OwnedProcess is not BackendProcessIdentity process ||
            !await _runtime.IsSameProcessAliveAsync(process, cancellationToken).ConfigureAwait(false))
        {
            _snapshot = _snapshot with
            {
                State = BackendLifecycleState.Stopped,
                OwnedProcess = null,
                Message = "Owned backend process уже остановлен.",
            };
            return Success();
        }

        _snapshot = _snapshot with { State = BackendLifecycleState.Stopping, Message = "Остановка owned backend…" };
        try
        {
            await _runtime.StopAsync(
                process,
                _adapter.CreateOfficialStopPlan(_snapshot.Target),
                cancellationToken).ConfigureAwait(false);
            _snapshot = _snapshot with
            {
                State = BackendLifecycleState.Stopped,
                OwnedProcess = null,
                Message = "Owned backend остановлен.",
            };
            return Success();
        }
        catch (Exception exception) when (IsExpectedLifecycleFailure(exception))
        {
            return Fail($"Не удалось безопасно остановить exact owned process ({exception.GetType().Name}).");
        }
    }

    private BackendLifecycleResult? CheckDestructiveOperation(string operation)
    {
        int active = _liveRequests.GetSnapshot().ActiveRequests.Count;
        return active == 0
            ? null
            : Fail($"Операция «{operation}» заблокирована: активных Inspector requests — {active}.");
    }

    public void Dispose()
    {
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    private ReadOnlyDictionary<string, string> CreateDefaults()
    {
        Dictionary<string, string> defaults = _adapter.Profile.Parameters
            .Where(item => item.DefaultValue is not null)
            .ToDictionary(item => item.Key, item => item.DefaultValue!, StringComparer.Ordinal);
        return new ReadOnlyDictionary<string, string>(defaults);
    }

    private bool IsTargetConfirmed() =>
        _snapshot.Target is not null &&
        _confirmedToken is not null &&
        CryptographicEquals(_snapshot.Target.ConfirmationToken, _confirmedToken);

    private BackendLifecycleResult Success() => new(true, _snapshot);

    private BackendLifecycleResult Fail(string message)
    {
        _snapshot = _snapshot with { Message = message };
        return new BackendLifecycleResult(false, _snapshot);
    }

    private static string FirstNonEmptyLine(params string[] values) =>
        values.SelectMany(value => value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            .Select(value => value.Trim())
            .FirstOrDefault(value => value.Length > 0) ?? string.Empty;

    private static bool CryptographicEquals(string left, string right)
    {
        byte[] leftBytes = System.Text.Encoding.UTF8.GetBytes(left);
        byte[] rightBytes = System.Text.Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
            System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static string CreateConfirmationToken(
        BackendKind backend,
        string executable,
        string version,
        Uri endpoint) =>
        string.Join('|', backend, executable, version, endpoint);

    private static bool IsExpectedLifecycleFailure(Exception exception) => exception is
        ArgumentException or
        IOException or
        InvalidOperationException or
        UnauthorizedAccessException or
        System.ComponentModel.Win32Exception or
        HttpRequestException or
        TimeoutException;
}
