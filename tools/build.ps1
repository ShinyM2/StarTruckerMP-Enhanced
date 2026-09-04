<#
.SYNOPSIS
  Builds StarTruckerMP Enhanced and packages it for players.

.EXAMPLE
  .\tools\build.ps1 -GameDir "D:\SteamLibrary\steamapps\common\Star Trucker"

.NOTES
  Needs the .NET 10 SDK, Node.js, and a Star Trucker install with BepInEx already
  launched once (so that <game>\BepInEx\interop exists). The BepInEx archive used
  for the player package is downloaded unless -BepInExZip points at one.
  Works in Windows PowerShell 5.1 and PowerShell 7.
#>
param(
    [Parameter(Mandatory = $true)][string]$GameDir,
    [string]$BepInExZip = "",
    [string]$Version = "1.8.0",
    [string]$NodeDir = "",
    [switch]$SkipPackage
)

$ErrorActionPreference = "Stop"
$BepInExUrl = "https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788%2B5b766a3.zip"

if ($NodeDir) { $env:PATH = "$NodeDir;$env:PATH" }
foreach ($tool in @("dotnet", "npm")) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        throw "'$tool' is not on PATH. Install the .NET 10 SDK and Node.js, or pass -NodeDir <folder with npm.cmd>."
    }
}

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$out = Join-Path $root "artifacts"
$shim = Join-Path $out "shim"

function Run($exe, [string[]]$argv) {
    & $exe @argv
    if ($LASTEXITCODE -ne 0) { throw "$exe $($argv -join ' ') failed with exit code $LASTEXITCODE" }
}

$interop = Join-Path $GameDir "BepInEx\interop"
if (-not (Test-Path (Join-Path $interop "Assembly-CSharp.dll"))) {
    throw "No interop assemblies in '$interop'. Install BepInEx into the game and launch it once first."
}

# 1. Shadow BepInEx folder: the game's core + interop plus the XGamingRuntime stub.
Write-Host "== Preparing shadow BepInEx folder" -ForegroundColor Cyan
New-Item -ItemType Directory -Force (Join-Path $shim "core") | Out-Null
New-Item -ItemType Directory -Force (Join-Path $shim "interop") | Out-Null
Copy-Item (Join-Path $GameDir "BepInEx\core\*") (Join-Path $shim "core") -Recurse -Force
Copy-Item (Join-Path $interop "*") (Join-Path $shim "interop") -Recurse -Force
$env:StarTruckBepInEx = $shim
$env:StarTruckBepInExSteam = $shim

if (-not (Test-Path (Join-Path $shim "interop\XGamingRuntime.dll"))) {
    Run dotnet @("build", (Join-Path $root "tools\XGamingRuntime.Stub\XGamingRuntime.Stub.csproj"), "-c", "Release", "-o", (Join-Path $out "xgr-stub"))
    Copy-Item (Join-Path $out "xgr-stub\XGamingRuntime.dll") (Join-Path $shim "interop")
}

# 2. Overlay host first, then the client (the client stages the overlay out of bin\Release).
Write-Host "== Building overlay host" -ForegroundColor Cyan
Run dotnet @("build", (Join-Path $root "StarTruckMP.Overlay.Host\StarTruckMP.Overlay.Host.csproj"), "-c", "Release", "-r", "win-x64")

Write-Host "== Publishing client" -ForegroundColor Cyan
$client = Join-Path $out "client"
Run dotnet @("publish", (Join-Path $root "StarTruckMP.Client\StarTruckMP.Client.csproj"), "-c", "Release", "-r", "win-x64", "-p:EnableWindowsTargeting=true", "-o", $client)
if (-not (Test-Path (Join-Path $client "overlay\StarTruckMP.Overlay.exe"))) { throw "Overlay was not staged into the client output." }

# Publish leaves the satellite-resource folders of packages that are no longer referenced behind, empty.
Get-ChildItem $client -Directory | Where-Object { -not (Get-ChildItem $_.FullName -Recurse -File) } | Remove-Item -Recurse -Force

# 3. Overlay UI, then the server.
Write-Host "== Building overlay UI" -ForegroundColor Cyan
Remove-Item (Join-Path $root "StarTruckMP.Server\wwwroot\overlay") -Recurse -Force -ErrorAction SilentlyContinue
Push-Location (Join-Path $root "StarTruckMP.Server\overlay-ui")
try {
    Run npm @("ci")
    Run npm @("run", "build")
} finally { Pop-Location }

Write-Host "== Publishing server" -ForegroundColor Cyan
$server = Join-Path $out "server"
Run dotnet @("publish", (Join-Path $root "StarTruckMP.Server\StarTruckMP.Server.csproj"), "-c", "Release", "-r", "win-x64", "--no-self-contained", "-p:PublishSingleFile=true", "-p:RunSvelteBuild=false", "-o", $server)

# The in-game Host button runs the copy that sits next to the plugin.
Remove-Item (Join-Path $client "server") -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item $server (Join-Path $client "server") -Recurse

if ($SkipPackage) { Write-Host "Done (no package)."; return }

# 4. Player package: BepInEx + plugin + config + readme, zipped with UTF-8 names.
Write-Host "== Packaging" -ForegroundColor Cyan
if (-not $BepInExZip) {
    $BepInExZip = Join-Path $out "BepInEx.zip"
    if (-not (Test-Path $BepInExZip)) {
        Write-Host "Downloading BepInEx from $BepInExUrl"
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $BepInExUrl -OutFile $BepInExZip
    }
}

$pkg = Join-Path $out "package"
Remove-Item $pkg -Recurse -Force -ErrorAction SilentlyContinue
$game = Join-Path $pkg "Copy into the game folder"
New-Item -ItemType Directory -Force $game | Out-Null
Expand-Archive -Path $BepInExZip -DestinationPath $game -Force
Remove-Item (Join-Path $game "changelog.txt") -ErrorAction SilentlyContinue

Copy-Item $client (Join-Path $game "BepInEx\plugins\StarTruckMP") -Recurse
New-Item -ItemType Directory -Force (Join-Path $game "BepInEx\config") | Out-Null
Copy-Item (Join-Path $root "tools\package\StarTruckMP.Client.cfg") (Join-Path $game "BepInEx\config\StarTruckMP.Client.cfg")
Copy-Item (Join-Path $root "tools\package\README.txt") $pkg
Copy-Item (Join-Path $root "tools\package\ЧИТАЙ МЕНЯ.txt") $pkg
Copy-Item (Join-Path $root "AI-INSTALL.md") $pkg

Add-Type -AssemblyName System.IO.Compression.FileSystem
$playerZip = Join-Path $out "StarTruckerMP-Enhanced-$Version-win-x64.zip"
Remove-Item $playerZip -ErrorAction SilentlyContinue
[System.IO.Compression.ZipFile]::CreateFromDirectory($pkg, $playerZip, [System.IO.Compression.CompressionLevel]::Optimal, $false, [System.Text.Encoding]::UTF8)

$serverZip = Join-Path $out "StarTruckerMP-Enhanced-server-$Version-win-x64.zip"
Remove-Item $serverZip -ErrorAction SilentlyContinue
[System.IO.Compression.ZipFile]::CreateFromDirectory($server, $serverZip, [System.IO.Compression.CompressionLevel]::Optimal, $false, [System.Text.Encoding]::UTF8)

Write-Host "Done:" -ForegroundColor Green
Write-Host "  $playerZip"
Write-Host "  $serverZip"
