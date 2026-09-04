namespace StarTruckMP.Server.Server.Services;

/// <summary>
/// A token bucket: <see cref="Allow"/> says yes while the caller stays under a steady rate,
/// with room for a burst above it. Used per player and per kind of packet, so one client that
/// floods the server cannot have its flood relayed to everyone else. Relay-thread only.
/// </summary>
public sealed class RateLimiter
{
    private readonly double _perMillisecond;
    private readonly double _burst;
    private double _tokens;
    private long _lastTicks;

    /// <param name="perSecond">The steady rate allowed.</param>
    /// <param name="burst">How many may come at once above that rate before any are refused.</param>
    public RateLimiter(double perSecond, double burst)
    {
        _perMillisecond = perSecond / 1000.0;
        _burst = burst;
        _tokens = burst;
        _lastTicks = Environment.TickCount64;
    }

    public bool Allow()
    {
        var now = Environment.TickCount64;
        var elapsed = now - _lastTicks;
        _lastTicks = now;

        _tokens = Math.Min(_burst, _tokens + elapsed * _perMillisecond);
        if (_tokens < 1.0) return false;

        _tokens -= 1.0;
        return true;
    }
}
