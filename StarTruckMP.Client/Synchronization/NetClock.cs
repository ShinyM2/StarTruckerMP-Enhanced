using System.Diagnostics;

namespace StarTruckMP.Client.Synchronization;

/// <summary>
/// The clock that timestamps movement.
///
/// <c>Environment.TickCount64</c> looked like the obvious choice and ticks in steps of about
/// sixteen milliseconds on Windows. Packets go out every thirty-three, so their timestamps came
/// out as 31, 47, 62, 78 … — a playback timeline that alternately hurried and dawdled, which is
/// exactly the twitch players saw at speed. <see cref="Stopwatch"/> resolves microseconds.
/// </summary>
internal static class NetClock
{
    private static readonly double TicksPerMillisecond = Stopwatch.Frequency / 1000.0;

    /// <summary>Milliseconds since the process started, to the microsecond.</summary>
    public static long Milliseconds => (long)(Stopwatch.GetTimestamp() / TicksPerMillisecond);

    /// <summary>The same clock in seconds, for arithmetic.</summary>
    public static double Seconds => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
}
