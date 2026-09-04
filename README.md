# StarTruckerMP Enhanced

Multiplayer for **Star Trucker**: your friends' trucks in your sector, painted the way they painted them, nameplates, chat, a CB radio that sounds like one, and a server you can start from inside the game.

A fork of [StarTruckMP](https://github.com/pitermcflebor/StarTruckMP) with fixed synchronisation, an in-game menu in every language the game ships, and hosting.

> Alpha. Crashes and desync happen.

[Русский](README.ru.md) · [How it works](HOW-IT-WORKS.md) · [Install with an AI assistant](AI-INSTALL.md) · [Build from source](AGENTS.md)

---

## Install

You need:

- **Star Trucker** on Steam (Windows).
- **[.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)** — the ".NET Runtime" section, Windows x64. Without it there is no player panel and no Host button.

Then:

1. Download `StarTruckerMP-Enhanced-<version>-win-x64.zip` from [Releases](../../releases/latest).
2. Open the game folder: Steam → right-click Star Trucker → Manage → Browse local files.
3. Copy the **contents** of the folder `Copy into the game folder` into the game folder, overwriting. `BepInEx`, `dotnet` and `winhttp.dll` must end up next to `Star Trucker.exe`.
4. Launch the game through Steam. **The first launch takes several minutes** and the window may look frozen. Wait for the main menu.
5. The main menu now has a **Multiplayer** entry → **Player** → enter the server address → **Connect**. The address is remembered.

The mod only connects once a save is loaded; in the main menu it just waits.

**Upgrading:** delete `BepInEx\plugins\StarTruckMP` first, then copy the new archive.
**Uninstalling:** delete `BepInEx`, `dotnet`, `winhttp.dll`, `doorstop_config.ini` and `.doorstop_version` from the game folder.

---

## Playing

- There is no shared world. Everyone plays their **own** save; the mod syncs trucks, their paint and parts, trailers, names, chat and the radio. Money and progress stay yours.
- You only see each other **in the same sector**. The panel on the right and the right-hand cab monitor show who is where. Agree on a sector and the others appear by themselves.
- **Trucks look like their owners made them**: livery, base material, the colours picked at the paint shop, the exhausts, grill, ornament, sensors, plate and decal where the game lets them be shown, plus wear and dirt. Their headlights switch with the owner's, and a hitched trailer swings behind the cab the way the owner's does. A trailer the owner unhitches stays where they left it, drifting as it drifts, until they hitch it back, take another or deliver it.
- **Nameplates** carry the player's colour and the distance to the truck; the same names, in the same colours, sit under their sectors on the galactic map.
- **CB radio.** Pick up the handset in the cab and hold the game's talk button; everyone on the server hears you with a proper radio sound, squelch bursts included. While the game's own radio conversation is running, the radio belongs to the game. A marker beside a name on the monitor and above a truck shows who is talking.
- **Inviting friends.** Multiplayer → Friends → **Invite friends via Steam** opens Steam's invite dialog. While you host or are on a server, friends see **Join game** beside your name in their Steam friends list; accepting it starts their game pointed at your server, nothing to type. The Friends page also lists friends already on a server, one press to join them.
- **The lobby line.** The Multiplayer page says whether the server answers and who is on it, from the main menu already: *Server 203.0.113.10:7777: 3 online — Alice, Bob, Carol*. The Connect page repeats the names under the connection status.
- **F2** opens the multiplayer menu over the game (connection, chat, settings). **Esc** closes it.
- **Chat**: sit in the driver's seat, switch the right-hand monitor to the docking camera, press **Enter** (rebindable), type, Enter to send. Standing up closes the line.
- **Settings** live in one place: Multiplayer → **Settings**. That page carries the chat key, no pause on a server, the update check and the F2 overlay switch, and opens two pages for the two bigger subjects: **Other players** (nameplates, collisions, ghosting at gates) and **Radio and microphone** (microphone, test, volumes, noise suppression, radio sound, mute during dialogue, radios in nearby trucks). Every setting is on exactly one page. The F2 menu shows the same groups under the same names. The menu speaks the game's language: English, Russian, German, French, Spanish, Portuguese, Polish, Italian and Chinese.
- The mod checks GitHub once at startup and says in the menu when a newer release is out.

More in [HOW-IT-WORKS.md](HOW-IT-WORKS.md).

---

## Hosting

**From the game.** Multiplayer → Host → Start. "Invite friends via Steam" sends them an invitation they accept with one click; "Copy details for friends" puts the address and port on the clipboard for everyone else. The server lives as long as the game is open.

**Standalone** (a VPS, say). Download `StarTruckerMP-Enhanced-server-<version>-win-x64.zip`, install the .NET 10 Runtime and run `StarTruckMP.Server.exe`. Settings appear in `server.json` next to it.

Either way, friends need your **public IP** and port **7777**, and your router must forward **7777 over TCP and UDP** to your computer. If forwarding is impossible, any VPN such as Radmin or ZeroTier works — give friends the VPN address instead.

By default the server takes clients at their word. To have it really check Steam tickets, put a `SteamWebApiKey` into `server.json` — get one at [steamcommunity.com/dev/apikey](https://steamcommunity.com/dev/apikey).

**Admin page.** `https://<server address>:7777/admin` shows who is online, where and with what ping, the chat and the log, with a kick button per player. Set `ApiAdminUsername` and `ApiAdminPassword` in `server.json` first: the page stays disabled while the password is the default. The certificate is self-signed, so the browser will warn once.

---

## If it does not work

The log is at `<game folder>\BepInEx\LogOutput.log`. The line `Handshake completed with server` means you are connected.

| What you see | Cause and fix |
|---|---|
| No "Multiplayer" entry in the menu | The files did not land in the game's root. `BepInEx`, `dotnet`, `winhttp.dll` must sit next to `Star Trucker.exe`. |
| The game "froze" on first launch | BepInEx is generating its files. Wait. |
| `actively refused it` in the log | The server is not running or the address is wrong. The client connects by itself once the server appears. |
| `Connection attempt N got no handshake` in the log | The server is unreachable: check the address and the host's port forwarding. |
| No player panel on the right | .NET 10 Runtime is not installed, or the F2 overlay is switched off under Multiplayer → Settings. |
| Connected, no errors, nobody visible | You are in different sectors. Check the panel on the right. |
| The game stutters with the mod, even off a server | Switch the F2 overlay off under Multiplayer → Settings and restart: it is a second browser drawn over the game, and on some machines that is the stutter. The in-game page and the cab monitor carry everything it did. |

---

## Credits

- The original **StarTruckMP** mod — [PiterMcFlebor](https://github.com/pitermcflebor) and [JayJay34](https://github.com/JayJay34).
- **Enhanced** — synchronisation fixes, in-game menu, chat, cab monitor, hosting, ghost mode, truck appearance, radio settings and sound, localisation — **ShinyM**.

ShinyM makes games. **Echo 91** is out: [Steam](https://store.steampowered.com/app/3906060/Echo_91/) · [itch.io](https://shinym2.itch.io/echo-91). More to come.

MIT licence — [LICENSE](LICENSE).
