using System;
using System.Collections.Generic;
using StarTruckMP.Client.Synchronization;
using UnityEngine;

namespace StarTruckMP.Client.Components;

/// <summary>
/// Moves a remote player's truck the way its owner moved it.
///
/// Packets arrive a few times a second, late and unevenly. Chasing each one as it lands — the
/// previous approach — made the copy lurch: every packet moved the goal, and the goal moved by a
/// different amount each time because the gaps between packets were never the same. So the copy
/// now plays the movement back slightly in the past. Each packet carries the sender's own clock;
/// the states are kept in order, and the truck is drawn where the owner was about 120 ms ago,
/// interpolated between the two states either side of that moment. That is enough of a cushion
/// for a packet to be late without the truck ever waiting for it, and the motion between two real
/// positions is exactly as smooth as the owner's. Only when the cushion runs dry does the truck
/// coast on its last velocity, and never for long.
/// </summary>
public class TruckControllerComponent : MonoBehaviour
{
    /// <summary>How far behind the newest state the truck is drawn. About four packets at the 30 Hz send rate.</summary>
    private const double InterpolationDelay = 0.12;

    /// <summary>The longest the truck coasts on its last velocity once the buffer is exhausted.</summary>
    private const double MaxExtrapolation = 0.5;

    /// <summary>States older than this behind the playback point are of no further use.</summary>
    private const double KeepBehind = 1.0;

    /// <summary>
    /// How fast the estimate of "their clock minus ours" follows the packets. Slow, so that one
    /// packet arriving late does not move the whole playback; the interpolation delay covers that.
    /// </summary>
    private const double ClockFollow = 0.05;

    private struct Snapshot
    {
        public double Time;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Velocity;
    }

    private readonly List<Snapshot> _snapshots = new();
    private readonly object _lock = new();

    private Rigidbody _rb;

    /// <summary>The player this truck belongs to.</summary>
    public int NetId = -1;

    /// <summary>Their clock minus ours, in seconds, including the one-way trip; learned from the packets.</summary>
    private double _clockOffset;
    private bool _clockKnown;

    private bool _hasFirstUpdate;

    // hide until we get the real position
    private GameObject _npcTruckVisual;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        if (_rb != null)
        {
            _rb.isKinematic = true;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
        }

        _npcTruckVisual = transform.Find("NPCTruck")?.gameObject;
    }

    void FixedUpdate()
    {
        if (!_hasFirstUpdate || _rb == null) return;

        Vector3 worldPosition;
        Quaternion rotation;

        lock (_lock)
        {
            if (_snapshots.Count == 0) return;

            // Where in the owner's time we are drawing right now.
            var playback = Now() + _clockOffset - InterpolationDelay;

            // Forget what is well behind the playback point, keeping one state before it.
            while (_snapshots.Count > 2 && _snapshots[1].Time < playback - KeepBehind)
                _snapshots.RemoveAt(0);

            Sample(playback, out worldPosition, out rotation);
        }

        // Converted every step rather than once on arrival: the scene can be recentred between
        // two packets, and a target left in stale scene coordinates would drag the truck across
        // the sector until the next update landed.
        _rb.MovePosition(FloatingOrigin.ToScene(worldPosition));
        _rb.MoveRotation(rotation);
    }

    /// <summary>The state at a moment of the owner's time, between two known states or coasting past the last.</summary>
    private void Sample(double time, out Vector3 position, out Quaternion rotation)
    {
        var newest = _snapshots[_snapshots.Count - 1];

        if (time >= newest.Time)
        {
            var ahead = Math.Min(time - newest.Time, MaxExtrapolation);
            position = newest.Position + newest.Velocity * (float)ahead;
            rotation = newest.Rotation;
            return;
        }

        var oldest = _snapshots[0];
        if (time <= oldest.Time)
        {
            position = oldest.Position;
            rotation = oldest.Rotation;
            return;
        }

        for (var i = 1; i < _snapshots.Count; i++)
        {
            var b = _snapshots[i];
            if (time > b.Time) continue;

            var a = _snapshots[i - 1];
            var span = b.Time - a.Time;
            var t = span > 0.0001 ? (float)((time - a.Time) / span) : 1f;

            position = Vector3.LerpUnclamped(a.Position, b.Position, t);
            rotation = Quaternion.Slerp(a.Rotation, b.Rotation, t);
            return;
        }

        position = newest.Position;
        rotation = newest.Rotation;
    }

    private static double Now() => Environment.TickCount64 / 1000.0;

    /// <summary>
    /// Takes a state off the wire. <paramref name="sentAtMs"/> is the sender's clock; a zero (an
    /// older client) is replaced by our own arrival time, which still plays back correctly, just
    /// with the network's unevenness left in.
    /// </summary>
    public void ApplyNetworkState(Vector3 pos, Quaternion rot, Vector3 vel, long sentAtMs)
    {
        var now = Now();
        var sentAt = sentAtMs > 0 ? sentAtMs / 1000.0 : now;

        lock (_lock)
        {
            // Their clock minus ours. Followed slowly, and never allowed to make the newest packet
            // look like it came from the future: that would stretch the cushion for no reason.
            var offset = sentAt - now;
            if (!_clockKnown)
            {
                _clockOffset = offset;
                _clockKnown = true;
            }
            else
            {
                _clockOffset += (offset - _clockOffset) * ClockFollow;
                if (offset < _clockOffset) _clockOffset = offset;
            }

            var snapshot = new Snapshot { Time = sentAt, Position = pos, Rotation = rot, Velocity = vel };

            // Keep the list ordered by the sender's time; a packet that overtook a newer one slots in behind it.
            var index = _snapshots.Count;
            while (index > 0 && _snapshots[index - 1].Time > sentAt) index--;
            _snapshots.Insert(index, snapshot);

            if (_snapshots.Count > 64) _snapshots.RemoveAt(0);
        }

        if (!_hasFirstUpdate)
        {
            _hasFirstUpdate = true;

            // move directly without lerp
            var scenePos = FloatingOrigin.ToScene(pos);
            if (_rb != null)
            {
                _rb.position = scenePos;
                _rb.rotation = rot;
            }
            else
            {
                transform.position = scenePos;
                transform.rotation = rot;
            }

            // show now, we have a position
            if (_npcTruckVisual != null)
                _npcTruckVisual.SetActive(true);
        }
    }
}
