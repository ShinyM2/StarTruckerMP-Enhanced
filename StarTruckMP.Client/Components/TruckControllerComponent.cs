using System;
using StarTruckMP.Client.Synchronization;
using UnityEngine;

namespace StarTruckMP.Client.Components;

/// <summary>
/// Moves a remote player's truck towards where the network says it is.
///
/// Packets arrive late and a few times a second; the truck must look continuous. Each packet's
/// position is carried forward by its velocity for the time it has been in flight and the time
/// since it arrived, and the truck is eased towards that. Without the flight time — half a round
/// trip on each side of the server — a remote truck trails its owner by latency times speed, which
/// at cruising speed is several truck lengths.
/// </summary>
public class TruckControllerComponent : MonoBehaviour
{
    private Rigidbody _rb;

    /// <summary>Target position in absolute world space — see <see cref="FloatingOrigin"/>.</summary>
    public Vector3 TargetPosition;
    public Quaternion TargetRotation;
    public Vector3 TargetVelocity;

    /// <summary>The player this truck belongs to, so their latency can be looked up.</summary>
    public int NetId = -1;

    // Interpolation
    private float _lerpSpeed = 15f;

    /// <summary>
    /// How far ahead of the last packet we are willing to dead-reckon, in seconds. Long enough
    /// to cover normal latency and a few dropped packets, short enough that a truck which
    /// stopped sending does not sail off into the distance.
    /// </summary>
    private const float MaxExtrapolation = 0.6f;

    /// <summary>The most flight time a packet is credited with; beyond this the estimate is noise.</summary>
    private const float MaxLatencyCompensation = 0.3f;

    /// <summary>Time.time when the last network state arrived, used to age the extrapolation.</summary>
    private float _lastUpdateTime;

    private bool _hasFirstUpdate = false;

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

        // Lerping straight at the last received position always trails the real truck by
        // roughly speed / lerpSpeed plus a full network round trip — metres at cruising speed,
        // which is what reads as desync. Carry the position forward by the velocity we were
        // sent instead, so the target is where the truck is now rather than where it was.
        var age = Mathf.Clamp(Time.time - _lastUpdateTime + FlightTime(), 0f, MaxExtrapolation);
        var predicted = TargetPosition + TargetVelocity * age;

        // Converted every step rather than once on arrival: the scene can be recentred between
        // two packets, and a target left in stale scene coordinates would drag the truck across
        // the sector until the next update landed.
        var target = FloatingOrigin.ToScene(predicted);

        _rb.MovePosition(Vector3.Lerp(transform.position, target, _lerpSpeed * Time.fixedDeltaTime));
        _rb.MoveRotation(Quaternion.Slerp(transform.rotation, TargetRotation, _lerpSpeed * Time.fixedDeltaTime));
    }

    /// <summary>
    /// How long a packet from this truck's owner has been on the wire when it reaches us: half
    /// their round trip to the server plus half of ours. Both come from the server's own latency
    /// table, so no clock has to be agreed on.
    /// </summary>
    private float FlightTime()
    {
        var mine = MultiplayerState.OwnPing;
        var theirs = -1;

        if (NetId >= 0)
        {
            foreach (var player in MultiplayerState.Players)
            {
                if (player.NetId != NetId) continue;
                theirs = player.Ping;
                break;
            }
        }

        if (mine < 0 && theirs < 0) return 0f;

        var seconds = (Math.Max(mine, 0) + Math.Max(theirs, 0)) / 2f / 1000f;
        return Mathf.Clamp(seconds, 0f, MaxLatencyCompensation);
    }

    public void ApplyNetworkState(Vector3 pos, Quaternion rot, Vector3 vel)
    {
        TargetPosition = pos;
        TargetRotation = rot;
        TargetVelocity = vel;
        _lastUpdateTime = Time.time;

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
