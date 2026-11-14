// Added for the macOS port on 2026-11-14; see NOTICE.
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ASPAssistant.Mac.Views;

/// <summary>One item currently on sale in the shop, with its tracking state.</summary>
public partial class ShopItemViewModel : ObservableObject
{
    public required string Name { get; init; }
    public required TrackingType Type { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label))]
    private bool _isTracked;

    public string Label => IsTracked ? $"✓ {Name}" : $"+ {Name}";
}

/// <summary>Page state: the shared tracking view model plus the last shop-scan results.</summary>
public partial class TrackingPageViewModel : ObservableObject
{
    public required TrackingViewModel Tracking { get; init; }
    public required ShopScanService Scanner { get; init; }
    public ObservableCollection<ShopItemViewModel> ShopItems { get; } = [];

    public bool HasShopItems => ShopItems.Count > 0;
    public int ShopItemCount => ShopItems.Count;

    [ObservableProperty]
    private bool _autoScan;

    [ObservableProperty]
    private string _scanStatus = "未扫描";

    partial void OnAutoScanChanged(bool value)
    {
        if (value) Scanner.Start();
        else Scanner.Stop();
    }

    public void ReplaceShopItems(IEnumerable<(string Name, TrackingType Type, bool IsTracked)> items)
    {
        ShopItems.Clear();
        foreach (var (name, type, isTracked) in items)
            ShopItems.Add(new ShopItemViewModel { Name = name, Type = type, IsTracked = isTracked });
        OnPropertyChanged(nameof(HasShopItems));
        OnPropertyChanged(nameof(ShopItemCount));
    }
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

        // Start sampling automatically when a device connection is configured, so opening the
        // app next to a running game immediately shows what is on sale.
        if (state.Connection.IsDeviceCapture) _vm.AutoScan = true;
    }

    private void OnScanned(ShopScanResult result) => Dispatcher.UIThread.Post(() =>
    {
        _vm.ReplaceShopItems(result.Items.Select(item => (
            item.Name,
            ResolveType(item.Name),
            _state.TrackingVm.IsTracked(item.Name))));
        RefreshScanStatus();
    });

    private string ThemeName => ThemeLabel(_vm.Scanner.Last?.Theme ?? _state.ShopTheme);

    private static string ThemeLabel(GameShopTheme theme) => theme switch
    {
        GameShopTheme.Ursus => "乌萨斯",
        GameShopTheme.Closure => "可露希尔",
        _ => "终端"
    };

    private TrackingType ResolveType(string name) =>
        _state.Operators.Any(o => o.Name == name) ? TrackingType.Operator : TrackingType.Equipment;

    private void RefreshScanStatus()
    {
        if (_vm.Scanner.Last is not { } last)
        {
            _vm.ScanStatus = "未扫描";
            return;
        }
        _vm.ScanStatus = last.Error is { Length: > 0 } error
            ? (error.Contains("PlayCover", StringComparison.Ordinal) || error.Contains("ADB", StringComparison.Ordinal)
                || error.Contains("无法截图", StringComparison.Ordinal)
                ? "等待游戏连接…"
                : error)
            : $"{last.At:HH:mm:ss} · 在售 {last.Items.Count} · {ThemeName}";
    }

    private async void OnScanOnce(object? sender, RoutedEventArgs e)
    {
        _vm.ScanStatus = "识别中…";
        await _state.Scanner.ScanOnceAsync();
        RefreshScanStatus();
    }

    private async void OnToggleShopItem(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not ShopItemViewModel item) return;
        if (_state.TrackingVm.IsTracked(item.Name)) _state.TrackingVm.RemoveTracking(item.Name);
        else _state.TrackingVm.AddTracking(item.Name, item.Type);
        item.IsTracked = _state.TrackingVm.IsTracked(item.Name);
        await _state.SaveTrackingAsync();
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
