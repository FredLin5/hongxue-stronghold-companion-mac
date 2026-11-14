// Added for the macOS port on 2026-11-14; see NOTICE.
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Mac.Views;

public partial class MainWindow : Window
{
    private readonly AppState _state;
    private Dictionary<Button, Control> _pages = [];

    public MainWindow()
    {
        InitializeComponent();
        _state = null!;
    }

    public MainWindow(AppState state)
    {
        _state = state;
        InitializeComponent();

        _pages = new Dictionary<Button, Control>
        {
            [this.FindControl<Button>("TabOperators")!] = new OperatorsPage(state),
            [this.FindControl<Button>("TabEquipment")!] = new EquipmentPage(state),
            [this.FindControl<Button>("TabGameInfo")!] = new GameInfoPage(state),
            [this.FindControl<Button>("TabTracking")!] = new TrackingPage(state),
            [this.FindControl<Button>("TabLineups")!] = new LineupPage(state),
            [this.FindControl<Button>("TabSettings")!] = new SettingsPage(state),
        };

        // Development aid: ASPASSISTANT_MAC_START_PAGE=settings|tracking|… opens that tab.
        var startPage = Environment.GetEnvironmentVariable("ASPASSISTANT_MAC_START_PAGE")?.ToLowerInvariant() switch
        {
            "equipment" => "TabEquipment",
            "tracking" => "TabTracking",
            "lineups" => "TabLineups",
            "gameinfo" => "TabGameInfo",
            "settings" => "TabSettings",
            _ => "TabOperators"
        };
        ShowPage(this.FindControl<Button>(startPage)!);
        // Development aid: start the periodic shop scan right away (no click automation available).
        if (Environment.GetEnvironmentVariable("ASPASSISTANT_MAC_AUTOSCAN") == "1")
            state.Scanner.Start();
        // Development aid: run the info-page analysis once after startup (used for live checks).
        state.Scanner.ItemsRecognized += items => Dispatcher.UIThread.Post(() =>
        {
            if (state.Overlay is { } window) window.UpdateItems(items);
        });
        if (state.ShowOverlay || Environment.GetEnvironmentVariable("ASPASSISTANT_MAC_OVERLAY") == "1")
        {
            var overlay = new OverlayWindow();
            overlay.SetTrackedPredicate(state.TrackingVm.IsTracked);
            state.Overlay = overlay;
            overlay.Closed += (_, _) => state.Overlay = null;
            overlay.Show();
        }
        if (Environment.GetEnvironmentVariable("ASPASSISTANT_MAC_AUTOINFO") == "1")
            Dispatcher.UIThread.Post(async () => await state.IdentifyGameInfoAsync(), DispatcherPriority.Background);
        _state.Scanner.Scanned += _ => Dispatcher.UIThread.Post(UpdateStatus);
        UpdateStatus();
    }

    private void ShowPage(Button tab)
    {
        foreach (var (button, _) in _pages)
            button.Classes.Set("active", button == tab);
        if (_pages.TryGetValue(tab, out var page)) PageHost.Content = page;
    }

    private void OnTabClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button button) ShowPage(button);
    }

    public void UpdateStatus()
    {
        if (this.FindControl<TextBlock>("StatusText") is not { } status) return;
        var level = _state.TrackingVm.TerminalLevel;
        if (this.FindControl<TextBlock>("TerminalLevelText") is { } levelText)
            levelText.Text = level > 0 ? level.ToString() : "–";

        var scan = _state.Scanner.Last;
        var parts = new List<string> { _state.Connection.Status };
        if (scan?.Error is { Length: > 0 } error) parts.Add(error);
        else if (scan is not null) parts.Add($"{scan.At:HH:mm:ss} 识别到 {scan.Items.Count} 个目标");
        status.Text = string.Join(" · ", parts);
    }
}
