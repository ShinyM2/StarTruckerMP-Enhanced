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
}
