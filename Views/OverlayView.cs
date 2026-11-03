using System.Globalization;
using System.Windows;
using System.Windows.Media;
using TimeInset.Core;

namespace TimeInset.Views;
// One render pass samples date and time together. Rows form two columns: icons on the
// left and values right-aligned, so the seconds of the clock and the timer line up.
// Tabular digits keep every second/state change position-stable.
public sealed class OverlayView : FrameworkElement
{
    private const double Gap = 12;
    private readonly Typeface font = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private readonly List<Row> rows = [];
    private int hourDigits = 2;
    private double dateSize;
    public AppSettings Settings { get; private set; } = new();
    public TimeModel Model { get; }
    public Rect TimerBounds { get; private set; }
    public OverlayView(TimeModel model) { Model = model; IsHitTestVisible = false; }

    // Icon at IconX; value right-aligned inside [SlotX, SlotX + SlotWidth); text top at Y.
    private readonly record struct Row(DisplayItems Kind, double Size, double IconX, double SlotX, double SlotWidth, double Y, Rect Bounds);

    private FormattedText Text(string text, double size, Brush brush) => new(text, CultureInfo.InvariantCulture,
        FlowDirection.LeftToRight, font, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
    private double DigitWidth(double size) => Enumerable.Range(0, 10).Max(i => Text(i.ToString(CultureInfo.InvariantCulture), size, Brushes.White).WidthIncludingTrailingWhitespace);
    private double CharWidth(char c, double size) => char.IsDigit(c) ? DigitWidth(size) : Text(c.ToString(), size, Brushes.White).WidthIncludingTrailingWhitespace;
    private double ValueWidth(DisplayItems kind, double size) => kind switch
    {
        DisplayItems.Date => 8 * DigitWidth(size) + 2 * CharWidth('/', size),
        DisplayItems.Timer => (hourDigits + 4) * DigitWidth(size) + 2 * CharWidth(':', size),
        _ => 6 * DigitWidth(size) + 2 * CharWidth(':', size),
    };
    private static double IconSize(double size) => size * .65;
    private double IconSlot(double size) => Settings.Icons ? IconSize(size) + size * .2 : 0;
    private double RowSize(DisplayItems kind) => kind == DisplayItems.Date ? dateSize : Settings.FontSize;
    private static int HoursLength(TimeSpan elapsed) => ((long)elapsed.TotalHours).ToString(CultureInfo.InvariantCulture).Length;

    public Size Configure(AppSettings settings)
    {
        Settings = settings;
        if (Model.State == TimerState.Ready) hourDigits = 2;
        hourDigits = Math.Max(hourDigits, Math.Max(2, HoursLength(Model.Elapsed)));
        rows.Clear(); TimerBounds = Rect.Empty;
        var kinds = settings.OrderedItems.Where(k => settings.Items.HasFlag(k)).ToArray();
        // Stacked under a clock or timer, the date is scaled to the same width as HH:mm:ss.
        // On one line, or shown alone, it uses the normal text size.
        dateSize = settings.FontSize;
        if (settings.Layout == DisplayLayout.Vertical && kinds.Length > 1 && kinds.Contains(DisplayItems.Date))
        {
            double size = settings.FontSize;
            dateSize = size * (6 * DigitWidth(size) + 2 * CharWidth(':', size)) / (8 * DigitWidth(size) + 2 * CharWidth('/', size));
        }
        if (kinds.Length == 0) { Width = Height = 8; InvalidateVisual(); return new(8, 8); }

        // The outline extends beyond the glyphs, so the outer padding grows with the text size.
        double pad = 4 + Math.Ceiling(settings.FontSize / 28.0);
        double right = 0, bottom = 0;
        if (settings.Layout == DisplayLayout.Vertical)
        {
            double iconColumn = kinds.Max(k => IconSlot(RowSize(k)));
            double valueColumn = kinds.Max(k => ValueWidth(k, RowSize(k)));
            double y = pad;
            foreach (var kind in kinds)
            {
                double size = RowSize(kind), h = Math.Ceiling(Text("0", size, Brushes.White).Height);
                var bounds = new Rect(pad, y, Math.Ceiling(iconColumn + valueColumn), h);
                Add(new(kind, size, pad, pad + iconColumn, valueColumn, y, bounds));
                y += h + 2 * size / 28;
                right = Math.Max(right, bounds.Right); bottom = Math.Max(bottom, bounds.Bottom);
            }
        }
        else
        {
            // Share one baseline so a smaller date sits on the same line as the clock.
            double baseline = kinds.Max(k => Text("0", RowSize(k), Brushes.White).Baseline);
            double x = pad;
            foreach (var kind in kinds)
            {
                double size = RowSize(kind);
                var ft = Text("0", size, Brushes.White);
                double y = pad + baseline - ft.Baseline, icon = IconSlot(size), value = ValueWidth(kind, size);
                var bounds = new Rect(x, y, Math.Ceiling(icon + value), Math.Ceiling(ft.Height));
                Add(new(kind, size, x, x + icon, value, y, bounds));
                x = bounds.Right + Gap;
                right = Math.Max(right, bounds.Right); bottom = Math.Max(bottom, bounds.Bottom);
            }
        }
        Width = Math.Max(8, Math.Ceiling(right + pad)); Height = Math.Max(8, Math.Ceiling(bottom + pad));
        InvalidateVisual();
        return new(Width, Height);
    }
    private void Add(Row row)
    {
        rows.Add(row);
        if (row.Kind == DisplayItems.Timer) TimerBounds = row.Bounds;
    }
    public void Tick()
    {
        if (HoursLength(Model.Elapsed) > hourDigits || Model.State == TimerState.Ready && hourDigits > 2) Configure(Settings);
        InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        var s = Settings;
        var background = (Color)ColorConverter.ConvertFromString(s.Background);
        background.A = (byte)Math.Round(s.BackgroundOpacity * 2.55);
        dc.DrawRectangle(new SolidColorBrush(background), null, new Rect(0, 0, Width, Height));
        DateTime now = Model.Now;
        var foreground = (Color)ColorConverter.ConvertFromString(s.Foreground);
        outline = new SolidColorBrush((Color)ColorConverter.ConvertFromString(AppSettings.OutlineFor(s.Foreground)));
        foreach (var row in rows)
        {
            // Timer state is shown by color alone: a muted text color before start, red while paused.
            Color color = row.Kind == DisplayItems.Timer ? Model.State switch
                { TimerState.Ready => ReadyColor(foreground), TimerState.Paused => Color.FromRgb(0xFF, 0x50, 0x50), _ => foreground } : foreground;
            Brush brush = new SolidColorBrush(color);
            double size = row.Size;
            if (s.Icons) DrawIcon(dc, row.Kind, new(row.IconX, row.Y + size * .3), IconSize(size), brush);
            string text = row.Kind switch
            {
                DisplayItems.Clock => now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                DisplayItems.Date => now.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture),
                _ => TimeModel.Format(Model.Elapsed),
            };
            DrawNumber(dc, text, row.SlotX, row.SlotWidth, row.Y, size, brush);
        }
    }
    private void DrawNumber(DrawingContext dc, string text, double slotX, double slotWidth, double y, double size, Brush brush)
    {
        double actual = text.Sum(c => CharWidth(c, size));
        double x = slotX + Math.Max(0, slotWidth - actual);
        foreach (char c in text)
        {
            var ft = Text(c.ToString(), size, brush);
            double width = CharWidth(c, size);
            DrawOutlined(dc, ft, new(x + (width - ft.WidthIncludingTrailingWhitespace) / 2, y), size, brush);
            x += width;
        }
    }
    private Brush outline = Brushes.Black;
    // Before start the timer is muted toward mid gray; gray text would barely change, so it gets darker instead.
    private static Color ReadyColor(Color c) => c == Color.FromRgb(0xA0, 0xA0, 0xA0) ? Color.FromRgb(0x68, 0x68, 0x68) : Mix(c, Color.FromRgb(0x80, 0x80, 0x80));
    private static Color Mix(Color a, Color b) => Color.FromRgb((byte)((a.R + b.R) / 2), (byte)((a.G + b.G) / 2), (byte)((a.B + b.B) / 2));
    // The outline is stroked first and the fill drawn over it, so only the outer half remains
    // (1.5 DIP at 28 DIP) and thin digit strokes keep their full text color.
    private void DrawOutlined(DrawingContext dc, FormattedText text, Point p, double size, Brush brush)
    {
        var geometry = text.BuildGeometry(p);
        if (Settings.Outline) dc.DrawGeometry(null, new Pen(outline, 3 * size / 28) { LineJoin = PenLineJoin.Round }, geometry);
        dc.DrawGeometry(brush, null, geometry);
    }
    private void DrawIcon(DrawingContext dc, DisplayItems kind, Point origin, double size, Brush brush)
    {
        double thickness = Math.Max(1, size * .07);
        if (kind == DisplayItems.Timer && Model.State == TimerState.Paused)
        {
            // Paused: the stopwatch is replaced by a filled pause mark.
            var bars = new GeometryGroup();
            bars.Children.Add(new RectangleGeometry(new Rect(origin.X + size * .18, origin.Y + size * .1, size * .22, size * .8)));
            bars.Children.Add(new RectangleGeometry(new Rect(origin.X + size * .6, origin.Y + size * .1, size * .22, size * .8)));
            if (Settings.Outline) dc.DrawGeometry(null, new Pen(outline, size / 7), bars);
            dc.DrawGeometry(brush, null, bars);
            return;
        }
        var geometry = new GeometryGroup();
        if (kind == DisplayItems.Date)
        {
            geometry.Children.Add(new RectangleGeometry(new Rect(origin.X, origin.Y, size, size * .85), size * .08, size * .08));
            geometry.Children.Add(new LineGeometry(new(origin.X, origin.Y + size * .25), new(origin.X + size, origin.Y + size * .25)));
        }
        else
        {
            var center = new Point(origin.X + size / 2, origin.Y + size / 2);
            geometry.Children.Add(new EllipseGeometry(center, size * .42, size * .42));
            geometry.Children.Add(new LineGeometry(center, new(center.X, center.Y - size * .26)));
            geometry.Children.Add(new LineGeometry(center, new(center.X + size * .24, center.Y)));
            if (kind == DisplayItems.Timer) geometry.Children.Add(new LineGeometry(new(center.X - size * .18, origin.Y - size * .1), new(center.X + size * .18, origin.Y - size * .1)));
        }
        if (Settings.Outline) dc.DrawGeometry(null, new Pen(outline, thickness + size / 7), geometry);
        dc.DrawGeometry(null, new Pen(brush, thickness), geometry);
    }
}
