using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using TimeInset.Core;
using TimeInset.Services;

namespace TimeInset.Views;
public sealed class OverlayWindow : Window
{
    public OverlayView View { get; }
    private readonly InputPlane input;
    private nint handle;
    public event Action? PositionChanged;
    public event Action? StartRequested;
    public event Action? WorkAreaChanged;
    public event Action<Point>? MenuRequested;
    public OverlayWindow(TimeModel model)
    {
        Title = "TimeInset";
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true;
        Background = Brushes.Transparent; ShowInTaskbar = false; ShowActivated = false; Focusable = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        View = new(model); Content = View;
        input = new InputPlane(this);
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            Native.SetStyle(handle, -20, Native.GetStyle(handle, -20) | 0x08000000 | 0x80 | 0x20);
            ((HwndSource)PresentationSource.FromVisual(this)).AddHook(Hook);
        };
        IsVisibleChanged += (_, _) => SyncInput();
        SizeChanged += (_, _) => SyncInput();
        Closed += (_, _) => input.Dispose();
    }
    private nint Hook(nint h, int msg, nint wp, nint lp, ref bool handled)
    {
        if (msg == 0x21) { handled = true; return 3; } // MA_NOACTIVATE
        if (msg is 0x2E0 or 0x7E) Dispatcher.BeginInvoke(() => { Constrain(); WorkAreaChanged?.Invoke(); PositionChanged?.Invoke(); });
        return 0;
    }
    public bool Fits(AppSettings s)
    {
        var old = View.Settings;
        var size = View.Configure(s);
        var area = handle == 0 ? Native.SavedArea(s) : Native.WindowArea(this);
        View.Configure(old);
        return size.Width * area.Scale <= area.Width && size.Height * area.Scale <= area.Height;
    }
    public void Apply(AppSettings s, bool restorePosition = false)
    {
        Topmost = s.Topmost;
        View.Configure(s);
        UpdateLayout();
        if (restorePosition) RestorePosition(s);
        else Constrain();
        SyncInput();
    }
    public void RestorePosition(AppSettings s)
    {
        var area = Native.SavedArea(s);
        var p = Positioning.Restore(s, area, View.Width, View.Height);
        Native.SetWindowPos(handle, 0, (int)p.Left, (int)p.Top, 0, 0, 0x15);
        Constrain(); SyncInput();
    }
    public AppSettings CapturePosition(AppSettings s)
    {
        var area = Native.WindowArea(this);
        Native.GetWindowRect(handle, out var rect);
        return s with { Monitor = area.Name, X = (rect.Left - area.Left) / area.Scale, Y = (rect.Top - area.Top) / area.Scale };
    }
    public void Constrain()
    {
        if (handle == 0) return;
        var area = Native.WindowArea(this);
        Native.GetWindowRect(handle, out var rect);
        int x = (int)Math.Clamp(rect.Left, area.Left, Math.Max(area.Left, area.Left + area.Width - rect.Right + rect.Left));
        int y = (int)Math.Clamp(rect.Top, area.Top, Math.Max(area.Top, area.Top + area.Height - rect.Bottom + rect.Top));
        Native.SetWindowPos(handle, 0, x, y, 0, 0, 0x15); SyncInput();
    }
    private void SyncInput()
    {
        if (handle == 0) return;
        Native.GetWindowRect(handle, out var r);
        input.Place(r, IsVisible, Topmost, handle);
    }
    // A non-layered, fully glass input plane owns the entire rectangle, including
    // zero-alpha pixels. The visual window is always click-through. This avoids
    // the usual alpha=1 workaround that changes supposedly transparent pixels.
    private sealed class InputPlane : IDisposable
    {
        private readonly HwndSource source;
        private readonly OverlayWindow owner;
        private Point down;
        private bool dragging, pressed;
        public InputPlane(OverlayWindow owner)
        {
            this.owner = owner;
            source = new HwndSource(new HwndSourceParameters("TimeInset.Input") {
                WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x08000080 });
            source.CompositionTarget.BackgroundColor = Colors.Transparent;
            var margins = new Margins { Left = -1 };
            DwmExtendFrameIntoClientArea(source.Handle, ref margins);
            source.AddHook(Hook);
        }
        public void Place(Native.Rect r, bool visible, bool topmost, nint visual)
        {
            // Keep the input plane immediately behind the visible overlay.
            Native.SetWindowPos(source.Handle, topmost ? new(-1) : new(-2), r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, (uint)(0x10 | (visible ? 0x40 : 0x80)));
            Native.SetWindowPos(visual, source.Handle, 0, 0, 0, 0, 0x13);
        }
        private nint Hook(nint hwnd, int msg, nint wp, nint lp, ref bool handled)
        {
            if (msg == 0x21) { handled = true; return 3; }
            if (msg == 0x84) { handled = true; return 1; }
            if (msg == 0x201)
            {
                GetCursorPos(out var pt); down = new(pt.X, pt.Y); pressed = true; dragging = false; SetCapture(hwnd);
                // Native double-click messages require CS_DBLCLKS, so compare Windows' thresholds explicitly.
                long now = Environment.TickCount64;
                if (now - lastClick <= GetDoubleClickTime() && Math.Abs(down.X - lastPoint.X) <= System.Windows.Forms.SystemInformation.DoubleClickSize.Width && Math.Abs(down.Y - lastPoint.Y) <= System.Windows.Forms.SystemInformation.DoubleClickSize.Height)
                {
                    var dip = owner.PointFromScreen(down);
                    if (owner.View.TimerBounds.Contains(dip)) owner.StartRequested?.Invoke();
                    lastClick = 0;
                }
                else { lastClick = now; lastPoint = down; }
                handled = true;
            }
            if (msg == 0x200 && pressed)
            {
                GetCursorPos(out var pt);
                var current = new Point(pt.X, pt.Y);
                if (!dragging && (Math.Abs(current.X - down.X) > SystemParameters.MinimumHorizontalDragDistance || Math.Abs(current.Y - down.Y) > SystemParameters.MinimumVerticalDragDistance)) dragging = true;
                if (dragging)
                {
                    Native.GetWindowRect(owner.handle, out var rect);
                    Native.SetWindowPos(owner.handle, 0, rect.Left + (int)(current.X - down.X), rect.Top + (int)(current.Y - down.Y), 0, 0, 0x15);
                    down = current; owner.SyncInput(); lastClick = 0;
                }
                handled = true;
            }
            if (msg is 0x202 or 0x215)
            {
                if (pressed) { pressed = false; ReleaseCapture(); if (dragging) { owner.Constrain(); owner.PositionChanged?.Invoke(); } }
                handled = true;
            }
            if (msg == 0x205) { GetCursorPos(out var pt); owner.MenuRequested?.Invoke(new(pt.X, pt.Y)); handled = true; }
            return 0;
        }
        private long lastClick;
        private Point lastPoint;
        public void Dispose() => source.Dispose();
        [StructLayout(LayoutKind.Sequential)] private struct Margins { public int Left, Right, Top, Bottom; }
        [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(nint hwnd, ref Margins margins);
        [DllImport("user32.dll")] private static extern nint SetCapture(nint hwnd);
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out System.Drawing.Point point);
        [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();
    }
}
