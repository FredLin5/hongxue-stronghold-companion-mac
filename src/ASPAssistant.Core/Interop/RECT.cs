// Added for the macOS port on 2026-11-14; see NOTICE.
using System.Runtime.InteropServices;

namespace ASPAssistant.Core.Interop;

/// <summary>
/// Plain screen rectangle (physical pixels). Kept platform-neutral so window geometry
/// helpers can be shared by the Windows and macOS builds.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public int Width => Right - Left;
    public int Height => Bottom - Top;

    public static RECT FromSize(int left, int top, int width, int height) => new()
    {
        Left = left, Top = top, Right = left + width, Bottom = top + height
    };
}
