// Added for the macOS port on 2026-11-14; see NOTICE.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ASPAssistant.Core;
using ASPAssistant.Core.Models;
using ASPAssistant.Mac.Overlay;

namespace ASPAssistant.Mac.Views;

/// <summary>
/// Prototype overlay: a transparent, click-through window that follows the PlayCover game
/// window and draws test markers. Used to verify that an overlay can float above the game.
/// </summary>
public partial class OverlayWindow : Window
{
    private static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#CC3CCFC0"));

    private readonly DispatcherTimer _timer;
    private readonly string _ownerName;
    private bool _nativeApplied;
    private Func<string, bool>? _isTracked;

    public OverlayWindow()
    {
        InitializeComponent();
        _ownerName = Environment.GetEnvironmentVariable("ASPASSISTANT_MAC_GAME_WINDOW") ?? "明日方舟";
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => FollowGameWindow();
    }

    public bool NativeApplied => _nativeApplied;

    /// <summary>Shows a badge for tracked items on sale, at their OCR position inside the game frame.</summary>
    public void SetTrackedPredicate(Func<string, bool> isTracked) => _isTracked = isTracked;

    // The 16:9 shop layout: each card is ~150 px wide at 1920 px, the portrait starts ~0.183 of the
    // height above the OCR name box and the card is clipped by the bottom of the screen.
    private const double CardWidth = 0.0781;
    private const double CardTopAboveName = 0.183;
    private const double CardHeight = 0.248;

    public void UpdateItems(IReadOnlyList<ShopItem> items)
    {
        MarkerLayer.Children.Clear();
        var tracked = (_isTracked is null ? items : items.Where(i => _isTracked(i.Name))).ToList();
        foreach (var item in tracked)
        {
            var nameCenterX = (item.OcrRegion.X + item.OcrRegion.Width / 2) * Width;
            var nameTop = item.OcrRegion.Y * Height;

            var cardWidth = CardWidth * Width;
            var cardHeight = Math.Min(CardHeight * Height, Height - (nameTop - CardTopAboveName * Height));
            var left = Math.Clamp(nameCenterX - cardWidth / 2, 0, Math.Max(0, Width - cardWidth));
            var top = Math.Clamp(nameTop - CardTopAboveName * Height, 0, Height - cardHeight);

            var highlight = new Border
            {
                Width = cardWidth,
                Height = cardHeight,
                CornerRadius = new CornerRadius(8),
                BorderBrush = Accent,
                BorderThickness = new Thickness(2.5),
                Background = new SolidColorBrush(Color.Parse("#1A3CCFC0")),
            };
            Canvas.SetLeft(highlight, left);
            Canvas.SetTop(highlight, top);
            MarkerLayer.Children.Add(highlight);

            var badge = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#E60D1114")),
                BorderBrush = Accent,
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(7, 2),
                Child = new TextBlock
                {
                    Text = item.Name,
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Color.Parse("#FF9BF2E8")),
                },
            };
            var badgeTop = Math.Max(0, top - 30);
            Canvas.SetLeft(badge, Math.Clamp(left, 0, Math.Max(0, Width - 90)));
            Canvas.SetTop(badge, badgeTop);
            MarkerLayer.Children.Add(badge);
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        var error = NativeOverlay.TryApply(handle);
        _nativeApplied = error is null;
        AppLogger.Info("Overlay", error is null
            ? $"native overlay applied (class={NativeOverlay.ClassName(handle)}, clickThrough={NativeOverlay.IgnoresMouseEvents(handle)})"
            : $"native overlay failed: {error}");
        FollowGameWindow();
        _timer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        base.OnClosed(e);
    }

    private void FollowGameWindow()
    {
        var rect = GameWindowLocator.Find(_ownerName);
        if (rect is not { } window)
        {
            StatusText.Text = $"未找到游戏窗口「{_ownerName}」";
            StatusText.IsVisible = true;
            return;
        }
        StatusText.IsVisible = false;

        // The game content is 16:9 and sits under the window's title bar.
        double width = window.Width;
        double height = width * 9.0 / 16.0;
        if (height > window.Height)
        {
            height = window.Height;
            width = height * 16.0 / 9.0;
        }
        double left = window.X + (window.Width - width) / 2;
        double top = window.Y + (window.Height - height);

        // Avalonia's PixelPoint on macOS is in screen points, the same unit CGWindowList reports.
        Width = width;
        Height = height;
        Position = new PixelPoint((int)Math.Round(left), (int)Math.Round(top));
        StatusText.Text = $"游戏窗口 {window.Width:0}×{window.Height:0} → 内容 {width:0}×{height:0} @ {left:0},{top:0}";
    }
}
