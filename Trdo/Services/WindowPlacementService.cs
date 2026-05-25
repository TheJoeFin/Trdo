using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using Windows.Graphics;
using WinUIEx;

namespace Trdo.Services;

internal static partial class WindowPlacementService
{
    private const int WindowMargin = 12;
    private static PointInt32? _lastAnchorPoint;

    public static void CapturePointerAnchor()
    {
        if (GetCursorPos(out POINT point))
            _lastAnchorPoint = new PointInt32(point.X, point.Y);
    }

    public static void PositionWindowNearAnchor(Window window, int width, int height)
    {
        // Win32 and WinUI positioning APIs all use physical pixels. Scale the
        // caller's logical width/height so placement and clamping are correct
        // at any DPI (125%, 150%, 200%, etc.).
        nint hwnd = window.GetWindowHandle();
        uint dpi = GetDpiForWindow(hwnd);
        if (dpi == 0) dpi = 96;
        int physWidth = (int)(width * dpi / 96.0);
        int physHeight = (int)(height * dpi / 96.0);

        PointInt32 anchor = GetAnchorPoint();
        DisplayArea? displayArea = DisplayArea.GetFromPoint(anchor, DisplayAreaFallback.Nearest);
        RectInt32 workArea = displayArea?.WorkArea ?? DisplayArea.Primary.WorkArea;

        bool placeAbove = anchor.Y >= workArea.Y + (workArea.Height / 2);

        int x = anchor.X - (physWidth / 2);
        int y = placeAbove ? anchor.Y - physHeight - WindowMargin : anchor.Y + WindowMargin;

        int maxX = System.Math.Max(workArea.X, workArea.X + workArea.Width - physWidth);
        int maxY = System.Math.Max(workArea.Y, workArea.Y + workArea.Height - physHeight);

        x = System.Math.Clamp(x, workArea.X, maxX);
        y = System.Math.Clamp(y, workArea.Y, maxY);

        window.AppWindow.MoveAndResize(new RectInt32(x, y, physWidth, physHeight));
    }

    private static PointInt32 GetAnchorPoint()
    {
        if (_lastAnchorPoint is PointInt32 anchor)
            return anchor;

        if (TryGetTaskbarAnchorPoint(out anchor))
            return anchor;

        RectInt32 workArea = DisplayArea.Primary.WorkArea;
        return new PointInt32(workArea.X + workArea.Width - WindowMargin, workArea.Y + workArea.Height - WindowMargin);
    }

    private static bool TryGetTaskbarAnchorPoint(out PointInt32 anchor)
    {
        APPBARDATA appBarData = new()
        {
            cbSize = Marshal.SizeOf<APPBARDATA>()
        };

        if (SHAppBarMessage(ABM_GETTASKBARPOS, ref appBarData) == 0)
        {
            anchor = default;
            return false;
        }

        anchor = appBarData.uEdge switch
        {
            ABE_BOTTOM => new PointInt32(appBarData.rc.Right - WindowMargin, appBarData.rc.Top + ((appBarData.rc.Bottom - appBarData.rc.Top) / 2)),
            ABE_TOP => new PointInt32(appBarData.rc.Right - WindowMargin, appBarData.rc.Bottom - WindowMargin),
            ABE_LEFT => new PointInt32(appBarData.rc.Right - WindowMargin, appBarData.rc.Bottom - WindowMargin),
            ABE_RIGHT => new PointInt32(appBarData.rc.Left + WindowMargin, appBarData.rc.Bottom - WindowMargin),
            _ => new PointInt32(appBarData.rc.Right - WindowMargin, appBarData.rc.Bottom - WindowMargin)
        };

        return true;
    }

    private const uint ABM_GETTASKBARPOS = 0x00000005;
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

    [LibraryImport("shell32.dll")]
    private static partial uint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);
}
