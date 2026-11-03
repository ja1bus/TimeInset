using TimeInset.Core;
namespace TimeInset.Tests;
public class SettingsTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "TimeInset-tests-" + Guid.NewGuid());
    private string FilePath => Path.Combine(folder, "settings.json");
    [Fact] public void DefaultsAndRestoreLastNonEmpty()
    {
        var d = new AppSettings();
        Assert.Equal(DisplayItems.Clock | DisplayItems.Date, d.Items); Assert.Equal(28, d.FontSize);
        Assert.Equal([DisplayItems.Date, DisplayItems.Clock, DisplayItems.Timer], d.OrderedItems);
        var s = d.WithItems(DisplayItems.Timer).WithItems(0);
        Assert.Equal(DisplayItems.Timer, s.RestoreItems().Items);
    }
    [Fact] public void DefaultAppearanceKeepsItemsPositionModeInputAndKey()
    {
        var s = new AppSettings
        {
            Items = DisplayItems.Timer, Monitor = "m", X = 55, Topmost = false, ToggleTimer = new(2, 65), SpecifiedDate = new(2030, 1, 2),
            Layout = DisplayLayout.Horizontal, Order = "Timer,Clock,Date", FontSize = 60, Foreground = "#FFD400", Background = "#FFFFFF",
            BackgroundOpacity = 70, Outline = false, Icons = false,
        };
        var r = s.WithDefaultAppearance();
        Assert.Equal(DisplayItems.Timer, r.Items); Assert.Equal("m", r.Monitor); Assert.Equal(55, r.X); Assert.False(r.Topmost);
        Assert.Equal(s.ToggleTimer, r.ToggleTimer); Assert.Equal(s.SpecifiedDate, r.SpecifiedDate);
        var d = new AppSettings();
        Assert.Equal(d.Layout, r.Layout); Assert.Equal(d.Order, r.Order); Assert.Equal(d.FontSize, r.FontSize); Assert.Equal(d.Foreground, r.Foreground);
        Assert.Equal(d.Background, r.Background); Assert.Equal(d.BackgroundOpacity, r.BackgroundOpacity); Assert.True(r.Outline); Assert.True(r.Icons);
    }
    [Theory]
    [InlineData("Timer,Clock,Date", "Timer,Clock,Date")]
    [InlineData(" Clock , Timer , Date ", "Clock,Timer,Date")]
    [InlineData("Clock,Clock,Date", AppSettings.DefaultOrder)]
    [InlineData("Clock,Date", AppSettings.DefaultOrder)]
    [InlineData("Clock,Date,None", AppSettings.DefaultOrder)]
    [InlineData("", AppSettings.DefaultOrder)]
    public void OrderIsNormalizedToAPermutation(string order, string expected) => Assert.Equal(expected, new AppSettings { Order = order }.Normalize().Order);
    [Theory]
    [InlineData("#FFFFFF", "#000000")][InlineData("#A0A0A0", "#505050")][InlineData("#000000", "#FFFFFF")]
    [InlineData("#ffd400", "#000000")][InlineData("#00C853", "#000000")][InlineData("#2F80FF", "#FFFFFF")]
    public void EachTextColorHasAFixedOutline(string foreground, string outline) => Assert.Equal(outline, AppSettings.OutlineFor(foreground));
    [Fact] public void EveryTextColorChoiceHasAnOutline() =>
        Assert.All(AppSettings.ForegroundChoices, c => Assert.Matches("^#[0-9A-F]{6}$", AppSettings.OutlineFor(c)));
    [Fact] public void EditMergeRetainsConcurrentTrayAndDragChanges()
    {
        var baseline = new AppSettings(); var edited = baseline with { FontSize = 44 };
        var current = baseline with { X = 100, Topmost = false, Items = DisplayItems.Timer };
        var merged = AppSettings.Merge(baseline, edited, current);
        Assert.Equal(44, merged.FontSize); Assert.Equal(100, merged.X); Assert.False(merged.Topmost); Assert.Equal(DisplayItems.Timer, merged.Items);
    }
    [Fact] public void SaveLoadDoesNotContainRuntimeState()
    {
        var store = new SettingsStore(FilePath);
        var settings = new AppSettings { ToggleTimer = new(2, 65), SpecifiedDate = new(2030, 1, 2), Monitor = "Display2", X = -10, Order = "Timer,Date,Clock", Foreground = "#A0A0A0" };
        store.Save(settings); Assert.Equal(settings, store.Load());
        var json = File.ReadAllText(FilePath); Assert.DoesNotContain("Elapsed", json); Assert.DoesNotContain("IsSpecified", json); Assert.DoesNotContain("Keys", json); Assert.DoesNotContain("orderedItems", json); Assert.DoesNotContain("isValid", json);
    }
    [Fact] public void ItemEditsMergeEachCheckboxIndependently()
    {
        var baseline = new AppSettings();
        var edited = baseline.WithItems(DisplayItems.Date); // user turns clock off
        var current = baseline.WithItems(baseline.Items | DisplayItems.Timer); // tray turns timer on
        var merged = AppSettings.Merge(baseline, edited, current);
        Assert.Equal(DisplayItems.Date | DisplayItems.Timer, merged.Items);
        Assert.Equal(merged.Items, merged.LastItems);
    }
    [Fact] public void CorruptJsonIsPreservedAndReplacedSafely()
    {
        Directory.CreateDirectory(folder); File.WriteAllText(FilePath, "{broken");
        var store = new SettingsStore(FilePath); var settings = store.Load();
        Assert.NotNull(store.Warning); Assert.Contains(FilePath, store.Warning); Assert.Equal(new AppSettings(), settings);
        Assert.Equal("{broken", File.ReadAllText(Assert.Single(Directory.GetFiles(folder, "settings.corrupt-*"))));
        store.Save(settings); Assert.Equal(settings, store.Load());
    }
    [Fact] public void DuplicateKeyIsTreatedAsCorrupt()
    {
        const string content = "{\"fontSize\":28,\"fontSize\":40}";
        Directory.CreateDirectory(folder); File.WriteAllText(FilePath, content);
        var store = new SettingsStore(FilePath); var settings = store.Load();
        Assert.NotNull(store.Warning); Assert.Equal(new AppSettings(), settings);
        Assert.Equal(content, File.ReadAllText(Assert.Single(Directory.GetFiles(folder, "settings.corrupt-*"))));
        store.Save(settings); Assert.Equal(settings, store.Load());
    }
    [Fact] public void FutureSchemaIsNotOverwritten()
    {
        Directory.CreateDirectory(folder); File.WriteAllText(FilePath, "{\"SchemaVersion\":99}");
        var store = new SettingsStore(FilePath); store.Load(); Assert.Throws<IOException>(() => store.Save(new()));
        Assert.Contains("99", File.ReadAllText(FilePath));
    }
    [Fact] public void InvalidFieldTypeAndRemovedSettingsDoNotDiscardOtherSettings()
    {
        Directory.CreateDirectory(folder);
        // language / clickThrough / dateScale / startTimer are settings of the first build and are now ignored.
        File.WriteAllText(FilePath, "{\"fontSize\":\"bad\",\"language\":\"en\",\"clickThrough\":true,\"dateScale\":80,\"startTimer\":{\"modifiers\":2,\"key\":65},\"icons\":false,\"future\":true}");
        var store = new SettingsStore(FilePath); var value = store.Load();
        Assert.Equal(28, value.FontSize); Assert.False(value.Icons); Assert.Null(value.ToggleTimer);
        Assert.NotNull(store.Warning); Assert.Empty(Directory.GetFiles(folder, "settings.corrupt-*"));
    }
    [Fact] public void FailedWriteDoesNotReplaceExistingSettings()
    {
        var store = new SettingsStore(FilePath); store.Save(new()); string original = File.ReadAllText(FilePath);
        using var held = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var error = Record.Exception(() => store.Save(new() { FontSize = 42 }));
        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.Equal(original, File.ReadAllText(FilePath)); Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
    }
    [Fact] public void InvalidValuesAreCorrectedIndividually()
    {
        var v = new AppSettings { FontSize = 999, Foreground = "#123456", Background = "oops", X = double.NaN, Layout = (DisplayLayout)98, BackgroundOpacity = 101 }.Normalize();
        Assert.Equal(28, v.FontSize); Assert.Equal("#FFFFFF", v.Foreground); Assert.Equal("#000000", v.Background); Assert.Equal(16, v.X);
        Assert.Equal(DisplayLayout.Vertical, v.Layout); Assert.Equal(0, v.BackgroundOpacity);
        Assert.Equal("#FFD400", new AppSettings { Foreground = "#ffd400" }.Normalize().Foreground); // choices match without case
    }
    [Theory][InlineData(2,65,true)][InlineData(0,65,false)][InlineData(8,65,false)][InlineData(2,123,false)][InlineData(2,17,false)]
    public void HotkeyValidation(uint mod, uint key, bool valid) => Assert.Equal(valid, new Hotkey(mod, key).IsValid);
    [Theory][InlineData(-1920, 1)][InlineData(-1920, 1.5)][InlineData(0, 2)]
    public void PlacementUsesMonitorRelativeDips(double left, double scale)
    {
        var area = new WorkArea("test", left, -400, 1920, 1040, scale);
        var p = Positioning.Restore(new() { X = 16, Y = 20 }, area, 300, 100);
        Assert.Equal(left + 16 * scale, p.Left); Assert.Equal(-400 + 20 * scale, p.Top);
        var clamped = Positioning.Restore(new() { X = 5000, Y = -100 }, area, 300, 100);
        Assert.Equal(left + 1920 - 300 * scale, clamped.Left); Assert.Equal(-400, clamped.Top);
    }
    public void Dispose() { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
}
