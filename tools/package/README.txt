===========================================================
  StarTruckerMP Enhanced — multiplayer for Star Trucker
===========================================================

Full guide and the latest version:
  https://github.com/ShinyM2/StarTruckerMP-Enhanced

Russian: see "ЧИТАЙ МЕНЯ.txt" in this archive.

Want an AI assistant to install it? Hand it the file AI-INSTALL.md
from this archive (or the link to it in the repository).


-----------------------------------------------------------
YOU NEED
-----------------------------------------------------------

1. Star Trucker on Steam.
2. .NET 10 Runtime (x64) — https://dotnet.microsoft.com/download/dotnet/10.0
   Section ".NET Runtime", the Windows x64 button.
   Without it the mod works, but there is no player panel and no
   "Host" button.


-----------------------------------------------------------
INSTALL
-----------------------------------------------------------

1. Find the game folder:
   Steam -> right-click Star Trucker -> Manage ->
   Browse local files. "Star Trucker.exe" is inside.

2. Copy EVERYTHING INSIDE the folder "Copy into the game folder"
   into the game folder, overwriting. The contents, not the folder
   itself. BepInEx, dotnet and winhttp.dll must end up next to
   "Star Trucker.exe".

   Installing over an older version — delete
   Star Trucker\BepInEx\plugins\StarTruckMP first.

3. Launch the game through Steam. The FIRST launch takes several
   minutes and the window may look frozen — that is normal, wait
   for the main menu.

4. In the main menu: Multiplayer -> Player -> server address ->
   Connect. The address is remembered.

The mod connects to the server once a save is loaded.


-----------------------------------------------------------
HOW TO TELL IT WORKS
-----------------------------------------------------------

A panel with the player list appears on the right, and other
players' trucks carry their names. F2 opens the multiplayer menu.

For certain: Star Trucker\BepInEx\LogOutput.log contains the line
   Handshake completed with server


-----------------------------------------------------------
SECTORS
-----------------------------------------------------------

There is no shared world. Everyone plays their OWN save; the mod
syncs trucks, their paint and parts, trailers, names, chat and
the CB radio.

You only see each other in the SAME SECTOR. Who is where is in
the panel on the right. Agree where to fly and the others appear
by themselves.


-----------------------------------------------------------
RADIO AND MICROPHONE
-----------------------------------------------------------

Pick up the CB handset in the cab and hold the game's talk button:
everyone on the server hears you through their radio. While an
NPC is calling you or you are being offered replies, the radio
belongs to the game.

Multiplayer -> Radio and microphone: choose the microphone, test
it (you hear yourself the way others will), microphone volume,
noise suppression, radio volume and the radio sound.


-----------------------------------------------------------
YOUR OWN SERVER
-----------------------------------------------------------

Multiplayer -> Host -> start. "Copy details for friends" puts the
address and port on the clipboard.

Friends need your public IP and port 7777. On your router, forward
port 7777, TCP and UDP, to this computer.


-----------------------------------------------------------
IF SOMETHING IS WRONG
-----------------------------------------------------------

No "Multiplayer" entry in the menu
   The files did not land in the game's root. BepInEx, dotnet and
   winhttp.dll must sit next to "Star Trucker.exe".

The game "froze" on first launch
   BepInEx is generating its files. Wait.

"actively refused it" in the log
   The server is not running. The client connects by itself once
   it appears.

No player panel, but the game works
   .NET 10 Runtime is not installed.

No errors, but nobody is visible
   You are in different sectors.


-----------------------------------------------------------

Uninstall: delete BepInEx, dotnet, winhttp.dll, doorstop_config.ini
and .doorstop_version from the game folder. The game is untouched.

Original StarTruckMP mod — PiterMcFlebor and JayJay34.
Enhanced version — ShinyM. Games by ShinyM: Echo 91 on Steam —
https://store.steampowered.com/app/3906060/Echo_91/ — more coming.
