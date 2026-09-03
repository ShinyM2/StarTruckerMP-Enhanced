using StarTruckMP.Client.Synchronization;
using UnityEngine;
using BepInEx;

namespace StarTruckMP.Client.Components;

public class TruckControllerComponent : MonoBehaviour
{
    private Rigidbody _rb;
    
    /// <summary>Target position in absolute world space — see <see cref="FloatingOrigin"/>.</summary>
    public Vector3 TargetPosition;
    public Quaternion TargetRotation;
    public Vector3 TargetVelocity;

    // Interpolation
    private float _lerpSpeed = 15f;

    /// <summary>
    /// How far ahead of the last packet we are willing to dead-reckon, in seconds. Long enough
    /// to cover normal latency and a few dropped packets, short enough that a truck which
    /// stopped sending does not sail off into the distance.
    /// </summary>
    private const float MaxExtrapolation = 0.5f;

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
        var age = Mathf.Clamp(Time.time - _lastUpdateTime, 0f, MaxExtrapolation);
        var predicted = TargetPosition + TargetVelocity * age;

        // Converted every step rather than once on arrival: the scene can be recentred between
        // two packets, and a target left in stale scene coordinates would drag the truck across
        // the sector until the next update landed.
        var target = FloatingOrigin.ToScene(predicted);

        _rb.MovePosition(Vector3.Lerp(transform.position, target, _lerpSpeed * Time.fixedDeltaTime));
        _rb.MoveRotation(Quaternion.Slerp(transform.rotation, TargetRotation, _lerpSpeed * Time.fixedDeltaTime));
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