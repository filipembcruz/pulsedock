using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using PulseDock.Models;
using Forms = System.Windows.Forms;

namespace PulseDock.Services
{
    /// <summary>Positioning in physical pixels (works across monitors with different DPI).</summary>
    public static class WindowHelper
    {
        public static IntPtr Hwnd(Window w) => new WindowInteropHelper(w).Handle;

        public static void ModifyExStyle(Window w, long add, long remove)
        {
            var hwnd = Hwnd(w);
            if (hwnd == IntPtr.Zero) return;
            long style = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
            long next = (style | add) & ~remove;
            if (next != style) Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, next);
        }

        /// <summary>Hides from Alt+Tab and the taskbar.</summary>
        public static void MakeToolWindow(Window w, bool noActivate)
        {
            ModifyExStyle(w, Native.WS_EX_TOOLWINDOW | (noActivate ? Native.WS_EX_NOACTIVATE : 0), Native.WS_EX_APPWINDOW);
        }

        public static Forms.Screen GetScreen(string? deviceName)
        {
            if (!string.IsNullOrEmpty(deviceName))
            {
                var s = Forms.Screen.AllScreens.FirstOrDefault(x => x.DeviceName == deviceName);
                if (s != null) return s;
            }
            return Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0];
        }

        public static double Scale(Window w)
        {
            try { return VisualTreeHelper.GetDpi(w).DpiScaleX; }
            catch { return 1.0; }
        }

        internal static Native.RECT GetRect(Window w)
        {
            Native.GetWindowRect(Hwnd(w), out var r);
            return r;
        }

        private static void MoveTo(Window w, int x, int y)
        {
            var hwnd = Hwnd(w);
            if (hwnd == IntPtr.Zero) return;
            Native.GetWindowRect(hwnd, out var r);
            if (r.Left == x && r.Top == y) return;
            Native.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0,
                Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
        }

        public static void PlaceAtCorner(Window w, Corner corner, int marginDip, string? deviceName)
        {
            if (Hwnd(w) == IntPtr.Zero) return;
            var r = GetRect(w);
            var wa = GetScreen(deviceName).WorkingArea;
            int m = (int)Math.Round(marginDip * Scale(w));

            int x = corner is Corner.TopLeft or Corner.BottomLeft ? wa.Left + m : wa.Right - r.Width - m;
            int y = corner is Corner.TopLeft or Corner.TopRight ? wa.Top + m : wa.Bottom - r.Height - m;
            MoveTo(w, x, y);
        }

        /// <summary>Places <paramref name="w"/> next to the anchor window, facing the center of the screen.</summary>
        public static void PlaceBeside(Window w, Window anchor, Corner corner, string? deviceName)
        {
            if (Hwnd(w) == IntPtr.Zero) return;
            var r = GetRect(w);
            var wa = GetScreen(deviceName).WorkingArea;
            int gap = (int)Math.Round(8 * Scale(w));

            int x, y;
            if (anchor.IsVisible && Hwnd(anchor) != IntPtr.Zero)
            {
                var a = GetRect(anchor);
                bool left = corner is Corner.TopLeft or Corner.BottomLeft;
                bool top = corner is Corner.TopLeft or Corner.TopRight;
                x = left ? a.Right + gap : a.Left - r.Width - gap;
                y = top ? a.Top : a.Bottom - r.Height;

                // Wide strip widget: open below/above instead of beside.
                if (x < wa.Left || x + r.Width > wa.Right)
                {
                    x = left ? a.Left : a.Right - r.Width;
                    y = top ? a.Bottom + gap : a.Top - r.Height - gap;
                }
            }
            else
            {
                x = wa.Left + (wa.Width - r.Width) / 2;
                y = wa.Top + (wa.Height - r.Height) / 2;
            }

            x = Math.Clamp(x, wa.Left, Math.Max(wa.Left, wa.Right - r.Width));
            y = Math.Clamp(y, wa.Top, Math.Max(wa.Top, wa.Bottom - r.Height));
            MoveTo(w, x, y);
        }

        public static void PlaceCentered(Window w, string? deviceName)
        {
            if (Hwnd(w) == IntPtr.Zero) return;
            var r = GetRect(w);
            var wa = GetScreen(deviceName).WorkingArea;
            int x = Math.Max(wa.Left, wa.Left + (wa.Width - r.Width) / 2);
            int y = Math.Max(wa.Top, wa.Top + (wa.Height - r.Height) / 2);
            MoveTo(w, x, y);
        }

        /// <summary>Tray panel: docks against the taskbar, near the cursor.</summary>
        public static void PlaceNearTray(Window w)
        {
            if (Hwnd(w) == IntPtr.Zero) return;
            var r = GetRect(w);
            var cursor = Forms.Cursor.Position;
            var screen = Forms.Screen.FromPoint(cursor);
            var wa = screen.WorkingArea;
            var b = screen.Bounds;
            int gap = (int)Math.Round(12 * Scale(w));

            int x, y;
            if (wa.Top > b.Top)            // taskbar on top
            { x = cursor.X - r.Width / 2; y = wa.Top + gap; }
            else if (wa.Left > b.Left)     // taskbar on the left
            { x = wa.Left + gap; y = cursor.Y - r.Height / 2; }
            else if (wa.Right < b.Right)   // taskbar on the right
            { x = wa.Right - r.Width - gap; y = cursor.Y - r.Height / 2; }
            else                           // taskbar at the bottom (default)
            { x = cursor.X - r.Width / 2; y = wa.Bottom - r.Height - gap; }

            x = Math.Clamp(x, wa.Left + gap, Math.Max(wa.Left + gap, wa.Right - r.Width - gap));
            y = Math.Clamp(y, wa.Top + gap, Math.Max(wa.Top + gap, wa.Bottom - r.Height - gap));
            MoveTo(w, x, y);
        }

        /// <summary>Quick menu: opens from the cursor, flipping inward when there isn't enough room.</summary>
        public static void PlaceNearCursor(Window w)
        {
            if (Hwnd(w) == IntPtr.Zero) return;
            var r = GetRect(w);
            var c = Forms.Cursor.Position;
            var wa = Forms.Screen.FromPoint(c).WorkingArea;
            int x = c.X + r.Width <= wa.Right ? c.X : c.X - r.Width;
            int y = c.Y + r.Height <= wa.Bottom ? c.Y : c.Y - r.Height;
            x = Math.Clamp(x, wa.Left, Math.Max(wa.Left, wa.Right - r.Width));
            y = Math.Clamp(y, wa.Top, Math.Max(wa.Top, wa.Bottom - r.Height));
            MoveTo(w, x, y);
        }

        /// <summary>Saved free position, always kept within the monitor's visible area.</summary>
        public static void PlaceFree(Window w, int x, int y)
        {
            if (Hwnd(w) == IntPtr.Zero) return;
            var r = GetRect(w);
            var wa = Forms.Screen.FromPoint(new System.Drawing.Point(x + r.Width / 2, y + r.Height / 2)).WorkingArea;
            x = Math.Clamp(x, wa.Left, Math.Max(wa.Left, wa.Right - r.Width));
            y = Math.Clamp(y, wa.Top, Math.Max(wa.Top, wa.Bottom - r.Height));
            MoveTo(w, x, y);
        }

        /// <summary>After dragging: returns the corner if the widget was dropped near one, otherwise null.</summary>
        public static (Corner? corner, string? device, int x, int y) ResolveDrop(Window w, int edgeMarginDip)
        {
            var r = GetRect(w);
            var screen = Forms.Screen.FromHandle(Hwnd(w));
            var wa = screen.WorkingArea;
            int snap = (int)Math.Round(Math.Max(48, edgeMarginDip + 24) * Scale(w));

            bool left = r.Left - wa.Left <= snap;
            bool right = wa.Right - r.Right <= snap;
            bool top = r.Top - wa.Top <= snap;
            bool bottom = wa.Bottom - r.Bottom <= snap;

            Corner? corner = null;
            if ((left || right) && (top || bottom))
            {
                // If the window is wide enough to touch both sides, decide by its center.
                bool isLeft = left && (!right || (r.Left + r.Width / 2) < (wa.Left + wa.Width / 2));
                bool isTop = top && (!bottom || (r.Top + r.Height / 2) < (wa.Top + wa.Height / 2));
                corner = isLeft ? (isTop ? Corner.TopLeft : Corner.BottomLeft) : (isTop ? Corner.TopRight : Corner.BottomRight);
            }
            return (corner, screen.Primary ? null : screen.DeviceName, r.Left, r.Top);
        }

        /// <summary>
        /// Windows 11: rounded corners and a subtle border drawn by DWM for our borderless,
        /// non-transparent windows (replaces the per-pixel transparent rounded corners).
        /// </summary>
        public static void ApplyDwmFrame(Window w)
        {
            var hwnd = Hwnd(w);
            if (hwnd == IntPtr.Zero) return;
            try
            {
                int corner = Native.DWMWCP_ROUND;
                Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
                int border = 0x003B322E; // COLORREF (0x00BBGGRR) for #2E323B
                Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_BORDER_COLOR, ref border, sizeof(int));
            }
            catch
            {
                // dwmapi missing or attribute unsupported (Windows 10): square corners, no border.
            }
        }

        public static void BringToTopmost(Window w)
        {
            var hwnd = Hwnd(w);
            if (hwnd == IntPtr.Zero) return;
            Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }
    }

    /// <summary>
    /// Widget modes: overlay (topmost), desktop only (pinned to the bottom, attached to the desktop)
    /// and click-through.
    /// </summary>
    public sealed class WidgetWindowController
    {
        private readonly Window _window;
        private IntPtr _hwnd;
        private bool _pinnedToDesktop;
        private bool _clickThrough;
        private bool _clickThroughAllowed;
        private int _opacity = 100;
        private DisplayMode? _appliedMode;

        public WidgetWindowController(Window window) => _window = window;

        /// <summary>Legacy per-pixel transparent window (AllowsTransparency): WPF owns the layering.</summary>
        public bool PerPixelTransparency { get; set; }

        public void Attach()
        {
            _hwnd = WindowHelper.Hwnd(_window);
            WindowHelper.MakeToolWindow(_window, noActivate: true);
            if (!PerPixelTransparency) WindowHelper.ApplyDwmFrame(_window);
            HwndSource.FromHwnd(_hwnd)?.AddHook(Hook);
            UpdateLayering();
        }

        /// <summary>
        /// Applies overlay / desktop / tray z-order. Only acts when the mode really changes:
        /// re-sending HWND_TOPMOST makes DWM re-evaluate the window stack (and overlay planes).
        /// </summary>
        public void ApplyMode(DisplayMode mode)
        {
            if (_hwnd == IntPtr.Zero || _appliedMode == mode) return;
            _appliedMode = mode;

            if (mode == DisplayMode.Desktop)
            {
                _window.Topmost = false;
                _pinnedToDesktop = true;
                // Having the desktop (Progman) as owner keeps the widget visible on Win+D.
                var progman = Native.FindWindow("Progman", null);
                if (progman != IntPtr.Zero) Native.SetWindowLong(_hwnd, Native.GWLP_HWNDPARENT, progman.ToInt64());
                Native.SetWindowPos(_hwnd, Native.HWND_BOTTOM, 0, 0, 0, 0,
                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }
            else
            {
                _pinnedToDesktop = false;
                Native.SetWindowLong(_hwnd, Native.GWLP_HWNDPARENT, 0);
                _window.Topmost = mode == DisplayMode.Overlay;
            }
        }

        /// <summary>Whole-window opacity, 40–100. 100 = not a layered window at all.</summary>
        public void SetOpacity(int percent)
        {
            percent = Math.Clamp(percent, 40, 100);
            if (percent == _opacity) return;
            _opacity = percent;
            UpdateLayering();
        }

        /// <summary>Click-through is only possible when the user enabled the feature.</summary>
        public void SetClickThroughAllowed(bool allowed)
        {
            _clickThroughAllowed = allowed;
            if (!allowed) SetClickThrough(false);
        }

        /// <summary>
        /// WS_EX_LAYERED makes WPF present through the layered path (readback + UpdateLayeredWindow
        /// on every frame), so the window is layered ONLY while it really needs to be:
        /// opacity below 100%, or click-through currently active (it needs WS_EX_LAYERED|WS_EX_TRANSPARENT).
        /// </summary>
        private void UpdateLayering()
        {
            if (_hwnd == IntPtr.Zero || PerPixelTransparency) return;
            long style = Native.GetWindowLong(_hwnd, Native.GWL_EXSTYLE);
            bool wantLayered = _opacity < 100 || _clickThrough;
            bool isLayered = (style & Native.WS_EX_LAYERED) != 0;
            long next = wantLayered ? style | Native.WS_EX_LAYERED : style & ~Native.WS_EX_LAYERED;
            next = _clickThrough ? next | Native.WS_EX_TRANSPARENT : next & ~Native.WS_EX_TRANSPARENT;

            if (next != style) Native.SetWindowLong(_hwnd, Native.GWL_EXSTYLE, next);
            if (wantLayered) Native.SetLayeredWindowAttributes(_hwnd, 0, (byte)Math.Round(_opacity * 2.55), Native.LWA_ALPHA);

            if (wantLayered != isLayered)
            {
                // Make WPF pick up the layered/non-layered change now (it re-reads the style on window-pos changes).
                Native.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
                _window.InvalidateVisual();
            }
        }

        public void SetClickThrough(bool on)
        {
            if (on == _clickThrough || _hwnd == IntPtr.Zero) return;
            if (on && !_clickThroughAllowed && !PerPixelTransparency) return;
            _clickThrough = on;
            if (PerPixelTransparency)
            {
                WindowHelper.ModifyExStyle(_window, on ? Native.WS_EX_TRANSPARENT : 0, on ? 0 : Native.WS_EX_TRANSPARENT);
                return;
            }
            UpdateLayering();
        }

        private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_WINDOWPOSCHANGING && _pinnedToDesktop && lParam != IntPtr.Zero)
            {
                var wp = Marshal.PtrToStructure<Native.WINDOWPOS>(lParam);
                wp.hwndInsertAfter = Native.HWND_BOTTOM;
                wp.flags &= ~Native.SWP_NOZORDER;
                Marshal.StructureToPtr(wp, lParam, false);
            }
            return IntPtr.Zero;
        }
    }
}
