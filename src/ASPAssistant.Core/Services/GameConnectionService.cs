// Modified for ASP Assistant Arknights on 2026-11-11; see NOTICE.
using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Models;
using MaaFramework.Binding;
using MaaFramework.Binding.Buffers;

namespace ASPAssistant.Core.Services;

/// <summary>
/// Shares the selected screenshot source and desktop viewport with all consumers.
///
/// Three sources are supported:
/// <list type="bullet">
///   <item><see cref="GameCaptureSource.Pc"/> — Windows desktop window (GDI capture). Windows only.</item>
///   <item><see cref="GameCaptureSource.Adb"/> — MuMu / Android over ADB (MaaAdbController).</item>
///   <item><see cref="GameCaptureSource.PlayCover"/> — iOS app under PlayCover on Apple Silicon,
///         through PlayTools' MaaTools TCP service (MaaPlayCoverController).</item>
/// </list>
/// </summary>
public sealed class GameConnectionService : IDisposable
{
    private readonly object _captureLock = new();
    private MaaAdbController? _adbController;
    private MaaPlayCoverController? _playCoverController;
    private DateTime _retryAfter;
    private bool _disposed;
    private string _status = "PC 窗口截图";
    private int _screenWidth;
    private int _screenHeight;

#if WINDOWS
    private IntPtr _window;
    private uint _windowProcessId;
#endif

    public GameConnectionSettings Settings { get; }
    public bool IsAdb => Settings.Source == GameCaptureSource.Adb;
    public bool IsPlayCover => Settings.Source == GameCaptureSource.PlayCover;
    /// <summary>True when frames come from a MaaFramework controller instead of the desktop window.</summary>
    public bool IsDeviceCapture => IsAdb || IsPlayCover;
    public string Status => Volatile.Read(ref _status);

    public GameConnectionService(GameConnectionSettings? settings = null
#if WINDOWS
        , IntPtr selectedWindow = default
#endif
        )
    {
        Settings = settings ?? new();
#if WINDOWS
        _window = selectedWindow;
        if (_window != IntPtr.Zero)
            User32.GetWindowThreadProcessId(_window, out _windowProcessId);
#endif
        if (IsAdb) _status = "等待 ADB 连接";
        else if (IsPlayCover) _status = "等待 PlayCover 连接";
    }

    /// <summary>Default MaaTools address of the PlayTools TCP service.</summary>
    public const string DefaultPlayCoverAddress = "127.0.0.1:1717";
    /// <summary>Bundle identifier of the PlayCover installation of Arknights.</summary>
    public const string DefaultPlayCoverBundleId = "com.hypergryph.arknights";

#if WINDOWS
    public IntPtr FindGameWindow()
    {
        if (IsDeviceCapture)
        {
            if (_window != IntPtr.Zero && User32.IsWindow(_window))
            {
                User32.GetWindowThreadProcessId(_window, out var processId);
                if (processId == _windowProcessId && User32.GetWindowTitle(_window) == Settings.WindowTitle)
                    return _window;
            }

            // Duplicate titles need an explicit selection in the connection dialog.
            // A closed instance must never silently attach the overlay to another instance.
            if (_window != IntPtr.Zero) return IntPtr.Zero;
            var matches = FindMuMuWindows().Where(w => w.Name == Settings.WindowTitle).ToList();
            if (matches.Count != 1) return IntPtr.Zero;
            _window = matches[0].Handle;
            User32.GetWindowThreadProcessId(_window, out _windowProcessId);
            return _window;
        }

        return User32.FindArknightsWindow();
    }

    public static IReadOnlyList<DesktopWindowInfo> FindMuMuWindows()
    {
        using var windows = MaaToolkit.Shared.Desktop.Window.Find();
        return windows.Where(w => User32.IsWindowVisible(w.Handle)
                && IsMuMuGameWindow(w.Handle))
            .Select(w => new DesktopWindowInfo(w.Handle, w.Name, w.ClassName,
                w.ScreencapMethod, w.MouseMethod, w.KeyboardMethod)).ToList();
    }

