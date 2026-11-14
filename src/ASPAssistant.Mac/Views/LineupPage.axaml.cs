// Added for the macOS port on 2026-11-14; see NOTICE.
using Avalonia.Controls;
using Avalonia.Interactivity;
using ASPAssistant.Core;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Mac.Views;

public partial class LineupPage : UserControl
{
    private readonly AppState _state;

    /// <summary>Designer / runtime XAML loader entry point.</summary>
    public LineupPage()
    {
        InitializeComponent();
        _state = null!;
    }

    public LineupPage(AppState state)
    {
        _state = state;
        InitializeComponent();
        DataContext = state.LineupVm;
    }

    private async void OnTrackLineup(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not Lineup lineup) return;
        try
        {
            var (operators, equipment) = _state.TrackingVm.TrackLineup(lineup);
            await _state.SaveTrackingAsync();
            button.Content = $"已追踪 {operators}+{equipment}";
        }
        catch (Exception ex)
        {
            button.Content = "追踪失败";
            AppLogger.Warn("Mac", $"Track lineup failed: {ex.Message}");
        }
    }

    private async void OnDeleteLineup(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not Lineup lineup) return;
        try
        {
            await _state.LineupVm.DeleteAsync(lineup);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Mac", $"Delete lineup failed: {ex.Message}");
        }
    }
}
