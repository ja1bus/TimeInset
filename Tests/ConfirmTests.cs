using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Threading;
using TimeInset.Core;
using TimeInset.Core.Localization;
using TimeInset.Views;
namespace TimeInset.Tests;
// 1.1.0: one confirmation at a time, requests ignored once exiting, placement, and the reset confirmation setting (spec §16).
public class ConfirmTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "TimeInset-tests-" + Guid.NewGuid());
    private string FilePath => Path.Combine(folder, "settings.json");
    public void Dispose() { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    private SettingsStore Write(string json) { Directory.CreateDirectory(folder); File.WriteAllText(FilePath, json); return new SettingsStore(FilePath); }

    [Fact] public void GateAllowsOneConfirmationAndBringsTheOpenOneToFront()
    {
        var gate = new ConfirmGate(); int fronts = 0;
        Assert.True(gate.TryOpen(() => fronts++)); Assert.True(gate.IsOpen); Assert.Equal(0, fronts);
        Assert.False(gate.TryOpen(() => fronts++)); Assert.True(gate.IsOpen); Assert.Equal(1, fronts);
        Assert.False(gate.TryOpen(() => fronts++)); Assert.False(gate.TryOpen(() => fronts++)); Assert.Equal(3, fronts);
        gate.Close(); Assert.False(gate.IsOpen);
        Assert.True(gate.TryOpen(() => fronts++)); Assert.True(gate.IsOpen); Assert.Equal(3, fronts);
    }
    [Fact] public void GateIgnoresRequestsOnceExitingAndExitsOnlyOnce()
    {
        var gate = new ConfirmGate(); int fronts = 0;
        Assert.True(gate.BeginExit()); Assert.True(gate.Exiting);
        Assert.False(gate.BeginExit());
        Assert.False(gate.TryOpen(() => fronts++)); Assert.False(gate.IsOpen); Assert.Equal(0, fronts);
        // Exiting while a confirmation is open (Windows sign-out): further requests are ignored, not brought to front.
        var open = new ConfirmGate();
        Assert.True(open.TryOpen(() => fronts++)); Assert.True(open.BeginExit());
        Assert.False(open.TryOpen(() => fronts++)); Assert.Equal(0, fronts);
    }

    [Theory]
    [InlineData(0, 0, 1920, 1040, 400, 150, 760, 445)]
    [InlineData(-1920, 0, 1920, 1040, 400, 150, -1160, 445)]
    [InlineData(0, 48, 1920, 1032, 400, 150, 760, 489)]
    [InlineData(100, 0, 800, 600, 798, 598, 101, 1)]
    [InlineData(100, 0, 800, 600, 799, 597, 100, 1)]   // odd difference is floored
    [InlineData(100, 0, 800, 600, 800, 600, 100, 0)]
    [InlineData(100, 0, 800, 600, 801, 601, 100, 0)]
    [InlineData(100, 0, 800, 600, 2000, 300, 100, 150)] // only the width is larger
    [InlineData(100, 0, 800, 600, 300, 1000, 350, 0)]   // only the height is larger
    public void CenterPlacesInsideTheWorkArea(double l, double t, double w, double h, double cw, double ch, double x, double y)
    {
        var p = Positioning.Center(new WorkArea("", l, t, w, h, 1), cw, ch);
        Assert.Equal(x, p.Left); Assert.Equal(y, p.Top);
    }

    [Fact] public void ConfirmResetDefaultsOnAndRoundTrips()
    {
        Assert.True(new AppSettings().ConfirmReset);
        var store = Write("""{"schemaVersion":1,"fontSize":40}""");
        var s = store.Load(); Assert.True(s.ConfirmReset); Assert.Null(store.Warning); Assert.Equal(40, s.FontSize);
        store = Write("""{"schemaVersion":1,"confirmReset":false}""");
        Assert.False(store.Load().ConfirmReset); Assert.Null(store.Warning);
        store = Write("""{"schemaVersion":1,"confirmReset":true}""");
        Assert.True(store.Load().ConfirmReset);
        store.Save(new AppSettings { ConfirmReset = false });
        var json = File.ReadAllText(FilePath);
        Assert.Contains("\"confirmReset\": false", json); Assert.Contains("\"schemaVersion\": 1", json);
        Assert.False(new SettingsStore(FilePath).Load().ConfirmReset);
    }
    [Theory]
    [InlineData("\"false\"")][InlineData("0")][InlineData("1")][InlineData("null")][InlineData("{}")][InlineData("[]")]
    public void WrongConfirmResetTypeFallsBackToOnAndWarns(string value)
    {
        var store = Write($$"""{"schemaVersion":1,"fontSize":40,"topmost":false,"confirmReset":{{value}}}""");
        var s = store.Load();
        Assert.True(s.ConfirmReset); Assert.Equal(40, s.FontSize); Assert.False(s.Topmost);
        Assert.NotNull(store.Warning);
        Assert.Contains(Strings.SettingsAdjusted, store.Warning); Assert.Contains(FilePath, store.Warning); Assert.Contains("ConfirmReset", store.Warning);
    }
    [Fact] public void ConfirmResetIsKeptByDefaultAppearanceAndMergedOnlyWhenEdited()
    {
        Assert.False(new AppSettings { ConfirmReset = false }.WithDefaultAppearance().ConfirmReset);
        var baseline = new AppSettings();
        var merged = AppSettings.Merge(baseline, baseline with { ConfirmReset = false }, baseline with { Topmost = false });
        Assert.False(merged.ConfirmReset); Assert.False(merged.Topmost);
        // Not edited on screen: the saved value wins.
        merged = AppSettings.Merge(baseline, baseline, baseline with { ConfirmReset = false });
        Assert.False(merged.ConfirmReset);
    }

    [Fact] public void NewStringsAreJapaneseEvenInEnglishUi()
    {
        var before = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
            Assert.Equal("確認", Strings.Confirmations); Assert.Equal("タイマーをリセットするときに確認する", Strings.ConfirmReset);
            Assert.Equal("はい", Strings.Yes); Assert.Equal("いいえ", Strings.No);
            Assert.Equal("計測値は失われます。TimeInsetを終了しますか？", Strings.ExitPrompt);
            Assert.Equal("計測を停止し、0秒にリセットしますか？", Strings.ResetPrompt);
        }
        finally { System.Globalization.CultureInfo.CurrentUICulture = before; }
    }

    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
    [Theory][InlineData(true)][InlineData(false)]
    public void ConfirmWindowHasYesNoAndIsTopmost(bool warning) => Sta(() =>
    {
        var w = new ConfirmWindow("message", warning, new System.Drawing.Point(0, 0));
        Assert.Equal("TimeInset", w.Title);
        Assert.Equal("はい", w.YesButton.Content); Assert.Equal("いいえ", w.NoButton.Content);
        Assert.True(w.YesButton.IsDefault); Assert.False(w.YesButton.IsCancel);
        Assert.True(w.NoButton.IsCancel); Assert.False(w.NoButton.IsDefault);
        Assert.True(w.Topmost); Assert.True(w.ShowInTaskbar); Assert.Equal(ResizeMode.NoResize, w.ResizeMode);
        w.Close();
    });
    // Through the automation peer so Button.OnClick runs (IsCancel closes the dialog there).
    private static void Press(System.Windows.Controls.Button b) =>
        ((System.Windows.Automation.Provider.IInvokeProvider)new System.Windows.Automation.Peers.ButtonAutomationPeer(b)
            .GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)).Invoke();
    // How the dialog is closed → ShowDialog result. Only "yes" approves.
    [Theory][InlineData("yes", true)][InlineData("no", false)][InlineData("close", false)]
    public void OnlyYesApproves(string answer, bool approved) => Sta(() =>
    {
        var w = new ConfirmWindow("message", true, new System.Drawing.Point(0, 0));
        w.Loaded += (_, _) => w.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            if (answer == "yes") Press(w.YesButton);
            else if (answer == "no") Press(w.NoButton);
            else w.Close();
        });
        Assert.Equal(approved, w.ShowDialog() == true);
    });
}
