using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using LlmInspector.Application;

namespace LlmInspector.Resources.Windows;

public sealed class WindowsBackendLifecycleRuntime : IBackendLifecycleRuntime, IDisposable
{
    private const int MaximumCapturedCharacters = 65_536;
    private static readonly TimeSpan ListenerTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan GracefulStopTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ForceStopTimeout = TimeSpan.FromSeconds(10);
    private readonly IBackendProcessResolver _resolver;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public WindowsBackendLifecycleRuntime(
        IBackendProcessResolver? resolver = null,
        HttpClient? httpClient = null)
    {
        _resolver = resolver ?? new WindowsBackendProcessResolver();
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(6) };
        _ownsHttpClient = httpClient is null;
    }

    public ValueTask<string?> ResolveExecutableAsync(
        IReadOnlyList<string> candidates,
        string? manualPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            return ValueTask.FromResult<string?>(null);
        }

        if (!string.IsNullOrWhiteSpace(manualPath))
        {
            return ValueTask.FromResult(ValidateExecutable(manualPath));
        }

        foreach (string candidate in candidates)
        {
            string? resolved = ResolveCandidate(candidate);
            if (resolved is not null)
            {
                return ValueTask.FromResult<string?>(resolved);
            }
        }

        return ValueTask.FromResult<string?>(null);
    }

    public async ValueTask<BackendCommandResult> ExecuteAsync(
        BackendCommandPlan command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        EnsureExactExecutable(command.ExecutablePath);
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(command.Timeout);
        using Process process = CreateProcess(command.ExecutablePath, command.Arguments, environment: null, redirectOutput: true);
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Process could not be started.");
            }

            Task<string> stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return new BackendCommandResult(
                process.ExitCode,
                Truncate(await stdout.ConfigureAwait(false)),
                Truncate(await stderr.ConfigureAwait(false)));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            KillStartedCommand(process);
            throw new TimeoutException("Backend command exceeded its bounded timeout.");
        }
        catch
        {
            KillStartedCommand(process);
            throw;
        }
    }

    public ValueTask<BackendProcessIdentity?> ResolveEndpointOwnerAsync(
        Uri endpoint,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TechnicalProcessAssociation? association = _resolver.Resolve(endpoint);
        return ValueTask.FromResult(association is null ? null : ResolveExactIdentity(association));
    }

    public async ValueTask<BackendProcessIdentity> StartAsync(
        BackendProcessStartPlan plan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        EnsureLoopback(plan.Endpoint);
        EnsureExactExecutable(plan.ExecutablePath);
        if (await ResolveEndpointOwnerAsync(plan.Endpoint, cancellationToken).ConfigureAwait(false) is not null)
        {
            throw new InvalidOperationException("Backend endpoint became occupied before start.");
        }

        if (plan.Ownership == BackendStartOwnership.DetachedListener)
        {
            BackendCommandResult result = await ExecuteAsync(
                new BackendCommandPlan(plan.ExecutablePath, plan.Arguments, ListenerTimeout),
                cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException("Official backend start command failed.");
            }

            return await WaitForListenerAsync(plan, expectedProcess: null, cancellationToken).ConfigureAwait(false);
        }

        Process process = CreateProcess(plan.ExecutablePath, plan.Arguments, plan.Environment, redirectOutput: false);
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Backend process could not be started.");
            }

            BackendProcessIdentity expected = new(
                process.Id,
                process.StartTime.ToUniversalTime(),
                Path.GetFullPath(plan.ExecutablePath));
            return await WaitForListenerAsync(plan, expected, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            KillStartedCommand(process);
            throw;
        }
        finally
        {
            process.Dispose();
        }
    }

    public ValueTask<bool> IsSameProcessAliveAsync(
        BackendProcessIdentity identity,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(IsSameProcessAlive(identity));
    }

    public async ValueTask StopAsync(
        BackendProcessIdentity identity,
        BackendCommandPlan? officialStop,
        CancellationToken cancellationToken)
    {
        if (!IsSameProcessAlive(identity))
        {
            return;
        }

        if (officialStop is not null)
        {
            BackendCommandResult result = await ExecuteAsync(officialStop, cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException("Official backend stop command failed.");
            }

            if (await WaitForExitAsync(identity, GracefulStopTimeout, cancellationToken).ConfigureAwait(false))
            {
                return;
            }
        }

        using Process process = GetExactProcess(identity);
        _ = process.CloseMainWindow();
        if (await WaitForExitAsync(identity, GracefulStopTimeout, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        if (!IsSameProcessAlive(identity))
        {
            return;
        }

        using Process exactProcess = GetExactProcess(identity);
        exactProcess.Kill(entireProcessTree: true);
        using CancellationTokenSource forceTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        forceTimeout.CancelAfter(ForceStopTimeout);
        try
        {
            await exactProcess.WaitForExitAsync(forceTimeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Exact backend process did not exit after bounded force stop.");
        }
    }

    public async ValueTask<string> SendHttpAsync(
        HttpMethod method,
        Uri address,
        string? jsonBody,
        CancellationToken cancellationToken)
    {
        EnsureLoopback(address);
        using HttpRequestMessage request = new(method, address);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (jsonBody is not null)
        {
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseContentRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return Truncate(body);
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private async ValueTask<BackendProcessIdentity> WaitForListenerAsync(
        BackendProcessStartPlan plan,
        BackendProcessIdentity? expectedProcess,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + ListenerTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BackendProcessIdentity? owner = await ResolveEndpointOwnerAsync(plan.Endpoint, cancellationToken).ConfigureAwait(false);
            if (owner is not null)
            {
                string image = Path.GetFileName(owner.ExecutablePath);
                bool allowedImage = plan.AllowedListenerImageNames.Any(
                    allowed => allowed.Equals(image, StringComparison.OrdinalIgnoreCase));
                if (!allowedImage || (expectedProcess is not null && !SameIdentity(owner, expectedProcess)))
                {
                    throw new InvalidOperationException("Listener owner does not match the Inspector-started backend identity.");
                }

                return owner;
            }

            if (expectedProcess is not null && !IsSameProcessAlive(expectedProcess))
            {
                throw new InvalidOperationException("Backend exited before opening its loopback endpoint.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException("Backend did not open its exact loopback endpoint in time.");
    }

    private static string? ResolveCandidate(string candidate)
    {
        if (Path.IsPathFullyQualified(candidate))
        {
            return ValidateExecutable(candidate);
        }

        if (candidate.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
        {
            return null;
        }

        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string? resolved = ValidateExecutable(Path.Combine(directory, candidate));
            if (resolved is not null)
            {
                return resolved;
            }
        }

        return null;
    }

    private static string? ValidateExecutable(string path)
    {
        if (!Path.IsPathFullyQualified(path) ||
            !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(path))
        {
            return null;
        }

        return Path.GetFullPath(path);
    }

    private static void EnsureExactExecutable(string path)
    {
        if (ValidateExecutable(path) is null)
        {
            throw new FileNotFoundException("Exact backend executable is unavailable.", path);
        }
    }

    private static Process CreateProcess(
        string executablePath,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment,
        bool redirectOutput)
    {
        ProcessStartInfo start = new()
        {
            FileName = executablePath,
            UseShellExecute = false,
            RedirectStandardOutput = redirectOutput,
            RedirectStandardError = redirectOutput,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(executablePath)!,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            foreach ((string key, string value) in environment)
            {
                start.Environment[key] = value;
            }
        }

        return new Process { StartInfo = start };
    }

    private static BackendProcessIdentity? ResolveExactIdentity(TechnicalProcessAssociation association)
    {
        try
        {
            using Process process = Process.GetProcessById(association.ProcessId);
            DateTimeOffset startedAt = process.StartTime.ToUniversalTime();
            string? executable = process.MainModule?.FileName;
            return startedAt == association.ProcessStartedAt && !string.IsNullOrWhiteSpace(executable)
                ? new BackendProcessIdentity(process.Id, startedAt, Path.GetFullPath(executable))
                : null;
        }
        catch (Exception exception) when (exception is
            ArgumentException or InvalidOperationException or NotSupportedException or
            System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static Process GetExactProcess(BackendProcessIdentity identity)
    {
        Process process = Process.GetProcessById(identity.ProcessId);
        try
        {
            string? executable = process.MainModule?.FileName;
            if (process.StartTime.ToUniversalTime() != identity.StartedAt ||
                string.IsNullOrWhiteSpace(executable) ||
                !Path.GetFullPath(executable).Equals(Path.GetFullPath(identity.ExecutablePath), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("PID was reused or executable identity changed.");
            }

            return process;
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    private static bool IsSameProcessAlive(BackendProcessIdentity identity)
    {
        try
        {
            using Process process = GetExactProcess(identity);
            return !process.HasExited;
        }
        catch (Exception exception) when (exception is
            ArgumentException or InvalidOperationException or NotSupportedException or
            System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static bool SameIdentity(BackendProcessIdentity left, BackendProcessIdentity right) =>
        left.ProcessId == right.ProcessId &&
        left.StartedAt == right.StartedAt &&
        Path.GetFullPath(left.ExecutablePath).Equals(
            Path.GetFullPath(right.ExecutablePath),
            StringComparison.OrdinalIgnoreCase);

    private static async ValueTask<bool> WaitForExitAsync(
        BackendProcessIdentity identity,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsSameProcessAlive(identity))
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
        }

        return !IsSameProcessAlive(identity);
    }

    private static void KillStartedCommand(Process process)
    {
        try
        {
            if (process.Id > 0 && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is
            InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    private static void EnsureLoopback(Uri address)
    {
        if (!address.IsAbsoluteUri ||
            !System.Net.IPAddress.TryParse(address.Host.Trim('[', ']'), out System.Net.IPAddress? ip) ||
            !System.Net.IPAddress.IsLoopback(ip) ||
            address.Scheme != Uri.UriSchemeHttp)
        {
            throw new InvalidOperationException("Lifecycle HTTP operations require a literal loopback HTTP endpoint.");
        }
    }

    private static string Truncate(string value) =>
        value.Length <= MaximumCapturedCharacters ? value : value[..MaximumCapturedCharacters];
}
