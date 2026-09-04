using System;
using System.Collections.Generic;
using StarTruckMP.Client.Authentication;
using StarTruckMP.Client.Synchronization;
using UnityEngine;

namespace StarTruckMP.Client.UI;

/// <summary>
/// Inviting friends and joining them, without a single address being read out loud.
///
/// Everything Steam-specific sits in <see cref="SteamPresence"/>; this is the side the menus
/// talk to, and it is what keeps Steam told where we are: while hosting or on a server, friends
/// see "Join game" beside our name, and accepting it points their game at the same server.
/// Nothing here runs when Steam is absent (the Xbox build); the rows simply do not appear.
/// </summary>
internal static class Invites
{
    /// <summary>Steamworks was found at startup; without it there is nothing to invite through.</summary>
    public static bool Available => App.SteamAvailable;

    /// <summary>Set once Steam has been initialised by the sign-in, after which its API may be called.</summary>
    public static volatile bool SteamReady;

    /// <summary>How often the presence and the friends list are looked at.</summary>
    private const float TickSeconds = 1f;
    private const float FriendsSeconds = 5f;

    private static float _nextTick;
    private static float _nextFriends;
    private static bool _listening;

    private static List<SteamPresence.Friend> _friends = new();

    /// <summary>Friends on a server right now, as of the last <see cref="RefreshFriends"/>.</summary>
    public static IReadOnlyList<SteamPresence.Friend> Friends => _friends;

    /// <summary>The server a Steam invitation pointed us at, and when; for the menu to say so.</summary>
    public static string LastJoin { get; private set; }
    public static float LastJoinAt { get; private set; } = float.NegativeInfinity;

    /// <summary>
    /// The "address:port" friends should be told. A host hands out the address the world sees,
    /// a player the one they connected with, unless that is the loopback: a server running on
    /// this machine, reachable from outside only by the public address again.
    /// Null while nothing is known yet.
    /// </summary>
    public static string ConnectString()
    {
        var port = (App.ServerPort.Value ?? string.Empty).Trim();
        if (port.Length == 0) return null;

        var address = (App.ServerAddress.Value ?? string.Empty).Trim();
        if (HostControl.IsHosting || IsLoopback(address))
        {
            NetworkAddresses.Refresh();
            address = NetworkAddresses.Public ?? NetworkAddresses.Local;
        }

        return string.IsNullOrEmpty(address) ? null : $"{address}:{port}";
    }

    private static bool IsLoopback(string address) =>
        address.Length == 0
        || address.StartsWith("127.", StringComparison.Ordinal)
        || string.Equals(address, "localhost", StringComparison.OrdinalIgnoreCase)
        || address == "::1" || address == "[::1]";

    /// <summary>Game thread, every frame. Keeps Steam told where we are and picks up invitations.</summary>
    public static void Tick()
    {
        if (!Available || !SteamReady) return;
        if (Time.unscaledTime < _nextTick) return;
        _nextTick = Time.unscaledTime + TickSeconds;

        try
        {
            if (!_listening)
            {
                _listening = true;
                SteamPresence.ListenForJoinRequests();
            }

            var join = SteamPresence.TakePendingJoin();
            if (join != null) Join(join, "Steam invitation", true);

            var connected = Network.NetId != -1;
            var hosting = HostControl.IsHosting;

            if (connected || hosting)
            {
                var connect = ConnectString();
                if (connect != null)
                    SteamPresence.Advertise(connect, connected ? MultiplayerState.Players.Count + 1 : 1);
            }
            else
            {
                SteamPresence.Clear();
            }
        }
        catch (Exception ex)
        {
            App.Log.LogWarning($"[Invites] {ex.Message}");
        }
    }

    /// <summary>True when there is a server to invite to: we host one, or we have signed in to one.</summary>
    public static bool CanInvite =>
        (HostControl.IsHosting || Network.NetId != -1 || !string.IsNullOrEmpty(PlayerState.Token)) && ConnectString() != null;

    /// <summary>Opens Steam's invite dialog for the server we are on. False when there is nothing to invite to.</summary>
    public static bool OpenSteamDialog()
    {
        if (!Available || !SteamReady) return false;

        var connect = ConnectString();
        if (connect == null) return false;

        return SteamPresence.OpenInviteDialog(connect);
    }

    /// <summary>Re-reads the friends list now and then; call from a page that shows it.</summary>
    public static void RefreshFriends()
    {
        if (!Available || !SteamReady) return;
        if (Time.unscaledTime < _nextFriends) return;
        _nextFriends = Time.unscaledTime + FriendsSeconds;

        _friends = SteamPresence.FriendsOnServers();
    }

    /// <summary>Points the game at a friend's server and starts signing in there.</summary>
    public static void Join(string connect, string how, bool fromInvite)
    {
        var colon = connect.LastIndexOf(':');
        var address = colon > 0 ? connect.Substring(0, colon) : connect;
        var port = colon > 0 ? connect.Substring(colon + 1) : App.ServerPort.Value;

        App.Log.LogInfo($"[Invites] Joining {address}:{port} ({how}).");
        Network.SwitchServer(address, port);
        ServerStatus.Reset();
        MultiplayerScreen.AddressChanged();

        if (!fromInvite) return;

        LastJoin = $"{address}:{port}";
        LastJoinAt = Time.unscaledTime;
    }
}
