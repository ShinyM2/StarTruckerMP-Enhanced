# How it works

A short explanation for the player: what the mod does, what it does not, and why. [Русская версия](HOW-IT-WORKS.ru.md).

## What it is made of

- **The plugin in the game** (`BepInEx\plugins\StarTruckMP`). BepInEx is a mod loader; it starts with the game through `winhttp.dll` and picks the plugin up. The plugin reads your truck's position, draws the others and adds the menus.
- **The server** (`StarTruckMP.Server.exe`). A small program that takes data from every player and passes it to the rest. Started from the game or on its own. There is no game world on the server: it is only a postman.
- **The overlay** (`overlay\StarTruckMP.Overlay.exe`). A transparent window over the game with the player panel and the F2 menu. It needs the .NET 10 Runtime.

## There is no shared world, there are other trucks

Everyone plays **their own save**: their own money, jobs, progress, truck. The mod does not merge worlds; it shows players each other's trucks.

25 times a second your truck sends one small packet to the server: the cab's position, rotation and velocity, the trailer's too when one is hitched, and a repeat of the couple of states before it, so a packet the network loses costs nothing. The packet is laid out by hand and weighs about 110 bytes; the server passes it on untouched to whoever is in the same sector the moment it arrives, and a copy of your truck appears for them, with your paint, your trailer and your name above the cab.

A trailer you unhitch is still yours as far as the others are concerned: it keeps riding in your packet as the trailer body, so their copy of it stands, or drifts, exactly where yours does, until you hitch it back, hitch another or the game takes it away at delivery. Leave the sector and it is gone for them, as it is for you.

Synchronised: truck position, the truck's whole look (livery, base material, colours, bolt-on parts, wear and dirt), headlights, trailer (count, cargo and where it swings, hitched or left standing), name, chat, radio.
Not synchronised: cargo in the bed, money, jobs, purchases, station state, the spacesuited player outside the truck.

Positions travel with the sender's own clock. The copy is drawn a little in the past — 80 ms on a clean link, more on one that jitters or drops packets, measured from what the link has actually been doing over the last few seconds — along the curve between the two real states around that moment, so it moves as smoothly as the owner's truck whatever the network did to the packets. The cab and the trailer share one clock, so the train stays coupled. When the delay has to change, the copy plays a few percent slower or faster for a moment rather than jumping, and when packets stop altogether it coasts and then glides back to the truth when they return. The header of the cab monitor's multiplayer page shows the ping, the share of movement packets lost and the current delay.

## Sectors

The game keeps only one sector in memory: the one you are in. So another truck can only appear in **your** sector. The server knows who is where and sends movement only within a sector.

Who is in which sector is in the panel on the right (grey rows are in other sectors), on the right-hand cab monitor, and on the galactic map, where each player's name sits under their sector. No need to reconnect: arrive in the same sector and you see each other.

The easiest meeting point is a new game: everybody's starting sector is the same.

## Collisions and ghost mode

By default other players' trucks are **not solid**. Because of network delay the other truck is always slightly off from where you see it, and a collision would knock you off course for no visible reason. Turn them on under Multiplayer → Other players → Collide with players.

At **warp gates and docking bays** everyone crowds into the same spot. There, another player's truck near you, trailer included, is drawn as a pale see-through hologram, `GHOST` appears under the name, and a line at the top of your screen says that the trucks here are see-through and cannot hit you. Your own truck is never changed. Switched off under Multiplayer → Settings → Other players.

## Interface

