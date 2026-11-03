using System.Diagnostics;
using System.Globalization;

namespace TimeInset.Core;

public interface ITimeSource { TimeSpan Elapsed { get; } DateTime LocalNow { get; } }
public sealed class SystemTimeSource : ITimeSource
{
    private readonly Stopwatch watch = Stopwatch.StartNew();
    public TimeSpan Elapsed => watch.Elapsed;
    public DateTime LocalNow => DateTime.Now;
}
public enum TimerState { Ready, Running, Paused }
public sealed class TimeModel(ITimeSource source)
{
    private TimeSpan accumulated, startedAt, specifiedAt;
    private DateTime specified;
    public TimerState State { get; private set; }
    public bool IsSpecified { get; private set; }
    public TimeSpan Elapsed => accumulated + (State == TimerState.Running ? source.Elapsed - startedAt : TimeSpan.Zero);
    public DateTime Now => IsSpecified ? specified.Add(source.Elapsed - specifiedAt) : source.LocalNow;
    public void Start() { if (State == TimerState.Running) return; startedAt = source.Elapsed; State = TimerState.Running; }
    public void Pause() { if (State != TimerState.Running) return; accumulated = Elapsed; State = TimerState.Paused; }
    public void Reset() { accumulated = TimeSpan.Zero; State = TimerState.Ready; }
    public void UseCurrent() => IsSpecified = false;
    public void UseSpecified(DateTime value) { specified = value; specifiedAt = source.Elapsed; IsSpecified = true; }
    public static string Format(TimeSpan elapsed)
    {
        var seconds = (long)Math.Max(0, elapsed.TotalSeconds);
        return string.Create(CultureInfo.InvariantCulture, $"{seconds / 3600:00}:{seconds / 60 % 60:00}:{seconds % 60:00}");
    }
}
