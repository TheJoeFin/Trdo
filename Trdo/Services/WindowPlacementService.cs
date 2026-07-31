using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Windows.Graphics;
using WinUIEx;

namespace Trdo.Services;

internal static partial class WindowPlacementService
{
    private const int WindowMargin = 12;
    private static PointInt32? _lastAnchorPoint;
    private static nint _trayIconWindowHandle;
    private static uint _trayIconId;
    private static bool _hasTrayIconSource;

    public static void CapturePointerAnchor()
    {
        if (GetCursorPos(out POINT point))
            _lastAnchorPoint = new PointInt32(point.X, point.Y);
    }

    public static void SetTrayIconSource(TrayIcon trayIcon)
    {
        if (TryGetTrayIconWindowHandle(trayIcon, out nint hwnd))
        {
            _trayIconWindowHandle = hwnd;
            _trayIconId = trayIcon.TrayIconId;
            _hasTrayIconSource = true;
        }
    }

    public static void PositionWindowNearAnchor(Window window, int width, int height)
    {
        bool usePointerPlacement = _lastAnchorPoint is PointInt32;
        PointInt32 anchor = GetAnchorPoint();
        DisplayArea? displayArea = DisplayArea.GetFromPoint(anchor, DisplayAreaFallback.Nearest);
        RectInt32 workArea = displayArea?.WorkArea ?? DisplayArea.Primary.WorkArea;

        // Win32 and WinUI positioning APIs all use physical pixels. Scale the
        // caller's logical width/height so placement and clamping are correct
        // at any DPI (125%, 150%, 200%, etc.). The DPI must come from the
        // anchor's monitor, not the window: a hidden window keeps the DPI of
        // wherever it last was, which goes stale across monitor/scale changes.
        uint dpi = GetDpiForAnchor(anchor, window);
        int physWidth = (int)(width * dpi / 96.0);
        int physHeight = (int)(height * dpi / 96.0);

        int x;
        int y;

        // When the pointer is over the tray icon, center on the icon rather than
        // offset from the cursor — matches native Windows tray flyout behavior.
        bool trayIconAvailable = TryGetTrayIconRect(out RECT iconRect);
        bool pointerIsOverTrayIcon = usePointerPlacement && trayIconAvailable
            && anchor.X >= iconRect.Left && anchor.X <= iconRect.Right
            && anchor.Y >= iconRect.Top && anchor.Y <= iconRect.Bottom;

        if (usePointerPlacement && !pointerIsOverTrayIcon)
        {
            bool placeLeft = anchor.X >= workArea.X + (workArea.Width / 2);
            bool placeAbove = anchor.Y >= workArea.Y + (workArea.Height / 2);

            x = placeLeft ? anchor.X - physWidth - WindowMargin : anchor.X + WindowMargin;
            y = placeAbove ? anchor.Y - physHeight - WindowMargin : anchor.Y + WindowMargin;
        }
        else if (trayIconAvailable)
        {
            int iconCenterX = (iconRect.Left + iconRect.Right) / 2;
            x = iconCenterX - (physWidth / 2);

            if (TryGetTaskbarRect(out RECT taskbarRect, out uint taskbarEdge))
            {
                y = taskbarEdge switch
                {
                    ABE_BOTTOM => taskbarRect.Top - physHeight,
                    ABE_TOP => taskbarRect.Bottom,
                    _ => iconRect.Top >= workArea.Y + (workArea.Height / 2)
                        ? iconRect.Top - physHeight - WindowMargin
                        : iconRect.Bottom + WindowMargin
                };
            }
            else
            {
                bool iconOnBottomHalf = iconRect.Top >= workArea.Y + (workArea.Height / 2);
                y = iconOnBottomHalf
                    ? iconRect.Top - physHeight - WindowMargin
                    : iconRect.Bottom + WindowMargin;
            }
        }
        else if (TryGetTaskbarRect(out RECT taskbarRect, out uint taskbarEdge))
        {
            bool isRtl = CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft;
            int taskbarMidY = taskbarRect.Top + ((taskbarRect.Bottom - taskbarRect.Top) / 2);

            x = taskbarEdge switch
            {
                ABE_BOTTOM or ABE_TOP => isRtl
                    ? taskbarRect.Left + WindowMargin
                    : taskbarRect.Right - WindowMargin - physWidth,
                _ => workArea.X + (workArea.Width / 2) - (physWidth / 2)
            };

            y = taskbarEdge switch
            {
                ABE_BOTTOM => taskbarRect.Top - physHeight,
                ABE_TOP => taskbarRect.Bottom,
                ABE_LEFT => taskbarMidY - (physHeight / 2),
                ABE_RIGHT => taskbarMidY - (physHeight / 2),
                _ => taskbarRect.Top - physHeight
            };

            if (taskbarEdge is ABE_LEFT or ABE_RIGHT)
            {
                x = taskbarEdge == ABE_LEFT
                    ? taskbarRect.Right + WindowMargin
                    : taskbarRect.Left - physWidth - WindowMargin;
            }
        }
        else
        {
            bool placeAbove = anchor.Y >= workArea.Y + (workArea.Height / 2);

            x = anchor.X - (physWidth / 2);
            y = placeAbove ? anchor.Y - physHeight - WindowMargin : anchor.Y + WindowMargin;
        }

        int maxX = System.Math.Max(workArea.X, workArea.X + workArea.Width - physWidth);
        int maxY = System.Math.Max(workArea.Y, workArea.Y + workArea.Height - physHeight);

        x = System.Math.Clamp(x, workArea.X, maxX);
        y = System.Math.Clamp(y, workArea.Y, maxY);

        window.AppWindow.MoveAndResize(new RectInt32(x, y, physWidth, physHeight));
    }

