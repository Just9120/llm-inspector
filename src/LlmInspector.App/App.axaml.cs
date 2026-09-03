using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace LlmInspector.App;

public partial class App : Avalonia.Application, IDisposable
{
    private ITrayHost? _tray;
    private BackgroundNotificationMonitor? _notificationMonitor;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (this.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            NotificationDispatcher? notificationDispatcher = null;
            TrayCommandRouter? trayCommands = null;
            IAutostartRegistration autostart = CreateAutostartRegistration();
            BackgroundSettingsService settings = new(
                new JsonBackgroundSettingsStore(Program.GetDefaultSettingsPath()),
                autostart);
            try
            {
                settings.InitializeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception exception) when (exception is
                IOException or
                UnauthorizedAccessException or
                System.Security.SecurityException or
                InvalidDataException or
                InvalidOperationException)
            {
                // Invalid settings never stop the local gateway; defaults remain active and UI save can recover.
                try
                {
                    settings.InitializeFromAutostartState();
                }
                catch (Exception fallbackException) when (fallbackException is
                    UnauthorizedAccessException or
                    System.Security.SecurityException or
                    InvalidOperationException)
                {
                    // The settings surface remains disabled-by-default if the registry itself is unavailable.
                }
            }

            Program.ResourceMonitor.ApplyProfile(settings.Current.Monitoring.Resolve());

            _tray = CreateTrayHost(
                command => Dispatcher.UIThread.Post(() => trayCommands?.Execute(command)),
                () => notificationDispatcher?.IsPaused ?? false);
            BackgroundLifetimeController createdLifetime = new(_tray.IsAvailable);
            NotificationDispatcher createdDispatcher = new(_tray);
            notificationDispatcher = createdDispatcher;
            MainWindow createdWindow = new(
                Program.RuntimeStatus,
                Program.LiveStateTracker,
                Program.ObservationStore,
                Program.ResourceMonitor,
                Program.HistoryStore,
                Program.HistoryState,
                settings,
                createdLifetime,
                Program.ResourceMonitor.ApplyProfile);
            desktop.ShutdownMode = _tray.IsAvailable
                ? ShutdownMode.OnExplicitShutdown
                : ShutdownMode.OnLastWindowClose;
            desktop.MainWindow = createdWindow;

            if (Program.LaunchConfiguration.StartInBackground && _tray.IsAvailable)
            {
                createdWindow.Opened += HideInitialWindow;
            }

            _notificationMonitor = new BackgroundNotificationMonitor(
                Program.NotificationObservations,
                settings,
                new NotificationRuleEngine(),
                createdDispatcher);
            trayCommands = new TrayCommandRouter(
                createdWindow.ShowFromTray,
                () => _ = createdDispatcher.TogglePaused(),
                () =>
                {
                    createdLifetime.RequestExit();
                    desktop.Shutdown();
                });
            _notificationMonitor.Start();
            desktop.Exit += (_, _) =>
            {
                createdLifetime.RequestExit();
                Dispose();
            };

            void HideInitialWindow(object? sender, EventArgs args)
            {
                createdWindow.Opened -= HideInitialWindow;
                createdWindow.HideToBackground();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    public void Dispose()
    {
        _notificationMonitor?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _notificationMonitor = null;
        _tray?.Dispose();
        _tray = null;
        GC.SuppressFinalize(this);
    }

    private static IAutostartRegistration CreateAutostartRegistration()
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(Environment.ProcessPath))
        {
            return new UnavailableAutostartRegistration();
        }

        return new WindowsAutostartRegistration(Environment.ProcessPath);
    }

    private static ITrayHost CreateTrayHost(
        Action<TrayCommand> command,
        Func<bool> notificationsPaused)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new UnavailableTrayHost();
        }

        try
        {
            return new WindowsTrayHost(command, notificationsPaused);
        }
        catch (Exception exception) when (exception is
            Win32Exception or
            InvalidOperationException or
            TimeoutException)
        {
            return new UnavailableTrayHost();
        }
    }
}
