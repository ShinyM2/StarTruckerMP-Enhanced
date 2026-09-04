using System;
using System.Text.Json;
using BepInEx.Configuration;
using UnityEngine;
using BepInEx.Logging;

namespace StarTruckMP.Client;

public static class App
{
    public static ConfigEntry<string> ServerAddress;
    public static ConfigEntry<string> ServerPort;
    public static ConfigEntry<string> MicrophoneDeviceName;
    public static ConfigEntry<bool> PreferSystemDefaultMicrophone;
    public static ConfigEntry<float> RadioEffectOutputGain;
    public static ConfigEntry<bool> NoiseSuppression;
    public static ConfigEntry<float> MicrophoneGain;
    public static ConfigEntry<float> RadioVolume;
    public static ConfigEntry<bool> MuteRadioDuringDialogue;
    public static ConfigEntry<int> RadioEffectStrength;
    public static ConfigEntry<bool> HearNearbyRadios;
    public static ConfigEntry<bool> CheckForUpdates;
    public static ConfigEntry<bool> NoPauseInMultiplayer;
    public static ConfigEntry<bool> IgnoreSslValidation;
    public static ConfigEntry<bool> ShowNameplates;
    public static ConfigEntry<bool> RemoteCollisions;
    public static ConfigEntry<KeyCode> ChatKey;
    public static ConfigEntry<bool> GhostMode;

    public static void Configure(ConfigFile config)
    {
        ServerAddress = config.Bind("Connection", "ServerAddress", "127.0.0.1", "StarTruckMP server address");
        ServerPort = config.Bind("Connection", "ServerPort", "7777", "StarTruckMP server port");
        MicrophoneDeviceName = config.Bind("Audio", "MicrophoneDeviceName", string.Empty, "Exact microphone device name to use. Leave empty to auto-select.");
        PreferSystemDefaultMicrophone = config.Bind("Audio", "PreferSystemDefaultMicrophone", true, "When auto-selecting, try the Windows default microphone before explicit devices.");
        RadioEffectOutputGain = config.Bind("Audio", "RadioEffectOutputGain", 1.0f, "Final output gain applied after the NWaves radio voice effect.");
        NoiseSuppression = config.Bind("Audio", "NoiseSuppression", true, "Run the microphone through RNNoise before sending. Turn off if your headset already suppresses noise and voices come out muffled.");
        MicrophoneGain = config.Bind("Audio", "MicrophoneGain", 1.0f, "Microphone volume multiplier, 1.0 = as captured. Check it with the microphone test on the multiplayer page.");
        RadioVolume = config.Bind("Audio", "RadioVolume", 1.0f, "How loud other players come out of the CB radio, 1.0 = as received.");
        MuteRadioDuringDialogue = config.Bind("Audio", "MuteRadioDuringDialogue", true, "Mute other players while the game's own radio conversation is running, so an NPC call is not talked over. Your talk button never transmits during one either way.");
        RadioEffectStrength = config.Bind("Audio", "RadioEffect", 2, "How much other players' voices are coloured like a CB radio: 0 = clean voice, 1 = band and compression only, 2 = the full set with a driven, clipping input stage and squelch clicks.");
        IgnoreSslValidation = config.Bind("Connection", "IgnoreSslValidation", true, "Accept the server's self-signed certificate. Every StarTruckMP server generates one on first start, so this stays on unless the host installed a real certificate.");
        HearNearbyRadios = config.Bind("Audio", "HearNearbyRadios", true, "Also play another player's radio voice from their truck when it is close by, so a transmission from the truck beside you sounds like it comes from there too.");
        CheckForUpdates = config.Bind("Multiplayer", "CheckForUpdates", true, "Ask GitHub once at startup whether a newer release exists and say so in the menu.");
        NoPauseInMultiplayer = config.Bind("Multiplayer", "NoPauseInMultiplayer", true, "Keep the world running while a menu, the map or a popup is open and when the window loses focus, for as long as you are on a server. A paused player stands still for everyone else and then leaps.");
        ShowNameplates = config.Bind("Multiplayer", "ShowNameplates", true, "Show other players' names above their trucks.");
        RemoteCollisions = config.Bind("Multiplayer", "RemoteCollisions", false, "Let other players' trucks collide with you. Off by default: with any latency the collision happens where the truck is not.");
        GhostMode = config.Bind("Multiplayer", "GhostMode", true, "Fade other players' trucks out when they are in the way at a warp gate or a docking bay. Your own truck is never touched.");
        ChatKey = config.Bind("Multiplayer", "ChatKey", KeyCode.Return, "The key that opens the chat line on the cab monitor. Rebindable from the multiplayer page; keep it off the game's own bindings.");
    }

    public static ManualLogSource Log;

    /// <summary>
    /// Requests a fresh session token from the platform, or null on platforms with no
    /// authentication wired up. Set during startup by whichever auth path applies, so the
    /// network layer can recover when a token expires or the server restarts under it.
    /// </summary>
    public static Action ReAuthenticate;

    public static JsonSerializerOptions JsonReaderOptions = new() { PropertyNameCaseInsensitive = true };
    public static JsonSerializerOptions JsonWriterOptions = new() { WriteIndented = false };
}