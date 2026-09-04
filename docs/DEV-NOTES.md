# Developer notes

What the game does, and what it cost to find out. Everything here was learned by experiment; none of it can be read off the source. Read with [AGENTS.md](../AGENTS.md).

## State of the fork

Verified in game:

| Area | Where |
|---|---|
| Positions in world space | `FloatingOrigin`, `TruckControllerComponent` |
| Stale movement packets kept apart | `MovementCodec` (seq), `MovementReceiver` |
| Movement scoped to the sector | `ServerManager.QueueSendToSectorExcept` |
| Reconnect regardless of start order | `Network.Polling` |
| Nameplates | `NameplateComponent` |
| Player list and chat in the F2 overlay | `overlay-ui`, `MultiplayerUiComponent` |
| Main-menu and pause-menu entry, settings page | `MainMenuScreen_Patch`, `PauseScreen_Patch`, `MultiplayerScreen` |
| Hosting from inside the game | `HostControl` |
| Steam ticket validation | `SteamTicketValidator`, with `SteamWebApiKey` set |

Built and compiled, **not yet seen with two players**: the cab monitor page (`MonitorPanel`, on the docking-camera channel), ghost mode (`GhostComponent`, `GhostZones`), the ping column, the rebindable chat key, chat itself and its seat gating (`MonitorPanel.InSeat`), the radio page (`MultiplayerScreen`, `VoiceInputComponent`): microphone choice, the loopback test, gain, noise suppression, radio volume, the radio sound levels and squelch bursts (`RadioVoiceEffectProcessor`, `CbRadioSpeakerComponent`), the radio yielding to the game's NPC calls (`CbRadioPttComponent.IsDialogueBusy`), the on-air markers on nameplates and the monitor, the full truck appearance (`TruckAppearanceSync`, `AppearanceSyncComponent`), and the eleven-language `Strings` table.

Also not yet seen with two players, from 1.5.0: the hand-laid movement packet with the cab and the trailer in one (`MovementCodec`, protocol 3), the copy spawning out of sight and seeded from the server's last sighting (`NetworkEventsComponent.Parking`, `TruckControllerComponent.Seed`), the NPC brain, engine and thrusters switched off on the copy and its obstacle, point-of-interest, fine reporter and horn removed (`TruckFactory.QuietenAi`; the root's components are logged once as `[Truck] Remote cab root components`), the playback clock re-basing itself after the sender's physics clock falls behind (`RemoteTimeline.OffsetDropSeconds`), the server's per-player rate limits and `MaxPlayers`, and the reconnect that keeps its token unless the server rejected it.

