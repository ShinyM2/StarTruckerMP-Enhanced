using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Il2CppInterop.Runtime.Attributes;
using StarTruckMP.Client.Synchronization;
using StarTruckMP.Client.UI;
using StarTruckMP.Shared;
using StarTruckMP.Shared.Cmd;
using StarTruckMP.Shared.Dto;
using StarTruckMP.Shared.Movement;
using UnityEngine;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

namespace StarTruckMP.Client.Components;

/// <summary>
/// Turns what the server says about other players into trucks in the scene.
///
/// Network events arrive on the socket thread, which is not attached to the IL2CPP runtime and
/// so may touch nothing of the game's. Everything but movement is therefore queued here and
/// done in <see cref="Update"/>; movement is handed straight to the mover of the truck it is
/// about, which keeps its own ordering and touches nothing of Unity's either.
/// </summary>
public class NetworkEventsComponent : MonoBehaviour
{
    private bool _connected;

    /// <summary>Work for the game thread, in the order it was queued.</summary>
    private readonly ConcurrentQueue<Action> _mainThread = new();

    private void Post(Action work) => _mainThread.Enqueue(work);

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);

        Network.OnConnected += HandleConnected;
        Network.OnDisconnected += HandleDisconnected;
        Network.OnPlayerDisconnected += HandlePlayerDisconnected;
        Network.OnPlayerSnapshot += HandlePlayerSnapshot;
        Network.OnPlayerSectorUpdate += HandlePlayerSectorUpdate;
        Network.OnPlayerMovement += HandlePlayerMovement;
        Network.OnTruckLiveryUpdate += HandleTruckLiveryUpdate;
        Network.OnTrailerUpdate += HandleTrailerUpdate;
        Network.OnPingsUpdate += HandlePings;
        Network.OnTruckStateUpdate += HandleTruckState;
        GameEventsComponent.ArrivedAtSector += HandleOwnSectorChanged;
        OverlayManager.MessageReceived += HandleOverlayMessage;

        App.Log.LogInfo("NetworkEventsComponent Awake and subscribed to network events");
    }

    /// <summary>
    /// Everything the server has told us about each remote player, kept whether or not they
    /// are currently spawned. Updates arrive in any order and often before the player enters
    /// our sector, so without this the livery and trailer they already had were simply lost
    /// and they showed up as a bare unpainted cab. Game thread.
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

        /// <summary>Where the server last saw the cab, and in which sector, so a spawn can start there.</summary>
        public bool HasSeed;
        public string SeedSector = string.Empty;
        public BodyState Seed;
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

    /// <summary>The players spawned in our sector. Read on the socket thread, written on the game thread.</summary>
    private readonly ConcurrentDictionary<int, NetPlayer> _players = new();

    private class NetPlayer
    {
        public int PlayerId;
        public string PlayerName;
        public GameObject TruckObj;

        /// <summary>The trailer container taken off the NPC cab and driven by the movement stream.</summary>
        public GameObject TrailerObj;

        /// <summary>
        /// The movers, kept apart from the objects so a state can be handed to them from the
        /// network thread without touching Unity: fetched once here on the game thread.
        /// </summary>
        public volatile TruckControllerComponent TruckMover;
        public volatile TruckControllerComponent TrailerMover;

        /// <summary>Counts the packets and feeds the movers; one per player for the life of the spawn.</summary>
        public MovementReceiver Receiver;

        /// <summary>The container spawns asynchronously; the attempt to take it over is queued at most this often.</summary>
        public volatile bool TrailerAttachQueued;
        public long TrailerRetryAtTicks;

        /// <summary>What the current cab was built for, so an identical trailer update does not rebuild it.</summary>
        public int BuiltTrailerCount;
        public string BuiltTrailerLivery = string.Empty;
        public string BuiltTrailerCargo = string.Empty;
    }

    /// <summary>
    /// Where a copy waits between being made and its first placement. Out of sight, rather than
    /// at the scene origin: the origin is wherever the player is, and a truck that flashed up
    /// there for a few frames read as one appearing on the bonnet.
    /// </summary>
    private static readonly Vector3 Parking = new(0f, -20000f, 0f);

    private void Update()
    {
        while (_mainThread.TryDequeue(out var work))
        {
            try { work(); }
            catch (Exception ex) { App.Log.LogError($"[Net] {ex}"); }
        }

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

    // ---------------------------------------------------------------------------------------
    // Spawning
    // ---------------------------------------------------------------------------------------

    private NetPlayer CreateNetPlayer(int netId)
    {
        var state = StateOf(netId);
        var player = new NetPlayer
        {
            PlayerId = netId,
            PlayerName = ResolveName(netId),
            Receiver = new MovementReceiver(netId)
        };

        BuildTruck(player, state, Parking, Quaternion.identity, previous: null);

        if (player.TruckObj != null && state.HasSeed)
        {
            // World coordinates belong to a sector: the server's last sighting only helps when it
            // was made where we are. Used once; a truck that comes back later has moved since.
            if (state.SeedSector == PlayerState.Sector) player.TruckMover?.Seed(state.Seed);
            state.HasSeed = false;
        }

        App.Log.LogInfo($"Created NetPlayer for netId {netId}");
        return player;
    }

    /// <summary>Makes the cab for the player's current cargo and gives it everything a copy needs.</summary>
    private void BuildTruck(NetPlayer player, RemoteState state, Vector3 position, Quaternion rotation, TruckControllerComponent previous)
    {
        player.TruckObj = TruckFactory.CreatePlayerTruck(state.TrailerCount, position, rotation);
        player.BuiltTrailerCount = state.TrailerCount;
        player.BuiltTrailerLivery = state.TrailerLivery;
        player.BuiltTrailerCargo = state.TrailerCargoTypeId;

        if (player.TruckObj == null)
        {
            App.Log.LogError($"({player.PlayerId}) Failed to create player truck object");
            player.TruckMover = null;
            return;
        }

        var controller = player.TruckObj.GetComponent<TruckControllerComponent>();
        if (controller != null)
        {
            controller.NetId = player.PlayerId;
            if (previous != null) controller.TakeOver(previous);
        }

        player.TruckMover = controller;
        _remoteTrucks[player.PlayerId] = player.TruckObj;

        ConfigureRemoteTruckPhysics(player.TruckObj);
        AttachNameplate(player.TruckObj, player.PlayerName, player.PlayerId);

        // Replay the paint and cargo we were told about earlier; the truck prefab above was
        // already picked for the right number of containers, so only the contents are left.
        TruckAppearanceSync.Apply(player.TruckObj, state.Livery, state.Appearance);
        ScheduleHeadlights(player.TruckObj, state.Headlights);
        ApplyTrailerContainers(player.PlayerId, player, state);

        App.Log.LogInfo($"({player.PlayerId}) Built truck with {state.TrailerCount} container(s)");
    }

    private void RecreateNetPlayerTruck(NetPlayer player)
    {
        var position = Parking;
        var rotation = Quaternion.identity;
        var state = StateOf(player.PlayerId);
        var previous = player.TruckMover;

        if (player.TruckObj != null)
        {
            // Keep where it was; everything else is rebuilt from what the server told us.
            player.TruckObj.transform.GetPositionAndRotation(out position, out rotation);
            player.TruckObj.SetActive(false);
            DestroyImmediate(player.TruckObj);
        }

        // The trailer was taken off the old cab and lives on its own; the new cab brings a new one.
        DestroyTrailer(player);

        BuildTruck(player, state, position, rotation, previous);
    }

    private void HandleTruckLiveryUpdate(UpdateLiveryDto liveryDto)
    {
        Post(() =>
        {
            var state = StateOf(liveryDto.NetId);
            state.Livery = liveryDto.Livery ?? string.Empty;
            if (liveryDto.Appearance != null) state.Appearance = liveryDto.Appearance;

            if (!_players.TryGetValue(liveryDto.NetId, out var player)) return;
            if (player.TruckObj == null) return;

            TruckAppearanceSync.Apply(player.TruckObj, state.Livery, state.Appearance);
        });
    }

    // ---------------------------------------------------------------------------------------
    // Movement
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// A movement packet, on the network thread the moment it lands. The movers that exist take
    /// it straight away, so no frame is spent waiting for the game thread. A trailer that has
    /// no mover yet — its container spawns asynchronously — is queued for the game thread to
    /// set up, at most a few times a second.
    /// </summary>
    [HideFromIl2Cpp]
    private void HandlePlayerMovement(MovementUpdate update)
    {
        if (!_players.TryGetValue(update.NetId, out var player)) return;

        var trailer = player.TrailerMover;
        player.Receiver.Take(update, player.TruckMover, trailer);

        if (update.Current.HasTrailer && trailer == null && !player.TrailerAttachQueued &&
            Environment.TickCount64 >= player.TrailerRetryAtTicks)
        {
            player.TrailerAttachQueued = true;
            Post(() => AttachTrailer(player));
        }
    }

    /// <summary>
    /// Takes the container out of the NPC cab's slot and gives it the same interpolating mover the
    /// cab has: the cab carries its containers rigidly, the owner's trailer swings on a joint.
    /// The container is spawned asynchronously by the game, so until it exists this is retried.
    /// </summary>
    private void AttachTrailer(NetPlayer player)
    {
        try
        {
            if (player.TruckObj == null || player.TrailerMover != null) return;

            var slots = player.TruckObj.GetComponentsInChildren<AIVehicleContainerSlot>(true);
            var container = slots != null && slots.Length > 0 ? slots[0].m_currentContainer : null;
            if (container == null)
            {
                player.TrailerRetryAtTicks = Environment.TickCount64 + 250;
                return;
            }

            container.transform.SetParent(null, true);

            var body = container.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.isKinematic = true;
                body.interpolation = RigidbodyInterpolation.None;
                body.detectCollisions = App.RemoteCollisions.Value;
            }

            if (!App.RemoteCollisions.Value)
            {
                foreach (var collider in container.GetComponentsInChildren<Collider>(true))
                    DestroyImmediate(collider);
            }

            TruckFactory.QuietenAi(container.gameObject);

            var mover = container.GetComponent<TruckControllerComponent>() ?? container.AddComponent<TruckControllerComponent>();
            mover.NetId = player.PlayerId;

            player.TrailerObj = container.gameObject;
            player.TrailerMover = mover;
            App.Log.LogInfo($"({player.PlayerId}) Trailer now follows its own body in the movement stream ({slots.Length} slot(s) on the cab).");
        }
        finally
        {
            player.TrailerAttachQueued = false;
        }
    }

    private static void DestroyTrailer(NetPlayer player)
    {
        player.TrailerMover = null;
        if (player.TrailerObj != null) Destroy(player.TrailerObj);
        player.TrailerObj = null;
        player.TrailerRetryAtTicks = 0;
    }

    // ---------------------------------------------------------------------------------------
    // Headlights
    // ---------------------------------------------------------------------------------------

    // Headlights are applied twice: at once, and again a second later when the light switchers
    // have run their Start and made the glow material the second pass needs.
    private readonly List<(float At, GameObject Truck, bool On)> _headlightsLater = new();

    private void ScheduleHeadlights(GameObject truck, bool on)
    {
        TruckStateSyncComponent.ApplyHeadlights(truck, on);
        _headlightsLater.Add((Time.unscaledTime + 1f, truck, on));
    }

    private void HandleTruckState(TruckStateDto state)
    {
        Post(() =>
        {
            StateOf(state.NetId).Headlights = state.Headlights;

            if (_players.TryGetValue(state.NetId, out var player) && player.TruckObj != null)
                TruckStateSyncComponent.ApplyHeadlights(player.TruckObj, state.Headlights);
        });
    }

    // ---------------------------------------------------------------------------------------
    // Trailers
    // ---------------------------------------------------------------------------------------

    private void HandleTrailerUpdate(UpdateTrailerDto trailerDto)
    {
        Post(() =>
        {
            var state = StateOf(trailerDto.NetId);
            state.TrailerCount = trailerDto.TrailerCount;
            state.TrailerLivery = trailerDto.LiveryId ?? string.Empty;
            state.TrailerCargoTypeId = trailerDto.CargoTypeId ?? string.Empty;

            if (!_players.TryGetValue(trailerDto.NetId, out var player)) return;

            App.Log.LogInfo($"Trailer update info for player {trailerDto.NetId}: TrailerCount=[{trailerDto.TrailerCount}], LiveryId='{trailerDto.LiveryId}', CargoTypeId='{trailerDto.CargoTypeId}'");

            // The cab prefab differs per container count, so the truck has to be rebuilt — but
            // not for an update that says what it was already built for.
            if (player.TruckObj != null &&
                player.BuiltTrailerCount == state.TrailerCount &&
                player.BuiltTrailerLivery == state.TrailerLivery &&
                player.BuiltTrailerCargo == state.TrailerCargoTypeId)
                return;

            RecreateNetPlayerTruck(player);
        });
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

    // ---------------------------------------------------------------------------------------
    // Names, physics, nameplates
    // ---------------------------------------------------------------------------------------

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

    /// <summary>
    /// The overlay page announces itself once its scripts are running. Roster pushes sent
    /// before that point can be missed, so resend the current one whenever it (re)loads.
    /// </summary>
    private void HandleOverlayMessage(string type, string payload)
    {
        if (type != "overlayLoaded") return;

        // Raised on the pipe reader thread — the roster dictionaries belong to the game thread.
        Post(PushOverlayRoster);
    }

    // ---------------------------------------------------------------------------------------
    // Roster
    // ---------------------------------------------------------------------------------------

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
        Post(() =>
        {
            foreach (var entry in pings.Players)
            {
                if (entry.NetId == Network.NetId) _ownPing = entry.Ping;
                else if (_remote.TryGetValue(entry.NetId, out var state)) state.Ping = entry.Ping;
            }

            PushOverlayRoster();
        });
    }

    // ---------------------------------------------------------------------------------------
    // Who is where
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Everything the server knows about a player, as one unit: on our own connect for everyone
    /// already there, and for each player who joins after. Applied whole before the sector is
    /// looked at, so a truck spawns once, with its paint and its trailer, rather than bare and
    /// then rebuilt as each piece arrives.
    /// </summary>
    [HideFromIl2Cpp]
    private void HandlePlayerSnapshot(PlayerSnapshotDto snapshot)
    {
        Post(() =>
        {
            var state = StateOf(snapshot.NetId);
            state.Name = snapshot.Name ?? string.Empty;
            state.Livery = snapshot.Livery ?? string.Empty;
            if (snapshot.Appearance != null) state.Appearance = snapshot.Appearance;
            state.Headlights = snapshot.Headlights;
            state.TrailerCount = snapshot.TrailersCount;
            state.TrailerLivery = snapshot.TrailerLivery ?? string.Empty;
            state.TrailerCargoTypeId = snapshot.TrailerCargoTypeId ?? string.Empty;
            state.Sector = snapshot.Sector ?? "none";

            // A player who has not sent a position yet comes with an all-zero rotation, which no
            // real reading has; their cab waits out of sight for its first packet instead.
            var truck = snapshot.Truck;
            var sighted = truck != null &&
                          (truck.Rotation.X != 0f || truck.Rotation.Y != 0f || truck.Rotation.Z != 0f || truck.Rotation.W != 0f);
            if (sighted)
            {
                state.HasSeed = true;
                state.SeedSector = state.Sector;
                state.Seed = new BodyState
                {
                    Position = truck.Position,
                    Rotation = truck.Rotation,
                    Velocity = truck.Velocity,
                    AngVel = truck.AngVel
                };
            }

            // Already spawned — a snapshot for a player we know is the server telling us again.
            if (_players.TryGetValue(snapshot.NetId, out var player))
            {
                player.PlayerName = ResolveName(snapshot.NetId);
                var nameplate = player.TruckObj != null ? player.TruckObj.GetComponent<NameplateComponent>() : null;
                nameplate?.SetName(player.PlayerName);

                if (player.TruckObj != null)
                {
                    TruckAppearanceSync.Apply(player.TruckObj, state.Livery, state.Appearance);
                    TruckStateSyncComponent.ApplyHeadlights(player.TruckObj, state.Headlights);
                }
            }

            ApplySectorVisibility(snapshot.NetId, state.Sector);
            PushOverlayRoster();
        });
    }

    private void HandlePlayerSectorUpdate(UpdateSectorDto sectorDto)
    {
        Post(() =>
        {
            StateOf(sectorDto.NetId).Sector = sectorDto.Sector;
            ApplySectorVisibility(sectorDto.NetId, sectorDto.Sector);
            PushOverlayRoster();
        });
    }

    /// <summary>
    /// Our own sector changed. Two things have to happen that nothing else does:
    /// the server still holds the sector we reported when we connected, so it needs the new
    /// one; and every player already known to us has to be re-checked, because their sector
    /// did not change and no UpdateSector packet is coming to re-run the comparison for us.
    /// </summary>
    private void HandleOwnSectorChanged(string sector)
    {
        Post(() =>
        {
            if (_connected)
                Network.SendServerMessage(new UpdateSectorCmd { Sector = sector }, PacketType.UpdateSector);

            App.Log.LogInfo($"Own sector is now {sector}, re-checking {_remote.Count} known player(s)");
            FloatingOrigin.LogState("sector change");

            foreach (var known in _remote)
                ApplySectorVisibility(known.Key, known.Value.Sector);

            PushOverlayRoster();
        });
    }

    /// <summary>
    /// Spawns or despawns a remote player depending on whether they share our sector,
    /// doing nothing when they are already in the right state — re-spawning an already
    /// visible player would orphan their current truck and suit objects in the scene.
    /// </summary>
    private void ApplySectorVisibility(int netId, string sector)
    {
        var sameSector = sector == PlayerState.Sector;
        var spawned = _players.ContainsKey(netId);

        if (sameSector == spawned) return;

        if (sameSector)
        {
            _players[netId] = CreateNetPlayer(netId);
            App.Log.LogInfo($"Added player {netId} to cache for sector {sector}");
        }
        else
        {
            DespawnPlayer(netId);
            App.Log.LogInfo($"Removed player {netId} from cache due to sector change ({sector} != {PlayerState.Sector})");
        }
    }

    /// <summary>
    /// Takes a player's copy out of the scene for good. Destroyed, not merely hidden: a hidden
    /// truck kept its nameplate, its ghost materials and its audio sources, and one was left
    /// behind every time a player changed sector.
    /// </summary>
    private void DespawnPlayer(int netId)
    {
        if (!_players.TryRemove(netId, out var player)) return;

        player.TruckMover = null;
        DestroyTrailer(player);
        if (player.TruckObj != null) Destroy(player.TruckObj);
        player.TruckObj = null;

        _remoteTrucks.Remove(netId);
        RemoteTimeline.Forget(netId);
    }

    private void HandlePlayerDisconnected(int netId)
    {
        Post(() =>
        {
            _remote.Remove(netId);
            DespawnPlayer(netId);
            PushOverlayRoster();
        });
    }

    private void HandleDisconnected()
    {
        _connected = false;

        // The connection now retries by itself, and the roster we come back to may be a
        // different one. Drop everybody rather than leaving frozen trucks from the old session.
        Post(() =>
        {
            foreach (var netId in new List<int>(_remote.Keys))
                DespawnPlayer(netId);

            _remote.Clear();
            PushOverlayRoster();
        });
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
        Network.OnPlayerSnapshot -= HandlePlayerSnapshot;
        Network.OnPlayerSectorUpdate -= HandlePlayerSectorUpdate;
        Network.OnPlayerMovement -= HandlePlayerMovement;
        Network.OnTruckLiveryUpdate -= HandleTruckLiveryUpdate;
        Network.OnTrailerUpdate -= HandleTrailerUpdate;
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
