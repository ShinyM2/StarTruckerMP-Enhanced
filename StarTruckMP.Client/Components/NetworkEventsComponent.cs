using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.Caching.Memory;
using StarTruckMP.Client.Synchronization;
using StarTruckMP.Client.UI;
using StarTruckMP.Shared;
using StarTruckMP.Shared.Cmd;
using StarTruckMP.Shared.Dto;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.SceneManagement;
using Object = Il2CppSystem.Object;
using Quaternion = UnityEngine.Quaternion;
using SynchronizationContext = Il2CppSystem.Threading.SynchronizationContext;
using Vector3 = UnityEngine.Vector3;

namespace StarTruckMP.Client.Components;

public class NetworkEventsComponent : MonoBehaviour
{
    private bool _connected;
    private SynchronizationContext _mainThreadContext;

    private void Awake()
    {
        _mainThreadContext = SynchronizationContext.Current;
        DontDestroyOnLoad(gameObject);

        Network.OnConnected += HandleConnected;
        Network.OnDisconnected += HandleDisconnected;
        Network.OnPlayerDisconnected += HandlePlayerDisconnected;
        Network.OnPlayerSectorUpdate += HandlePlayerSectorUpdate;
        Network.OnPlayerPositionUpdate += HandlePlayerPositionUpdate;
        Network.OnTruckLiveryUpdate += HandleTruckLiveryUpdate;
        Network.OnTrailerUpdate += HandleTrailerUpdate;
        Network.OnPlayerNameUpdate += HandlePlayerNameUpdate;
        Network.OnPingsUpdate += HandlePings;
        Network.OnTruckStateUpdate += HandleTruckState;
        GameEventsComponent.ArrivedAtSector += HandleOwnSectorChanged;
        OverlayManager.MessageReceived += HandleOverlayMessage;

        App.Log.LogInfo("NetworkEventsComponent Awake and subscribed to network events");
    }

    private IMemoryCache _players = new MemoryCache(new MemoryCacheOptions());

    /// <summary>
    /// Everything the server has told us about each remote player, kept whether or not they
    /// are currently spawned. Updates arrive in any order and often before the player enters
    /// our sector, so without this the livery and trailer they already had were simply lost
    /// and they showed up as a bare unpainted cab.
    /// </summary>
    private readonly Dictionary<int, RemoteState> _remote = new();

    private class RemoteState
    {
        public string Name = string.Empty;
        public string Sector = "none";
        public string Livery = string.Empty;
        public TruckAppearance Appearance;
        public bool Headlights;
        public int TrailerCount;
        public string TrailerLivery = string.Empty;
        public string TrailerCargoTypeId = string.Empty;

        /// <summary>Milliseconds as the server last measured them, -1 until it says.</summary>
        public int Ping = -1;
    }

    private RemoteState StateOf(int netId)
    {
        if (_remote.TryGetValue(netId, out var state)) return state;

        state = new RemoteState();
        _remote[netId] = state;
        return state;
    }

    /// <summary>
    /// The trucks of the players in our sector, by net id, for anything that wants to measure or
    /// point at them: the monitor's distance column, the nameplates. Game thread only.
    /// </summary>
    private static readonly Dictionary<int, GameObject> _remoteTrucks = new();

    /// <summary>A remote player's truck, or null when they are not in our sector.</summary>
    public static GameObject RemoteTruck(int netId) =>
        _remoteTrucks.TryGetValue(netId, out var truck) && truck != null ? truck : null;

    private class NetPlayer
    {
        public int PlayerId { get; set; }
        public string PlayerName { get; set; }
        public GameObject TruckObj { get; set; }
        public GameObject PlayerObj { get; set; }
        public GameObject SuitObj { get; set; }

        /// <summary>Trailer containers taken off the NPC cab and driven by their own position stream, by index in the train.</summary>
        public Dictionary<int, GameObject> Trailers { get; } = new();

        /// <summary>
        /// The movers, kept apart from the objects so a position can be handed to them from the
        /// network thread without touching Unity: fetched once here on the game thread.
        /// </summary>
        public volatile TruckControllerComponent TruckMover;
        public ConcurrentDictionary<byte, TruckControllerComponent> TrailerMovers { get; } = new();
    }

