using System;
using System.Collections.Concurrent;

namespace StarTruckMP.Client.Synchronization;

/// <summary>
/// One remote player's clock, as seen from here: which moment of their time is being drawn
/// right now, and how far behind live that has to be for the drawing to stay smooth.
///
/// Every position packet carries the sender's own clock. Comparing it with the moment the packet
/// landed gives "their clock minus ours" plus the trip; the largest such value over the last
/// few seconds is the fastest trip seen, and every other packet is measured against it as
/// <em>lateness</em>. The playback delay is then set from what the link has actually been doing:
/// the worst lateness lately, the longest run of packets that went missing in a row, and a frame
/// of slack — never from a guess, and never grown after the fact by a truck that already froze.
///
/// Playback itself is a clock that runs at very nearly one second per second. When the delay it
/// should have differs from the delay it has, it runs a little slow or a little fast until they
/// agree, so the truck never jumps to a new offset; it drifts there over a fraction of a second at
/// a pace nobody notices. A single timeline serves the cab and every trailer of the same player,
/// so the train is always sampled at one moment of its owner's time and stays coupled.
///
/// Records arrive on a network thread; the frame advances on the game thread.
/// </summary>
internal sealed class RemoteTimeline
{
    /// <summary>What the sender does; see <c>GameEventsComponent.SendInterval</c>.</summary>
    public const double SendInterval = 0.04;

    /// <summary>The delay is never shorter than this, whatever the link looks like.</summary>
    private const double MinDelay = 0.08;

    /// <summary>Nor longer: past this a link is so bad the truck is better coasting than crawling behind.</summary>
    private const double MaxDelay = 0.45;

    /// <summary>Slack for the frame in which a packet lands too late to be drawn that frame.</summary>
    private const double FrameMargin = 0.02;

    /// <summary>The worst lateness over the last three seconds sets the jitter part of the delay.</summary>
    private const double JitterBucketSeconds = 0.5;
    private const int JitterBuckets = 6;

    /// <summary>The longest run of lost packets over the last five seconds sets the loss part.</summary>
    private const double BurstBucketSeconds = 1.0;
    private const int BurstBuckets = 5;

    /// <summary>
    /// The fastest trip is remembered for ten seconds: long enough that a lucky packet does not
    /// need to repeat itself, short enough that two clocks running at slightly different rates,
    /// or a sender whose physics clock fell behind after a long hitch, do not skew it for long.
    /// </summary>
    private const double OffsetBucketSeconds = 2.0;
    private const int OffsetBuckets = 5;

    /// <summary>
    /// How hard playback pushes towards where it should be: an error of 100 ms plays at 1.15x
    /// or 0.85x, and the error is gone in well under a second. The cap keeps a truck from ever
    /// visibly hurrying.
    /// </summary>
    private const double RateGain = 1.5;
    private const double RateLimit = 0.15;

    /// <summary>Further off than this and drifting would take seconds; the playback just jumps once.</summary>
    private const double SnapError = 0.6;

    /// <summary>The delay may grow at once but shrinks no faster than this, so a quiet spell does not oscillate it.</summary>
    private const double DelayShrinkPerSecond = 0.04;

    private readonly object _lock = new();

    private readonly WindowMax _offsets = new(OffsetBucketSeconds, OffsetBuckets);
    private readonly WindowMax _lateness = new(JitterBucketSeconds, JitterBuckets);
    private readonly WindowMax _bursts = new(BurstBucketSeconds, BurstBuckets);

    private bool _known;
    private double _offset;

    private bool _playing;
    private double _playback;
    private double _rate = 1.0;
    private double _delay = MinDelay;

    private int _lastFrame = -1;
    private double _lastNow;

    /// <summary>The moment of the sender's time to draw, valid after the first <see cref="Advance"/> that had data.</summary>
    public double Playback { get { lock (_lock) return _playback; } }

    /// <summary>How fast playback is running relative to real time this frame, about 1.</summary>
    public double Rate { get { lock (_lock) return _rate; } }

    /// <summary>How far behind live the sender is drawn right now.</summary>
    public double DelaySeconds { get { lock (_lock) return _delay; } }

    public bool HasData { get { lock (_lock) return _known; } }

