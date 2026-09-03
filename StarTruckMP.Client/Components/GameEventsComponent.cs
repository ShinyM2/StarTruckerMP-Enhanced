using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;
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
        OverlayManager.MessageReceived += (type, data) =>
        {
            switch (type)
            {
                case "inspectObjectExtra":
                    App.Log.LogInfo($"[Object Inspector] request extra data for object {data}");
                    GetMoreData(data);
                    break;
            }
        };

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

        if (!_pptRunning) PlayerPositionThread();
        if (!_tptRunning) TruckPositionThread();
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

        if (Input.GetKeyDown(KeyCode.F3))
        {
            // Raycast
            if (Camera.main)
            {
                var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray, out var hit))
                {
                    App.Log.LogInfo("[Object Inspector] object hit!");
                    // we hit something
                    var objectData = new ObjectData();

                    var go = hit.transform.gameObject;
                    objectData.Name = go.name;
                    objectData.Type = $"{go.GetType().FullName}";
                    objectData.Go = go;
                    AddChildComponents(objectData, go);

                    OverlayManager.PostMessage("inspectObject", objectData);

                    _inspectedObject = objectData;

                    void AddChildComponents(ObjectData data, GameObject obj, int depth = 0)
                    {
                        if (depth > 3) return;

                        // components of this obj
                        foreach (var component in obj.GetComponents<Component>())
                        {
                            var compData = new ObjectData()
                            {
                                Name = component.name,
                                Type = component.GetIl2CppType().FullName,
                                Go = component.gameObject
                            };
                            data.Children.Add(compData);
                        }

                        // childs of this obj
                        for (int i = 0; i < obj.transform.childCount; i++)
                        {
                            var child = obj.transform.GetChild(i).gameObject;
                            var childData = new ObjectData()
                            {
                                Name = child.name,
                                Type = child.GetIl2CppType().FullName,
                                Go = child
                            };
                            AddChildComponents(childData, child, depth + 1);
                            data.Children.Add(childData);
                        }
                    }
                }
            }
        }

        if (Input.GetKeyDown(KeyCode.F8))
        {
            App.Log.LogInfo("[Overlay] running diagnostics page");
            OverlayManager.RunDiagnostics();
        }
    }

    private class ObjectData
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "n/a";
        public string Type { get; set; } = "unk";
        public List<ObjectData> Children { get; set; } = [];

        [NotMapped]
        [JsonIgnore]
        internal GameObject Go { get; set; }
    }

    private class ObjectExtraData
    {
        public string K { get; set; }
        public string V { get; set; }
    }

    private ObjectData _inspectedObject;

    private void GetMoreData(string id)
    {
        App.Log.LogInfo($"[Object Inspector] GetMoreData for object with id {id}");
        if (_inspectedObject == null) return;

        // search in all tree an object with that id
        var selected = FindInside(_inspectedObject, id);
        if (selected == null)
        {
            App.Log.LogInfo($"[Object Inspector] object with id {id} not found in the inspected tree");
            return;
        }
        if (selected.Go == null)
        {
            App.Log.LogInfo($"[Object Inspector] object with id {id} has no GameObject reference");
            return;
        }

        var data = new List<ObjectExtraData>();

        switch (selected.Type)
        {
            case "Transform":
            {
                data.Add(new ObjectExtraData { K = "position", V = selected.Go.transform.localPosition.ToString() });
                data.Add(new ObjectExtraData { K = "rotation", V = selected.Go.transform.localEulerAngles.ToString() });
                data.Add(new ObjectExtraData { K = "scale", V = selected.Go.transform.localScale.ToString() });
                break;
            }
            case "Rigidbody":
            {
                var rigidbody = selected.Go.GetComponent<Rigidbody>();
                if (rigidbody == null) break;
                data.Add(new ObjectExtraData { K = "mass", V = rigidbody.mass.ToString(CultureInfo.InvariantCulture) });
                data.Add(new ObjectExtraData { K = "drag", V = rigidbody.drag.ToString(CultureInfo.InvariantCulture) });
                data.Add(new ObjectExtraData { K = "angularDrag", V = rigidbody.angularDrag.ToString(CultureInfo.InvariantCulture) });
                data.Add(new ObjectExtraData { K = "useGravity", V = rigidbody.useGravity.ToString() });
                data.Add(new ObjectExtraData { K = "isKinematic", V = rigidbody.isKinematic.ToString() });
                break;
            }
            case "Collider":
            {
                var collider = selected.Go.GetComponent<Collider>();
                if (collider == null) break;
                data.Add(new ObjectExtraData { K = "enabled", V = collider.enabled.ToString() });
                data.Add(new ObjectExtraData { K = "isTrigger", V = collider.isTrigger.ToString() });
                data.Add(new ObjectExtraData { K = "material", V = collider.material?.name ?? "null" });
                break;
            }
            case "MeshRenderer":
            {
                var mr = selected.Go.GetComponent<MeshRenderer>();
                if (mr == null) break;
                data.Add(new ObjectExtraData { K = "enabled", V = mr.enabled.ToString() });
                data.Add(new ObjectExtraData { K = "castShadows", V = mr.shadowCastingMode.ToString()});
                data.Add(new ObjectExtraData { K = "material", V = mr.material?.name ?? "null"});
                break;
            }
        }

        OverlayManager.PostMessage("inspectObjectExtra", data);
        App.Log.LogInfo($"[Object Inspector] sent extra data for object with id {id}");
        App.Log.LogInfo(data);

        return;

        ObjectData FindInside(ObjectData oData, string innerId)
        {
            if (oData.Id == innerId) return oData;

            foreach (var objectData in oData.Children)
            {
                if (objectData.Id == innerId)
                    return objectData;
                if (FindInside(objectData, innerId) is {} found)
                    return found;
            }

            return null;
        }
    }

    private CancellationTokenSource _cts = new();
    private const float ThresholdChange = 0.1f;

    /// <summary>
    /// Gap between position sends. Receivers dead-reckon between packets, so 30/s is plenty
    /// and halves the traffic the old 15 ms loop pushed through the tunnel.
    /// </summary>
    private const int SendIntervalMs = 33;

    /// <summary>Per-stream packet counters, so receivers can discard out-of-order updates.</summary>
    private uint _playerSeq;
    private uint _truckSeq;

    #region Player Location updates

    private bool _pptRunning = false;

    /// <summary>
    /// Unity isn't thread-safe, so we will update the position outside the main thread
    /// to avoid stuck the Update thread
    /// </summary>
    /// <returns></returns>
    private void PlayerPositionThread()
    {
        if (_pptRunning) return;

        var ct = _cts.Token;
        Plugin.StartAttachedThread(() =>
        {
            _pptRunning = true;

            App.Log.LogInfo("PlayerPositionThread started");

            var lastPosition = Vector3.zero;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var player = _player;
                    var rigid = _playerRigid;
                    if (Network.NetId == -1 || player == null || rigid == null)
                    {
                        ct.WaitHandle.WaitOne(100);
                        continue;
                    }

                    player.transform.GetPositionAndRotation(out var position, out var rotation);
                    if (Vector3.Distance(lastPosition, position) > ThresholdChange)
                    {
                        Network.SendServerMessage(new UpdatePositionCmd
                        {
                            // Sent in absolute world space: every client recentres its scene
                            // differently, so raw local coordinates mean nothing to the receiver.
                            Position = ConvertToSharedVector3(FloatingOrigin.ToWorld(position)),
                            Rotation = ConvertToSharedQuaternion(rotation),
                            Velocity = ConvertToSharedVector3(rigid.velocity),
                            AngVel = ConvertToSharedVector3(rigid.angularVelocity),
                            IsTruck = false,
                            InSeat = false,
                            Seq = ++_playerSeq
                        }, PacketType.UpdatePosition);
                        lastPosition = position;
                    }
                }
                catch (Exception ex)
                {
                    // The player object dies with the world when a save is unloaded; the next
                    // arrival in a sector finds the new one. An unhandled exception here would
                    // take the whole game down with the thread.
                    App.Log.LogWarning($"[Sync] Player position send failed: {ex.Message}");
                    ct.WaitHandle.WaitOne(1000);
                    continue;
                }

                ct.WaitHandle.WaitOne(SendIntervalMs);
            }

            _pptRunning = false;
        });
    }

    #endregion

    #region Truck Location updates

    private bool _tptRunning = false;

    private void TruckPositionThread()
    {
        if (_tptRunning) return;

        var ct = _cts.Token;
        Plugin.StartAttachedThread(() =>
        {
            _tptRunning = true;

            App.Log.LogInfo("TruckPositionThread started");

            var lastPosition = Vector3.zero;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var truck = _truck;
                    var rigid = _truckRigid;
                    if (Network.NetId == -1 || truck == null || rigid == null)
                    {
                        ct.WaitHandle.WaitOne(100);
                        continue;
                    }

                    truck.transform.GetPositionAndRotation(out var position, out var rotation);
                    if (Vector3.Distance(lastPosition, position) > ThresholdChange)
                    {
                        Network.SendServerMessage(new UpdatePositionCmd
                        {
                            Position = ConvertToSharedVector3(FloatingOrigin.ToWorld(position)),
                            Rotation = ConvertToSharedQuaternion(rotation),
                            Velocity = ConvertToSharedVector3(rigid.velocity),
                            AngVel = ConvertToSharedVector3(rigid.angularVelocity),
                            IsTruck = true,
                            InSeat = false,
                            Seq = ++_truckSeq
                        }, PacketType.UpdatePosition);
                        lastPosition = position;
                    }
                }
                catch (Exception ex)
                {
                    App.Log.LogWarning($"[Sync] Truck position send failed: {ex.Message}");
                    ct.WaitHandle.WaitOne(1000);
                    continue;
                }

                ct.WaitHandle.WaitOne(SendIntervalMs);
            }

            _tptRunning = false;
        });
    }

    #endregion

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