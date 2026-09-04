using System;
using System.Threading;
using StarTruckMP.Client.Synchronization;
using StarTruckMP.Client.UI;
using StarTruckMP.Shared;
using StarTruckMP.Shared.Cmd;
using StarTruckMP.Shared.Movement;
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

        // Raised on the socket thread: only a flag is set here, the truck is read in Update.
        Network.OnConnected += netId => _reportHitchPending = true;
    }

    private GameObject _player;
    private GameObject _truck;
    private Rigidbody _truckRigid;
    private StarTruck _starTruck;

    private volatile bool _reportHitchPending;

    /// <summary>
    /// The trailer we last had on the hook, so the one we let go of can be named the moment the
    /// hitch releases — by then the connector has already forgotten it.
    /// </summary>
    private CargoContainer _lastHitched;

    /// <summary>
    /// A trailer we unhitched and left standing in this sector. It is still ours as far as the
    /// other players are concerned: it keeps travelling in our movement packet as the trailer
    /// body, so their copy of it stays where we put it and drifts as it drifts, instead of
    /// vanishing the instant the maglock lets go. Dropped when the game destroys it (delivered),
    /// when we hitch it back or hitch something else, or when we leave the sector.
    /// </summary>
    private CargoContainer _loose;

    private float _nextLooseCheck;
    private const float LooseCheckSeconds = 0.5f;

    private void OnArrivedAtSector(Il2CppSystem.Object sender, Il2CppSystem.EventArgs args)
    {
        var sectorRoot = GameObject.Find("[Sector]");
        if (sectorRoot != null) PlayerState.Sector = sectorRoot.scene.name;

        // A trailer left behind in the old sector is not in the new one, whatever the game does
        // with the object; the players here must not see it at our old coordinates.
        if (_loose != null)
        {
            _loose = null;
            _lastHitched = null;
            SendTrailer(null, 0);
        }

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

    /// <summary>
    /// Tells a server we have just joined what trailer is ours: the one on the hook, or the one
    /// we left standing nearby, or none. Game thread.
    /// </summary>
    private void ReportCurrentHitch()
    {
        try
        {
            var connector = _starTruck != null ? _starTruck.maglockConnector : null;
            if (connector != null && connector.hitched && connector.hitchedCargo != null)
                OnHitchCargo(connector.hitchedCargo);
            else if (Alive(_loose))
                SendTrailer(_loose, 1);
            else
                SendTrailer(null, 0);
        }
        catch (Exception ex)
        {
            App.Log.LogError($"[World] Could not report the hitched trailer: {ex.Message}");
        }
    }

    /// <summary>
    /// The maglock let go. The trailer is still there, so nothing is sent: it goes on riding in
    /// the movement packet as a loose body and the other players keep seeing it. Only a trailer
    /// nobody remembers — a hitch the mod never saw — is reported gone at once.
    /// </summary>
    private void OnUnhitchCargo()
    {
        _loose = Alive(_lastHitched) ? _lastHitched : null;
        _lastHitched = null;

        if (_loose != null)
        {
            App.Log.LogInfo("Unhitched cargo; it stays in the world for the other players while it is here.");
            return;
        }

        App.Log.LogInfo("Unhitched cargo");
        SendTrailer(null, 0);
    }

    /// <summary>
    /// The loose trailer stops being ours when the game takes it away — delivered, or cleared
    /// with the sector — and the other players are told so once. Game thread, now and then.
    /// </summary>
    private void WatchLooseTrailer()
    {
        if (_loose == null || Time.unscaledTime < _nextLooseCheck) return;
        _nextLooseCheck = Time.unscaledTime + LooseCheckSeconds;

        if (Alive(_loose)) return;

        _loose = null;
        App.Log.LogInfo("The trailer we left behind is gone; other players no longer see it.");
        SendTrailer(null, 0);
    }

    /// <summary>True while the game still has the object; a destroyed one compares equal to null.</summary>
    private static bool Alive(UnityEngine.Object o)
    {
        try { return o != null; }
        catch (Exception) { return false; }
    }

    private void OnHitchCargo(CargoContainer cargo)
    {
        App.Log.LogInfo("Hitched cargo");

        // Whatever we had left standing is forgotten the moment something is on the hook: the
        // packet carries one trailer body, and that is the hitched one. Hitching the same
        // trailer back sends what the others already know, so their copy simply carries on.
        _loose = null;
        _lastHitched = cargo;

        var trailersCount = Mathf.Max(1, _starTruck.HitchedTrailersCount);
        SendTrailer(cargo, trailersCount);
    }

    /// <summary>What the other players should build behind our cab: this many containers, painted and loaded like this one.</summary>
    private void SendTrailer(CargoContainer cargo, int trailersCount)
    {
        if (cargo == null || trailersCount == 0)
        {
            Network.SendServerMessage(new UpdateTrailerCmd
            {
                TrailerCount = 0,
                LiveryId = null
            }, PacketType.UpdateTrailer);
            return;
        }

        // we hitched a cargo, we need to retrieve the cargo size, livery and share it to the server
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

        if (_reportHitchPending)
        {
            _reportHitchPending = false;
            ReportCurrentHitch();
        }

        WatchLooseTrailer();

        // Real overlay hotkeys: F2 toggles UI mode and Esc closes it.
        if (Input.GetKeyDown(KeyCode.F2) && OverlayManager.Enabled)
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

    /// <summary>A packet goes out when the cab or the trailer moved this far or turned this much; a resting train only by the heartbeat.</summary>
    private const float ThresholdMetres = 0.02f;
    private const float ThresholdDegrees = 0.25f;

    /// <summary>
    /// Gap between movement sends. Receivers interpolate between states along the reported
    /// velocities, and every packet repeats the states before it, so 25 a second is plenty.
    /// Rounded to whole physics steps so the spacing is exactly even.
    /// </summary>
    private const float SendInterval = 0.04f;

    /// <summary>A standing truck is still reported now and then, so a late joiner is not left guessing.</summary>
    private const float HeartbeatInterval = 1f;

    private int _stepsUntilSend;

    // What the last packet said, to decide whether the next one is due.
    private uint _seq;
    private Vector3 _lastCabPosition;
    private Quaternion _lastCabRotation = Quaternion.identity;
    private Vector3 _lastTrailerPosition;
    private Quaternion _lastTrailerRotation = Quaternion.identity;
    private bool _lastHadTrailer;
    private float _lastSendTime;

    // The states sent before this one, newest first, for the next packet to repeat.
    private readonly MovementEntry[] _recent = new MovementEntry[MovementCodec.MaxHistory];
    private int _recentCount;

    private readonly byte[] _packet = new byte[MovementCodec.MaxPayloadBytes];

    /// <summary>
    /// Movement goes out from the physics step, on the game thread: the cab and the hitched
    /// trailer in one packet, read in the same step and stamped with the same clock.
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
            SendMovement(stamp);
        }
        catch (Exception ex)
        {
            // The player and the truck die with the world when a save is unloaded; the next arrival
            // in a sector finds the new ones.
            App.Log.LogWarning($"[Sync] Movement send failed: {ex.Message}");
        }
    }

    private void SendMovement(long stamp)
    {
        var truck = _truck;
        var rigid = _truckRigid;
        if (truck == null || rigid == null) return;

        truck.transform.GetPositionAndRotation(out var cabPosition, out var cabRotation);

        // The hitched trailer swings on a joint behind the truck, and the copy other players see
        // should swing the same way rather than sit bolted to the cab, so it travels as its own body.
        // A trailer we unhitched and left here travels the same way until the game takes it away,
        // so it stands, or drifts, for the others exactly where it does for us.
        var starTruck = _starTruck;
        var trailer = starTruck != null ? starTruck.hitchedTrailer : null;
        if (trailer != null) _lastHitched = trailer;
        else if (_loose != null && Alive(_loose)) trailer = _loose;
        var hasTrailer = trailer != null;

        var trailerPosition = Vector3.zero;
        var trailerRotation = Quaternion.identity;
        Rigidbody trailerBody = null;
        if (hasTrailer)
        {
            trailer.transform.GetPositionAndRotation(out trailerPosition, out trailerRotation);
            trailerBody = trailer.rb;
        }

        var due = Moved(_lastCabPosition, _lastCabRotation, cabPosition, cabRotation) ||
                  hasTrailer != _lastHadTrailer ||
                  (hasTrailer && Moved(_lastTrailerPosition, _lastTrailerRotation, trailerPosition, trailerRotation)) ||
                  Time.unscaledTime - _lastSendTime >= HeartbeatInterval;
        if (!due) return;

        var entry = new MovementEntry
        {
            Seq = ++_seq,
            SentAt = stamp,
            HasCab = true,
            // Sent in absolute world space: every client recentres its scene differently, so raw
            // local coordinates mean nothing to the receiver.
            Cab = Body(FloatingOrigin.ToWorld(cabPosition), cabRotation, rigid.velocity, rigid.angularVelocity),
            HasTrailer = hasTrailer
        };

        if (hasTrailer)
        {
            entry.Trailer = Body(FloatingOrigin.ToWorld(trailerPosition), trailerRotation,
                trailerBody != null ? trailerBody.velocity : Vector3.zero,
                trailerBody != null ? trailerBody.angularVelocity : Vector3.zero);
        }

        var depth = Mathf.Clamp(App.MovementRedundancy.Value, 0, MovementCodec.MaxHistory);
        var repeat = Mathf.Min(depth, _recentCount);
        var length = MovementCodec.Write(_packet, entry, new ReadOnlySpan<MovementEntry>(_recent, 0, repeat));
        Network.SendMovement(new ReadOnlySpan<byte>(_packet, 0, length));

        // Remember it for the next packet to repeat, newest first.
        for (var i = _recent.Length - 1; i > 0; i--) _recent[i] = _recent[i - 1];
        _recent[0] = entry;
        if (_recentCount < _recent.Length) _recentCount++;

        _lastCabPosition = cabPosition;
        _lastCabRotation = cabRotation;
        _lastTrailerPosition = trailerPosition;
        _lastTrailerRotation = trailerRotation;
        _lastHadTrailer = hasTrailer;
        _lastSendTime = Time.unscaledTime;
    }

    private static bool Moved(Vector3 fromPosition, Quaternion fromRotation, Vector3 toPosition, Quaternion toRotation) =>
        Vector3.Distance(fromPosition, toPosition) > ThresholdMetres ||
        Quaternion.Angle(fromRotation, toRotation) > ThresholdDegrees;

    private static BodyState Body(Vector3 position, Quaternion rotation, Vector3 velocity, Vector3 angVel)
    {
        return new BodyState
        {
            Position = new StarTruckMP.Shared.Vector3 { X = position.x, Y = position.y, Z = position.z },
            Rotation = new StarTruckMP.Shared.Quaternion { X = rotation.x, Y = rotation.y, Z = rotation.z, W = rotation.w },
            Velocity = new StarTruckMP.Shared.Vector3 { X = velocity.x, Y = velocity.y, Z = velocity.z },
            AngVel = new StarTruckMP.Shared.Vector3 { X = angVel.x, Y = angVel.y, Z = angVel.z }
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