From 1.6.0, also unseen in game: Steam invitations (`SteamPresence`, `Invites`): the Rich Presence `connect` key that gives friends "Join game", `ActivateGameOverlayInviteDialogConnectString` for the invite dialog, `+connect address:port` read off the command line at the first tick, and the friends list read through `GetFriendGamePlayed` + `GetFriendRichPresence`. The `Callback<GameRichPresenceJoinRequested_t>` for an invitation accepted while the game runs is a generic over a struct that IL2CPP only carries if the game instantiates it; `[Steam] Join requests while the game is running are not available` in the log means it does not, and only the launch path and the Friends page work. The lobby line (`ServerStatus` polling the server's unauthenticated `GET /api/status`), a server switch that drops the old token and signs in afresh (`Network.SwitchServer`; the sign-in loop stops when a newer one starts, `SteamAuthHelper._generation`), and the game's own menu entries put back into the state they had when the page closes (`MultiplayerScreen.Restore`).

1.6.1 fixes a loader crash that 1.5.0 introduced and 1.6.0 shipped: `ClassInjector.RegisterTypeInIl2Cpp` walks every instance method of an injected component, public or private, and treats any value type as convertible, so a method that takes one of the mod's own structs (`BodyState`, `MovementUpdate`, the private `Snapshot`) passes the eligibility check and then dies with a `NullReferenceException` in `ConvertMethodInfo`, which takes the whole plugin down before the menu entry exists. Such methods carry `[HideFromIl2Cpp]`; a reference type in a signature only produces the `unsupported return type` warning and is skipped, but the attribute is there too so the log stays clean.

## Game internals

### The floating origin

Star Trucker recentres the scene around the player as they travel. `transform.position` is meaningless outside the client that produced it: at sector load the scene origin is already kilometres from the world origin, and two players' offsets diverge as they separate. This was the cause of the "giant desync".

```csharp
FloatingOriginManager.ToWorldPosition(scenePos)   // for sending
FloatingOriginManager.ToScenePosition(worldPos)   // for rendering
FloatingOriginManager.SceneOriginInWorldSpace     // the offset itself
```

Everything on the wire is world space, and the target is converted **every physics step**, not once on arrival: the scene can be recentred between two packets.

### A menu entry has two labels

`MenuButton` carries `Option_Off_Label` (resting) and `Option_On_Label` (highlighted). Write both, or the original text shows the moment the row is selected.

### `RemoveAllListeners` does not remove the button's real action

The real behaviour is a persistent call baked into the prefab. Switch it off by index:

```csharp
for (var i = 0; i < click.GetPersistentEventCount(); i++)
    click.SetPersistentListenerState(i, UnityEventCallState.Off);
```

### Menu entries own the selection, and re-disable themselves

`MenuButton` carries an `Animator` (bools Pressed/Selected/Disabled) and a `disableOnEnable` flag; the screen's controller hands the UI selection to an entry every frame and disables entries it does not know about when it reshuffles its own. Consequences, all seen in game: a cloned entry switched back on comes up in the Disabled state with its resting label faded to nothing until hovered; a `TMP_InputField` inside a row takes one character and goes deaf; a clone of the pause menu's first entry inherits its lit plate. `MultiplayerScreen` therefore revives every visible row on each refresh (`Revive`: `disableOnEnable = false`, `SetEnabled(true)`, `ForceRefresh()`), strips button, animator and images from read-only and input rows, and draws and reads its text boxes itself (`TextField`, `HandleFields`).

The game's own entries are hit by the same thing when the page closes: they were switched off with `SetActive(false)` to make room, and switching them back on ran `OnEnable` with `disableOnEnable` set, so they came back in the Disabled state, faded, until the cursor passed over them. `MultiplayerScreen.Show` remembers each entry's `isInteractable` and `Hide` calls `Restore`, which puts it back with `SetEnabled` + `ForceRefresh` — enabled, or greyed when the game had greyed it (the pause menu's Save entry can be).

### The menu font has no Cyrillic

`TMP_FontAsset.HasCharacter` says no for Cyrillic while the menu is plainly in Russian: TMP draws it through a fallback. Never judge the language from the font. `StringTable.language` (`en`, `ru`, `pt-br`, ...) is the answer, valid once `StringTable.isReady`.

### `StringTableLookup` must be destroyed immediately

Every label carries a binding that restores the original text. `Object.Destroy` is deferred and the binding wins; use `DestroyImmediate`.

### The settings UI does not exist in the main menu

From the main menu: `DisplayScreen: 0, ItemListChooser: 0, OptionScreen: 0, MenuButton: 29`. The settings screens live in a scene that is not loaded, so `MultiplayerScreen` builds its rows from cloned `MenuButton`s.

### The pause menu holds two kinds of button

List entries are `Button_Option [...]`; the input hints along the bottom are `Button_Helper_Input_...`. Both are `MenuButton`s. Filter by name.

### The cab monitors

A monitor steps through **channels** (`MonitorChannelSwitcher.channels`): mirror and approach cameras, the docking camera (`dockingCameraChannel` is its index), and interface pages. The interface pages are switched by `MonitorOverlaySwitcher.ShowOverlayType(MonitorChannel)` — it takes the channel, not the enum; the page is `channel.overlayType`. Patching it with the enum in the signature broke every monitor in the cab.

The multiplayer page rides the docking-camera channel of the right-hand monitor. The `DockedStatus` *page* was the first target and was wrong: it only exists while docked. The camera behind the page is blanked by clear flags, not the culling mask; the skybox ignores masks. Nothing is restored when the page comes down: the game configures the camera for the incoming channel before the postfix runs.

A channel is a **slot and a view**: `channelIdx` is what the arrows step through, `channelViewIdx` picks among several entries of one slot. The docking slot (`channelIdx=0`) has five entries in `channels` — the camera with the hitching page (view 0), the trailer readout (view 1), two docked pages (views 2, 3) and the multi-tow page (view 4) — and `ProcessAutoViewSelection` moves between them on hitch and dock without the player touching anything. The page therefore claims by `channelIdx` (`MonitorPanel.Claims`), not by identity with the docking entry; matching that one entry is why a hitch used to replace the page with the trailer readout. The trailer and docked readouts remain on the left monitor, which has the same views.

`ShowOverlayType` runs *inside* `ActivateChannel`, before the channel postfix. The overlay postfix must decide from the channel it was given, not from whether our page happens to be active: reasserting on "active" took the incoming channel's page down on the way out and left a black screen. `MonitorPanel.HoldScreen` is guarded against re-entry — switching a page off can bring the overlay switcher straight back round to the same postfix.

### Nameplates need to ignore depth

Set `_ZTestMode = 8` (Always) and render queue 4000 on the label's **instanced** material (`fontMaterial`). `fontSharedMaterial` restyles every other piece of text using that font.

### The CB radio

`CBRadioController` sits under the player's truck; `cbHeldBinding.Get()` is true while the handset is in hand and `ControlBindings.cbTalk.Held()` while the talk button is down — that pair is the push-to-talk, as in the original mod. The handset (`CBHandsetController.isTalking`) knows the same thing; it is logged on every PTT transition so the two can be compared.

The game's own conversations run through the singleton `RadioChatState.instance`: `dialogueView.currentPanels` holds the NPC lines currently on screen and `availableResponseIds` the answers the player may pick (`cbResponse1..3`). Either being non-empty is what the mod treats as "the game owns the radio". Whether hollers (the player's own calls to NPCs) also show up in `availableResponseIds` is **unverified**; if they do, PTT is blocked whenever the handset is up near an NPC and the `[CB Radio] Game dialogue took the radio` line in the log will say so.

The interop assembly can be read offline with `System.Reflection.MetadataLoadContext` over `BepInEx\interop` + `BepInEx\core` + the .NET runtime directory; PowerShell 5.1's `ReflectionOnlyLoadFrom` cannot resolve its `System.Runtime 6.0` reference.

### What a truck's look is made of

The owner's choices live in `CustomizationState.CurrentCustomizationState` under the player's truck: `equippedLivery`, `equippedMaterial` (the base material), `equippedColors` — an `Il2CppStructArray<int>` of the game's own packed colour ints in the order Base, Primary, Secondary, Tertiary, Chrome, Chassis — and the bolt-on parts (`equippedExhaust`, `equippedGrill`, `equippedOrnament`, `equippedSensors`, `equippedLicensePlate` + `licensePlateLabel`, `equippedWindowDecal`, `equippedMaglockTopper`). Wear is `DamageState.OverallDamagePercent` and `OverallDirtPercent`.

A remote truck is an NPC cab. Its `AIVehicleCustomiser.m_cabLiveryApplier` is a `LiveryAndDamageApplierBase` that takes `LoadAndApplyLiveryById`, `SetBaseMaterialOverride`, `SetColorOverrides(Il2CppStructArray<int>)` and `SetOverallDamagePercent`; the `TruckExterior` subclass adds `SetOverallDirtPercent`. That covers the paint. The bolt-on parts go through a `CustomizationApplier` (`LoadAndApplyCustomization(CustomizationSlotKey.SingleIndexSlot(type), id, content)`), and **whether the NPC cab prefab carries one is unverified**: `TruckAppearanceSync` logs `[Appearance] The remote truck has no CustomizationApplier` once if it does not, and the parts are then simply not shown.

Whether `SetColorOverrides` survives a later `AssignCabLivery` (the livery loads asynchronously) is also unverified; the overrides are set before the livery is assigned on that assumption.

### Remote trucks: what else is driven, and how

- **Movement** is one packet per physics send with the cab and, when hitched, the trailer, laid out by hand in `MovementCodec` (protocol 3): positions as floats, rotations as their three smallest components in 16 bits, velocities as half floats, 31 bytes a body; the previous `MovementRedundancy` entries are repeated behind it with consecutive sequence numbers implied. The server reads only the header and the cab (`TryReadCurrent`) for the snapshot and relays the client's bytes behind a net id (`WriteRelayed`). Stamps are the sender's physics clock (`Time.fixedUnscaledTimeAsDouble` ms, so a catch-up burst of physics steps after a hitch is still spaced as the motion was). One `RemoteTimeline` per player turns the stamps into a playback clock: the offset is the windowed max of `sentAt - arrivedAt` (the fastest trip), the delay is `SendInterval * (worst recent loss burst + 1.5) + worst recent lateness + 20 ms` clamped to 80–450 ms, and playback runs at 0.85–1.15x to reach its target rather than jumping. When the raw offset sits more than 0.25 s under that max for eight packets — a physics clock that fell behind after a long hitch or a pause, which never catches the time up — the window is cleared and playback snaps once instead of running ahead of its data for ten seconds. `MovementReceiver` (per player) counts the packets and feeds `TruckControllerComponent` (cab and trailer alike, sharing the timeline), which interpolates with cubic Hermite through the reported velocities, coasts on velocity and angular velocity when dry, and folds any frame-to-frame discontinuity into an error that fades over ~0.2 s. A packet overtaken in flight still has its state inserted (dedup by seq), only its loss accounting is skipped. Packets are taken in on LiteNetLib's socket thread (`UnsyncedReceiveEvent`) and applied to the movers there. Both ends call `TriggerUpdate()` after `Send()` so nothing waits for the library's 15 ms tick. Positions are read in `FixedUpdate` on the game thread; the old background threads could read a position and the floating-origin offset on either side of a recentre.
- **The socket thread is not attached to IL2CPP.** Anything raised from it — every `Network.On*` event — may do managed work only. Touching the game, including `Il2CppSystem.Threading.SynchronizationContext.Post` and the `Action<Object>` it takes, allocates IL2CPP objects from a thread the runtime does not know, which is the "GC: Collecting from unknown thread" crash `Plugin.StartAttachedThread` exists to avoid. `NetworkEventsComponent` and `MultiplayerUiComponent` therefore queue everything but movement and voice in a managed `ConcurrentQueue<Action>` drained from `Update()`; `GameEventsComponent` sets a flag on connect and reports the hitched trailer from `Update()`.
- **Trailers** ride in the cab's packet as a second body. The receiver takes `AIVehicleContainerSlot.m_currentContainer` out of the NPC cab's hierarchy and gives it a `TruckControllerComponent` (kinematic, driven by the transform). The container spawns asynchronously, so the attempt is queued from the socket thread at most four times a second until it exists. Only the first hitched trailer is sent; how to enumerate a train from `StarTruck.hitchedTrailer` is **unverified**.
- **The copy of a truck is the NPC prefab with the NPC turned off.** `AIVehicle_Truck` (route planning in `AIVehicleBase.Update`), `AIVehicleEngine` (thrust in `FixedUpdate`) and `AIVehicleThrusters` are disabled rather than destroyed — the customiser and the container slots may reference them — while `NavObstacle`, `RegisterPointOfInterest`, `CollisionFineReporter`, `AITruckHorn` and `DevCameraTarget` are destroyed so the world stops counting the copy. `WarpGateTraverserAIVehicle` and `NPCVehicleAudio` are left alone: the former only answers a gate's calls with rigidbody forces a kinematic body ignores, the latter is the engine hum. Whether any of this changes what the game's traffic, radar or fines see is **unverified**; the root's component list is logged once.
- **Spawning.** A copy is made at `NetworkEventsComponent.Parking` (twenty kilometres below the origin, out of sight) and placed by its first state; spawning at the scene origin put it on the player's bonnet for a few frames. The server's snapshot carries the cab's last position, and a copy spawned in the same sector as that sighting is seeded with it (`TruckControllerComponent.Seed`) so a parked truck, which only reports itself once a second, stands in the right place at once. A snapshot is applied as one unit before the sector is looked at, so a towing player spawns once with the right cab rather than bare and then rebuilt. A rebuilt cab takes over the old mover's states (`TakeOver`). Despawn destroys the objects; hiding them leaked a truck, its nameplate and its audio sources per sector change.
- **Headlights** (`TruckStateCmd.Headlights`) come from `ExteriorLightSwitcher.m_binding.Get()` on the player's truck. On the NPC cab the switchers are disabled and their `m_objectsToToggle`, `m_glowRenderers` and `m_glowMaterialInst` emission are toggled directly, because the binding asset may be shared with the player's own truck. Applied twice, the second time a second later once `Start` has made the glow material. Whether the NPC cab carries `ExteriorLightSwitcher` at all is **unverified**.
- **Galactic map** (`MapPlayers_Patch`): sector buttons are matched to the wire's sector id by the first number in `SectorMetadata.sectorName` / `shortSectorId` / `displayNameId` / `SectorId.name`. The first button's identifiers are logged once as `[Map] Sector button …` so the matching can be checked.

### The game's string tables

`Star Trucker_Data\StreamingAssets\XML\Strings\<lang>.xml` are zip files holding `strings.xml` (`<string id="STR_BACK">Back</string>`). Languages: en, ru, de, fr, es, es-419, pt-br, pl, it (partial), zh-cn, zh-hant. `StringTable.language` returns the code; `StringTable.Get("STR_BACK")` etc. is what `Strings.Back/On/Off` use so those words match the game's menus.

### Reading a method's real signature

The interop assembly names the backing field after the signature, e.g. `NativeMethodInfoPtr_ShowOverlayType_Public_Void_MonitorChannel_0`. Check it before patching.

## Deployment reminders

- Build the overlay host **before** the client, in the **same** configuration.
- The client package carries the server under `server/` so the in-game Host button has something to start. `tools/build.ps1` does that copy.
- The overlay UI is served by the server the client authenticated against, so a change to `overlay-ui` appears only after that server's `wwwroot` is updated.
- Delete `StarTruckMP.Server/wwwroot/overlay` before `npm run build`. Vite writes hashed file names and clears nothing, so old bundles pile up and get published.
- Client, server and `StarTruckMP.Shared` ship together.

## The loop that works

1. Change, build, publish, copy into the game, launch.
2. Look at the screen.
3. Read `LogOutput.log` for `[MP screen]`, `[MainMenu]`, `[Pause]`, `[Monitor]`, `[Origin]`, `[Nameplate]`, `[Ghost]`, `[World]`, `[Sync]`.
4. When the hierarchy is in question, **log it** rather than guess twice.