    private static bool IsMuMuGameWindow(IntPtr window)
    {
        var display = User32.FindChildWindow(window, "nemudisplay");
        return display != IntPtr.Zero && User32.GetAncestor(display, 2) == window;
    }

    public RECT? GetViewport(IntPtr window)
    {
        if (window == IntPtr.Zero || User32.IsIconic(window)) return null;
        if (!IsDeviceCapture) return User32.GetClientRectScreen(window);
        var display = User32.FindChildWindow(window, "nemudisplay");
        var bounds = display == IntPtr.Zero ? null : User32.GetClientRectScreen(display);
        return bounds is { Width: > 0, Height: > 0 } rect
            ? WindowGeometry.FitAspectRatio(rect, 16, 9) : null;
    }
#endif

    /// <summary>Captures the selected ADB device or PlayCover app; its dimensions also bound information-panel scrolling.</summary>
    public byte[]? CaptureDeviceScreen(int maxWidth)
    {
        lock (_captureLock)
        {
            if (_disposed || !IsDeviceCapture || DateTime.UtcNow < _retryAfter) return null;
            try
            {
                var controller = (MaaController?)GetOrCreateController();
                if (controller is null) throw new IOException(IsAdb ? "ADB 控制器不可用" : "PlayCover 控制器不可用");

                if (controller.Screencap().Wait() != MaaJobStatus.Succeeded)
                    throw new IOException(IsAdb
                        ? "ADB 截图失败，请检查模拟器与连接地址"
                        : "PlayCover 截图失败，请确认游戏正在运行且 MaaTools 已开启");

                using var image = new MaaImageBuffer();
                if (!controller.GetCachedImage(image) || image.IsEmpty)
                    throw new IOException("未返回有效画面");
                var width = image.Width;
                var height = image.Height;
                _screenWidth = width;
                _screenHeight = height;
                if (!WindowGeometry.IsSupportedGameAspectRatio(width, height))
                {
                    _status = $"当前画面 {width}×{height}，请设置为横屏 16∶9";
                    return null;
                }
                if (width > maxWidth)
                    image.TryResize(maxWidth, (int)Math.Round(height * (double)maxWidth / width));
                if (!image.TryGetEncodedData(out byte[]? png) || png is null)
                    throw new IOException("画面编码失败");
                _status = $"{SourceLabel()} 已连接 · {width}×{height}";
                _retryAfter = default;
                return png;
            }
            catch (Exception ex)
            {
                _status = $"{ex.Message}（5 秒后重试）";
                AppLogger.Warn(IsAdb ? "AdbCapture" : "PlayCoverCapture", _status);
                ResetController();
                _retryAfter = DateTime.UtcNow.AddSeconds(5);
                return null;
            }
        }
    }

    /// <summary>Kept for the existing ADB call sites.</summary>
    public byte[]? CaptureAdbScreen(int maxWidth) => CaptureDeviceScreen(maxWidth);

    private string SourceLabel() => IsAdb ? "ADB" : "PlayCover";

