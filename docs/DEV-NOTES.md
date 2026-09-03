# Developer notes

What the game does, and what it cost to find out. Everything here was learned by experiment; none of it can be read off the source. Read with [AGENTS.md](../AGENTS.md).

## State of the fork

Verified in game:

| Area | Where |
|---|---|
| Positions in world space | `FloatingOrigin`, `TruckControllerComponent` |
| Stale movement packets dropped | `UpdatePositionCmd.Seq`, `NetworkEventsComponent.IsFreshMove` |
| Movement scoped to the sector | `ServerManager.QueueSendToSectorExcept` |
| Reconnect regardless of start order | `Network.Polling` |
| Nameplates | `NameplateComponent` |
| Player list and chat in the F2 overlay | `overlay-ui`, `MultiplayerUiComponent` |
| Main-menu and pause-menu entry, settings page | `MainMenuScreen_Patch`, `PauseScreen_Patch`, `MultiplayerScreen` |
| Hosting from inside the game | `HostControl` |
| Steam ticket validation | `SteamTicketValidator`, with `SteamWebApiKey` set |

Built and compiled, **not yet seen with two players**: the cab monitor page (`MonitorPanel`, on the docking-camera channel), ghost mode (`GhostComponent`, `GhostZones`), the ping column, the rebindable chat key, chat itself and its seat gating (`MonitorPanel.InSeat`), the radio page (`MultiplayerScreen`, `VoiceInputComponent`): microphone choice, the loopback test, gain, noise suppression, radio volume, the radio sound levels and squelch bursts (`RadioVoiceEffectProcessor`, `CbRadioSpeakerComponent`), the radio yielding to the game's NPC calls (`CbRadioPttComponent.IsDialogueBusy`), the on-air markers on nameplates and the monitor, the full truck appearance (`TruckAppearanceSync`, `AppearanceSyncComponent`), and the eleven-language `Strings` table.

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
