using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace StarTruckMP.Client;

/// <summary>
/// What the mod knows about the session right now, in one place.
///
/// The roster and the chat log are produced by the networking components and consumed by two
/// very different surfaces — the browser overlay and the truck's own monitor — so neither
/// surface reaches into the other's internals to get them.
/// </summary>
internal static class MultiplayerState
{
    public class Player
    {
        public int NetId;
        public string Name = string.Empty;
        public string Sector = string.Empty;
        public bool SameSector;
        public int Ping = -1;
    }

    public class ChatLine
    {
        public string Name = string.Empty;
        public string Message = string.Empty;
        public bool SectorOnly;
        public bool Mine;
    }

    /// <summary>Everyone on the server apart from us, newest state.</summary>
    public static readonly List<Player> Players = new();

    /// <summary>
    /// Our own latency to the server, in milliseconds, or -1 before the server has said.
    /// It is not in <see cref="Players"/> because that list is everyone else.
    /// </summary>
    public static int OwnPing = -1;

    /// <summary>Recent chat, oldest first.</summary>
    public static readonly List<ChatLine> Chat = new();

    /// <summary>How many lines of chat are worth keeping for a screen that shows a handful.</summary>
    public const int ChatLimit = 50;

    public static void AddChat(ChatLine line)
    {
        Chat.Add(line);
        if (Chat.Count > ChatLimit) Chat.RemoveAt(0);
    }

    // ---------------------------------------------------------------------------------------
    // Who is which colour
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Eight colours that read on both a bright hull and the black of space and stay apart from
    /// each other: the game's own amber first, then the rest of the instrument-panel family.
    /// </summary>
    private static readonly string[] Palette =
    {
        "#EFC806", // amber
        "#5FD3F0", // cyan
        "#8CE06B", // green
        "#F07BE0", // magenta
        "#FF9A3C", // orange
        "#C7F04A", // lime
        "#7FA8FF", // sky
        "#FF7A8A"  // coral
    };

    /// <summary>A player's colour as "#rrggbb", fixed for the session: the same one everywhere they are named.</summary>
    public static string ColorHex(int netId) => Palette[((netId % Palette.Length) + Palette.Length) % Palette.Length];

    // ---------------------------------------------------------------------------------------
    // Who is on the air
    // ---------------------------------------------------------------------------------------

    /// <summary>A voice counts as still speaking this long after its last frame, so a gap between words does not flicker.</summary>
    private const long SpeakingHoldMs = 350;

    /// <summary>Last voice frame per player, as <see cref="Environment.TickCount64"/>. Written from the network thread.</summary>
    private static readonly ConcurrentDictionary<int, long> _lastVoice = new();

    /// <summary>Called for every voice frame that arrives from a player.</summary>
    public static void MarkSpeaking(int netId) => _lastVoice[netId] = Environment.TickCount64;

    /// <summary>True while frames from this player keep arriving.</summary>
    public static bool IsSpeaking(int netId) =>
        _lastVoice.TryGetValue(netId, out var ticks) && Environment.TickCount64 - ticks < SpeakingHoldMs;

    public static void ForgetSpeaker(int netId) => _lastVoice.TryRemove(netId, out _);
}
