// Added for the macOS port on 2026-11-14; see NOTICE.
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ASPAssistant.Core;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Mac.Views;

public partial class SettingsPage : UserControl
{
    private readonly AppState _state;

    /// <summary>Designer / runtime XAML loader entry point.</summary>
    public SettingsPage()
    {
        InitializeComponent();
        _state = null!;
    }

    public SettingsPage(AppState state)
    {
        _state = state;
        InitializeComponent();
        _loaded = true;
        LoadFromSettings();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        this.FindControl<TextBlock>("VersionText")!.Text =
            $"鸿雪·卫戍助手 macOS 版 {version?.ToString(3)} · MaaFramework 5.8";
        this.FindControl<TextBlock>("PathsText")!.Text =
            $"数据：{state.DataDir}\n设置：{state.SettingsDir}";
    }

    private void LoadFromSettings()
    {
        var settings = _state.Connection.Settings;
        var sourceIndex = settings.Source switch
        {
            GameCaptureSource.PlayCover => 0,
            GameCaptureSource.Adb => 1,
            _ => 2
        };
        this.FindControl<ComboBox>("SourceBox")!.SelectedIndex = sourceIndex;
        this.FindControl<TextBox>("PlayCoverAddressBox")!.Text = settings.PlayCoverAddress;
        this.FindControl<TextBox>("PlayCoverBundleBox")!.Text = settings.PlayCoverBundleId;
        this.FindControl<TextBox>("AdbPathBox")!.Text = settings.AdbPath;
        this.FindControl<TextBox>("AdbSerialBox")!.Text = settings.AdbSerial;
        this.FindControl<CheckBox>("DarkThemeCheck")!.IsChecked = _state.IsDarkTheme;
        this.FindControl<CheckBox>("AutoThemeCheck")!.IsChecked = _state.Scanner.AutomaticThemeDetection;
        this.FindControl<ComboBox>("ThemeBox")!.SelectedIndex = _state.ShopTheme switch
        {
            GameShopTheme.Ursus => 1,
            GameShopTheme.Closure => 2,
            _ => 0
        };
        UpdatePanels();
    }

    private bool _loaded;

    private void UpdatePanels()
    {
        if (!_loaded) return; // selection changes during XAML load run before the fields exist
        var index = this.FindControl<ComboBox>("SourceBox")?.SelectedIndex ?? 0;
        if (this.FindControl<StackPanel>("PlayCoverPanel") is { } playCover) playCover.IsVisible = index == 0;
        if (this.FindControl<StackPanel>("AdbPanel") is { } adb) adb.IsVisible = index == 1;
    }

    private void OnSourceChanged(object? sender, SelectionChangedEventArgs e) => UpdatePanels();

    private async void OnAutoThemeToggled(object? sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        var enabled = this.FindControl<CheckBox>("AutoThemeCheck")?.IsChecked == true;
        _state.Scanner.AutomaticThemeDetection = enabled;
        try { await _state.Settings.SaveAutomaticGameThemeAsync(enabled); }
        catch (Exception ex) { AppLogger.Warn("Mac", $"Save automatic theme failed: {ex.Message}"); }
    }

    private void OnThemeToggled(object? sender, RoutedEventArgs e)
    {
        var dark = this.FindControl<CheckBox>("DarkThemeCheck")!.IsChecked == true;
        _state.IsDarkTheme = dark;
        App.ApplyTheme(dark);
    }

    private async void OnShopThemeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_loaded) return;
        var theme = this.FindControl<ComboBox>("ThemeBox")!.SelectedIndex switch
        {
            1 => GameShopTheme.Ursus,
            2 => GameShopTheme.Closure,
            _ => GameShopTheme.Terminal
        };
        await _state.SetShopThemeAsync(theme);
    }

    private void OnOverlayToggled(object? sender, RoutedEventArgs e)
    {
        var show = this.FindControl<CheckBox>("OverlayCheck")?.IsChecked == true;
        if (show)
        {
            if (_state.Overlay is null)
            {
                _state.Overlay = new OverlayWindow();
                _state.Overlay.SetTrackedPredicate(_state.TrackingVm.IsTracked);
                _state.Overlay.UpdateItems(_state.LastShopItems);
                _state.Overlay.Closed += (_, _) => _state.Overlay = null;
                _state.Overlay.Show();
            }
        }
        else
        {
            _state.Overlay?.Close();
            _state.Overlay = null;
        }
    }

    private GameConnectionSettings BuildSettings() => new()
    {
        Source = this.FindControl<ComboBox>("SourceBox")!.SelectedIndex switch
        {
            0 => GameCaptureSource.PlayCover,
            1 => GameCaptureSource.Adb,
            _ => GameCaptureSource.Pc
        },
        PlayCoverAddress = this.FindControl<TextBox>("PlayCoverAddressBox")!.Text?.Trim() ?? "",
        PlayCoverBundleId = this.FindControl<TextBox>("PlayCoverBundleBox")!.Text?.Trim() ?? "",
        AdbPath = this.FindControl<TextBox>("AdbPathBox")!.Text?.Trim() ?? "",
        AdbSerial = this.FindControl<TextBox>("AdbSerialBox")!.Text?.Trim() ?? "",
        WindowTitle = _state.Connection.Settings.WindowTitle,
    };

    private async void OnSaveConnection(object? sender, RoutedEventArgs e)
    {
        var status = this.FindControl<TextBlock>("ConnectionStatus")!;
        try
        {
            await _state.ApplyConnectionAsync(BuildSettings());
            status.Text = $"已保存 · {_state.Connection.Status}";
        }
        catch (Exception ex)
        {
            status.Text = $"保存失败：{ex.Message}";
        }
    }

    private void OnTestConnection(object? sender, RoutedEventArgs e)
    {
        var status = this.FindControl<TextBlock>("ConnectionStatus")!;
        status.Text = "正在测试…";
        var settings = BuildSettings();
        Task.Run(async () =>
        {
            await _state.ApplyConnectionAsync(settings);
            var (ok, message) = _state.Connection.TestDeviceConnection();
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                status.Text = ok ? $"连接成功 · {message}" : $"连接失败 · {message}");
        });
    }
}