    /// <summary>
    /// Positions a transient overlay window (e.g. the song-change popup) at
    /// the bottom-center of the work area belonging to the taskbar/tray
    /// monitor — not necessarily the primary display or whichever monitor the
    /// window last lived on. Falls back to the last pointer/tray anchor, then
    /// the primary display, if no taskbar can be located.
    /// </summary>
    public static void PositionWindowBottomCenter(Window window, int width, int height)
    {
        RectInt32 workArea;
        PointInt32 dpiProbePoint;

        if (TryGetTaskbarRect(out RECT taskbarRect, out _))
        {
            PointInt32 taskbarPoint = new(
                (taskbarRect.Left + taskbarRect.Right) / 2,
                (taskbarRect.Top + taskbarRect.Bottom) / 2);
            DisplayArea? taskbarDisplay = DisplayArea.GetFromPoint(taskbarPoint, DisplayAreaFallback.Nearest);
            workArea = taskbarDisplay?.WorkArea ?? DisplayArea.Primary.WorkArea;
            dpiProbePoint = taskbarPoint;
        }
        else
        {
            PointInt32 anchor = GetAnchorPoint();
            DisplayArea? displayArea = DisplayArea.GetFromPoint(anchor, DisplayAreaFallback.Nearest);
            workArea = displayArea?.WorkArea ?? DisplayArea.Primary.WorkArea;
            dpiProbePoint = anchor;
        }

        // Physical pixels, scaled by the taskbar monitor's DPI — see the
        // remarks on PositionWindowNearAnchor for why the anchor's monitor
        // (not the window's) must supply the DPI.
        uint dpi = GetDpiForAnchor(dpiProbePoint, window);
        int physWidth = (int)(width * dpi / 96.0);
        int physHeight = (int)(height * dpi / 96.0);

        // Flush with the bottom of the work area (i.e. the top of the taskbar).
        // The visible gap between the taskbar and the popup's surface comes from
        // the overlay content's own bottom margin, so it only lives in one place.
        int x = workArea.X + (workArea.Width / 2) - (physWidth / 2);
        int y = workArea.Y + workArea.Height - physHeight;

        int maxX = System.Math.Max(workArea.X, workArea.X + workArea.Width - physWidth);
        int maxY = System.Math.Max(workArea.Y, workArea.Y + workArea.Height - physHeight);

        x = System.Math.Clamp(x, workArea.X, maxX);
        y = System.Math.Clamp(y, workArea.Y, maxY);

        window.AppWindow.MoveAndResize(new RectInt32(x, y, physWidth, physHeight));
    }

    private static uint GetDpiForAnchor(PointInt32 anchor, Window window)
    {
        POINT point = new() { X = anchor.X, Y = anchor.Y };
        nint monitor = MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST);
        if (monitor != 0
            && GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0
            && dpiX != 0)
        {
            return dpiX;
        }

