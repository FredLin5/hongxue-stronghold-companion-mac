// Added for the macOS port on 2026-11-14; see NOTICE.
using System.Runtime.InteropServices;

namespace ASPAssistant.Mac.Overlay;

/// <summary>
/// Makes an Avalonia window behave like a game overlay: floating above other apps' windows,
/// transparent, without shadow and **click-through** (mouse events ignore it).
///
/// Avalonia exposes the platform handle as an NSView on some versions and as the NSWindow on
/// others, so the class is checked (and `[view window]` is used) before any setter is sent.
/// </summary>
public static class NativeOverlay
{
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SendVoidBool(IntPtr receiver, IntPtr selector, byte value);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SendVoidInteger(IntPtr receiver, IntPtr selector, nint value);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SendVoidUnsigned(IntPtr receiver, IntPtr selector, nuint value);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendPtr(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern byte SendBool(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr sel_registerName(string name);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr object_getClassName(IntPtr obj);

    /// <summary>NSStatusWindowLevel — above normal and floating windows of any app.</summary>
    private const nint StatusWindowLevel = 25;

    /// <summary>CanJoinAllSpaces | FullScreenAuxiliary.</summary>
    private const nuint CollectionBehavior = (1 << 0) | (1 << 8);

    public static string? TryApply(IntPtr platformHandle)
    {
        if (platformHandle == IntPtr.Zero) return "no platform handle";
        try
        {
            var nsWindow = ResolveWindow(platformHandle);
            if (nsWindow == IntPtr.Zero)
                return $"not an NSWindow ({ClassName(platformHandle)})";

            SetBool(nsWindow, "setIgnoresMouseEvents:", true);
            SetBool(nsWindow, "setOpaque:", false);
            SetBool(nsWindow, "setHasShadow:", false);
            SetBool(nsWindow, "setMovableByWindowBackground:", false);
            SendVoidInteger(nsWindow, sel_registerName("setLevel:"), StatusWindowLevel);
            SendVoidUnsigned(nsWindow, sel_registerName("setCollectionBehavior:"), CollectionBehavior);
            return null;
        }
        catch (Exception ex)
        {
            return $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    /// <summary>True when the window ignores mouse events (click-through).</summary>
    public static bool IgnoresMouseEvents(IntPtr platformHandle)
    {
        var nsWindow = ResolveWindow(platformHandle);
        return nsWindow != IntPtr.Zero && SendBool(nsWindow, sel_registerName("ignoresMouseEvents")) != 0;
    }

    public static string ClassName(IntPtr handle)
    {
        var name = object_getClassName(handle);
        return name == IntPtr.Zero ? "?" : Marshal.PtrToStringUTF8(name) ?? "?";
    }

    private static IntPtr ResolveWindow(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return IntPtr.Zero;
        // Avalonia.Native hands out its own NSWindow subclass (AvnWindow); accept anything that
        // implements the window setters instead of matching the class name exactly.
        if (RespondsTo(handle, "setLevel:") && RespondsTo(handle, "setIgnoresMouseEvents:"))
            return handle;
        // Views answer `window` with their containing NSWindow.
        if (RespondsTo(handle, "window"))
        {
            var window = SendPtr(handle, sel_registerName("window"));
            if (window != IntPtr.Zero && RespondsTo(window, "setLevel:")) return window;
        }
        return IntPtr.Zero;
    }

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern byte SendBoolPtr(IntPtr receiver, IntPtr selector, IntPtr arg);

    private static bool RespondsTo(IntPtr handle, string selector)
        => SendBoolPtr(handle, sel_registerName("respondsToSelector:"), sel_registerName(selector)) != 0;

    private static void SetBool(IntPtr window, string selector, bool value)
        => SendVoidBool(window, sel_registerName(selector), value ? (byte)1 : (byte)0);
}
