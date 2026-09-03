using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace LlmInspector.App;

public enum TrayCommand
{
    OpenApplication,
    OpenNotificationSettings,
    ToggleNotifications,
    Exit,
}

public interface ITrayHost : IDesktopNotificationPublisher, IDisposable
{
    bool IsAvailable { get; }
}

public sealed class UnavailableTrayHost : ITrayHost
{
    public bool IsAvailable => false;

    public void Publish(DesktopNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
    }

    public void Dispose()
    {
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsAutostartRegistration : IAutostartRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LLM Inspector";
    private readonly string _command;

    public WindowsAutostartRegistration(string executablePath)
    {
        _command = CreateCommand(executablePath);
    }

    public bool IsEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string value &&
               string.Equals(value, _command, StringComparison.Ordinal);
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true) ??
                throw new InvalidOperationException("The current-user Windows autostart registry key is unavailable.");
            key.SetValue(ValueName, _command, RegistryValueKind.String);
        }
        else
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    public static string CreateCommand(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException("An executable path is required.", nameof(executablePath));
        }

        string path = Path.GetFullPath(executablePath);
        if (path.IndexOfAny(['"', '\r', '\n']) >= 0)
        {
            throw new ArgumentException("The executable path contains unsupported characters.", nameof(executablePath));
        }

        return $"\"{path}\" --background";
    }
}

