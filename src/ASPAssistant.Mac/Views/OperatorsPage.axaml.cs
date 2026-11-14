// Added for the macOS port on 2026-11-14; see NOTICE.
using Avalonia.Controls;
using Avalonia.Interactivity;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Mac.Views;

public partial class OperatorsPage : UserControl
{
    private readonly AppState _state;

    /// <summary>Designer / runtime XAML loader entry point.</summary>
    public OperatorsPage()
    {
        InitializeComponent();
        _state = null!;
    }

    public OperatorsPage(AppState state)
    {
        _state = state;
        InitializeComponent();
        DataContext = state.OperatorVm;
    }

    private void OnResetFilters(object? sender, RoutedEventArgs e) => _state.OperatorVm.ResetFilters();

    private async void OnTrackOperator(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not Operator op) return;
        _state.TrackingVm.AddTracking(op.Name, TrackingType.Operator);
        await _state.SaveTrackingAsync();
        button.Content = "已追踪";
        button.IsEnabled = false;
    }
}
