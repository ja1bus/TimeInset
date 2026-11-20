using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TimeInset.Core;
using TimeInset.Core.Localization;
using TimeInset.Services;
using Forms = System.Windows.Forms;

namespace TimeInset.Views;
/// <summary>
/// 終了・リセットの確認（1.1.0）。MessageBox は親がないと主モニター等に出て、表示のあるモニターで
/// 気付けなかったため、メニューを開いた位置のモニターの作業領域の中央に、最前面で出す（仕様書§6）。
/// </summary>
public sealed class ConfirmWindow : Window
{
    private readonly System.Drawing.Point anchor;
    public Button YesButton { get; }
    public Button NoButton { get; }

    /// <param name="anchor">この点（物理ピクセル）を含むモニターに出す。</param>
    public ConfirmWindow(string message, bool warning, System.Drawing.Point anchor)
    {
        this.anchor = anchor;
        Title = "TimeInset";
        FontFamily = new FontFamily("Yu Gothic UI"); FontSize = 13;
        SizeToContent = SizeToContent.WidthAndHeight; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Topmost = true; ShowInTaskbar = true;

        var icon = warning ? System.Drawing.SystemIcons.Warning : System.Drawing.SystemIcons.Question;
        var image = new Image
        {
            Source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()),
            Width = 32, Height = 32, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 14, 0),
        };
        var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 360, VerticalAlignment = VerticalAlignment.Center };
        var body = new DockPanel { Margin = new Thickness(20, 20, 24, 20) };
        DockPanel.SetDock(image, Dock.Left);
        body.Children.Add(image); body.Children.Add(text);

        YesButton = new Button { Content = Strings.Yes, IsDefault = true, MinWidth = 88, Height = 26 };
        NoButton = new Button { Content = Strings.No, IsCancel = true, MinWidth = 88, Height = 26, Margin = new Thickness(8, 0, 0, 0) };
        YesButton.Click += (_, _) => DialogResult = true;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(YesButton); buttons.Children.Add(NoButton);
        var footer = new Border { Background = SystemColors.ControlBrush, Padding = new Thickness(12, 10, 12, 10), Child = buttons };

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer); root.Children.Add(body);
        Content = root;

        // Loaded can come before SourceInitialized for this window, so placement starts there.
        // The DPI change on moving to the monitor can resize the window again after a later layout
        // pass, so it is re-centered on each size change until the first frame is shown (review R3).
        SourceInitialized += (_, _) => { placing = true; MoveToMonitor(); Center(); };
        SizeChanged += (_, _) => { if (placing) Center(); };
        ContentRendered += (_, _) => { if (placing) Center(); placing = false; };
        Closed += (_, _) => placing = false;
        Loaded += (_, _) => YesButton.Focus();
    }

    private bool placing;
    private System.Drawing.Rectangle Area => Forms.Screen.FromPoint(anchor).WorkingArea;
    private nint Handle => new WindowInteropHelper(this).Handle;

    // Onto the monitor first: the window takes that monitor's DPI there (physical pixels).
    private void MoveToMonitor()
    {
        var r = Area;
        Native.SetWindowPos(Handle, 0, r.Left, r.Top, 0, 0, 0x15); // NOSIZE | NOZORDER | NOACTIVATE
    }

    // Centered by the real size. If the size cannot be read, the window stays where it is
    // (the top-left of the work area after MoveToMonitor).
    private void Center()
    {
        var handle = Handle;
        if (handle == 0 || !Native.GetWindowRect(handle, out var rect)) return;
        var r = Area;
        var p = Positioning.Center(new WorkArea("", r.Left, r.Top, r.Width, r.Height, 1), rect.Right - rect.Left, rect.Bottom - rect.Top);
        if (rect.Left != (int)p.Left || rect.Top != (int)p.Top) Native.SetWindowPos(handle, 0, (int)p.Left, (int)p.Top, 0, 0, 0x15);
    }

    /// <summary>試験用: 物理ピクセルの位置と大きさ、対象モニターの作業領域。</summary>
    public (System.Drawing.Rectangle Window, System.Drawing.Rectangle WorkArea) Placement
    { get { Native.GetWindowRect(Handle, out var r); return (System.Drawing.Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom), Area); } }

    /// <summary>開いている確認を前面に出す（最小化されていれば戻す）。</summary>
    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }
}
