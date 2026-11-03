using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using TimeInset.Core;
using Forms = System.Windows.Forms;

namespace TimeInset.Services;
internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern nint GetStyle(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern nint SetStyle(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern nint MonitorFromPoint(System.Drawing.Point p, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(nint monitor, int type, out uint x, out uint y);
    public static WorkArea Area(Forms.Screen screen)
    {
        var monitor = MonitorFromPoint(new(screen.Bounds.Left + 1, screen.Bounds.Top + 1), 2);
        GetDpiForMonitor(monitor, 0, out var dpi, out _);
        var r = screen.WorkingArea;
        return new(screen.DeviceName, r.Left, r.Top, r.Width, r.Height, dpi > 0 ? dpi / 96.0 : 1);
    }
    public static WorkArea SavedArea(AppSettings settings) => Area(Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == settings.Monitor) ?? Forms.Screen.PrimaryScreen!);
    public static WorkArea WindowArea(Window window) => Area(Forms.Screen.FromHandle(new WindowInteropHelper(window).Handle));
}
