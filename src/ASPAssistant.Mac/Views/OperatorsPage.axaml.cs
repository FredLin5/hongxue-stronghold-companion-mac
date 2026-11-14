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

        // Ban changes arrive on a background thread and keep existing rows, so rebuild the
        // item containers to re-evaluate the "本局禁用" badge converter.
        state.BanListChanged += () => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (this.FindControl<ItemsControl>("OperatorList") is { } list)
            {
                list.ItemsSource = null;
                list.ItemsSource = _state.OperatorVm.FilteredOperators;
            }
        });
    }

    private void OnResetFilters(object? sender, RoutedEventArgs e) => _state.OperatorVm.ResetFilters();

    private void OnAvailableOnlyToggled(object? sender, RoutedEventArgs e)
    {
        var availableOnly = this.FindControl<CheckBox>("AvailableOnly")?.IsChecked == true;
        _state.OperatorVm.SelectedAvailabilityFilter = availableOnly
            ? ASPAssistant.Core.ViewModels.AvailabilityFilter.Available
            : ASPAssistant.Core.ViewModels.AvailabilityFilter.All;
    }

    private async void OnTrackOperator(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not Operator op) return;
        _state.TrackingVm.AddTracking(op.Name, TrackingType.Operator);
        await _state.SaveTrackingAsync();
        button.Content = "已追踪";
        button.IsEnabled = false;
    }
}
