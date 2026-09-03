# Installing StarTruckerMP Enhanced with an AI assistant

**Для игрока (по-русски).** Дайте вашему ИИ-помощнику (Claude Code, Codex, Cursor и т.п.) эту ссылку и одну фразу:

> Установи мне мод по инструкции https://github.com/ShinyM2/StarTruckerMP-Enhanced/blob/main/AI-INSTALL.md. Адрес сервера: `<адрес>`, порт `7777`.

Если адреса пока нет — не указывайте, его можно ввести позже в игре (Мультиплеер → Игрок).
Всё, что ниже, написано для ИИ.

---

## Instructions for the AI agent

You are installing **StarTruckerMP Enhanced**, a multiplayer mod for the Steam game **Star Trucker** (appid `2380050`) on **Windows**. Nothing is compiled; everything is prebuilt. Reply to the user in the language they write in.

Inputs you may have been given: a server address and port. If not given, skip step 5; the user can enter it in the game later.

### 1. Check prerequisites

Report anything missing instead of working around it.

1. **Star Trucker installed on Steam.** The mod authenticates with a Steam ticket, so there is no way around this.
2. **.NET 10 Runtime, x64.** Check with `dotnet --list-runtimes` for a line starting `Microsoft.NETCore.App 10.`. If missing, send the user to <https://dotnet.microsoft.com/download/dotnet/10.0>, section ".NET Runtime", Windows x64 installer. Without it the mod still runs, but the player list panel and the in-game Host button do not work.

### 2. Find the game folder

Do not assume `C:\Program Files (x86)\Steam`. Steam libraries are often on another drive.

```powershell
$steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam').SteamPath
# Read "$steam\steamapps\libraryfolders.vdf": each "path" entry is a library.
# The game is in <library>\steamapps\common\Star Trucker in whichever library lists appid 2380050.
```

The folder is right when it contains `Star Trucker.exe` and `GameAssembly.dll`. If you cannot find it, ask the user: Steam → right-click Star Trucker → Manage → Browse local files, and paste the path. Call it `$GameDir`.

### 3. Download the release

Get the latest `StarTruckerMP-Enhanced-<version>-win-x64.zip` from
<https://github.com/ShinyM2/StarTruckerMP-Enhanced/releases/latest> (about 220 MB). With the GitHub CLI:

```powershell
gh release download --repo ShinyM2/StarTruckerMP-Enhanced --pattern "StarTruckerMP-Enhanced-*-win-x64.zip" --dir "$env:TEMP\stmp"
```

Without `gh`, use `Invoke-WebRequest` on the asset URL from the releases page. Extract with `Expand-Archive`.

### 4. Copy the files

If `$GameDir\BepInEx\plugins\StarTruckMP` already exists (an older version), delete that folder first.

Copy the **contents** of the extracted folder `Copy into the game folder` into `$GameDir`, overwriting. Copy the contents, not the folder itself. Afterwards all of these must exist:

```
$GameDir\winhttp.dll
$GameDir\doorstop_config.ini
$GameDir\dotnet\
$GameDir\BepInEx\core\BepInEx.Core.dll
$GameDir\BepInEx\plugins\StarTruckMP\StarTruckMP.Client.dll
$GameDir\BepInEx\plugins\StarTruckMP\overlay\StarTruckMP.Overlay.exe
$GameDir\BepInEx\config\StarTruckMP.Client.cfg
```

If any is missing, the files went one level too deep. Fix that before going on.

### 5. Set the server address (only if you were given one)

Edit `$GameDir\BepInEx\config\StarTruckMP.Client.cfg`:

```ini
ServerAddress = <address the user gave you>
ServerPort = 7777
IgnoreSslValidation = true
```

Leave `IgnoreSslValidation = true`: every server uses a self-signed certificate. The user can also change the address later in the game under Multiplayer → Player.

### 6. First launch

Start the game through Steam, for example `Start-Process "steam://rungameid/2380050"`. Do not run the exe directly.

**The first launch takes several minutes and the window may look frozen.** BepInEx is generating interop assemblies from the game code. Do not kill it and do not report it as a hang. It is finished when `$GameDir\BepInEx\interop\` holds roughly 130 DLLs including `Assembly-CSharp.dll`.

The mod only connects once the user loads a save. Sitting in the main menu is not enough.

### 7. Verify

Read `$GameDir\BepInEx\LogOutput.log`. A good install shows, in order:

```
Loading [StarTruckMP 1.2.5]
[Auth] Steam persona name: <nickname>
[Auth] Steam token: <32 hex chars>
Connected to server <address>:7777, waiting for handshake...
Handshake completed with server. NetId: <n>
```

`Handshake completed with server` is the line that matters. There should be no `[Error   :StarTruckMP]` lines.

In game: a **Multiplayer** entry in the main menu, a player list panel on the right edge (F2 opens the full menu), and other players' names above their trucks when they are in the same sector.

### 8. If something is wrong

Match the actual log line. Do not guess.

| Log line / symptom | Cause and fix |
|---|---|
| No `StarTruckMP` lines at all, or no log file | BepInEx is not in the game root. Re-check step 4. |
| `Multiplayer` entry missing from the menu | Same as above. |
| `actively refused it` | The server is not running, or the address is wrong. The client retries every 5 s by itself; the user just waits for the host. |
| `Connection attempt N got no handshake, retrying` | Server unreachable, or the host forgot to forward TCP+UDP 7777. Check the address, then ask the host. |
| `UntrustedRoot` / `SSL connection could not be established` | `IgnoreSslValidation` is `false` in the cfg. Set it to `true`. |
| `StarTruckMP.Overlay.exe not found` | The `overlay` subfolder was not copied. Re-extract. |
| Overlay starts and exits, no player panel | .NET 10 Runtime is missing (step 1). |
| Connected, no errors, other players invisible | Expected when players are in different sectors. There is no shared world; players only render for each other inside the same sector. The panel on the right shows who is where. |

### Do not

- Do not compile anything or clone the repository; the release is prebuilt.
- Do not install a VPN unless the host explicitly uses one.
- Do not delete anything in `$GameDir` other than an old `BepInEx\plugins\StarTruckMP` folder when upgrading.

### Uninstall

Delete `BepInEx`, `dotnet`, `winhttp.dll`, `doorstop_config.ini` and `.doorstop_version` from `$GameDir`. The game itself is untouched.
