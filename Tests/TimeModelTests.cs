using TimeInset.Core;
namespace TimeInset.Tests;
public sealed class FakeTime : ITimeSource
{
    public TimeSpan Elapsed { get; set; }
    public DateTime LocalNow { get; set; } = new(2026, 11, 2, 23, 59, 59);
    public void Advance(double seconds) { Elapsed += TimeSpan.FromSeconds(seconds); LocalNow = LocalNow.AddSeconds(seconds); }
}
public class TimeModelTests
{
    [Fact] public void TransitionsPreserveFractionAndIgnoreRepeatedStart()
    {
        var time = new FakeTime(); var model = new TimeModel(time);
        model.Pause(); Assert.Equal(TimerState.Ready, model.State);
        model.Start(); time.Advance(600.75); model.Start(); model.Pause();
        time.Advance(300); model.Pause(); Assert.Equal(600.75, model.Elapsed.TotalSeconds);
        model.Start(); time.Advance(.5); Assert.Equal(601.25, model.Elapsed.TotalSeconds);
        model.Reset(); time.Advance(10); Assert.Equal(TimeSpan.Zero, model.Elapsed); Assert.Equal(TimerState.Ready, model.State);
    }
    [Theory][InlineData(24, "24:00:00")][InlineData(99, "99:00:00")][InlineData(100, "100:00:00")][InlineData(1000, "1000:00:00")]
    public void TotalHoursNeverWrap(int hours, string expected) => Assert.Equal(expected, TimeModel.Format(TimeSpan.FromHours(hours)));
    [Fact] public void WallClockChangesDoNotChangeTimerOrSpecifiedClock()
    {
        var t = new FakeTime(); var m = new TimeModel(t); m.Start();
        m.UseSpecified(new(2000, 12, 31, 23, 59, 59)); t.Advance(2); t.LocalNow = t.LocalNow.AddYears(3);
        Assert.Equal(new DateTime(2001, 1, 1, 0, 0, 1), m.Now); Assert.Equal(2, m.Elapsed.TotalSeconds);
        m.UseCurrent(); Assert.Equal(t.LocalNow, m.Now);
        var restarted = new TimeModel(t); Assert.False(restarted.IsSpecified); Assert.Equal(TimerState.Ready, restarted.State);
    }
    [Fact] public void LongSuspensionCountsOnlyWhileRunning()
    {
        var t = new FakeTime(); var m = new TimeModel(t); m.Start(); t.Advance(86400); Assert.Equal(86400, m.Elapsed.TotalSeconds);
        m.Pause(); t.Advance(86400); Assert.Equal(86400, m.Elapsed.TotalSeconds);
    }
}
