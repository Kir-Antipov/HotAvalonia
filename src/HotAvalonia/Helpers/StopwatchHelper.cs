using System.Diagnostics;

namespace HotAvalonia.Helpers;

/// <summary>
/// Provides utility methods for working with the <see cref="Stopwatch"/> class and measuring time intervals.
/// </summary>
internal static class StopwatchHelper
{
    /// <summary>
    /// The number of ticks per millisecond.
    /// </summary>
    private const long TicksPerMillisecond = 10000;

    /// <summary>
    /// The number of ticks per second.
    /// </summary>
    private const long TicksPerSecond = TicksPerMillisecond * 1000;

    /// <summary>
    /// The tick frequency for the underlying timer mechanism.
    /// </summary>
    private static readonly double s_tickFrequency = (double)TicksPerSecond / Stopwatch.Frequency;

    /// <summary>
    /// Returns the current number of ticks in the timer mechanism.
    /// </summary>
    /// <returns>
    /// A long integer representing the tick counter value of the underlying timer mechanism.
    /// </returns>
    public static long GetTimestamp() => Stopwatch.GetTimestamp();

    /// <summary>
    /// Returns the elapsed time since the <paramref name="startingTimestamp"/> value retrieved using <see cref="GetTimestamp"/>.
    /// </summary>
    /// <param name="startingTimestamp">The timestamp marking the beginning of the time period.</param>
    /// <returns>
    /// A <see cref="TimeSpan"/> for the elapsed time between the starting timestamp and the time of this call.
    /// </returns>
    public static TimeSpan GetElapsedTime(long startingTimestamp)
        => GetElapsedTime(startingTimestamp, GetTimestamp());

    /// <summary>
    /// Returns the elapsed time between two timestamps retrieved using <see cref="GetTimestamp"/>.
    /// </summary>
    /// <param name="startingTimestamp">The timestamp marking the beginning of the time period.</param>
    /// <param name="endingTimestamp">The timestamp marking the end of the time period.</param>
    /// <returns>
    /// A <see cref="TimeSpan"/> for the elapsed time between the starting and ending timestamps.
    /// </returns>
    public static TimeSpan GetElapsedTime(long startingTimestamp, long endingTimestamp)
        => new((long)((endingTimestamp - startingTimestamp) * s_tickFrequency));
}
