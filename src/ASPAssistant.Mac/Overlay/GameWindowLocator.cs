// Added for the macOS port on 2026-11-14; see NOTICE.
using System.Runtime.InteropServices;

namespace ASPAssistant.Mac.Overlay;

public readonly record struct WindowRect(double X, double Y, double Width, double Height);

/// <summary>
/// Finds the PlayCover game window through CoreGraphics. Window owner names and bounds are
/// available without Screen Recording permission (only window *titles* are restricted).
/// </summary>
public static class GameWindowLocator
{
    private const uint OnScreenOnly = 1 << 0;
    private const uint ExcludeDesktopElements = 1 << 4;

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern bool CGRectMakeWithDictionaryRepresentation(IntPtr dictionary, out CGRect rect);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern long CFArrayGetCount(IntPtr array);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, nint index);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFDictionaryGetValue(IntPtr dictionary, IntPtr key);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string value, uint encoding);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern bool CFStringGetCString(IntPtr value, IntPtr buffer, nint bufferSize, uint encoding);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern bool CFNumberGetValue(IntPtr number, int type, out int value);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr value);

    [StructLayout(LayoutKind.Sequential)]
    private struct CGRect
    {
        public double X;
        public double Y;
        public double Width;
        public double Height;
    }

    private const uint Utf8Encoding = 0x08000100;

    private static readonly IntPtr OwnerNameKey = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowOwnerName", Utf8Encoding);
    private static readonly IntPtr BoundsKey = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowBounds", Utf8Encoding);
    private static readonly IntPtr LayerKey = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowLayer", Utf8Encoding);

    /// <summary>Screen rectangle (points, top-left origin) of the first normal window owned by <paramref name="ownerName"/>.</summary>
    public static WindowRect? Find(string ownerName)
    {
        var list = CGWindowListCopyWindowInfo(OnScreenOnly | ExcludeDesktopElements, 0);
        if (list == IntPtr.Zero) return null;
        try
        {
            long count = CFArrayGetCount(list);
            for (long i = 0; i < count; i++)
            {
                var info = CFArrayGetValueAtIndex(list, (nint)i);
                if (info == IntPtr.Zero) continue;
                if (GetString(info, OwnerNameKey) != ownerName) continue;
                if (GetInt(info, LayerKey) != 0) continue;
                var bounds = CFDictionaryGetValue(info, BoundsKey);
                if (bounds == IntPtr.Zero) continue;
                if (!CGRectMakeWithDictionaryRepresentation(bounds, out var rect)) continue;
                if (rect.Width < 200 || rect.Height < 200) continue;
                return new WindowRect(rect.X, rect.Y, rect.Width, rect.Height);
            }
        }
        finally
        {
            CFRelease(list);
        }
        return null;
    }

    private static string? GetString(IntPtr dictionary, IntPtr key)
    {
        var value = CFDictionaryGetValue(dictionary, key);
        if (value == IntPtr.Zero) return null;
        var buffer = Marshal.AllocHGlobal(256);
        try
        {
            return CFStringGetCString(value, buffer, 256, Utf8Encoding)
                ? Marshal.PtrToStringUTF8(buffer)
                : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static int GetInt(IntPtr dictionary, IntPtr key)
    {
        var value = CFDictionaryGetValue(dictionary, key);
        // kCFNumberSInt32Type = 3
        return value != IntPtr.Zero && CFNumberGetValue(value, 3, out var result) ? result : 0;
    }
}