public sealed class UnavailableAutostartRegistration : IAutostartRegistration
{
    public bool IsEnabled() => false;

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            throw new PlatformNotSupportedException("Windows autostart is available only on Windows.");
        }
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsTrayHost : ITrayHost
{
    private const uint WindowMessageTray = 0x8001;
    private const uint WindowMessagePublish = 0x8002;
    private const uint WindowMessageClose = 0x0010;
    private const uint WindowMessageDestroy = 0x0002;
    private const uint WindowMessageLeftButtonUp = 0x0202;
    private const uint WindowMessageRightButtonUp = 0x0205;
    private const uint WindowMessageContextMenu = 0x007B;
    private const uint NotifyIconSelect = 0x0400;
    private const uint NotifyIconAdd = 0x00000000;
    private const uint NotifyIconModify = 0x00000001;
    private const uint NotifyIconDelete = 0x00000002;
    private const uint NotifyIconSetVersion = 0x00000004;
    private const uint NotifyIconMessage = 0x00000001;
    private const uint NotifyIconIcon = 0x00000002;
    private const uint NotifyIconTip = 0x00000004;
    private const uint NotifyIconInfo = 0x00000010;
    private const uint NotifyInfoInfo = 0x00000001;
    private const uint NotifyInfoNoSound = 0x00000010;
    private const uint NotifyIconVersion4 = 4;
    private const uint MenuString = 0x00000000;
    private const uint MenuSeparator = 0x00000800;
    private const uint TrackReturnCommand = 0x0100;
    private const uint TrackRightButton = 0x0002;
    private const uint SystemApplicationIcon = 32512;
    private const uint IconId = 1;
    private const int OpenCommandId = 1001;
    private const int SettingsCommandId = 1002;
    private const int ToggleCommandId = 1003;
    private const int ExitCommandId = 1004;
    private static readonly IntPtr MessageOnlyWindow = new(-3);

    private readonly Action<TrayCommand> _command;
    private readonly Func<bool> _notificationsPaused;
    private readonly ConcurrentQueue<DesktopNotification> _notifications = new();
    private readonly ManualResetEventSlim _started = new(initialState: false);
    private readonly Thread _thread;
    private readonly WindowProcedure _windowProcedure;
    private Exception? _startupFailure;
    private IntPtr _windowHandle;
    private IntPtr _iconHandle;
    private int _disposed;

    public WindowsTrayHost(Action<TrayCommand> command, Func<bool> notificationsPaused)
    {
        _command = command ?? throw new ArgumentNullException(nameof(command));
        _notificationsPaused = notificationsPaused ?? throw new ArgumentNullException(nameof(notificationsPaused));
        _windowProcedure = WindowProc;
        _thread = new Thread(RunMessageLoop)
        {
            IsBackground = true,
            Name = "LLM Inspector Windows tray",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        if (!_started.Wait(TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException("Windows tray initialization timed out.");
        }

        if (_startupFailure is not null)
        {
            throw new InvalidOperationException("Windows tray initialization failed.", _startupFailure);
        }
    }

    public bool IsAvailable => _windowHandle != IntPtr.Zero && Volatile.Read(ref _disposed) == 0;

    public void Publish(DesktopNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (!IsAvailable)
        {
            return;
        }

        _notifications.Enqueue(notification);
        _ = PostMessage(_windowHandle, WindowMessagePublish, UIntPtr.Zero, IntPtr.Zero);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        IntPtr window = _windowHandle;
        if (window != IntPtr.Zero)
        {
            _ = PostMessage(window, WindowMessageClose, UIntPtr.Zero, IntPtr.Zero);
            _ = _thread.Join(TimeSpan.FromSeconds(5));
        }

        _started.Dispose();
    }

    private void RunMessageLoop()
    {
        try
        {
            string className = $"LlmInspector.Tray.{Environment.ProcessId}";
            IntPtr instance = GetModuleHandle(null);
            WindowClass windowClass = new()
            {
                WindowProcedure = _windowProcedure,
                Instance = instance,
                ClassName = className,
            };
            if (RegisterClass(ref windowClass) == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            _windowHandle = CreateWindowEx(
                0,
                className,
                "LLM Inspector tray",
                0,
                0,
                0,
                0,
                0,
                MessageOnlyWindow,
                IntPtr.Zero,
                instance,
                IntPtr.Zero);
            if (_windowHandle == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            _iconHandle = LoadIcon(IntPtr.Zero, new IntPtr(SystemApplicationIcon));
            if (_iconHandle == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            NotifyIconData data = CreateNotifyIconData();
            data.Flags = NotifyIconMessage | NotifyIconIcon | NotifyIconTip;
            data.CallbackMessage = WindowMessageTray;
            data.Icon = _iconHandle;
            data.Tip = "LLM Inspector — monitoring is active";
            if (!ShellNotifyIcon(NotifyIconAdd, ref data))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            data.TimeoutOrVersion = NotifyIconVersion4;
            _ = ShellNotifyIcon(NotifyIconSetVersion, ref data);
            _started.Set();

            while (GetMessage(out Message message, IntPtr.Zero, 0, 0) > 0)
            {
                _ = TranslateMessage(ref message);
                _ = DispatchMessage(ref message);
            }
        }
        catch (Exception exception)
        {
            _startupFailure = exception;
            _started.Set();
        }
        finally
        {
            RemoveIcon();
            _windowHandle = IntPtr.Zero;
        }
    }

    private IntPtr WindowProc(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case WindowMessageTray:
                HandleTrayMessage(unchecked((uint)lParam.ToInt64()) & 0xffff);
                return IntPtr.Zero;
            case WindowMessagePublish:
                while (_notifications.TryDequeue(out DesktopNotification? notification))
                {
                    PublishNative(notification);
                }

                return IntPtr.Zero;
            case WindowMessageClose:
                _ = DestroyWindow(window);
                return IntPtr.Zero;
            case WindowMessageDestroy:
                RemoveIcon();
                PostQuitMessage(0);
                return IntPtr.Zero;
            default:
                return DefWindowProc(window, message, wParam, lParam);
        }
    }

    private void HandleTrayMessage(uint eventMessage)
    {
        if (eventMessage is WindowMessageLeftButtonUp or NotifyIconSelect)
        {
            _command(TrayCommand.OpenApplication);
        }
        else if (eventMessage is WindowMessageRightButtonUp or WindowMessageContextMenu)
        {
            ShowMenu();
        }
    }

    private void ShowMenu()
    {
        IntPtr menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            _ = AppendMenu(menu, MenuString, OpenCommandId, "Open LLM Inspector");
            _ = AppendMenu(menu, MenuString, SettingsCommandId, "Notification settings");
            _ = AppendMenu(
                menu,
                MenuString,
                ToggleCommandId,
                _notificationsPaused() ? "Resume notifications" : "Pause notifications");
            _ = AppendMenu(menu, MenuSeparator, 0, null);
            _ = AppendMenu(menu, MenuString, ExitCommandId, "Exit");
            _ = SetForegroundWindow(_windowHandle);
            _ = GetCursorPos(out Point cursor);
            int selected = TrackPopupMenu(
                menu,
                TrackReturnCommand | TrackRightButton,
                cursor.X,
                cursor.Y,
                0,
                _windowHandle,
                IntPtr.Zero);
            TrayCommand? command = selected switch
            {
                OpenCommandId => TrayCommand.OpenApplication,
                SettingsCommandId => TrayCommand.OpenNotificationSettings,
                ToggleCommandId => TrayCommand.ToggleNotifications,
                ExitCommandId => TrayCommand.Exit,
                _ => null,
            };
            if (command is TrayCommand value)
            {
                _command(value);
            }
        }
        finally
        {
            _ = DestroyMenu(menu);
        }
    }

    private void PublishNative(DesktopNotification notification)
    {
        NotifyIconData data = CreateNotifyIconData();
        data.Flags = NotifyIconInfo;
        data.InfoTitle = notification.Title;
        data.Info = notification.Body;
        data.InfoFlags = NotifyInfoInfo | (notification.Silent ? NotifyInfoNoSound : 0);
        data.TimeoutOrVersion = 10_000;
        _ = ShellNotifyIcon(NotifyIconModify, ref data);
    }

    private NotifyIconData CreateNotifyIconData() => new()
    {
        Size = Marshal.SizeOf<NotifyIconData>(),
        Window = _windowHandle,
        Id = IconId,
        Tip = string.Empty,
        Info = string.Empty,
        InfoTitle = string.Empty,
    };

    private void RemoveIcon()
    {
        if (_windowHandle == IntPtr.Zero)
        {
            return;
        }

        NotifyIconData data = CreateNotifyIconData();
        _ = ShellNotifyIcon(NotifyIconDelete, ref data);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Style;
        public WindowProcedure WindowProcedure;
        public int ClassExtraBytes;
        public int WindowExtraBytes;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string ClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr Window;
        public uint Value;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Cursor;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int Size;
        public IntPtr Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr Icon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;

        public uint State;
        public uint StateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;

        public uint TimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;

        public uint InfoFlags;
        public Guid ItemGuid;
        public IntPtr BalloonIcon;
    }

    private delegate IntPtr WindowProcedure(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClass(ref WindowClass windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Message message, IntPtr window, uint minimum, uint maximum);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref Message message);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Message message);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr iconName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, int item, string? text);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int TrackPopupMenu(
        IntPtr menu,
        uint flags,
        int x,
        int y,
        int reserved,
        IntPtr window,
        IntPtr rectangle);
}
