// Added for the macOS port on 2026-11-14; see NOTICE.
using System.Timers;
using ASPAssistant.Core;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using Timer = System.Timers.Timer;

namespace ASPAssistant.Mac;

public sealed record ShopScanResult(
    DateTime At,
    IReadOnlyList<ShopItem> Items,
    GameShopTheme? Theme,
    string? Error,
    byte[]? Preview,
    int Width,
    int Height);

/// <summary>
/// Periodic shop scan for the macOS build: capture the game (PlayCover or ADB),
/// detect the shop theme, OCR the name strip and report the tracked items on sale.
/// </summary>
public sealed class ShopScanService : IDisposable
{
    private readonly Func<byte[]?> _capture;
    private readonly ShopReader _reader;
    private readonly GameShopThemeDetector _themeDetector;
    private readonly Func<IReadOnlyList<string>> _trackedNames;
    private readonly Timer _timer;
    private int _busy;
    private bool _disposed;

    public event Action<ShopScanResult>? Scanned;
    public event Action<GameShopTheme>? ThemeDetected;
    public event Action<IReadOnlyList<ShopItem>>? ItemsRecognized;

    /// <summary>
    /// Fingerprint-based theme detection. Off by default on macOS: PlayCover renders at 4K with
    /// different shopkeeper art, which the Windows reference fingerprints do not match reliably;
    /// the user picks the theme in settings instead.
    /// </summary>
    public bool AutomaticThemeDetection { get; set; }

    public ShopScanResult? Last { get; private set; }
    public bool IsRunning => _timer.Enabled;

    public ShopScanService(Func<byte[]?> capture, ShopReader reader,
        GameShopThemeDetector themeDetector, Func<IReadOnlyList<string>> trackedNames, int intervalMs = 2500)
    {
        _capture = capture;
        _reader = reader;
        _themeDetector = themeDetector;
        _trackedNames = trackedNames;
        _timer = new Timer(intervalMs);
        _timer.Elapsed += (_, _) => _ = ScanOnceAsync();
    }

    public void Start()
    {
        if (_disposed) return;
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    /// <summary>Uses the theme chosen in the settings page for the name-strip layout.</summary>
    public void ApplyTheme(GameShopTheme theme) => _reader.Theme = theme;

    public async Task<ShopScanResult> ScanOnceAsync()
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            return Last ?? new ShopScanResult(DateTime.Now, [], null, "识别进行中", null, 0, 0);
        try
        {
            var frame = _capture();
            if (frame is null)
            {
                var error = new ShopScanResult(DateTime.Now, [], null, "无法截图，请检查游戏连接", null, 0, 0);
                Publish(error);
                return error;
            }

            var (width, height) = ImageBytes.GetDimensions(frame);
            var theme = AutomaticThemeDetection ? _themeDetector.Detect(frame) : null;
            if (theme is { } detected)
            {
                _reader.Theme = detected;
                ThemeDetected?.Invoke(detected);
            }

            var items = await _reader.ReadAsync(frame, width, height, _trackedNames());
            var result = new ShopScanResult(DateTime.Now, items, theme, null, frame, width, height);
            Publish(result);
            ItemsRecognized?.Invoke(items);
            return result;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("MacScan", ex.ToString());
            var error = new ShopScanResult(DateTime.Now, [], null, ex.Message, null, 0, 0);
            Publish(error);
            return error;
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private void Publish(ShopScanResult result)
    {
        Last = result;
        Scanned?.Invoke(result);
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
        _timer.Dispose();
    }
}
