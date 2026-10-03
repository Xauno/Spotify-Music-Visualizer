using IdleViz.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace IdleViz.App;

/// <summary>
/// A borderless black window that covers a display (or, extended, several), above everything
/// including the taskbar. It is created ahead of time and hidden between opens, so opening is instant.
/// </summary>
internal sealed class VisualizerWindow() : NativeWindow(
    "IdleViz visualizer",
    // Layered, so the whole window can fade. Tool window, so it has no taskbar button and isn't in Alt+Tab.
    WINDOW_EX_STYLE.WS_EX_TOPMOST | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_LAYERED,
    WINDOW_STYLE.WS_POPUP)
{
    /// <summary>While true, the mouse pointer is invisible over the window.</summary>
    public bool HidesCursor { get; set; }

    /// <summary>
    /// Hides a pointer that is showing while at rest over the window, and returns whether it had to.
    /// Windows shows the cursor of whichever window last got a mouse message, and only asks a window
    /// for its cursor when the mouse moves over it. A move closes the visualizer, so a still pointer
    /// would keep the shape the window underneath gave it, and Windows also puts the arrow back when
    /// its "app starting" pointer ends after an open through the URL. Setting the pointer to where it
    /// already is makes Windows send that message without moving anything. Call it repeatedly while
    /// open: while the window is fully see-through, Windows treats the pointer as over the window underneath.
    /// </summary>
    public bool HideRestingCursor()
    {
        var cursor = new CURSORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<CURSORINFO>() };
        if (!HidesCursor || !PInvoke.GetCursorInfo(ref cursor) || cursor.hCursor.IsNull)
        {
            return false;
        }

        // The page is a child window, so the window under the pointer is that child or this one.
        if (PInvoke.GetAncestor(PInvoke.WindowFromPoint(cursor.ptScreenPos), GET_ANCESTOR_FLAGS.GA_ROOT) != Handle)
        {
            return false;
        }

        PInvoke.SetCursorPos(cursor.ptScreenPos.X, cursor.ptScreenPos.Y);
        return true;
    }

    /// <summary>Shows the window where the plan puts it, on top, without taking focus.</summary>
    public void Show(PlannedWindow place)
    {
        PInvoke.SetWindowPos(
            Handle,
            new HWND(-1), // HWND_TOPMOST
            place.Left,
            place.Top,
            place.Width,
            place.Height,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);
    }

    public void Hide() => PInvoke.ShowWindow(Handle, SHOW_WINDOW_CMD.SW_HIDE);

    /// <summary>Asks Windows for keyboard focus. Windows may refuse when nobody triggered the open by hand.</summary>
    public bool TakeFocus() => PInvoke.SetForegroundWindow(Handle);

    /// <summary>0 is invisible, 1 is solid.</summary>
    public void SetOpacity(double opacity)
    {
        var alpha = (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255);
        PInvoke.SetLayeredWindowAttributes(Handle, new COLORREF(0), alpha, LAYERED_WINDOW_ATTRIBUTES_FLAGS.LWA_ALPHA);
    }

    /// <summary>While true, clicks go through to whatever is underneath, as during a fade-out.</summary>
    public void SetClickThrough(bool clickThrough)
    {
        var style = (WINDOW_EX_STYLE)PInvoke.GetWindowLongPtr(Handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        style = clickThrough ? style | WINDOW_EX_STYLE.WS_EX_TRANSPARENT : style & ~WINDOW_EX_STYLE.WS_EX_TRANSPARENT;
        PInvoke.SetWindowLongPtr(Handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, (nint)style);
    }

    protected override LRESULT? OnMessage(uint message, WPARAM wParam, LPARAM lParam)
    {
        if (message == PInvoke.WM_SETCURSOR && HidesCursor)
        {
            PInvoke.SetCursor(HCURSOR.Null);
            return (LRESULT)1;
        }

        return null;
    }
}