    private NetPlayer CreateNetPlayer(int netId)
    {
        var state = StateOf(netId);
        var player = new NetPlayer
        {
            PlayerId = netId,
            PlayerName = ResolveName(netId)
        };

        var go = GameObject.Find("[Sector]");
        var scene = go?.scene;
        if (scene == null) App.Log.LogError($"({netId}) Could not find sector root object to get scene for player");

        #region Truck setup

        player.TruckObj = TruckFactory.CreatePlayerTruck(state.TrailerCount, Vector3.zero, Quaternion.identity);
        if (player.TruckObj == null)
        {
            App.Log.LogError($"({netId}) Failed to create player truck object");
        }
        else
        {
            App.Log.LogInfo($"({netId}) Created player truck object");

            var controller = player.TruckObj.GetComponent<TruckControllerComponent>();
            if (controller != null) controller.NetId = netId;
            player.TruckMover = controller;
            _remoteTrucks[netId] = player.TruckObj;

            ConfigureRemoteTruckPhysics(player.TruckObj);

            AttachNameplate(player.TruckObj, player.PlayerName, netId);

            // Replay the paint and cargo we were told about earlier; the truck prefab above was
            // already picked for the right number of containers, so only the contents are left.
            TruckAppearanceSync.Apply(player.TruckObj, state.Livery, state.Appearance);
            ScheduleHeadlights(player.TruckObj, state.Headlights);

            ApplyTrailerContainers(netId, player, state);
        }

        #endregion

        #region Player setup

        player.PlayerObj = new GameObject($"RemotePlayer-{netId}");
        App.Log.LogInfo($"({netId}) Created player object");
        if (scene != null)
        {
            SceneManager.MoveGameObjectToScene(player.PlayerObj, scene.Value);
            App.Log.LogInfo($"({netId}) Moved player object to scene");
        }
        player.PlayerObj.transform.SetParent(null);

        #endregion

        #region Player suit setup

        if (PlayerState.SpaceSuit == null)
        {
            App.Log.LogWarning($"({netId}) No space suit prefab known yet, the player on foot will be invisible");
            App.Log.LogInfo($"Created NetPlayer for netId {netId}");
            return player;
        }

        var suit = Instantiate(PlayerState.SpaceSuit, Vector3.zero, Quaternion.identity, player.PlayerObj.transform);
        App.Log.LogInfo($"({netId}) Instantiated player suit");
        player.SuitObj = suit;
        var suitRenderer = suit.GetComponent<MeshRenderer>();
        if (PlayerState.SpaceSuitMats != null && suitRenderer != null)
            PlayerState.SpaceSuitMats.CopyTo(suitRenderer.materials, 0);
        suit.active = true;
        suit.name = $"ClientSuit-{netId}";
        Destroy(suit.transform.GetComponent<SpaceSuitController>());
        Destroy(suit.transform.GetComponent<CapsuleCollider>());
        Destroy(suit.transform.GetComponent<OutlinableSetterUpper>());
        Destroy(suit.transform.GetComponent<EPOOutline.Outlinable>());
        Destroy(suit.transform.GetComponent<EPOOutline.TargetStateListener>());
        Destroy(suit.transform.GetComponent<MaterialSwitcher>());
        Destroy(suit.transform.GetComponent<InteractTarget>());
        Destroy(suit.transform.GetComponent<DoorController>());
        App.Log.LogInfo($"({netId}) Configured player suit");

        #endregion

        App.Log.LogInfo($"Created NetPlayer for netId {netId}");

        return player;
    }