        uint dpi = GetDpiForWindow(window.GetWindowHandle());
        return dpi == 0 ? 96u : dpi;
    }

    private static PointInt32 GetAnchorPoint()
    {
        if (_lastAnchorPoint is PointInt32 anchor)
            return anchor;

        if (TryGetTrayIconAnchorPoint(out anchor))
            return anchor;

        if (TryGetTaskbarAnchorPoint(out anchor))
            return anchor;

        RectInt32 workArea = DisplayArea.Primary.WorkArea;
        return new PointInt32(
            workArea.X + (workArea.Width / 2),
            workArea.Y + workArea.Height - WindowMargin);
    }

    private static bool TryGetTrayIconAnchorPoint(out PointInt32 anchor)
    {
        if (!TryGetTrayIconRect(out RECT iconRect))
        {
            anchor = default;
            return false;
        }

        anchor = new PointInt32(
            (iconRect.Left + iconRect.Right) / 2,
            (iconRect.Top + iconRect.Bottom) / 2);
        return true;
    }

    private static bool TryGetTrayIconRect(out RECT iconRect)
    {
        iconRect = default;

        if (!_hasTrayIconSource || _trayIconWindowHandle == 0)
            return false;

        NOTIFYICONIDENTIFIER identifier = new()
        {
            cbSize = Marshal.SizeOf<NOTIFYICONIDENTIFIER>(),
            hWnd = _trayIconWindowHandle,
            uID = _trayIconId,
        };

        return Shell_NotifyIconGetRect(ref identifier, out iconRect) == 0;
    }

    private static bool TryGetTrayIconWindowHandle(TrayIcon trayIcon, out nint hwnd)
    {
        hwnd = 0;

        try
        {
            FieldInfo? field = typeof(TrayIcon).GetField(
                "_windowHandle",
                BindingFlags.Instance | BindingFlags.NonPublic);

            if (field?.GetValue(trayIcon) is nint handle && handle != 0)
            {
                hwnd = handle;
                return true;
            }
        }
        catch
        {
            // WinUIEx internals may change across versions
        }

        return false;
    }

    private static bool TryGetTaskbarRect(out RECT rc, out uint edge)
    {
        APPBARDATA appBarData = new()
        {
            cbSize = Marshal.SizeOf<APPBARDATA>()
        };

        if (SHAppBarMessage(ABM_GETTASKBARPOS, ref appBarData) == 0)
        {
            rc = default;
            edge = 0;
            return false;
        }

        rc = appBarData.rc;
        edge = appBarData.uEdge;
        return true;
    }

    private static bool TryGetTaskbarAnchorPoint(out PointInt32 anchor)
    {
        if (!TryGetTaskbarRect(out RECT taskbarRect, out uint edge))
        {
            anchor = default;
            return false;
        }

        bool isRtl = CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft;
        int taskbarMidY = taskbarRect.Top + ((taskbarRect.Bottom - taskbarRect.Top) / 2);

        anchor = edge switch
        {
            ABE_BOTTOM => new PointInt32(
                isRtl ? taskbarRect.Left + WindowMargin : taskbarRect.Right - WindowMargin,
                taskbarMidY),
            ABE_TOP => new PointInt32(
                isRtl ? taskbarRect.Left + WindowMargin : taskbarRect.Right - WindowMargin,
                taskbarRect.Bottom - WindowMargin),
            ABE_LEFT => new PointInt32(
                taskbarRect.Right - WindowMargin,
                taskbarRect.Bottom - WindowMargin),
            ABE_RIGHT => new PointInt32(
                taskbarRect.Left + WindowMargin,
                taskbarRect.Bottom - WindowMargin),
            _ => new PointInt32(
                isRtl ? taskbarRect.Left + WindowMargin : taskbarRect.Right - WindowMargin,
                taskbarRect.Bottom - WindowMargin)
        };

        return true;
    }

    private const uint ABM_GETTASKBARPOS = 0x00000005;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const uint MDT_EFFECTIVE_DPI = 0;
    private const uint ABE_LEFT = 0;
    private const uint ABE_TOP = 1;
    private const uint ABE_RIGHT = 2;
    private const uint ABE_BOTTOM = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NOTIFYICONIDENTIFIER
    {
        public int cbSize;
        public nint hWnd;
        public uint uID;
        public Guid guidItem;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize;
        public nint hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public nint lParam;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out POINT point);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromPoint(POINT pt, uint dwFlags);

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(nint hMonitor, uint dpiType, out uint dpiX, out uint dpiY);

    [LibraryImport("shell32.dll")]
    private static partial uint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT iconLocation);
}
