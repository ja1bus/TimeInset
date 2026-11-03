using System.Globalization;
using System.Text.Json;

namespace TimeInset.Core;

[Flags] public enum DisplayItems { None = 0, Clock = 1, Date = 2, Timer = 4 }
public enum DisplayLayout { Vertical, Horizontal }
public sealed record Hotkey(uint Modifiers, uint Key)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid => (Modifiers & ~7u) == 0 && Modifiers != 0 && Key is >= 8 and <= 254 &&
        Key is not (0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0x7B or >= 0xA0 and <= 0xA5);
    public override string ToString() => string.Join("+", new[] { (Modifiers & 2) != 0 ? "Ctrl" : null,
        (Modifiers & 1) != 0 ? "Alt" : null, (Modifiers & 4) != 0 ? "Shift" : null,
        Key is >= 0x70 and <= 0x87 ? $"F{Key - 0x6F}" : Key is >= 0x30 and <= 0x5A ? ((char)Key).ToString() : $"VK{Key:X2}" }.Where(x => x != null));
}
public sealed record AppSettings
{
    public int SchemaVersion { get; init; } = 1;
    public DisplayItems Items { get; init; } = DisplayItems.Clock | DisplayItems.Date;
    public DisplayItems LastItems { get; init; } = DisplayItems.Clock | DisplayItems.Date;
    public DisplayLayout Layout { get; init; }
    // Item order shared by both layouts, e.g. "Date,Clock,Timer". A string keeps record equality by value.
    public string Order { get; init; } = DefaultOrder;
    public const string DefaultOrder = "Date,Clock,Timer";
    public int FontSize { get; init; } = 28;
    public string Foreground { get; init; } = "#FFFFFF";
    public string Background { get; init; } = "#000000";
    public int BackgroundOpacity { get; init; }
    public bool Outline { get; init; } = true;
    public bool Icons { get; init; } = true;
    public bool Topmost { get; init; } = true;
    public string Monitor { get; init; } = "";
    public double X { get; init; } = 16;
    public double Y { get; init; } = 16;
    public DateTime SpecifiedDate { get; init; } = new(2026, 1, 1);
    public Hotkey? ToggleTimer { get; init; }
    // Index order is shared with HotkeyService ids and the settings window rows.
    [System.Text.Json.Serialization.JsonIgnore]
    public Hotkey?[] Keys => [ToggleTimer];
    public AppSettings WithKeys(IReadOnlyList<Hotkey?> keys) => this with { ToggleTimer = keys[0] };
    // Text colors are a fixed choice; each has a fixed outline color that keeps it readable.
    // Gray is for an unobtrusive clock, so its outline is a dark gray rather than black.
    public static readonly string[] ForegroundChoices = ["#FFFFFF", "#A0A0A0", "#000000", "#FFD400", "#00C853", "#2F80FF"];
    private static readonly string[] OutlineColors = ["#000000", "#505050", "#FFFFFF", "#000000", "#000000", "#FFFFFF"];
    public static string OutlineFor(string foreground)
    {
        int i = Array.FindIndex(ForegroundChoices, c => string.Equals(c, foreground, StringComparison.OrdinalIgnoreCase));
        return i >= 0 ? OutlineColors[i] : "#000000";
    }
    [System.Text.Json.Serialization.JsonIgnore]
    public DisplayItems[] OrderedItems => ParseOrder(Order) ?? ParseOrder(DefaultOrder)!;
    public static string FormatOrder(IEnumerable<DisplayItems> items) => string.Join(",", items);
    private static DisplayItems[]? ParseOrder(string? value)
    {
        var parts = (value ?? "").Split(',', StringSplitOptions.TrimEntries);
        var items = parts.Select(p => Enum.TryParse<DisplayItems>(p, out var d) ? d : DisplayItems.None).ToArray();
        return items.Length == 3 && items.Distinct().Count() == 3 && items.All(i => i is DisplayItems.Clock or DisplayItems.Date or DisplayItems.Timer) ? items : null;
    }
    // Backgrounds are a fixed choice: black, white, gray, chroma green, chroma blue.
    public static readonly string[] BackgroundChoices = ["#000000", "#FFFFFF", "#808080", "#00B140", "#0047BB"];
    // Appearance defaults restored by the settings window; items, position, mode, keys and language are kept.
    public AppSettings WithDefaultAppearance()
    {
        var d = new AppSettings();
        return this with { Layout = d.Layout, Order = d.Order, FontSize = d.FontSize, Foreground = d.Foreground,
            Background = d.Background, BackgroundOpacity = d.BackgroundOpacity, Outline = d.Outline, Icons = d.Icons };
    }
    public AppSettings Normalize()
    {
        var d = new AppSettings();
        return (this with {
            SchemaVersion = 1, Items = (Items & ~((DisplayItems)7)) == 0 ? Items : d.Items,
            LastItems = LastItems is > DisplayItems.None and <= (DisplayItems)7 ? LastItems : d.LastItems,
            Layout = Enum.IsDefined(Layout) ? Layout : d.Layout,
            FontSize = FontSize is >= 12 and <= 120 ? FontSize : d.FontSize,
            Order = ParseOrder(Order) != null ? FormatOrder(ParseOrder(Order)!) : d.Order,
            Foreground = ForegroundChoices.FirstOrDefault(c => string.Equals(c, Foreground, StringComparison.OrdinalIgnoreCase)) ?? d.Foreground,
            Background = BackgroundChoices.FirstOrDefault(c => string.Equals(c, Background, StringComparison.OrdinalIgnoreCase)) ?? d.Background,
            BackgroundOpacity = BackgroundOpacity is >= 0 and <= 100 ? BackgroundOpacity : 0,
            X = double.IsFinite(X) && Math.Abs(X) < 100000 ? X : 16,
            Y = double.IsFinite(Y) && Math.Abs(Y) < 100000 ? Y : 16,
            Monitor = Monitor ?? "",
            SpecifiedDate = SpecifiedDate.Year is >= 2000 and <= 2099 ? DateTime.SpecifyKind(SpecifiedDate, DateTimeKind.Unspecified) : d.SpecifiedDate
        }).WithKeys(Keys.Select(k => k?.IsValid == true ? k : null).ToArray());
    }
    public AppSettings WithItems(DisplayItems items) => this with { Items = items, LastItems = items != 0 ? items : LastItems };
    public AppSettings RestoreItems() => Items != 0 ? this : WithItems(LastItems != 0 ? LastItems : DisplayItems.Clock);
    // Merge only fields edited by the settings window, keeping concurrent tray/drag changes.
    public static AppSettings Merge(AppSettings baseline, AppSettings edited, AppSettings current)
    {
        var json = JsonSerializer.SerializeToNode(current)!.AsObject();
        var before = JsonSerializer.SerializeToNode(baseline)!.AsObject();
        foreach (var pair in JsonSerializer.SerializeToNode(edited)!.AsObject())
        {
            if (pair.Key is nameof(Items) or nameof(LastItems)) continue;
            if (!System.Text.Json.Nodes.JsonNode.DeepEquals(before[pair.Key], pair.Value)) json[pair.Key] = pair.Value?.DeepClone();
        }
        var changedItems = baseline.Items ^ edited.Items;
        return json.Deserialize<AppSettings>()!.WithItems((current.Items & ~changedItems) | (edited.Items & changedItems));
    }
}

public readonly record struct WorkArea(string Name, double Left, double Top, double Width, double Height, double Scale);
public readonly record struct Placement(double Left, double Top);
public static class Positioning
{
    public static Placement Restore(AppSettings s, WorkArea area, double widthDip, double heightDip) => new(
        Math.Clamp(area.Left + s.X * area.Scale, area.Left, Math.Max(area.Left, area.Left + area.Width - widthDip * area.Scale)),
        Math.Clamp(area.Top + s.Y * area.Scale, area.Top, Math.Max(area.Top, area.Top + area.Height - heightDip * area.Scale)));
}
