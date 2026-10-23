namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// A clock that moves only when a test moves it, so a run's elapsed time and
/// throughput are exact: a <see cref="TimeProvider"/> whose timestamp is
/// counted in ticks of 100 ns, starting at zero.
/// </summary>
internal sealed class SteppedClock : TimeProvider
{
    private long _ticks;

    /// <summary>Moves the clock on by <paramref name="by"/>.</summary>
    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => Interlocked.Read(ref _ticks);
}
