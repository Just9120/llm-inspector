using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using LlmInspector.Application;
using LlmInspector.Domain;

namespace LlmInspector.Resources.Windows;

public sealed record ProcessResourceSnapshot(
    TimeSpan TotalProcessorTime,
    ulong WorkingSetBytes,
    ulong ReadTransferBytes,
    ulong WriteTransferBytes);

public sealed record GpuResourceSnapshot(
    TechnicalIdentifier DeviceId,
    TechnicalIdentifier? DriverVersion,
    decimal? UtilizationPercent,
    decimal? VramUsedMebibytes,
    decimal? VramTotalMebibytes,
    decimal? TemperatureCelsius,
    decimal? PowerWatts);

public sealed record WindowsResourceSnapshot(
    DateTimeOffset CapturedAt,
    ulong IdleTimeTicks,
    ulong KernelTimeTicks,
    ulong UserTimeTicks,
    ulong TotalPhysicalMemoryBytes,
    ulong AvailablePhysicalMemoryBytes,
    ProcessResourceSnapshot? Process,
    GpuResourceSnapshot? Gpu);

public interface IWindowsResourceProbe
{
    ValueTask<WindowsResourceSnapshot> CaptureAsync(
        TechnicalProcessAssociation? process,
        CancellationToken cancellationToken);
}

public interface IBackendProcessResolver
{
    TechnicalProcessAssociation? Resolve(Uri backendBaseAddress);
}

public sealed class WindowsBackendProcessResolver : IBackendProcessResolver
{
    private const string SourceVersion = "windows-ip-helper-listener-owner-v1";

    public TechnicalProcessAssociation? Resolve(Uri backendBaseAddress)
    {
        ArgumentNullException.ThrowIfNull(backendBaseAddress);
        if (!OperatingSystem.IsWindows() ||
            !IPAddress.TryParse(backendBaseAddress.Host.Trim('[', ']'), out IPAddress? address) ||
            !IPAddress.IsLoopback(address))
        {
            return null;
        }

        List<int> owners;
        try
        {
            owners = TcpListenerOwnerReader.GetOwners(backendBaseAddress.Port, address.AddressFamily);
        }
        catch (Exception exception) when (exception is InvalidOperationException or OutOfMemoryException)
        {
            return null;
        }

        if (owners.Distinct().Take(2).ToArray() is not [int processId])
        {
            return null;
        }

        try
        {
            using Process process = Process.GetProcessById(processId);
            TechnicalIdentifier? imageName = TechnicalIdentifier.FromBackend(process.ProcessName);
            return imageName is null
                ? null
                : new TechnicalProcessAssociation(
                    processId,
                    process.StartTime.ToUniversalTime(),
                    imageName,
                    SourceVersion);
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            InvalidOperationException or
            NotSupportedException or
            System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static class TcpListenerOwnerReader
    {
        private const int AfInet = 2;
        private const int AfInet6 = 23;
        private const int TcpTableOwnerPidListener = 3;
        private const uint ErrorInsufficientBuffer = 122;

        public static List<int> GetOwners(int port, System.Net.Sockets.AddressFamily family)
        {
            int addressFamily = family switch
            {
                System.Net.Sockets.AddressFamily.InterNetwork => AfInet,
                System.Net.Sockets.AddressFamily.InterNetworkV6 => AfInet6,
                _ => throw new InvalidOperationException("Only IPv4/IPv6 listener ownership is supported."),
            };
            int size = 0;
            uint first = GetExtendedTcpTable(
                IntPtr.Zero,
                ref size,
                order: false,
                addressFamily,
                TcpTableOwnerPidListener,
                reserved: 0);
            if (first != ErrorInsufficientBuffer || size < sizeof(int))
            {
                throw new InvalidOperationException($"Unable to size the TCP owner table ({first}).");
            }

            IntPtr table = Marshal.AllocHGlobal(size);
            try
            {
                uint result = GetExtendedTcpTable(
                    table,
                    ref size,
                    order: false,
                    addressFamily,
                    TcpTableOwnerPidListener,
                    reserved: 0);
                if (result != 0)
                {
                    throw new InvalidOperationException($"Unable to read the TCP owner table ({result}).");
                }

                int count = Marshal.ReadInt32(table);
                int rowSize = addressFamily == AfInet ? 24 : 56;
                int portOffset = addressFamily == AfInet ? 8 : 20;
                int processOffset = addressFamily == AfInet ? 20 : 52;
                List<int> owners = [];
                for (int index = 0; index < count; index++)
                {
                    IntPtr row = IntPtr.Add(table, sizeof(int) + (index * rowSize));
                    int encodedPort = Marshal.ReadInt32(row, portOffset);
                    if (DecodeNetworkPort(encodedPort) == port)
                    {
                        int processId = Marshal.ReadInt32(row, processOffset);
                        if (processId > 0)
                        {
                            owners.Add(processId);
                        }
                    }
                }

                return owners;
            }
            finally
            {
                Marshal.FreeHGlobal(table);
            }
        }

        private static int DecodeNetworkPort(int value)
        {
            int low = value & 0xff;
            int high = (value >> 8) & 0xff;
            return (low << 8) | high;
        }

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(
            IntPtr tcpTable,
            ref int size,
            [MarshalAs(UnmanagedType.Bool)] bool order,
            int addressFamily,
            int tableClass,
            uint reserved);
    }
}

public sealed class WindowsResourceProbe : IWindowsResourceProbe
{
    private readonly NvidiaSmiGpuProbe _gpuProbe;

    public WindowsResourceProbe(NvidiaSmiGpuProbe? gpuProbe = null)
    {
        _gpuProbe = gpuProbe ?? new NvidiaSmiGpuProbe();
    }

    public async ValueTask<WindowsResourceSnapshot> CaptureAsync(
        TechnicalProcessAssociation? process,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows resource APIs require Windows.");
        }

        if (!GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user))
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        MemoryStatus memory = MemoryStatus.Create();
        if (!GlobalMemoryStatusEx(ref memory))
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        ProcessResourceSnapshot? processSnapshot = process is null ? null : CaptureProcess(process);
        GpuResourceSnapshot? gpu = await _gpuProbe.CaptureAsync(cancellationToken).ConfigureAwait(false);
        return new WindowsResourceSnapshot(
            DateTimeOffset.UtcNow,
            idle.Value,
            kernel.Value,
            user.Value,
            memory.TotalPhysical,
            memory.AvailablePhysical,
            processSnapshot,
            gpu);
    }