    private MaaController? GetOrCreateController()
    {
        if (IsAdb)
        {
            if (_adbController is not null) return _adbController;
            if (!File.Exists(Settings.AdbPath))
                throw new IOException("请选择有效的 adb 路径");
            if (string.IsNullOrWhiteSpace(Settings.AdbSerial))
                throw new IOException("请填写 ADB 连接地址或选择发现的设备");
            _status = "正在连接 ADB…";
            // Keep portable screenshot transport separate from the emulator's desktop viewport.
            var controller = new MaaAdbController(Settings.AdbPath, Settings.AdbSerial,
                AdbScreencapMethods.Encode | AdbScreencapMethods.EncodeToFileAndPull
                    | AdbScreencapMethods.RawWithGzip | AdbScreencapMethods.RawByNetcat,
                AdbInputMethods.AdbShell,
                agentPath: Path.Combine(AppContext.BaseDirectory, "MaaAgentBinary"),
                link: LinkOption.None);
            if (controller.LinkStart().Wait() != MaaJobStatus.Succeeded)
            {
                controller.Dispose();
                throw new IOException("ADB 连接失败，请检查设备与连接地址");
            }
            controller.SetOption_ScreenshotUseRawSize(true);
            return _adbController = controller;
        }

        if (_playCoverController is not null) return _playCoverController;
        var address = string.IsNullOrWhiteSpace(Settings.PlayCoverAddress)
            ? DefaultPlayCoverAddress : Settings.PlayCoverAddress.Trim();
        var bundleId = string.IsNullOrWhiteSpace(Settings.PlayCoverBundleId)
            ? DefaultPlayCoverBundleId : Settings.PlayCoverBundleId.Trim();
        _status = "正在连接 PlayCover…";
        var playCover = new MaaPlayCoverController(address, bundleId,
            LinkOption.None, CheckStatusOption.None);
        if (playCover.LinkStart().Wait() != MaaJobStatus.Succeeded)
        {
            playCover.Dispose();
            throw new IOException($"PlayCover 连接失败（{address}）。请在 PlayCover 的应用设置中开启 MaaTools，"
                + "确认端口一致，并让明日方舟处于运行状态");
        }
        playCover.SetOption_ScreenshotUseRawSize(true);
        return _playCoverController = playCover;
    }

    private void ResetController()
    {
        _adbController?.Dispose();
        _adbController = null;
        _playCoverController?.Dispose();
        _playCoverController = null;
    }

    /// <summary>Checks that a controller can be created and produces a frame (used by the connection dialog).</summary>
    public (bool Ok, string Message) TestDeviceConnection()
    {
        var frame = CaptureDeviceScreen(1920);
        return frame is null ? (false, Status) : (true, Status);
    }

    public void Dispose()
    {
        lock (_captureLock)
        {
            _disposed = true;
            ResetController();
        }
    }

    public bool ScrollInformationPanelDown()
    {
        lock (_captureLock)
        {
            if (_disposed) return false;
            int x = (int)(_screenWidth * .35);
            int y1 = (int)(_screenHeight * .72);
            int y2 = (int)(_screenHeight * .64);
            if (_playCoverController is { } playCover)
                return TouchSwipe(playCover, x, y1, x, y2);
            return _adbController?.Swipe(x, y1, x, y2, 200).Wait() == MaaJobStatus.Succeeded;
        }
    }

    /// <summary>A single tap in capture coordinates (PlayCover and ADB both support touch).</summary>
    public bool Tap(int x, int y)
    {
        lock (_captureLock)
        {
            if (_disposed) return false;
            // PlayCover's control unit only implements touch_down/touch_up (click is deprecated).
            if (_playCoverController is { } playCover)
                return playCover.TouchDown(0, x, y, 1).Wait() == MaaJobStatus.Succeeded
                    && playCover.TouchUp(0).Wait() == MaaJobStatus.Succeeded;
            return _adbController?.Click(x, y).Wait() == MaaJobStatus.Succeeded;
        }
    }

    /// <summary>PlayCover swipe built from explicit touch events (its controller has no swipe).</summary>
    private static bool TouchSwipe(MaaController controller, int x1, int y1, int x2, int y2, int steps = 8)
    {
        if (controller.TouchDown(0, x1, y1, 1).Wait() != MaaJobStatus.Succeeded) return false;
        for (int step = 1; step <= steps; step++)
        {
            int x = x1 + (x2 - x1) * step / steps;
            int y = y1 + (y2 - y1) * step / steps;
            controller.TouchMove(0, x, y, 1).Wait();
            Thread.Sleep(16);
        }
        return controller.TouchUp(0).Wait() == MaaJobStatus.Succeeded;
    }
}