    private void RecreateNetPlayerTruck(int netId, int cargoCount)
    {
        if (!_players.TryGetValue(netId, out NetPlayer player))
            return;

        var currentPos = Vector3.zero;
        var currentRot = Quaternion.identity;
        var state = StateOf(netId);

        if (player.TruckObj != null)
        {
            // Keep where it was; everything else is rebuilt from what the server told us.
            player.TruckObj.transform.GetPositionAndRotation(out currentPos, out currentRot);
            player.TruckObj.SetActive(false);
            DestroyImmediate(player.TruckObj);
        }

        // The trailers were taken off the old cab and live on their own; the new cab brings new ones.
        DestroyTrailers(player);

        // recreate with same data
        player.TruckObj = TruckFactory.CreatePlayerTruck(cargoCount, currentPos, currentRot);
        if (player.TruckObj == null)
        {
            App.Log.LogError($"Failed to recreate truck for player {netId}");
            return;
        }

        var rebuiltController = player.TruckObj.GetComponent<TruckControllerComponent>();
        if (rebuiltController != null) rebuiltController.NetId = netId;
        player.TruckMover = rebuiltController;
        _remoteTrucks[netId] = player.TruckObj;

        TruckAppearanceSync.Apply(player.TruckObj, state.Livery, state.Appearance);
        ScheduleHeadlights(player.TruckObj, state.Headlights);
        ConfigureRemoteTruckPhysics(player.TruckObj);
        // The old truck carried the nameplate, so the rebuilt one needs its own.
        AttachNameplate(player.TruckObj, player.PlayerName, netId);
        App.Log.LogInfo($"Recreated truck for player {netId} with cargo count {cargoCount}");
    }

    private void HandleTruckLiveryUpdate(UpdateLiveryDto liveryDto)
    {
        _mainThreadContext.Post(new Action<Object>(_ =>
        {
            var state = StateOf(liveryDto.NetId);
            state.Livery = liveryDto.Livery ?? string.Empty;
            if (liveryDto.Appearance != null) state.Appearance = liveryDto.Appearance;

            if (!_players.TryGetValue(liveryDto.NetId, out NetPlayer player)) return;
            if (player.TruckObj == null) return;

            TruckAppearanceSync.Apply(player.TruckObj, state.Livery, state.Appearance);
        }), null);
    }

    /// <summary>What a packet is about: its Kind, or for an older sender the IsTruck flag.</summary>
    private static byte KindOf(UpdatePositionDto dto) => dto.Kind != 0 ? dto.Kind : (byte)(dto.IsTruck ? 1 : 0);

    /// <summary>
    /// A movement packet, on the network thread the moment it lands. The trucks and trailers that
    /// already have a mover take it straight away — the mover keeps its own ordering and touches
    /// nothing of Unity's — so no frame is spent waiting for the game thread. Only what needs
    /// Unity, a trailer that has to be set up first or the player on foot, is posted across.
    /// </summary>
    private void HandlePlayerPositionUpdate(UpdatePositionDto positionDto, double arrivedAt)
    {
        if (!_players.TryGetValue(positionDto.NetId, out NetPlayer player)) return;

        var kind = KindOf(positionDto);

        if (kind == 1)
        {
            var mover = player.TruckMover;
            if (mover != null) mover.ApplyNetworkState(positionDto, arrivedAt);
            return;
        }

        if (kind == 2 && player.TrailerMovers.TryGetValue(positionDto.Index, out var trailerMover))
        {
            trailerMover.ApplyNetworkState(positionDto, arrivedAt);
            return;
        }

        _mainThreadContext.Post(new Action<Object>(_ =>
        {
            if (!_players.TryGetValue(positionDto.NetId, out NetPlayer current)) return;

            if (kind == 2)
            {
                ApplyTrailerMove(positionDto.NetId, current, positionDto, arrivedAt);
            }
            else if (kind == 0 && current.PlayerObj != null)
            {
                current.PlayerObj.transform.SetPositionAndRotation(
                    FloatingOrigin.ToScene(Vec(positionDto.Position)),
                    Quat(positionDto.Rotation)
                );
                var rigid = current.PlayerObj.transform.GetComponent<Rigidbody>();
                if (rigid != null)
                {
                    rigid.velocity = Vec(positionDto.Velocity);
                    rigid.angularVelocity = Vec(positionDto.AngVel);
                }
            }
        }), null);
    }

