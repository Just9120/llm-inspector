using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using LlmInspector.Application;

namespace LlmInspector.Resources.Windows;

public interface ICurrentUserDataProtector
{
    byte[] Protect(ReadOnlySpan<byte> plaintext);

    byte[] Unprotect(ReadOnlySpan<byte> protectedData);
}

[SupportedOSPlatform("windows")]
public sealed class WindowsDpapiCurrentUserProtector : ICurrentUserDataProtector
{
    private const uint CryptProtectUiForbidden = 0x1;

    public byte[] Protect(ReadOnlySpan<byte> plaintext)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows DPAPI is required for remote credential storage.");
        }

        return Transform(plaintext, protect: true);
    }

    public byte[] Unprotect(ReadOnlySpan<byte> protectedData)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows DPAPI is required for remote credential storage.");
        }

        return Transform(protectedData, protect: false);
    }

    private static byte[] Transform(ReadOnlySpan<byte> input, bool protect)
    {
        if (input.IsEmpty)
        {
            throw new ArgumentException("Secret material cannot be empty.", nameof(input));
        }

        IntPtr inputBuffer = Marshal.AllocHGlobal(input.Length);
        DataBlob inputBlob = new()
        {
            Length = input.Length,
            Data = inputBuffer,
        };
        DataBlob outputBlob = default;
        try
        {
            byte[] inputCopy = input.ToArray();
            try
            {
                Marshal.Copy(inputCopy, 0, inputBuffer, inputCopy.Length);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(inputCopy);
            }

            bool succeeded = protect
                ? CryptProtectData(
                    ref inputBlob,
                    null,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out outputBlob)
                : CryptUnprotectData(
                    ref inputBlob,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out outputBlob);
            if (!succeeded || outputBlob.Data == IntPtr.Zero || outputBlob.Length <= 0)
            {
                throw new CryptographicException(Marshal.GetLastPInvokeError());
            }

            byte[] output = new byte[outputBlob.Length];
            Marshal.Copy(outputBlob.Data, output, 0, output.Length);
            return output;
        }
        finally
        {
            if (inputBuffer != IntPtr.Zero)
            {
                byte[] zero = new byte[input.Length];
                Marshal.Copy(zero, 0, inputBuffer, input.Length);
                Marshal.FreeHGlobal(inputBuffer);
            }

            if (outputBlob.Data != IntPtr.Zero)
            {
                if (outputBlob.Length > 0)
                {
                    byte[] zero = new byte[outputBlob.Length];
                    Marshal.Copy(zero, 0, outputBlob.Data, outputBlob.Length);
                }

                _ = LocalFree(outputBlob.Data);
            }
        }
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string? dataDescription,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr prompt,
        uint flags,
        out DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        IntPtr dataDescription,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr prompt,
        uint flags,
        out DataBlob dataOut);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr memory);

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Length;

        public IntPtr Data;
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsRemoteAccessCredentialStore : IRemoteAccessCredentialStore
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly string _path;
    private readonly ICurrentUserDataProtector _protector;

    public WindowsRemoteAccessCredentialStore(string path, ICurrentUserDataProtector? protector = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Remote credential storage requires Windows.");
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A remote credential path is required.", nameof(path));
        }

        _path = Path.GetFullPath(path);
        _protector = protector ?? new WindowsDpapiCurrentUserProtector();
    }

    public async ValueTask<RemoteAccessStoredConfiguration> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return new RemoteAccessStoredConfiguration(false, null, null);
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
            RemoteAccessFileModel model = await JsonSerializer.DeserializeAsync<RemoteAccessFileModel>(
                stream,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false) ??
                throw new InvalidDataException("The remote access settings document is empty.");
            Validate(model);
            byte[]? token = null;
            if (model.ProtectedBearerToken is not null)
            {
                byte[] protectedToken = Convert.FromBase64String(model.ProtectedBearerToken);
                try
                {
                    token = _protector.Unprotect(protectedToken);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(protectedToken);
                }
            }

            return new RemoteAccessStoredConfiguration(model.Enabled, token, model.UpdatedAt);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The remote access settings document is invalid.", exception);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("The protected remote token encoding is invalid.", exception);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidDataException("The protected remote token cannot be decrypted for this Windows user.", exception);
        }
    }

    public async ValueTask SaveAsync(
        RemoteAccessStoredConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.Enabled && configuration.BearerToken is null)
        {
            throw new InvalidDataException("Enabled remote access requires a bearer token.");
        }

        if (configuration.BearerToken is not null &&
            configuration.BearerToken.Length != RemoteAccessManager.BearerTokenBytes)
        {
            throw new InvalidDataException("The remote bearer token has an invalid length.");
        }

        byte[]? protectedToken = configuration.BearerToken is null
            ? null
            : _protector.Protect(configuration.BearerToken);
        try
        {
            RemoteAccessFileModel model = new()
            {
                SchemaVersion = CurrentSchemaVersion,
                Enabled = configuration.Enabled,
                ProtectedBearerToken = protectedToken is null ? null : Convert.ToBase64String(protectedToken),
                UpdatedAt = configuration.UpdatedAt ?? DateTimeOffset.UtcNow,
            };
            string? directory = Path.GetDirectoryName(_path);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new IOException("The remote access settings directory is unavailable.");
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
                        model,
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
        finally
        {
            if (protectedToken is not null)
            {
                CryptographicOperations.ZeroMemory(protectedToken);
            }
        }
    }

    private static void Validate(RemoteAccessFileModel model)
    {
        if (model.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException("The remote access settings schema version is unsupported.");
        }

        if (model.Enabled && string.IsNullOrWhiteSpace(model.ProtectedBearerToken))
        {
            throw new InvalidDataException("Enabled remote access is missing its protected bearer token.");
        }

        if (model.ProtectedBearerToken is { Length: > 4096 })
        {
            throw new InvalidDataException("The protected remote bearer token is too large.");
        }
    }

    private sealed record RemoteAccessFileModel
    {
        public int SchemaVersion { get; init; }

        public bool Enabled { get; init; }

        public string? ProtectedBearerToken { get; init; }

        public DateTimeOffset UpdatedAt { get; init; }
    }
}
