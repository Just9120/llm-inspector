using Avalonia;

namespace LlmInspector.App;

public static class Program
{
    private const string SmokeTestArgument = "--smoke-test";

    [STAThread]
    public static int Main(string[] args)
    {
        if (args is [SmokeTestArgument])
        {
            BuildAvaloniaApp().SetupWithoutStarting();
            return 0;
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