    /// <summary>
    /// Moves a remote player's trailer where its owner's trailer is.
    ///
    /// The NPC cab carries its containers rigidly in slots; the owner's trailer swings on a real
    /// joint. So the first time a trailer stream arrives, the slot's container is taken out of
    /// the cab's hierarchy and given the same interpolating mover the cab has. The container is
    /// spawned asynchronously by the game, so until it exists the packet is simply skipped.
    /// </summary>
    private void ApplyTrailerMove(int netId, NetPlayer player, UpdatePositionDto dto, double arrivedAt)
    {
        if (player.TruckObj == null) return;

        if (!player.Trailers.TryGetValue(dto.Index, out var trailer) || trailer == null)
        {
            var slots = player.TruckObj.GetComponentsInChildren<AIVehicleContainerSlot>(true);
            if (slots == null || dto.Index >= slots.Length) return;

            var container = slots[dto.Index].m_currentContainer;
            if (container == null) return;

            container.transform.SetParent(null, true);

            var body = container.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.isKinematic = true;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.detectCollisions = App.RemoteCollisions.Value;
            }

            var mover = container.GetComponent<TruckControllerComponent>() ?? container.AddComponent<TruckControllerComponent>();
            mover.NetId = netId;

            player.Trailers[dto.Index] = container;
            player.TrailerMovers[dto.Index] = mover;
            App.Log.LogInfo($"({netId}) Trailer {dto.Index} now follows its own position stream ({slots.Length} slot(s) on the cab).");
            trailer = container;
        }

