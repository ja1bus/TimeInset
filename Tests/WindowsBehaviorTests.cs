using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TimeInset.Core;
using TimeInset.Services;
using TimeInset.Views;
namespace TimeInset.Tests;
public class WindowsBehaviorTests
{
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
    [Theory][InlineData(DisplayLayout.Vertical)][InlineData(DisplayLayout.Horizontal)]
    public void TimerWidthIsStableAcrossStatesAndGrowsOnlyFromHundredHours(DisplayLayout layout) => Sta(() =>
    {
        var time = new FakeTime(); var model = new TimeModel(time); var view = new OverlayView(model);
        var s = new AppSettings { Items = DisplayItems.Timer, Layout = layout };
        var startSize = view.Configure(s); model.Start(); time.Advance(99 * 3600); view.Tick(); Assert.Equal(startSize.Width, view.Width);
        model.Pause(); view.Configure(s); Assert.Equal(startSize.Width, view.Width); Assert.Equal(startSize.Height, view.Height);
        model.Start(); time.Advance(3600); view.Tick(); double hundred = view.Width; Assert.True(hundred > startSize.Width);
        model.Pause(); view.Configure(s); Assert.Equal(hundred, view.Width); // does not shrink while the value is kept
        model.Reset(); view.Tick(); Assert.Equal(startSize.Width, view.Width);
    });
    [Theory][InlineData(1)][InlineData(2)][InlineData(3)][InlineData(4)][InlineData(5)][InlineData(6)][InlineData(7)]
    public void DisplayCombinationsHaveValidBounds(int items) => Sta(() =>
    {
        var view = new OverlayView(new(new FakeTime()));
        foreach (var layout in Enum.GetValues<DisplayLayout>())
        foreach (int size in new[] { 12, 28, 120 })
        foreach (string order in new[] { AppSettings.DefaultOrder, "Timer,Clock,Date" })
        {
            var measured = view.Configure(new() { Items = (DisplayItems)items, FontSize = size, Layout = layout, Order = order });
            Assert.True(measured.Width > 8 && measured.Height > 8);
            if ((items & 4) != 0) Assert.True(new Rect(measured).Contains(view.TimerBounds));
        }
    });
    [Theory][InlineData(true)][InlineData(false)]
    public void StackedDateHasTheSameWidthAsTheClock(bool icons) => Sta(() =>
    {
        var view = new OverlayView(new(new FakeTime()));
        double clockOnly = view.Configure(new() { Items = DisplayItems.Clock, Icons = icons }).Width;
        double withDate = view.Configure(new() { Items = DisplayItems.Clock | DisplayItems.Date, Icons = icons }).Width;
        Assert.InRange(withDate - clockOnly, -1, 1);
        // Shown alone or on one line, the date keeps the normal size and is wider than the clock.
        double dateOnly = view.Configure(new() { Items = DisplayItems.Date, Icons = icons }).Width;
        Assert.True(dateOnly > clockOnly + 4);
    });
    [Fact] public void OrderChangesTheVerticalPositionOfTheTimer() => Sta(() =>
    {
        var view = new OverlayView(new(new FakeTime()));
        var all = DisplayItems.Clock | DisplayItems.Date | DisplayItems.Timer;
        view.Configure(new() { Items = all, Order = "Timer,Clock,Date" }); double top = view.TimerBounds.Top;
        view.Configure(new() { Items = all, Order = "Clock,Date,Timer" }); double bottom = view.TimerBounds.Top;
        Assert.True(bottom > top);
    });
    [Fact] public void BackgroundZeroReallyHasZeroAlpha() => Sta(() =>
    {
        var view = new OverlayView(new(new FakeTime()));
        var size = view.Configure(new() { BackgroundOpacity = 0 });
        view.Measure(size); view.Arrange(new Rect(size));
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var pixel = new byte[4]; bitmap.CopyPixels(new Int32Rect(0, 0, 1, 1), pixel, 4, 0);
        Assert.Equal(0, pixel[3]);
    });
    // The fill is drawn over the outline, so a yellow digit must contain yellow pixels, not only outline black.
    [Fact] public void TextColorIsVisibleWithTheOutline() => Sta(() =>
    {
        var time = new FakeTime(); var view = new OverlayView(new(time));
        var size = view.Configure(new() { Items = DisplayItems.Clock, Icons = false, Foreground = "#FFD400", Outline = true, FontSize = 40 });
        view.Measure(size); view.Arrange(new Rect(size));
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        int w = bitmap.PixelWidth, h = bitmap.PixelHeight; var px = new byte[w * h * 4]; bitmap.CopyPixels(px, w * 4, 0);
        int yellow = 0, black = 0;
        for (int i = 0; i < px.Length; i += 4)
        {
            byte b = px[i], g = px[i + 1], r = px[i + 2], a = px[i + 3];
            if (a > 240 && r > 230 && g > 190 && b < 60) yellow++;
            if (a > 240 && r < 30 && g < 30 && b < 30) black++;
        }
        Assert.True(yellow > 100, $"yellow={yellow}"); Assert.True(black > 50, $"black={black}");
    });
    [Fact] public void NativeRegistrationFailureRetainsOldAssignmentAndDoesNotSave() => Sta(() =>
    {
        using var blocker = new HotkeyService(); using var tested = new HotkeyService();
        AppSettings occupied = FreeKey(blocker);
        var old = new AppSettings(); bool saved = false;
        tested.Register(old);
        Assert.Throws<ArgumentException>(() => tested.ApplyChanges(old, occupied, () => saved = true));
        Assert.False(saved); Assert.All(tested.Errors, e => Assert.Null(e));
    });
    [Fact] public void ProbeReportsAKeyInUseAndReleasesAFreeKey() => Sta(() =>
    {
        using var blocker = new HotkeyService(); using var tested = new HotkeyService();
        var occupied = FreeKey(blocker); var key = occupied.ToggleTimer!;
        string? reason = tested.Probe(key);
        Assert.NotNull(reason); Assert.Contains(key.ToString(), reason);
        blocker.Clear();
        Assert.Null(tested.Probe(key));
        Assert.Null(tested.Probe(key)); // the first probe released its trial registration
    });
    [Fact] public void EditingSuspendsKeysAndRestorationReportsNewConflicts() => Sta(() =>
    {
        using var tested = new HotkeyService(); using var blocker = new HotkeyService();
        var settings = new AppSettings { ToggleTimer = new(7, 0x76) };
        tested.Register(settings); Assert.Null(tested.Errors[0]);
        tested.Suspend();
        tested.ApplyChanges(settings, settings with { FontSize = 40 }, () => { });
        blocker.Register(settings); Assert.Null(blocker.Errors[0]);
        tested.Resume(settings); Assert.NotNull(tested.Errors[0]);
        blocker.Clear(); tested.Resume(settings); Assert.Null(tested.Errors[0]);
    });
    // Registers the first free Ctrl+Alt+Shift+F-key with the given service and returns those settings.
    private static AppSettings FreeKey(HotkeyService service)
    {
        AppSettings settings = new();
        foreach (uint key in new uint[] { 0x79, 0x78, 0x77, 0x76 })
        {
            settings = new AppSettings { ToggleTimer = new(7, key) }; service.Register(settings);
            if (service.Errors.All(e => e == null)) return settings;
        }
        throw new InvalidOperationException("No free test key");
    }
}
