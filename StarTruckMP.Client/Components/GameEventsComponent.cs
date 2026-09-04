using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using StarTruckMP.Client.Synchronization;
using StarTruckMP.Client.UI;
using StarTruckMP.Shared;
using StarTruckMP.Shared.Cmd;
using UnityEngine;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

namespace StarTruckMP.Client.Components;

public class GameEventsComponent : MonoBehaviour
{
    public static event Action<string> ArrivedAtSector;

    private void Awake()
    {
        App.Log.LogInfo("GameEventsComponent Awake");
        DontDestroyOnLoad(gameObject);

        Network.OnConnected += netId => Connected();
    }

    private GameObject _player;
    private GameObject _playerCam;
    private Rigidbody _playerRigid;
    private PlayerLocation _playerLocation;

    private GameObject _truck;
    private Rigidbody _truckRigid;
    private StarTruck _starTruck;

    private void OnArrivedAtSector(Il2CppSystem.Object sender, Il2CppSystem.EventArgs args)
    {
        var sectorRoot = GameObject.Find("[Sector]");
        if (sectorRoot != null) PlayerState.Sector = sectorRoot.scene.name;
        ArrivedAtSector?.Invoke(PlayerState.Sector);

        // This event also fires when a save is loaded. The player and the truck are new objects
        // every time that happens — the old ones went with the previous world — so they are
        // looked up again whenever the ones we hold are gone, and only then: within one save a
        // warp keeps the same truck, and subscribing to its hitch twice would report every
        // trailer twice.
        if (_player == null || _truck == null) AcquireWorldObjects();

    }

    private void AcquireWorldObjects()
    {
        _player = GameObject.FindGameObjectWithTag("Player");
        PlayerState.Player = _player;
        _playerCam = GameObject.FindGameObjectWithTag("MainCamera");
        _playerRigid = _player != null ? _player.GetComponent<Rigidbody>() : null;
        _playerLocation = _player != null ? _player.GetComponent<PlayerLocation>() : null;

        _truck = GameObject.Find("StarTruck(Clone)");
        PlayerState.Truck = _truck;
        _truckRigid = null;
        _starTruck = null;

        if (_truck == null)
        {
            App.Log.LogWarning("[World] Player truck not found; nothing will be sent until it is.");
            return;
        }

        if (_truck.GetComponent<CbRadioPttComponent>() == null)
            _truck.AddComponent<CbRadioPttComponent>();

        _truckRigid = _truck.GetComponent<Rigidbody>();
        var truckInterior = _truck.transform.Find("Interior");
        var root = truckInterior?.Find("SpaceSuit_Root");
        var suit = root?.Find("SpaceSuit");
        if (suit != null && suit.childCount > 0)
        {
            PlayerState.SpaceSuit = suit.GetChild(0).gameObject;
            PlayerState.SpaceSuitMats = null; // re-read from the new suit
        }

        _starTruck = _truck.GetComponentInChildren<StarTruck>();
        var connector = _starTruck != null ? _starTruck.maglockConnector : null;
        if (connector == null || connector.hitchControl == null)
        {
            App.Log.LogWarning("[World] Truck has no maglock connector; trailer changes will not be reported.");
            return;
        }

        connector.hitchControl.onTriggered += new System.Action<Il2CppSystem.Object, Il2CppSystem.EventArgs>((s, e) =>
        {
            try
            {
                var current = _starTruck != null ? _starTruck.maglockConnector : null;
                if (current == null) return;

                if (current.hitchedCargo)
                    OnHitchCargo(current.hitchedCargo);
                else
                    OnUnhitchCargo();
            }
            catch (Exception ex)
            {
                App.Log.LogError($"[World] Hitch update failed: {ex.Message}");
            }
        });

        App.Log.LogInfo("[World] Player and truck acquired");
    }

