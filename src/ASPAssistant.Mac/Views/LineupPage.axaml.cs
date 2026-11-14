// Added for the macOS port on 2026-11-14; see NOTICE.
using Avalonia.Controls;
using Avalonia.Interactivity;
using ASPAssistant.Core;
using System.Net.Http;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

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

    private void OnToggleImport(object? sender, RoutedEventArgs e)
    {
        var panel = this.FindControl<Border>("ImportPanel");
        if (panel is not null) panel.IsVisible = !panel.IsVisible;
    }

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        var box = this.FindControl<TextBox>("ImportBox");
        var status = this.FindControl<TextBlock>("ImportStatus");
        if (box is null || status is null) return;
        var input = box.Text?.Trim() ?? "";
        if (input.Length == 0)
        {
            status.Text = "请先粘贴攻略链接。";
            return;
        }
        status.Text = "正在读取攻略…";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            var importer = new SklandGuideImporter(http, _state.Operators, _state.Strategies);
            var result = await importer.ImportAsync(input);
            _state.ResolveStrategies(result.Lineup);
            await _state.LineupVm.AddOrUpdateAsync(result.Lineup);
            status.Text = result.Notice;
            box.Text = "";
            AppLogger.Info("Mac", $"Imported lineup '{result.Lineup.Name}' with {result.Lineup.RecommendedStrategies.Count} strategies");
        }
        catch (Exception ex)
        {
            status.Text = ex.Message;
            AppLogger.Warn("Mac", $"Lineup import failed: {ex}");
        }
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
