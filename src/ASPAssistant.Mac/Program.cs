// Added for the macOS port on 2026-11-14; see NOTICE.
using Avalonia;

namespace ASPAssistant.Mac;

internal static class Program
{
    // Avalonia configuration: keep this apart from App so the desktop lifetime can be reused by tests.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