    private void Connected()
    {
        try
        {
            // we already have something attached
            var connector = _starTruck != null ? _starTruck.maglockConnector : null;
            if (connector != null && connector.hitched && connector.hitchedCargo != null)
                OnHitchCargo(connector.hitchedCargo);
        }
        catch (Exception ex)
        {
            App.Log.LogError($"[World] Could not report the hitched trailer: {ex.Message}");
        }
    }

    private void OnUnhitchCargo()
    {
        App.Log.LogInfo("Unhitched cargo");
        // we unhitched a cargo, we need to notify the server
        Network.SendServerMessage(new UpdateTrailerCmd()
        {
            TrailerCount = 0,
            LiveryId = null
        }, PacketType.UpdateTrailer);
    }

    private void OnHitchCargo(CargoContainer cargo)
    {
        App.Log.LogInfo("Hitched cargo");
        // we hitched a cargo, we need to retrieve the cargo size, livery and share it to the server
        var trailersCount = _starTruck.HitchedTrailersCount;
        var livery = cargo.damageApplier.CurrentLiveryId ?? cargo.damageApplier.AppliedLiveryId;

        if (string.IsNullOrEmpty(livery))
            App.Log.LogError("Couldn't retrieve livery for hitched cargo, sending null");

        var cargoType = cargo.cargoRecord?.cargoType;
        string cargoTypeId = null;

        var catalogue = CargoMetadataProvider.instance?.cargoCatalogue;
        if (cargoType != null && catalogue != null)
        {
            foreach (var kvp in catalogue.lookUp)
            {
                if (kvp.value._displayNameId == cargoType._displayNameId)
                {
                    cargoTypeId = kvp.key;
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(cargoTypeId))
            App.Log.LogError("Couldn't retrieve cargoTypeId for hitched cargo, sending null");

        App.Log.LogInfo($"Cargo data: {trailersCount}, {livery}, {cargoTypeId}");

        Network.SendServerMessage(new UpdateTrailerCmd()
        {
            TrailerCount = trailersCount,
            LiveryId = livery,
            CargoTypeId = cargoTypeId
        }, PacketType.UpdateTrailer);
    }

    private bool _subscribedToSectorArrival = false;

    private void Update()
    {
        if (SectorPersistence.instance && !_subscribedToSectorArrival)
        {
            SectorPersistence.instance.onArrivedAtSector.onTriggered +=
                new System.Action<Il2CppSystem.Object, Il2CppSystem.EventArgs>(OnArrivedAtSector);
            _subscribedToSectorArrival = true;
        }

        // TODO: maybe this component not exists?
        if (PlayerState.SpaceSuitMats == null &&  PlayerState.SpaceSuit != null)
            PlayerState.SpaceSuitMats = PlayerState.SpaceSuit.GetComponent<MeshRenderer>()?.materials.ToArray();

        // Real overlay hotkeys: F2 toggles UI mode and Esc closes it.
        if (Input.GetKeyDown(KeyCode.F2))
        {
            OverlayManager.ToggleInteractiveMode();
            App.Log.LogInfo($"[Overlay] F2 => toggle interactive mode => {(OverlayManager.IsInteractiveMode ? "ON" : "OFF")}");
        }

        if (OverlayManager.IsInteractiveMode && Input.GetKeyDown(KeyCode.Escape))
        {
            App.Log.LogInfo("[Overlay] Esc => interactive mode OFF (click-through ON)");
            OverlayManager.SetInteractiveMode(false);
        }
    }

    private CancellationTokenSource _cts = new();
    /// <summary>A stream is sent when it moved this far or turned this much; a resting one only by the heartbeat.</summary>
    private const float ThresholdMetres = 0.02f;
    private const float ThresholdDegrees = 0.25f;

    /// <summary>
    /// Gap between position sends. Receivers interpolate between states along the reported
    /// velocities, and every packet repeats the states before it, so 25 a second is plenty.
    /// Rounded to whole physics steps so the spacing is exactly even.
    /// </summary>
    private const float SendInterval = 0.04f;

    /// <summary>A standing truck is still reported now and then, so a late joiner is not left guessing.</summary>
    private const float HeartbeatInterval = 1f;

    private int _stepsUntilSend;

    private readonly MotionStream _truckStream = new();
    private readonly MotionStream _trailerStream = new();
    private readonly MotionStream _playerStream = new();

    /// <summary>
    /// What one moving thing last sent: its counter, where it was, and the last few states for
    /// the next packet to repeat (see <see cref="MotionSample"/>).
    /// </summary>
    private sealed class MotionStream
    {
        public uint Seq;
        public Vector3 LastPosition;
        public Quaternion LastRotation = Quaternion.identity;
        public float LastSendTime;
        private readonly List<MotionSample> _recent = new();

        public bool Due(Vector3 position, Quaternion rotation) =>
            Vector3.Distance(LastPosition, position) > ThresholdMetres ||
            Quaternion.Angle(LastRotation, rotation) > ThresholdDegrees ||
            Time.unscaledTime - LastSendTime >= HeartbeatInterval;

        /// <summary>The states sent before this one, newest first, as many as the setting asks for.</summary>
        public MotionSample[] History()
        {
            var depth = Mathf.Clamp(App.MovementRedundancy.Value, 0, 3);
            if (depth == 0 || _recent.Count == 0) return null;
            var count = Mathf.Min(depth, _recent.Count);
            var history = new MotionSample[count];
            for (var i = 0; i < count; i++) history[i] = _recent[i];
            return history;
        }

        public void Sent(UpdatePositionCmd cmd, Vector3 position, Quaternion rotation)
        {
            LastPosition = position;
            LastRotation = rotation;
            LastSendTime = Time.unscaledTime;

            _recent.Insert(0, new MotionSample
            {
                Position = cmd.Position,
                Rotation = cmd.Rotation,
                Velocity = cmd.Velocity,
                AngVel = cmd.AngVel,
                Seq = cmd.Seq,
                SentAt = cmd.SentAt
            });
            while (_recent.Count > 3) _recent.RemoveAt(_recent.Count - 1);
        }
    }

    /// <summary>
    /// Positions go out from the physics step, on the game thread.
    ///
    /// They used to be read by two background threads, which Unity does not promise anything
    /// about: a position and the scene origin it had to be converted with could be read on either
    /// side of the game recentring the world, and the result was a packet a few kilometres off —
    /// the "teleport" other players saw. Here the position, the velocity and the origin are all
    /// read within one physics step.
    ///
    /// The timestamp is the physics clock, not the wall clock: when the game hitches and then
    /// runs several physics steps in one frame to catch up, the wall clock would stamp all of
    /// those states with the same instant and receivers would see the truck leap through them,
    /// whereas the physics clock spaces them exactly as the motion happened.
    /// </summary>
    private void FixedUpdate()
    {
        if (Network.NetId == -1) return;
        if (--_stepsUntilSend > 0) return;
        _stepsUntilSend = Mathf.Max(1, Mathf.RoundToInt(SendInterval / Time.fixedDeltaTime));

        var stamp = (long)(Time.fixedUnscaledTimeAsDouble * 1000.0);

        try
        {
            SendTruck(stamp);
            SendTrailer(stamp);
            SendPlayer(stamp);
        }
        catch (Exception ex)
        {
            // The player and the truck die with the world when a save is unloaded; the next arrival
            // in a sector finds the new ones.
            App.Log.LogWarning($"[Sync] Position send failed: {ex.Message}");
        }
    }

    private void SendTruck(long stamp)
    {
        var truck = _truck;
        var rigid = _truckRigid;
        if (truck == null || rigid == null) return;

        truck.transform.GetPositionAndRotation(out var position, out var rotation);
        if (!_truckStream.Due(position, rotation)) return;

        var cmd = new UpdatePositionCmd
        {
            // Sent in absolute world space: every client recentres its scene differently, so raw
            // local coordinates mean nothing to the receiver.
            Position = ConvertToSharedVector3(FloatingOrigin.ToWorld(position)),
            Rotation = ConvertToSharedQuaternion(rotation),
            Velocity = ConvertToSharedVector3(rigid.velocity),
            AngVel = ConvertToSharedVector3(rigid.angularVelocity),
            IsTruck = true,
            InSeat = false,
            Kind = 1,
            Seq = ++_truckStream.Seq,
            SentAt = stamp,
            History = _truckStream.History()
        };
        Network.SendServerMessage(cmd, PacketType.UpdatePosition);
        _truckStream.Sent(cmd, position, rotation);
    }

    /// <summary>
    /// The hitched trailer, as its own stream: it swings on a joint behind the truck, and the copy
    /// other players see should swing the same way rather than sit bolted to the cab.
    /// </summary>
    private void SendTrailer(long stamp)
    {
        var truck = _starTruck;
        var trailer = truck != null ? truck.hitchedTrailer : null;
        if (trailer == null) return;

        trailer.transform.GetPositionAndRotation(out var position, out var rotation);
        if (!_trailerStream.Due(position, rotation)) return;

        var body = trailer.rb;

        var cmd = new UpdatePositionCmd
        {
            Position = ConvertToSharedVector3(FloatingOrigin.ToWorld(position)),
            Rotation = ConvertToSharedQuaternion(rotation),
            Velocity = ConvertToSharedVector3(body != null ? body.velocity : Vector3.zero),
            AngVel = ConvertToSharedVector3(body != null ? body.angularVelocity : Vector3.zero),
            IsTruck = false,
            InSeat = false,
            Kind = 2,
            Index = 0,
            Seq = ++_trailerStream.Seq,
            SentAt = stamp,
            History = _trailerStream.History()
        };
        Network.SendServerMessage(cmd, PacketType.UpdatePosition);
        _trailerStream.Sent(cmd, position, rotation);
    }

    private void SendPlayer(long stamp)
    {
        var player = _player;
        var rigid = _playerRigid;
        if (player == null || rigid == null) return;

        player.transform.GetPositionAndRotation(out var position, out var rotation);
        if (!_playerStream.Due(position, rotation)) return;

        // The player on foot is placed directly on arrival, so there is nothing to interpolate
        // a repeated state into; the packet stays small.
        var cmd = new UpdatePositionCmd
        {
            Position = ConvertToSharedVector3(FloatingOrigin.ToWorld(position)),
            Rotation = ConvertToSharedQuaternion(rotation),
            Velocity = ConvertToSharedVector3(rigid.velocity),
            AngVel = ConvertToSharedVector3(rigid.angularVelocity),
            IsTruck = false,
            InSeat = false,
            Seq = ++_playerStream.Seq,
            SentAt = stamp
        };
        Network.SendServerMessage(cmd, PacketType.UpdatePosition);
        _playerStream.Sent(cmd, position, rotation);
    }

    private StarTruckMP.Shared.Vector3 ConvertToSharedVector3(Vector3 unityVector)
    {
        return new StarTruckMP.Shared.Vector3
        {
            X = unityVector.x,
            Y = unityVector.y,
            Z = unityVector.z
        };
    }

    private StarTruckMP.Shared.Quaternion ConvertToSharedQuaternion(Quaternion unityQuaternion)
    {
        return new Shared.Quaternion()
        {
            X = unityQuaternion.x,
            Y = unityQuaternion.y,
            Z = unityQuaternion.z,
            W = unityQuaternion.w
        };
    }

    #region Recycle

    private void OnDestroy()
    {
        _cts.Cancel();
    }

    private void OnDisable()
    {
        if (!_cts.IsCancellationRequested)
            _cts.Cancel();
    }

    #endregion
}