    private static ProcessResourceSnapshot? CaptureProcess(TechnicalProcessAssociation association)
    {
        try
        {
            using Process process = Process.GetProcessById(association.ProcessId);
            if (process.StartTime.ToUniversalTime() != association.ProcessStartedAt.UtcDateTime ||
                !GetProcessIoCounters(process.Handle, out IoCounters counters))
            {
                return null;
            }

            return new ProcessResourceSnapshot(
                process.TotalProcessorTime,
                checked((ulong)process.WorkingSet64),
                counters.ReadTransferCount,
                counters.WriteTransferCount);
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            InvalidOperationException or
            NotSupportedException or
            OverflowException or
            System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct FileTime
    {
        private readonly uint _low;
        private readonly uint _high;

        public ulong Value => ((ulong)_high << 32) | _low;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;

        public static MemoryStatus Create() => new() { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus buffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessIoCounters(IntPtr process, out IoCounters counters);
}

public sealed class NvidiaSmiGpuProbe
{
    private const string Query = "index,uuid,driver_version,utilization.gpu,memory.used,memory.total,temperature.gpu,power.draw";
    private readonly string? _executablePath;
    private readonly TimeSpan _timeout;

    public NvidiaSmiGpuProbe(string? executablePath = null, TimeSpan? timeout = null)
    {
        _executablePath = executablePath ?? FindExecutable();
        _timeout = timeout ?? TimeSpan.FromSeconds(1);
    }

    public async ValueTask<GpuResourceSnapshot?> CaptureAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows() || _executablePath is null)
        {
            return null;
        }

        ProcessStartInfo start = new()
        {
            FileName = _executablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add($"--query-gpu={Query}");
        start.ArgumentList.Add("--format=csv,noheader,nounits");

        using Process process = new() { StartInfo = start };
        try
        {
            if (!process.Start())
            {
                return null;
            }

            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_timeout);
            string output = await process.StandardOutput.ReadToEndAsync(timeout.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return process.ExitCode == 0 ? ParseCsv(output) : null;
        }
        catch (Exception exception) when (exception is
            InvalidOperationException or
            IOException or
            System.ComponentModel.Win32Exception or
            OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception killException) when (killException is
                InvalidOperationException or
                System.ComponentModel.Win32Exception)
            {
            }

            return null;
        }
    }

    public static GpuResourceSnapshot? ParseCsv(string output)
    {
        string[]? primary = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(',', StringSplitOptions.TrimEntries))
            .Where(fields => fields.Length == 8 && int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out _))
            .OrderBy(fields => int.Parse(fields[0], CultureInfo.InvariantCulture))
            .FirstOrDefault();
        if (primary is null || TechnicalIdentifier.FromBackend(primary[1]) is not TechnicalIdentifier deviceId)
        {
            return null;
        }

        return new GpuResourceSnapshot(
            deviceId,
            ParseIdentifier(primary[2]),
            ParseMetric(primary[3]),
            ParseMetric(primary[4]),
            ParseMetric(primary[5]),
            ParseMetric(primary[6]),
            ParseMetric(primary[7]));
    }

    private static decimal? ParseMetric(string value) =>
        decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal parsed) && parsed >= 0
            ? parsed
            : null;

    private static TechnicalIdentifier? ParseIdentifier(string value) =>
        value.Equals("N/A", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("[Not Supported]", StringComparison.OrdinalIgnoreCase)
            ? null
            : TechnicalIdentifier.FromBackend(value);

    private static string? FindExecutable()
    {
        string systemPath = Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe");
        if (File.Exists(systemPath))
        {
            return systemPath;
        }

        string? programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            string vendorPath = Path.Combine(programFiles, "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe");
            if (File.Exists(vendorPath))
            {
                return vendorPath;
            }
        }

        return null;
    }
}
