// Added for the macOS port on 2026-11-14; see NOTICE.
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ASPAssistant.Mac.Views;

/// <summary>Page state: the shared tracking view model plus the last shop-scan results.</summary>
public partial class TrackingPageViewModel : ObservableObject
{
    public required TrackingViewModel Tracking { get; init; }
    public ObservableCollection<string> MatchedNames { get; } = [];

    [ObservableProperty]
    private bool _autoScan;

    [ObservableProperty]
    private string _scanStatus = "未扫描";

    partial void OnAutoScanChanged(bool value)
    {
        if (value) Scanner.Start();
        else Scanner.Stop();
    }

    public required ShopScanService Scanner { get; init; }
}

public partial class TrackingPage : UserControl
{
    private readonly AppState _state;
    private readonly TrackingPageViewModel _vm;

    /// <summary>Designer / runtime XAML loader entry point.</summary>
    public TrackingPage()
    {
        InitializeComponent();
        _state = null!;
        _vm = null!;
    }

    public TrackingPage(AppState state)
    {
        _state = state;
        _vm = new TrackingPageViewModel { Tracking = state.TrackingVm, Scanner = state.Scanner };
        InitializeComponent();
        DataContext = _vm;

        state.Scanner.Scanned += OnScanned;
        state.TrackingVm.PropertyChanged += (_, _) => RefreshScanStatus();
    }

    private void OnScanned(ShopScanResult result) => Dispatcher.UIThread.Post(() =>
    {
        _vm.MatchedNames.Clear();
        foreach (var item in result.Items) _vm.MatchedNames.Add(item.Name);
        RefreshScanStatus();
    });

    private void RefreshScanStatus()
    {
        if (_vm.Scanner.Last is not { } last)
        {
            _vm.ScanStatus = "未扫描";
            return;
        }
        _vm.ScanStatus = last.Error is { Length: > 0 } error
            ? error
            : $"{last.At:HH:mm:ss} · {last.Width}×{last.Height} · 在售 {last.Items.Count}";
    }

    private async void OnScanOnce(object? sender, RoutedEventArgs e)
    {
        _vm.ScanStatus = "识别中…";
        await _state.Scanner.ScanOnceAsync();
        RefreshScanStatus();
    }

    private async void OnToggleAcquired(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not TrackingEntry entry) return;
        _state.TrackingVm.ToggleAcquired(entry.Name);
        await _state.SaveTrackingAsync();
    }

    private async void OnRemove(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not TrackingEntry entry) return;
        _state.TrackingVm.RemoveTracking(entry.Name);
        await _state.SaveTrackingAsync();
    }

    private async void OnClearAll(object? sender, RoutedEventArgs e)
    {
        _state.TrackingVm.ClearAllTracking();
        await _state.SaveTrackingAsync();
    }
}
