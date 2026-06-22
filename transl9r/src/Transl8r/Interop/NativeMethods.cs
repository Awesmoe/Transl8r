using System;
using System.Runtime.InteropServices;

namespace Transl8r.Interop;

/// <summary>Win32 P/Invoke surface used by the overlay and hotkeys.</summary>
internal static class NativeMethods
{
    // Extended window styles
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TRANSPARENT = 0x00000020; // click-through
    public const int WS_EX_LAYERED = 0x00080000;     // translucency (WPF sets this)
    public const int WS_EX_TOOLWINDOW = 0x00000080;  // keep out of alt-tab
    public const int WS_EX_NOACTIVATE = 0x08000000;  // never take focus

    // Z-order / SetWindowPos
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOACTIVATE = 0x0010;

    // Hotkeys
    public const int WM_HOTKEY = 0x0312;
    public const uint MOD_NOREPEAT = 0x4000;

    // SetWindowDisplayAffinity: exclude a window from screen capture so our own
    // overlays don't get OCR'd back in (the self-capture feedback loop). Visible
    // to the user, invisible to BitBlt/CopyFromScreen and other capture APIs.
    // WDA_EXCLUDEFROMCAPTURE needs Windows 10 build 19041 (2004) or newer.
    public const uint WDA_NONE = 0x00000000;
    public const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);
}