- **Main menu → Multiplayer** and **pause → Multiplayer**. These are the game's own buttons, added, and everything on them is in the language the game is set to. First the three ways onto a server, in the order to try them — Friends, Connect by address, Your own server on this PC — then one entry, Settings, for everything else. The settings page carries the single switches itself (the chat key, no pause on a server, the update check, the F2 overlay) and opens Other players and Radio and microphone for the two subjects with more to them. No setting and no button appears on two pages: the Steam invite lives on Friends, and the Host page points there rather than repeating it.
- **The lobby line.** The first page carries one line about the server the mod is pointed at: checking, not answering, up with nobody on it, or *3 online — Alice, Bob, Carol*. It is asked every few seconds while a page is up, from the main menu too, so a player sees the server answer before loading a save. The Connect page repeats the names under the connection status.
- **Friends** (Steam only). *Invite friends via Steam* opens Steam's own invite dialog for the server you host or sit on. While you are on a server, Steam shows *Join game* beside your name to every friend; accepting it launches their game with the server on its command line, and the mod signs in there at once. The page also lists friends who are on a server right now, each a row to press. If the game is already running when an invitation is accepted, the join arrives through a Steam callback; should the game's build lack that callback, the invitation is picked up at the next launch, and the Friends page works either way.
- **F2**: the overlay over the game: connection, your own server, chat, settings. Esc closes. The panel with the player list is always visible. The overlay is a second browser drawn over the game, and it can be switched off under Settings if a machine stutters with it; the in-game page and the cab monitor carry everything it does.
- **The right-hand cab monitor.** Switch the channel with the arrows to the docking camera; the multiplayer page lives there: who is on the server, ping, chat, and a marker beside whoever is talking on the radio. Enter opens the input line, Esc cancels. The line only opens while you are in the driver's seat, and standing up closes it. While the line is open the keys do not drive the truck.
- **Ping** is measured by the server and sent to everyone every two seconds.
- **Radio** (CB radio): voice. Pick up the handset in the cab and hold the game's talk button; everyone on the server hears you, coloured like a radio, with a burst of static as you come on and drop off. While a story conversation is running (an NPC is speaking or you are being offered replies) the radio belongs to the game: your button does not transmit, and by default other players are muted until the call ends. Microphone, its volume, noise suppression, the radio sound and the "hear yourself" test are under Multiplayer → Radio and microphone.

## Settings

The file is `BepInEx\config\StarTruckMP.Client.cfg`. Everything in it can also be changed from the game.

| Key | What |
|---|---|
| `ServerAddress`, `ServerPort` | Where to connect. |
| `IgnoreSslValidation` | Accept the server's self-signed certificate. Must be `true`. |
| `ShowNameplates` | Names above trucks. |
| `RemoteCollisions` | Whether other trucks are solid. |
| `GhostMode` | Translucency at gates and bays. |
| `ChatKey` | The chat key on the monitor. |
| `MicrophoneDeviceName` | Which microphone to use. Empty means choose automatically. |
| `MicrophoneGain` | Microphone volume, `1.0` is as captured. |
| `NoiseSuppression` | Noise suppression (RNNoise) before sending. |
| `RadioVolume` | How loud other players are on the radio. |
| `RadioEffect` | The radio sound: `0` clean voice, `1` light, `2` full CB. |
| `MuteRadioDuringDialogue` | Mute players while a story conversation is running. |
| `Overlay` | Run the F2 overlay. Off if the game stutters with it; takes effect at the next start. |

## Server and ports

The server listens on port **7777** twice: **TCP** for login and the overlay, **UDP** for game traffic. If the host is at home behind a router, both have to be forwarded to their computer or nobody from outside gets in. The "Copy details for friends" button shows the host's public IP.

The server can start first or last; it does not matter. The client waits and connects by itself, and reconnects after a drop.

`https://<server address>:7777/api/status` answers without a sign-in: the player count, the names and their sectors. It is what the menu's lobby line reads, and anyone can open it in a browser to see whether a server is up.

## Security

Login is by **Steam ticket**: the game asks Steam for a one-time ticket, and the server can check it through the Steam Web API (if the host has put a `SteamWebApiKey` into `server.json`). Without the key the server takes clients at their word, which is enough for playing with friends.

All game traffic is encrypted (ECDH + ChaCha20-Poly1305). The server makes itself an HTTPS certificate on first start, which is why the client does not verify it.

## What the mod puts into the game folder

`BepInEx\`, `dotnet\`, `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version`. That is BepInEx and its runtime; the plugin itself is in `BepInEx\plugins\StarTruckMP`. The game's files are not touched; deleting these folders puts everything back.

The mod's log: `BepInEx\LogOutput.log`. When something is wrong, the answer is usually there.
