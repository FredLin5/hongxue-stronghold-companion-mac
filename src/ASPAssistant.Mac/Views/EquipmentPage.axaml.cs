// Added for the macOS port on 2026-11-14; see NOTICE.
using Avalonia.Controls;
using Avalonia.Interactivity;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Mac.Views;

public partial class EquipmentPage : UserControl
{
    private readonly AppState _state;

    /// <summary>Designer / runtime XAML loader entry point.</summary>
    public EquipmentPage()
    {
        InitializeComponent();
        _state = null!;
    }

    public EquipmentPage(AppState state)
    {
        _state = state;
        InitializeComponent();
        DataContext = state.EquipmentVm;
    }

    private void OnResetFilters(object? sender, RoutedEventArgs e)
    {
        _state.EquipmentVm.ResetFilters();
    }

    private async void OnTrackEquipment(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not Equipment equipment) return;
        _state.TrackingVm.AddTracking(equipment.Name, TrackingType.Equipment);
        await _state.SaveTrackingAsync();
        button.Content = "已追踪";
        button.IsEnabled = false;
    }
}
