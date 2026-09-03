# AGENTS.md

Instructions for AI coding agents and developers working in this repository.

**StarTruckerMP Enhanced** is a multiplayer mod for **Star Trucker** (Steam appid `2380050`): a BepInEx 6 IL2CPP plugin plus a .NET 10 dedicated server. It is a fork of [pitermcflebor/StarTruckMP](https://github.com/pitermcflebor/StarTruckMP).

| The user wants | Go to |
|---|---|
| The mod installed so they can play | [AI-INSTALL.md](AI-INSTALL.md). Do not build from source for a player; releases are prebuilt. |
| To build, change or debug the code | This file. Game internals learned the hard way are in [docs/DEV-NOTES.md](docs/DEV-NOTES.md). |

Users are usually Russian-speaking; answer in the language they write in.

---

## Layout

| Project | Target | Purpose |
|---|---|---|
| `StarTruckMP.Client` | net6.0 | BepInEx IL2CPP plugin loaded into the game |
| `StarTruckMP.Server` | net10.0 | Dedicated server (Kestrel + LiteNetLib) |
| `StarTruckMP.Shared` | netstandard2.1 | Wire protocol shared by both |
| `StarTruckMP.Overlay.Host` / `.Browser` / `.Core` | net10.0-windows | Avalonia + CefSharp overlay window drawn over the game |
| `StarTruckMP.Server/overlay-ui` | SvelteKit | The overlay's web UI, served by the server |
| `tools/XGamingRuntime.Stub` | net6.0 | Compile-time stub so the client builds against a Steam copy of the game |
| `tools/build.ps1` | | Builds everything and packages a release |

## Build

Requirements: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), [Node.js](https://nodejs.org), and Star Trucker with BepInEx installed **and launched once** — the client compiles against the interop assemblies BepInEx generates into `<game>\BepInEx\interop`.

The easy way:

```powershell
.\tools\build.ps1 -GameDir "D:\SteamLibrary\steamapps\common\Star Trucker"
```

It builds the stub, the overlay, the client, the overlay UI and the server, and writes a player package and a server package into `artifacts\`. Add `-SkipPackage` to stop after publishing, `-NodeDir <folder>` if Node.js is not on PATH, `-BepInExZip <file>` to package with a BepInEx archive you already have.

By hand, the same steps:

```powershell
# 1. A shadow BepInEx folder: the game's core + interop, plus the XGamingRuntime stub.
#    XGamingRuntime.dll only ships with the Xbox build; the Steam build has none and the
#    client will not compile without a stand-in. It is never loaded at runtime on Steam.
$shim = "$PWD\artifacts\shim"
Copy-Item "<game>\BepInEx\core"    "$shim\core"    -Recurse
Copy-Item "<game>\BepInEx\interop" "$shim\interop" -Recurse
$env:StarTruckBepInEx = $shim; $env:StarTruckBepInExSteam = $shim
dotnet build tools\XGamingRuntime.Stub -c Release -o artifacts\xgr-stub
Copy-Item artifacts\xgr-stub\XGamingRuntime.dll "$shim\interop"

# 2. Overlay first, then the client (the client stages the overlay out of the host's bin\Release).
dotnet build StarTruckMP.Overlay.Host -c Release -r win-x64
dotnet publish StarTruckMP.Client -c Release -r win-x64 -p:EnableWindowsTargeting=true -o artifacts\client

# 3. Overlay UI, then the server.
Remove-Item StarTruckMP.Server\wwwroot\overlay -Recurse -ErrorAction SilentlyContinue
cd StarTruckMP.Server\overlay-ui; npm ci; npm run build; cd ..\..
dotnet publish StarTruckMP.Server -c Release -r win-x64 --no-self-contained -p:PublishSingleFile=true -p:RunSvelteBuild=false -o artifacts\server
Copy-Item artifacts\server artifacts\client\server -Recurse   # the in-game Host button runs this copy
```

Check `artifacts\client\overlay\StarTruckMP.Overlay.exe` exists. Build the overlay and the client in the **same configuration**: a Debug overlay and a Release client silently produce a package with no overlay.

To test, delete `<game>\BepInEx\plugins\StarTruckMP` and copy `artifacts\client` there.

## Conventions

- Wire types live in `StarTruckMP.Shared`. Changing one changes the protocol; client and server are deployed together. `[MessagePackObject(true)]` types are keyed by name, so adding a field is tolerant in both directions.
- The client runs under IL2CPP. New `MonoBehaviour`s need an `(IntPtr ptr) : base(ptr)` constructor and `ClassInjector.RegisterTypeInIl2Cpp<T>()` in `Plugin.cs`.
- Anything touching Unity objects runs on the game thread: post it through `_mainThreadContext.Post(...)` as the existing handlers do. Network callbacks arrive on other threads.
- Steamworks types are touched only inside `Authentication/SteamAuthHelper`, behind an assembly-presence check.
- Source files use CRLF and a UTF-8 BOM. Keep both.
- Log rather than guess: every assumption about the game's object hierarchy that was not checked against `BepInEx\LogOutput.log` turned out wrong. `docs/DEV-NOTES.md` has the traces to look for.

## Do not

- Do not commit `StarTruckMP.Server/wwwroot/`, `bin/`, `obj/`, `node_modules/` or `artifacts/`.
- Do not commit `server.json`, certificates or a Steam Web API key.
- Do not bump `NetProtocol.CurrentVersion` casually; it disconnects every client whose build does not match.
