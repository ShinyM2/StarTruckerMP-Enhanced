using System;
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
    private const float ThresholdChange = 0.1f;

    /// <summary>
    /// Gap between position sends. Receivers interpolate between packets, so 30/s is plenty.
    /// </summary>
    private const float SendInterval = 0.033f;

    /// <summary>A standing truck is still reported now and then, so a late joiner is not left guessing.</summary>
    private const float HeartbeatInterval = 1f;

    /// <summary>Per-stream packet counters, so receivers can discard out-of-order updates.</summary>
    private uint _playerSeq;
    private uint _truckSeq;

    private float _nextSend;
    private Vector3 _lastTruckSent;
    private Vector3 _lastPlayerSent;
    private float _lastTruckSendTime;
    private float _lastPlayerSendTime;

    /// <summary>
    /// Positions go out from the physics step, on the game thread.
    ///
    /// They used to be read by two background threads, which Unity does not promise anything
    /// about: a position and the scene origin it had to be converted with could be read on either
    /// side of the game recentring the world, and the result was a packet a few kilometres off —
    /// the "teleport" other players saw. Here the position, the velocity and the origin are all
    /// read within one physics step, and the timestamp is taken at the same instant.
    /// </summary>
    private void FixedUpdate()
    {
        if (Network.NetId == -1) return;
        if (Time.unscaledTime < _nextSend) return;
        _nextSend = Time.unscaledTime + SendInterval;

        try
        {
            SendTruck();
            SendTrailer();
            SendPlayer();
        }
        catch (Exception ex)
        {
            // The player and the truck die with the world when a save is unloaded; the next arrival
            // in a sector finds the new ones.
            App.Log.LogWarning($"[Sync] Position send failed: {ex.Message}");
        }
    }

    private void SendTruck()
    {
        var truck = _truck;
        var rigid = _truckRigid;
        if (truck == null || rigid == null) return;

        truck.transform.GetPositionAndRotation(out var position, out var rotation);
        var moved = Vector3.Distance(_lastTruckSent, position) > ThresholdChange;
        if (!moved && Time.unscaledTime - _lastTruckSendTime < HeartbeatInterval) return;

        Network.SendServerMessage(new UpdatePositionCmd
        {
            // Sent in absolute world space: every client recentres its scene differently, so raw
            // local coordinates mean nothing to the receiver.
            Position = ConvertToSharedVector3(FloatingOrigin.ToWorld(position)),
            Rotation = ConvertToSharedQuaternion(rotation),
            Velocity = ConvertToSharedVector3(rigid.velocity),
            AngVel = ConvertToSharedVector3(rigid.angularVelocity),
            IsTruck = true,
            InSeat = false,
            Seq = ++_truckSeq,
            SentAt = NetClock.Milliseconds
        }, PacketType.UpdatePosition);

        _lastTruckSent = position;
        _lastTruckSendTime = Time.unscaledTime;
    }

    private uint _trailerSeq;
    private Vector3 _lastTrailerSent;
    private float _lastTrailerSendTime;

    /// <summary>
    /// The hitched trailer, as its own stream: it swings on a joint behind the truck, and the copy
    /// other players see should swing the same way rather than sit bolted to the cab.
    /// </summary>
    private void SendTrailer()
    {
        var truck = _starTruck;
        var trailer = truck != null ? truck.hitchedTrailer : null;
        if (trailer == null) return;

        trailer.transform.GetPositionAndRotation(out var position, out var rotation);
        var moved = Vector3.Distance(_lastTrailerSent, position) > ThresholdChange;
        if (!moved && Time.unscaledTime - _lastTrailerSendTime < HeartbeatInterval) return;

        var body = trailer.rb;

        Network.SendServerMessage(new UpdatePositionCmd
        {
            Position = ConvertToSharedVector3(FloatingOrigin.ToWorld(position)),
            Rotation = ConvertToSharedQuaternion(rotation),
            Velocity = ConvertToSharedVector3(body != null ? body.velocity : Vector3.zero),
            AngVel = ConvertToSharedVector3(body != null ? body.angularVelocity : Vector3.zero),
            IsTruck = false,
            InSeat = false,
            Kind = 2,
            Index = 0,
            Seq = ++_trailerSeq,
            SentAt = NetClock.Milliseconds
        }, PacketType.UpdatePosition);

        _lastTrailerSent = position;
        _lastTrailerSendTime = Time.unscaledTime;
    }

    private void SendPlayer()
    {
        var player = _player;
        var rigid = _playerRigid;
        if (player == null || rigid == null) return;

        player.transform.GetPositionAndRotation(out var position, out var rotation);
        var moved = Vector3.Distance(_lastPlayerSent, position) > ThresholdChange;
        if (!moved && Time.unscaledTime - _lastPlayerSendTime < HeartbeatInterval) return;

        Network.SendServerMessage(new UpdatePositionCmd
        {
            Position = ConvertToSharedVector3(FloatingOrigin.ToWorld(position)),
            Rotation = ConvertToSharedQuaternion(rotation),
            Velocity = ConvertToSharedVector3(rigid.velocity),
            AngVel = ConvertToSharedVector3(rigid.angularVelocity),
            IsTruck = false,
            InSeat = false,
            Seq = ++_playerSeq,
            SentAt = NetClock.Milliseconds
        }, PacketType.UpdatePosition);

        _lastPlayerSent = position;
        _lastPlayerSendTime = Time.unscaledTime;
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