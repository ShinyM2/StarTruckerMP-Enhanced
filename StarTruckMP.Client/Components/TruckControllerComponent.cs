using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.Attributes;
using StarTruckMP.Client.Synchronization;
using StarTruckMP.Shared.Movement;
using UnityEngine;

namespace StarTruckMP.Client.Components;

/// <summary>
/// Moves a remote player's truck, or their trailer, the way its owner moved it.
///
/// States arrive a few times a second, late, unevenly and sometimes not at all. The copy is
/// therefore drawn a little in the past, at a moment of the owner's time chosen by the player's
/// <see cref="RemoteTimeline"/>, between the two real states either side of that moment. The
/// curve between them follows the velocities the owner reported, so a state that went missing
/// leaves no corner in the path; and every packet repeats the states before it, so most missing
/// states turn up anyway. Only when nothing at all has arrived for the moment being drawn does
/// the truck coast on its last velocity and spin, and when real data returns after that, the gap
/// between where the coasting put it and where it really was is closed over a fraction of a
/// second rather than in one jump.
///
/// States are put in by <see cref="MovementReceiver"/> on the network thread the instant they
/// land; the transform is placed on the game thread once per rendered frame.
/// </summary>
public class TruckControllerComponent : MonoBehaviour
{
    public TruckControllerComponent(IntPtr ptr) : base(ptr) { }

    /// <summary>The longest the truck coasts on its last velocity once the buffer is exhausted.</summary>
    private const double MaxExtrapolation = 0.6;

    /// <summary>States older than this behind the playback point are of no further use.</summary>
    private const double KeepBehind = 1.0;

    /// <summary>The most states kept per stream: well over a second at the send rate, with history.</summary>
    private const int MaxSnapshots = 96;

    /// <summary>A gap wider than this between two states is bridged in a straight line: the velocities are stale by then.</summary>
    private const double HermiteMaxSpan = 0.5;

    /// <summary>A frame-to-frame jump larger than this is a discontinuity to be smoothed over, not motion.</summary>
    private const float JumpMetres = 0.35f;
    private const float JumpDegrees = 4f;

    /// <summary>And larger than this is a warp or a respawn, shown as it is.</summary>
    private const float TeleportMetres = 200f;

    /// <summary>How quickly a smoothed-over discontinuity fades: about two thirds gone after this long.</summary>
    private const float SmoothingSeconds = 0.18f;

    /// <summary>
    /// The stamp of a seed state: far enough in the past that any real state comes after it, and
    /// finite so the interpolation between the two stays arithmetic.
    /// </summary>
    private const double SeedTime = -1e6;

    private struct Snapshot
    {
        public double Time;
        public uint Seq;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Velocity;
        public Vector3 AngVel;
    }

    private readonly List<Snapshot> _snapshots = new();
    private readonly object _lock = new();

    /// <summary>The player this truck belongs to.</summary>
    public int NetId = -1;

    private RemoteTimeline _timeline;

    private volatile bool _hasData;
    private bool _shown;

    // Smoothing over discontinuities: what was sampled last frame, and the offset still being faded.
    private bool _hasPrevious;
    private Vector3 _previousPosition;
    private Vector3 _previousVelocity;
    private Quaternion _previousRotation;
    private Vector3 _previousAngVel;
    private Vector3 _positionError;
    private Quaternion _rotationError = Quaternion.identity;

    private Rigidbody _rb;

    // hidden until there is a real position to show it at
    private GameObject _npcTruckVisual;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        if (_rb != null)
        {
            // Kinematic and not interpolated: the transform is placed directly every rendered
            // frame below, and physics interpolation would only fight that.
            _rb.isKinematic = true;
            _rb.interpolation = RigidbodyInterpolation.None;
        }

