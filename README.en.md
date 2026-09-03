# StarTruckerMP Enhanced

Multiplayer for **Star Trucker**: your friends' trucks in your sector, nameplates, chat, CB radio, and a server you can start from inside the game.

A fork of [StarTruckMP](https://github.com/pitermcflebor/StarTruckMP) with fixed synchronisation, an in-game menu and hosting.

> Alpha. Crashes and desync happen.

[Русский](README.md) · [Install with an AI assistant](AI-INSTALL.md) · [Build from source](AGENTS.md)

---

## Install

You need:

- **Star Trucker** on Steam (Windows).
- **[.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)** — the ".NET Runtime" section, Windows x64. Without it there is no player panel and no Host button.

Then:

1. Download `StarTruckerMP-Enhanced-<version>-win-x64.zip` from [Releases](../../releases/latest).
2. Open the game folder: Steam → right-click Star Trucker → Manage → Browse local files.
3. Copy the **contents** of the folder `Скопировать в папку игры` ("copy into the game folder") into the game folder, overwriting. `BepInEx`, `dotnet` and `winhttp.dll` must end up next to `Star Trucker.exe`.
4. Launch the game through Steam. **The first launch takes several minutes** and the window may look frozen. Wait for the main menu.
5. The main menu now has a **Multiplayer** entry → **Player** → enter the server address → **Connect**. The address is remembered.

The mod only connects once a save is loaded; in the main menu it just waits.

**Upgrading:** delete `BepInEx\plugins\StarTruckMP` first, then copy the new archive.
**Uninstalling:** delete `BepInEx`, `dotnet`, `winhttp.dll`, `doorstop_config.ini` and `.doorstop_version` from the game folder.

---

## Playing

- There is no shared world. Everyone plays their **own** save; the mod syncs trucks, trailers, names, chat and the radio. Money and progress stay yours.
- You only see each other **in the same sector**. The panel on the right and the right-hand cab monitor show who is where. Agree on a sector and the others appear by themselves.
- **F2** opens the multiplayer menu over the game (connection, chat, settings). **Esc** closes it.
- **Chat**: on the right-hand cab monitor switch the channel to the docking camera, press **Enter** (rebindable under Multiplayer → Display), type, Enter to send.
- **Settings** (Multiplayer → Display): nameplates, collisions with other players' trucks, ghosting at gates and bays, chat key.

---

## Hosting

**From the game.** Multiplayer → Host → Start. "Copy details for friends" puts the address and port on the clipboard. The server lives as long as the game is open.

**Standalone** (a VPS, say). Download `StarTruckerMP-Enhanced-server-<version>-win-x64.zip`, install the .NET 10 Runtime and run `StarTruckMP.Server.exe`. Settings appear in `server.json` next to it.

Either way friends need your **public IP** and port **7777**, and your router must forward port **7777, both TCP and UDP**, to your machine. If you cannot forward ports, any VPN such as Radmin or ZeroTier works; give friends the VPN address instead.

By default the server takes clients at their word. To have it verify Steam tickets, set `SteamWebApiKey` in `server.json` — get one at [steamcommunity.com/dev/apikey](https://steamcommunity.com/dev/apikey).

---

## Troubleshooting

The log is `<game folder>\BepInEx\LogOutput.log`. The line `Handshake completed with server` means you are connected.

| Symptom | Cause and fix |
|---|---|
| No "Multiplayer" entry in the menu | Files went one level too deep. `BepInEx`, `dotnet`, `winhttp.dll` must sit next to `Star Trucker.exe`. |
| Game "hangs" on the first launch | BepInEx is generating its files. Wait. |
| `actively refused it` in the log | The server is down or the address is wrong. The client reconnects on its own once it is up. |
| `Connection attempt N got no handshake` | Server unreachable: check the address and the host's port forwarding. |
| No player panel on the right | .NET 10 Runtime is missing. |
| Connected, no errors, nobody visible | Different sectors. Check the panel on the right. |

---

## Credits

- The original **StarTruckMP** — [PiterMcFlebor](https://github.com/pitermcflebor) and [JayJay34](https://github.com/JayJay34).
- **Enhanced** — sync fixes, in-game menu, chat, cab monitor page, hosting, ghost mode — **ShinyM**.

ShinyM makes games. **Echo 91** is out now: [Steam](https://store.steampowered.com/app/3906060/Echo_91/) · [itch.io](https://shinym2.itch.io/echo-91). More coming soon.

MIT licence — [LICENSE](LICENSE).
