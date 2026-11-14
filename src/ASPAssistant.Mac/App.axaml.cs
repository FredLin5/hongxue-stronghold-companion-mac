// Added for the macOS port on 2026-11-14; see NOTICE.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ASPAssistant.Core;

namespace ASPAssistant.Mac;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var dataDir = ResolveDataDirectory();
            var settingsDir = Environment.GetEnvironmentVariable("ASPASSISTANT_SETTINGS_DIR")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ASPAssistant");
            Directory.CreateDirectory(settingsDir);

            AppState state;
            try
            {
                // Run off the UI thread: CreateAsync awaits I/O and would deadlock on the
                // Avalonia synchronization context if the UI thread blocked on it.
                state = Task.Run(() => AppState.CreateAsync(dataDir, settingsDir)).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                AppLogger.Error("Mac", "Startup failed", ex);
                throw;
            }

            var window = new Views.MainWindow(state);
            desktop.MainWindow = window;
            desktop.Exit += (_, _) => state.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static string ResolveDataDirectory()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "data");
        if (File.Exists(Path.Combine(bundled, "operators.json"))) return bundled;

        // Development fallback: walk up from bin/ to the repository's data/ folder.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "data");
            if (File.Exists(Path.Combine(candidate, "operators.json"))) return candidate;
            dir = dir.Parent;
        }
        return bundled;
    }

    /// <summary>Swaps the palette dictionary; DynamicResource references update immediately.</summary>
    public static void ApplyTheme(bool dark)
    {
        if (Current?.Resources is not { } resources) return;
        var source = new Uri(dark
            ? "avares://HongxueStronghold/Themes/Dark.axaml"
            : "avares://HongxueStronghold/Themes/Light.axaml");
        var dictionary = (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri(source.ToString()));
        var merged = resources.MergedDictionaries;
        if (merged.Count > 0) merged[0] = dictionary;
        else merged.Add(dictionary);
    }
}