        var controller = trailer.GetComponent<TruckControllerComponent>();
        controller?.ApplyNetworkState(dto, arrivedAt);
    }

    private static void DestroyTrailers(NetPlayer player)
    {
        foreach (var trailer in player.Trailers.Values)
        {
            if (trailer != null) Destroy(trailer);
        }

        player.Trailers.Clear();
        player.TrailerMovers.Clear();
    }

    // Headlights are applied twice: at once, and again a second later when the light switchers
    // have run their Start and made the glow material the second pass needs.
    private readonly List<(float At, GameObject Truck, bool On)> _headlightsLater = new();

    private void ScheduleHeadlights(GameObject truck, bool on)
    {
        TruckStateSyncComponent.ApplyHeadlights(truck, on);
        _headlightsLater.Add((Time.unscaledTime + 1f, truck, on));
    }

    private void Update()
    {
        LinkStats.Publish();

        if (_headlightsLater.Count == 0) return;

        for (var i = _headlightsLater.Count - 1; i >= 0; i--)
        {
            var (at, truck, on) = _headlightsLater[i];
            if (Time.unscaledTime < at) continue;

            _headlightsLater.RemoveAt(i);
            if (truck != null) TruckStateSyncComponent.ApplyHeadlights(truck, on);
        }
    }

    private void HandleTruckState(TruckStateDto state)
    {
        _mainThreadContext.Post(new Action<Object>(_ =>
        {
            StateOf(state.NetId).Headlights = state.Headlights;

            if (_players.TryGetValue(state.NetId, out NetPlayer player) && player.TruckObj != null)
                TruckStateSyncComponent.ApplyHeadlights(player.TruckObj, state.Headlights);
        }), null);
    }

    private static Vector3 Vec(global::StarTruckMP.Shared.Vector3 vec) => new(vec.X, vec.Y, vec.Z);

    private static Quaternion Quat(global::StarTruckMP.Shared.Quaternion quat) => new(quat.X, quat.Y, quat.Z, quat.W);

    private void HandleTrailerUpdate(UpdateTrailerDto trailerDto)
    {
        _mainThreadContext.Post(new Action<Object>(_ =>
        {
            var state = StateOf(trailerDto.NetId);
            state.TrailerCount = trailerDto.TrailerCount;
            state.TrailerLivery = trailerDto.LiveryId ?? string.Empty;
            state.TrailerCargoTypeId = trailerDto.CargoTypeId ?? string.Empty;

            if (!_players.TryGetValue(trailerDto.NetId, out NetPlayer player)) return;

            App.Log.LogInfo($"Trailer update info for player {trailerDto.NetId}: TrailerCount=[{trailerDto.TrailerCount}], LiveryId='{trailerDto.LiveryId}', CargoTypeId='{trailerDto.CargoTypeId}'");

            // The cab prefab differs per container count, so the truck has to be rebuilt.
            RecreateNetPlayerTruck(trailerDto.NetId, trailerDto.TrailerCount);
            ApplyTrailerContainers(trailerDto.NetId, player, state);
        }), null);
    }

    /// <summary>
    /// Fills the truck's container slots from the player's last known cargo. Called both on a
    /// live trailer update and when the player is spawned into our sector, so someone who
    /// hitched their load before we ever saw them still turns up towing it.
    /// </summary>
    private void ApplyTrailerContainers(int netId, NetPlayer player, RemoteState state)
    {
        if (state.TrailerCount == 0 || player.TruckObj == null) return; // nothing to do

        var existingSlots = player.TruckObj.GetComponentsInChildren<AIVehicleContainerSlot>(true);

        foreach (var slot in existingSlots)
        {
            if (slot == null)
            {
                App.Log.LogInfo($"Tried to spawn container for {netId} but no AIVehicleContainerSlot found in truck hierarchy");
                return;
            }
            if (CargoMetadataProvider.instance == null)
            {
                App.Log.LogError($"CargoMetadataProvider is null, cannot spawn container for {netId}");
                return;
            }
            if (CargoMetadataProvider.instance.cargoCatalogue == null)
            {
                App.Log.LogError($"CargoCatalogue is null, cannot spawn container for {netId}");
                return;
            }

            CargoType cargoType = null;
            if (!string.IsNullOrEmpty(state.TrailerCargoTypeId))
            {
                if (!CargoMetadataProvider.instance.cargoCatalogue.lookUp.TryGetValue(state.TrailerCargoTypeId, out cargoType))
                    App.Log.LogWarning($"GetById returned null for CargoTypeId '{state.TrailerCargoTypeId}', falling back to index 0");
            }
            cargoType ??= CargoMetadataProvider.instance.cargoCatalogue.GetByIndex(0);
            if (cargoType == null)
            {
                App.Log.LogError($"Cargo type at index 0 is null, cannot spawn container for {netId}");
                return;
            }
            if (cargoType.container == null)
            {
                App.Log.LogError($"cargoType.container is null for {netId}, cannot spawn container");
                return;
            }

            var liveryAssetRef = CustomizationManager.instance.GetLiveryAssetRefFromId(state.TrailerLivery);
            if (liveryAssetRef == null)
                App.Log.LogWarning($"GetLiveryAssetRefFromId returned null for livery ID '{state.TrailerLivery}', SpawnContainer may fail");

            App.Log.LogInfo($"Spawning container for {netId}: cargoType={cargoType}, container={cargoType.container}, liveryAssetRef={liveryAssetRef}");

            slot.SpawnContainer(new AIVehicleCustomizationData.CargoContainerData
            {
                m_container = cargoType.container,
                m_cargoType = cargoType,
                m_containerLivery = liveryAssetRef,
                m_damagePercent = 0f
            });
        }
    }

    /// <summary>
    /// Name to show for a player: whatever they reported at handshake, falling back to their
    /// net id when the platform gave them no name.
    /// </summary>
    private string ResolveName(int netId) =>
        _remote.TryGetValue(netId, out var state) && !string.IsNullOrWhiteSpace(state.Name)
            ? state.Name
            : $"Player #{netId}";

    /// <summary>
    /// Decides whether a remote truck is solid. Collisions are off by default and stripped
    /// rather than merely disabled: a remote truck is a kinematic body driven by packets, so
    /// with any latency it collides where it visually is not, which throws the local player
    /// off course for no reason they can see.
    /// </summary>
    private static void ConfigureRemoteTruckPhysics(GameObject truck)
    {
        if (truck == null) return;

        var body = truck.GetComponent<Rigidbody>();
        var solid = App.RemoteCollisions.Value;

        if (body != null)
            body.detectCollisions = solid;

        if (solid) return;

        foreach (var collider in truck.GetComponentsInChildren<Collider>())
            DestroyImmediate(collider);
    }

    private static void AttachNameplate(GameObject truck, string name, int netId)
    {
        if (truck == null) return;

        if (App.ShowNameplates.Value)
        {
            var nameplate = truck.AddComponent<NameplateComponent>();
            nameplate.NetId = netId;
            nameplate.SetName(name);
        }

        // Ghosting is about being able to move, not about labels, so it is attached whether or
        // not nameplates are on. It reads the setting itself and costs nothing while it is off.
        truck.AddComponent<GhostComponent>();
    }

    private void HandlePlayerNameUpdate(int netId, string name)
    {
        _mainThreadContext.Post(new Action<Object>(_ =>
        {
            StateOf(netId).Name = name ?? string.Empty;

            // The player may already be spawned — a snapshot delivers the name and the
            // sector separately, and either can arrive first.
            if (_players.TryGetValue(netId, out NetPlayer player))
            {
                player.PlayerName = ResolveName(netId);
                var nameplate = player.TruckObj?.GetComponent<NameplateComponent>();
                nameplate?.SetName(player.PlayerName);
            }

            PushOverlayRoster();
        }), null);
    }

    /// <summary>
    /// The overlay page announces itself once its scripts are running. Roster pushes sent
    /// before that point can be missed, so resend the current one whenever it (re)loads.
    /// </summary>
    private void HandleOverlayMessage(string type, string payload)
    {
        if (type != "overlayLoaded") return;

        // Raised on the pipe reader thread — the roster dictionaries belong to the game thread.
        _mainThreadContext.Post(new Action<Object>(_ => PushOverlayRoster()), null);
    }

    /// <summary>
    /// Sends the current roster to the CEF overlay: who is connected, what they are called
    /// and which sector they are in, plus ourselves so the panel can show a total.
    /// </summary>
    private void PushOverlayRoster()
    {
        var roster = new OverlayRoster
        {
            Self = new OverlayRosterEntry
            {
                NetId = -1,
                Name = string.IsNullOrWhiteSpace(PlayerState.Name) ? "You" : PlayerState.Name,
                Sector = PlayerState.Sector,
                SameSector = true,
                Ping = _ownPing
            }
        };

        foreach (var known in _remote)
        {
            roster.Players.Add(new OverlayRosterEntry
            {
                NetId = known.Key,
                Name = ResolveName(known.Key),
                Sector = known.Value.Sector,
                SameSector = known.Value.Sector == PlayerState.Sector,
                Ping = known.Value.Ping,
                Color = MultiplayerState.ColorHex(known.Key)
            });
        }

        roster.Total = roster.Players.Count + 1;

        // The truck's monitor reads the same roster, so publish it rather than keeping it
        // private to the browser overlay.
        MultiplayerState.OwnPing = _ownPing;
        MultiplayerState.Players.Clear();
        foreach (var entry in roster.Players)
        {
            MultiplayerState.Players.Add(new MultiplayerState.Player
            {
                NetId = entry.NetId,
                Name = entry.Name,
                Sector = entry.Sector,
                SameSector = entry.SameSector,
                Ping = entry.Ping
            });
        }

        OverlayManager.PostMessage("players", roster);
    }

    private class OverlayRoster
    {
        public int Total { get; set; }
        public OverlayRosterEntry Self { get; set; }
        public List<OverlayRosterEntry> Players { get; set; } = new();
    }

    private class OverlayRosterEntry
    {
        public int NetId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Sector { get; set; } = string.Empty;
        public bool SameSector { get; set; }
        public int Ping { get; set; } = -1;

        /// <summary>The player's colour as "#rrggbb", the same one their nameplate and monitor line use.</summary>
        public string Color { get; set; } = string.Empty;
    }

    /// <summary>Our own latency, as the server reports it back to us.</summary>
    private int _ownPing = -1;

    /// <summary>
    /// The server's latency table for everyone, including us.
    ///
    /// It arrives on a network thread every couple of seconds and is folded into the roster the
    /// two surfaces already read, so neither the overlay nor the monitor learns a new source for
    /// it. Players the table does not mention are left as they were: an entry going missing for
    /// one broadcast means the packet was lost, not that the player has no latency.
    /// </summary>
    private void HandlePings(PingsDto pings)
    {
        _mainThreadContext.Post(new Action<Object>(_ =>
        {
            foreach (var entry in pings.Players)
            {
                if (entry.NetId == Network.NetId) _ownPing = entry.Ping;
                else if (_remote.ContainsKey(entry.NetId)) _remote[entry.NetId].Ping = entry.Ping;
            }

            PushOverlayRoster();
        }), null);
    }

    private void HandlePlayerSectorUpdate(UpdateSectorDto sectorDto)
    {
        _mainThreadContext.Post(new Action<Object>(_ =>
        {
            StateOf(sectorDto.NetId).Sector = sectorDto.Sector;

            lock (sectorDto.NetId.ToString())
            {
                ApplySectorVisibility(sectorDto.NetId, sectorDto.Sector);
            }

            PushOverlayRoster();
        }), null);
    }

    /// <summary>
    /// Our own sector changed. Two things have to happen that nothing else does:
    /// the server still holds the sector we reported when we connected, so it needs the new
    /// one; and every player already known to us has to be re-checked, because their sector
    /// did not change and no UpdateSector packet is coming to re-run the comparison for us.
    /// </summary>
    private void HandleOwnSectorChanged(string sector)
    {
        _mainThreadContext.Post(new Action<Object>(_ =>
        {
            if (_connected)
                Network.SendServerMessage(new UpdateSectorCmd { Sector = sector }, PacketType.UpdateSector);

            App.Log.LogInfo($"Own sector is now {sector}, re-checking {_remote.Count} known player(s)");
            FloatingOrigin.LogState("sector change");

            foreach (var known in _remote)
                ApplySectorVisibility(known.Key, known.Value.Sector);

            PushOverlayRoster();
        }), null);
    }

    /// <summary>
    /// Spawns or despawns a remote player depending on whether they share our sector,
    /// doing nothing when they are already in the right state — re-spawning an already
    /// visible player would orphan their current truck and suit objects in the scene.
    /// </summary>
    private void ApplySectorVisibility(int netId, string sector)
    {
        var sameSector = sector == PlayerState.Sector;
        var spawned = _players.TryGetValue(netId, out NetPlayer _);

        if (sameSector == spawned) return;

        if (sameSector)
        {
            _players.Set(netId, CreateNetPlayer(netId));
            App.Log.LogInfo($"Added player {netId} to cache for sector {sector}");
        }
        else
        {
            DespawnPlayer(netId);
            App.Log.LogInfo($"Removed player {netId} from cache due to sector change ({sector} != {PlayerState.Sector})");
        }
    }

    private void DespawnPlayer(int netId)
    {
        if (!_players.TryGetValue(netId, out NetPlayer player)) return;
        if (player.TruckObj != null) player.TruckObj.SetActive(false);
        if (player.PlayerObj != null) player.PlayerObj.SetActive(false);
        DestroyTrailers(player);
        _players.Remove(netId);
        _remoteTrucks.Remove(netId);
        RemoteTimeline.Forget(netId);
    }

    private void HandlePlayerDisconnected(int netId)
    {
        _mainThreadContext.Post(new Action<Object>(_ =>
        {
            lock (netId.ToString())
            {
                _remote.Remove(netId);
                DespawnPlayer(netId);
            }

            PushOverlayRoster();
        }), null);
    }

    private void HandleDisconnected()
    {
        _connected = false;

        // The connection now retries by itself, and the roster we come back to may be a
        // different one. Drop everybody rather than leaving frozen trucks from the old session.
        _mainThreadContext.Post(new Action<Object>(_ =>
        {
            foreach (var netId in new List<int>(_remote.Keys))
                DespawnPlayer(netId);

            _remote.Clear();
            PushOverlayRoster();
        }), null);
    }

    private void HandleConnected(int netId)
    {
        _connected = true;
        Network.SendServerMessage(new UpdateSectorCmd { Sector = PlayerState.Sector }, PacketType.UpdateSector);

        // The truck's look — livery, colours, parts, wear — is sent by AppearanceSyncComponent,
        // which forgets what it last sent on every connection and so reports it afresh here.
    }

    private void Unsubscribe()
    {
        Network.OnConnected -= HandleConnected;
        Network.OnDisconnected -= HandleDisconnected;
        Network.OnPlayerDisconnected -= HandlePlayerDisconnected;
        Network.OnPlayerSectorUpdate -= HandlePlayerSectorUpdate;
        Network.OnPlayerPositionUpdate -= HandlePlayerPositionUpdate;
        Network.OnTruckLiveryUpdate -= HandleTruckLiveryUpdate;
        Network.OnTrailerUpdate -= HandleTrailerUpdate;
        Network.OnPlayerNameUpdate -= HandlePlayerNameUpdate;
        Network.OnPingsUpdate -= HandlePings;
        Network.OnTruckStateUpdate -= HandleTruckState;
        GameEventsComponent.ArrivedAtSector -= HandleOwnSectorChanged;
        OverlayManager.MessageReceived -= HandleOverlayMessage;
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }
}