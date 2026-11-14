// Added for the macOS port on 2026-11-14; see NOTICE.
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ASPAssistant.Mac.Views;

public partial class GameInfoPage : UserControl
{
    private readonly AppState _state;

    /// <summary>Designer / runtime XAML loader entry point.</summary>
    public GameInfoPage()
    {
        InitializeComponent();
        _state = null!;
    }

    public GameInfoPage(AppState state)
    {
        _state = state;
        InitializeComponent();
        DataContext = state.GameInfo;
        if (this.FindControl<CheckBox>("AutoDetectCheck") is { } check)
            check.IsChecked = state.AutoDetectGameInfo;
    }

    private async void OnAutoDetectToggled(object? sender, RoutedEventArgs e)
    {
        var enabled = this.FindControl<CheckBox>("AutoDetectCheck")?.IsChecked == true;
        await _state.SetAutoDetectGameInfoAsync(enabled);
    }

    private async void OnIdentify(object? sender, RoutedEventArgs e) => await _state.IdentifyGameInfoAsync();

    private void OnClear(object? sender, RoutedEventArgs e)
    {
        _state.BanVm.ClearBans();
        _state.OperatorVm.NotifyBansChanged();
        _state.GameInfo.Clear();
    }
}