    /// <summary>
    /// A packet stamped <paramref name="sentAt"/> by the sender landed here at <paramref name="arrivedAt"/>.
    /// Any thread.
    /// </summary>
    public void Record(double sentAt, double arrivedAt)
    {
        lock (_lock)
        {
            var raw = sentAt - arrivedAt;
            _offsets.Add(arrivedAt, raw);
            _offset = _offsets.Max(arrivedAt);
            _known = true;

            // Zero for the fastest packet seen lately, positive for everything slower than it.
            _lateness.Add(arrivedAt, Math.Max(0.0, _offset - raw));
        }
    }

    /// <summary>
    /// The packet that just landed was preceded by <paramref name="lost"/> packets that did not,
    /// so the arrivals had a hole that many sends wide. Any thread.
    /// </summary>
    public void NoteBurst(int lost, double arrivedAt)
    {
        if (lost <= 0) return;
        lock (_lock) _bursts.Add(arrivedAt, lost);
    }

    /// <summary>
    /// Moves playback on by one rendered frame. Idempotent within a frame, so the cab and the
    /// trailers may all call it. Game thread.
    /// </summary>
    public void Advance(double now, int frame)
    {
        lock (_lock)
        {
            if (frame == _lastFrame) return;
            var dt = _lastFrame >= 0 ? Math.Max(0.0, Math.Min(now - _lastNow, 0.25)) : 0.0;
            _lastFrame = frame;
            _lastNow = now;

            if (!_known) return;

            UpdateDelay(now, dt);

            var target = now + _offset - _delay;

            if (!_playing || Math.Abs(target - _playback) > SnapError)
            {
                _playing = true;
                _playback = target;
                _rate = 1.0;
                return;
            }

            var error = target - _playback;
            _rate = 1.0 + Math.Max(-RateLimit, Math.Min(RateLimit, error * RateGain));
            _playback += dt * _rate;
        }
    }

    private void UpdateDelay(double now, double dt)
    {
        var jitter = _lateness.Max(now);
        if (double.IsNegativeInfinity(jitter)) jitter = 0;

        var burst = _bursts.Max(now);
        if (double.IsNegativeInfinity(burst)) burst = 0;

        // Interpolation needs the state after the playback point to have arrived. With a hole of
        // `burst` sends in the arrivals that is (burst + 1) sends, plus half a send so the
        // ordinary spacing never quite runs dry, plus what the link has been late by.
        var wanted = SendInterval * (burst + 1.5) + jitter + FrameMargin;
        wanted = Math.Max(MinDelay, Math.Min(MaxDelay, wanted));

        if (wanted >= _delay) _delay = wanted;
        else _delay = Math.Max(wanted, _delay - DelayShrinkPerSecond * dt);
    }

    /// <summary>The largest value recorded over the last few buckets of time; -infinity when empty.</summary>
    private sealed class WindowMax
    {
        private readonly double _bucketSeconds;
        private readonly long[] _ids;
        private readonly double[] _max;

        public WindowMax(double bucketSeconds, int buckets)
        {
            _bucketSeconds = bucketSeconds;
            _ids = new long[buckets];
            _max = new double[buckets];
            for (var i = 0; i < buckets; i++) _ids[i] = long.MinValue;
        }

        public void Add(double time, double value)
        {
            var id = (long)Math.Floor(time / _bucketSeconds);
            var slot = (int)(((id % _ids.Length) + _ids.Length) % _ids.Length);
            if (_ids[slot] != id)
            {
                _ids[slot] = id;
                _max[slot] = value;
            }
            else if (value > _max[slot])
            {
                _max[slot] = value;
            }
        }

        public double Max(double now)
        {
            var id = (long)Math.Floor(now / _bucketSeconds);
            var oldest = id - _ids.Length + 1;
            var result = double.NegativeInfinity;
            for (var i = 0; i < _ids.Length; i++)
            {
                if (_ids[i] < oldest || _ids[i] > id) continue;
                if (_max[i] > result) result = _max[i];
            }

            return result;
        }
    }

    // ---------------------------------------------------------------------------------------
    // One per player
    // ---------------------------------------------------------------------------------------

    private static readonly ConcurrentDictionary<int, RemoteTimeline> Timelines = new();

    /// <summary>The timeline for a player, made on first use. Any thread.</summary>
    public static RemoteTimeline For(int netId) => Timelines.GetOrAdd(netId, _ => new RemoteTimeline());

    /// <summary>Drops what was learned about a player who left or went out of sight.</summary>
    public static void Forget(int netId) => Timelines.TryRemove(netId, out _);

    public static void ForgetAll() => Timelines.Clear();
}
