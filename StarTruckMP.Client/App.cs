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
        IgnoreSslValidation = config.Bind("Connection", "IgnoreSslValidation", true, "Accept the server's self-signed certificate. Every StarTruckMP server generates one on first start, so this stays on unless the host installed a real certificate.");
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