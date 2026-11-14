// Added for the macOS port on 2026-11-14; see NOTICE.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.ViewModels;

namespace ASPAssistant.Mac.Views;

/// <summary>
/// 阵容规划 tab: the Core <see cref="LineupPlannerViewModel"/> with an Avalonia front end.
/// The window is narrow (400px), so the covenant targets are compact tiles and each
/// candidate lists its operators in two columns instead of expanding into a grid.
/// </summary>
public partial class PlannerPage : UserControl
{
    private readonly AppState _state;
    private readonly LineupPlannerViewModel _vm;

    /// <summary>Hover delay before the shared detail popup opens.</summary>
    private static readonly TimeSpan DetailDelay = TimeSpan.FromMilliseconds(320);
    private readonly DispatcherTimer _detailTimer = new() { Interval = DetailDelay };
    private Control? _detailAnchor;
    private object? _detailData;

    /// <summary>Designer / runtime XAML loader entry point.</summary>
    public PlannerPage()
    {
        InitializeComponent();
        _state = null!;
        _vm = null!;
    }

    public PlannerPage(AppState state)
    {
        _state = state;
        _vm = state.PlannerVm;
        InitializeComponent();
        DataContext = _vm;
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LineupPlannerViewModel.Strategy)) BuildStrategyChips();
        };
        BuildStrategyChips();
        _detailTimer.Tick += (_, _) =>
        {
            _detailTimer.Stop();
            OpenDetail();
        };
        // Searching is deferred while the tab is hidden (ban changes only mark it stale).
        AttachedToVisualTree += (_, _) =>
        {
            _vm.CurrentTerminalLevel = _state.TrackingVm.TerminalLevel;
            _vm.IsActive = true;
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _vm.IsActive = false;
            CloseDetail();
        };
    }

    // ── 悬停详情 ────────────────────────────────────────────────────────────

    private void OnDetailEnter(object? sender, PointerEventArgs e)
    {
        if (sender is not Control anchor) return;
        // 空位没有干员资料，保留原来的纯文字提示。
        if (anchor.DataContext is not (PlannerCovenantItem or PlannerOperatorItem { Operator: not null })) return;
        _detailAnchor = anchor;
        _detailData = anchor.DataContext;
        _detailTimer.Stop();
        _detailTimer.Start();
    }

    private void OnDetailLeave(object? sender, PointerEventArgs e)
    {
        if (sender is Control anchor && ReferenceEquals(anchor, _detailAnchor)) CloseDetail();
    }

    private void OnPagePointerPressed(object? sender, PointerPressedEventArgs e) => CloseDetail();
    private void OnPagePointerWheel(object? sender, PointerWheelEventArgs e) => CloseDetail();

    private void OpenDetail()
    {
        if (_detailAnchor is null || _detailData is null) return;
        var key = _detailData switch
        {
            PlannerCovenantItem => "CovenantDetails",
            PlannerOperatorItem { Operator: not null } => "OperatorDetails",
            _ => null,
        };
        if (key is null || !this.TryFindResource(key, out var resource) || resource is not IDataTemplate template) return;
        DetailHost.Content = _detailData;
        DetailHost.ContentTemplate = template;
        // Anchor the card just outside the panel's right edge (like the Windows layout) so it never
        // covers the tile/row being hovered; the vertical offset still follows that item.
        DetailPopup.PlacementTarget = DetailAnchor;
        var itemTop = _detailAnchor.TranslatePoint(new Point(0, 0), this)?.Y ?? 0;
        var anchorTop = DetailAnchor.TranslatePoint(new Point(0, 0), this)?.Y ?? 0;
        // Keep the whole card on screen: measure it first, then slide it up when the item is near the
        // bottom (a card anchored to the last visible row would otherwise be cut off).
        DetailHost.Measure(new Size(320, double.PositiveInfinity));
        var cardHeight = DetailHost.DesiredSize.Height;
        var lowest = Math.Max(8, Bounds.Height - cardHeight - 10);
        DetailPopup.VerticalOffset = Math.Clamp(itemTop - 6, 8, lowest) - anchorTop;
        DetailPopup.IsOpen = true;
    }

    private void CloseDetail()
    {
        _detailTimer.Stop();
        _detailAnchor = null;
        _detailData = null;
        if (DetailPopup is not null) DetailPopup.IsOpen = false;
    }

    private void BuildStrategyChips()
    {
        var row = StrategyRow;
        row.Children.Clear();
        foreach (var choice in _vm.StrategyChoices)
        {
            var button = new Button
            {
                Content = new TextBlock { Text = choice.Name, FontSize = 11 },
                Tag = choice,
                Margin = new Avalonia.Thickness(0, 0, 6, 6),
                Padding = new Avalonia.Thickness(9, 3)
            };
            foreach (var cls in new[] { "stratchip" }) button.Classes.Add(cls);
            if (choice.Strategy == _vm.Strategy) button.Classes.Add("active");
            button.Click += OnStrategyClick;
            row.Children.Add(button);
        }
        if (MechEliteCheck is not null) MechEliteCheck.IsChecked = _vm.MechElite;
    }

    private void OnStrategyClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PlannerStrategyChoice choice }) _vm.SelectStrategyCommand.Execute(choice);
    }

    private void OnMechEliteToggled(object? sender, RoutedEventArgs e)
    {
        _vm.MechElite = MechEliteCheck.IsChecked == true;
    }

    private void OnOnlyRunToggled(object? sender, RoutedEventArgs e)
    {
        _vm.OnlyCurrentRun = OnlyRunCheck.IsChecked == true;
    }

    private void OnLockKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (_vm.AddLocked(_vm.LockSearchText)) e.Handled = true;
    }

    private void OnPickLock(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: Operator op }) _vm.AddLocked(op.Name);
    }

    private void OnSelectVariant(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: PlannerVariantCell cell }) _vm.SelectedVariant = cell;
    }

    private void OnSaveLineup(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: PlannerLineupItem item }) _vm.SaveLineupCommand.Execute(item);
    }

    private void OnTrackLineup(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: PlannerLineupItem item }) _vm.TrackLineupCommand.Execute(item);
    }
}