        _npcTruckVisual = transform.Find("NPCTruck")?.gameObject;
    }

    [HideFromIl2Cpp]
    private RemoteTimeline Timeline()
    {
        if (_timeline == null && NetId >= 0) _timeline = RemoteTimeline.For(NetId);
        return _timeline;
    }

    // ---------------------------------------------------------------------------------------
    // Taking states in (network thread)
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Slots a state into the list by the sender's time. A state already known, by its counter,
    /// is not added twice. Touches nothing of Unity's, so any thread may call it.
    /// </summary>
    [HideFromIl2Cpp]
    public bool Insert(uint seq, double time, in BodyState state)
    {
        var snapshot = new Snapshot
        {
            Time = time,
            Seq = seq,
            Position = new Vector3(state.Position.X, state.Position.Y, state.Position.Z),
            Rotation = new Quaternion(state.Rotation.X, state.Rotation.Y, state.Rotation.Z, state.Rotation.W),
            Velocity = new Vector3(state.Velocity.X, state.Velocity.Y, state.Velocity.Z),
            AngVel = new Vector3(state.AngVel.X, state.AngVel.Y, state.AngVel.Z)
        };

        bool inserted;
        lock (_lock)
        {
            inserted = InsertLocked(snapshot);
            while (_snapshots.Count > MaxSnapshots) _snapshots.RemoveAt(0);
        }

        if (inserted) _hasData = true;
        return inserted;
    }

    /// <summary>
    /// Where the truck last was according to the server, for a copy spawned before its first
    /// packet: a parked truck reports itself only once a second, and without this it would stand
    /// in the wrong place until then. The state is placed in the far past and holds still, so
    /// the first real state simply takes over from it. Game thread, before any packet.
    /// </summary>
    [HideFromIl2Cpp]
    public void Seed(in BodyState state)
    {
        var resting = state;
        resting.Velocity = default;
        resting.AngVel = default;
        Insert(0, SeedTime, resting);
    }

    /// <summary>
    /// Takes over the states of the mover this one replaces, so a truck rebuilt for a new
    /// trailer carries on from where the old copy was rather than waiting for the next packet.
    /// </summary>
    public void TakeOver(TruckControllerComponent previous)
    {
        if (previous == null || ReferenceEquals(previous, this)) return;

        List<Snapshot> copy;
        lock (previous._lock) copy = new List<Snapshot>(previous._snapshots);
        if (copy.Count == 0) return;

        lock (_lock)
        {
            foreach (var snapshot in copy) InsertLocked(snapshot);
            while (_snapshots.Count > MaxSnapshots) _snapshots.RemoveAt(0);
        }

        _hasData = true;
    }

    [HideFromIl2Cpp]
    private bool InsertLocked(Snapshot snapshot)
    {
        var index = _snapshots.Count;
        while (index > 0 && _snapshots[index - 1].Time > snapshot.Time) index--;

        if (index > 0 && _snapshots[index - 1].Seq == snapshot.Seq) return false;
        if (index < _snapshots.Count && _snapshots[index].Seq == snapshot.Seq) return false;

        _snapshots.Insert(index, snapshot);
        return true;
    }

    // ---------------------------------------------------------------------------------------
    // Placing the truck (game thread)
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Placed once per rendered frame, after the game's own Update.
    ///
    /// This used to run in FixedUpdate through the rigidbody. Two things were wrong with that at
    /// speed: the physics step and the rendered frame do not line up, so a 50 Hz placement
    /// showed through as a stutter at higher frame rates; and the game recentres its scene in
    /// its Update, so for the rest of that frame a truck placed under the old origin sat a whole
    /// shift away from where it belonged. LateUpdate follows the recentre and the frame alike.
    /// </summary>
    void LateUpdate()
    {
        if (!_hasData) return;

        var timeline = Timeline();
        if (timeline == null) return;

        var now = NetClock.Seconds;
        timeline.Advance(now, Time.frameCount);
        var playback = timeline.Playback;
        var rate = (float)timeline.Rate;

        Vector3 position, velocity, angVel;
        Quaternion rotation;

        lock (_lock)
        {
            if (_snapshots.Count == 0) return;

            // Forget what is well behind the playback point, keeping one state before it.
            while (_snapshots.Count > 2 && _snapshots[1].Time < playback - KeepBehind)
                _snapshots.RemoveAt(0);

            Sample(playback, out position, out rotation, out velocity, out angVel);
        }

        var dt = Time.unscaledDeltaTime;
        Smooth(position, rotation, velocity, angVel, dt * rate);
        Fade(dt);

        _previousPosition = position;
        _previousRotation = rotation;
        _previousVelocity = velocity;
        _previousAngVel = angVel;
        _hasPrevious = true;

        // Converted every frame rather than once on arrival: the scene can be recentred between
        // two packets, and a target left in stale scene coordinates would drag the truck across
        // the sector until the next update landed.
        transform.SetPositionAndRotation(FloatingOrigin.ToScene(position + _positionError), _rotationError * rotation);

        if (!_shown)
        {
            _shown = true;
            if (_npcTruckVisual != null) _npcTruckVisual.SetActive(true);
        }

        MultiplayerState.InterpolationMs = (int)(timeline.DelaySeconds * 1000);
    }

    /// <summary>
    /// Notices when the sampled state jumped from where last frame's state was heading — the end
    /// of a coast, a state that landed behind the playback point — and folds the jump into an
    /// offset that <see cref="Fade"/> then takes away gradually, so the truck glides to its
    /// corrected place instead of teleporting there. A jump too big to be an error is a warp and
    /// is shown as it is.
    /// </summary>
    private void Smooth(Vector3 position, Quaternion rotation, Vector3 velocity, Vector3 angVel, float elapsed)
    {
        if (!_hasPrevious) return;

        var predicted = _previousPosition + _previousVelocity * elapsed;
        var jump = position - predicted;
        var distance = jump.magnitude;

        if (distance >= TeleportMetres)
        {
            _positionError = Vector3.zero;
            _rotationError = Quaternion.identity;
            return;
        }

        if (distance > JumpMetres) _positionError -= jump;

        var predictedRotation = Rotate(_previousRotation, _previousAngVel, elapsed);
        if (Quaternion.Angle(predictedRotation, rotation) > JumpDegrees)
            _rotationError = _rotationError * predictedRotation * Quaternion.Inverse(rotation);
    }

    private void Fade(float dt)
    {
        var keep = Mathf.Exp(-dt / SmoothingSeconds);

        _positionError *= keep;
        if (_positionError.sqrMagnitude < 0.0001f) _positionError = Vector3.zero;

        _rotationError = Quaternion.Slerp(_rotationError, Quaternion.identity, 1f - keep);
        if (Quaternion.Angle(_rotationError, Quaternion.identity) < 0.05f) _rotationError = Quaternion.identity;
    }

    // ---------------------------------------------------------------------------------------
    // Sampling the recorded motion
    // ---------------------------------------------------------------------------------------

    /// <summary>The state at a moment of the owner's time: between two known states, or coasting past the last.</summary>
    [HideFromIl2Cpp]
    private void Sample(double time, out Vector3 position, out Quaternion rotation, out Vector3 velocity, out Vector3 angVel)
    {
        var newest = _snapshots[_snapshots.Count - 1];

        if (time >= newest.Time)
        {
            var ahead = time - newest.Time;
            var t = (float)Math.Min(ahead, MaxExtrapolation);
            position = newest.Position + newest.Velocity * t;
            rotation = Rotate(newest.Rotation, newest.AngVel, t);

            // Once the coast has run its course the truck holds still; reporting it so keeps the
            // smoother's prediction honest.
            var coasting = ahead < MaxExtrapolation;
            velocity = coasting ? newest.Velocity : Vector3.zero;
            angVel = coasting ? newest.AngVel : Vector3.zero;
            return;
        }

        var oldest = _snapshots[0];
        if (time <= oldest.Time)
        {
            position = oldest.Position;
            rotation = oldest.Rotation;
            velocity = oldest.Velocity;
            angVel = oldest.AngVel;
            return;
        }

        for (var i = 1; i < _snapshots.Count; i++)
        {
            var b = _snapshots[i];
            if (time > b.Time) continue;

            var a = _snapshots[i - 1];
            var span = b.Time - a.Time;

            if (span < 0.0001)
            {
                position = b.Position;
                rotation = b.Rotation;
                velocity = b.Velocity;
                angVel = b.AngVel;
                return;
            }

            var t = (float)((time - a.Time) / span);
            rotation = Quaternion.Slerp(a.Rotation, b.Rotation, t);
            angVel = Vector3.Lerp(a.AngVel, b.AngVel, t);

            if (span > HermiteMaxSpan)
            {
                position = Vector3.LerpUnclamped(a.Position, b.Position, t);
                velocity = (b.Position - a.Position) / (float)span;
                return;
            }

            // Cubic Hermite through both states with their velocities: the curve the truck really
            // drew, so a missing state in between costs nothing and the motion has no corners.
            var s = (float)span;
            var t2 = t * t;
            var t3 = t2 * t;
            var h00 = 2f * t3 - 3f * t2 + 1f;
            var h10 = t3 - 2f * t2 + t;
            var h01 = -2f * t3 + 3f * t2;
            var h11 = t3 - t2;
            position = h00 * a.Position + (h10 * s) * a.Velocity + h01 * b.Position + (h11 * s) * b.Velocity;

            var d00 = 6f * t2 - 6f * t;
            var d10 = 3f * t2 - 4f * t + 1f;
            var d01 = -6f * t2 + 6f * t;
            var d11 = 3f * t2 - 2f * t;
            velocity = (d00 * a.Position + (d10 * s) * a.Velocity + d01 * b.Position + (d11 * s) * b.Velocity) / s;
            return;
        }

        position = newest.Position;
        rotation = newest.Rotation;
        velocity = newest.Velocity;
        angVel = newest.AngVel;
    }

    /// <summary>A rotation carried on by an angular velocity (world space, radians per second) for a while.</summary>
    private static Quaternion Rotate(Quaternion rotation, Vector3 angVel, float seconds)
    {
        var speed = angVel.magnitude;
        if (speed < 1e-4f || seconds <= 0f) return rotation;
        return Quaternion.AngleAxis(speed * seconds * Mathf.Rad2Deg, angVel / speed) * rotation;
    }
}
