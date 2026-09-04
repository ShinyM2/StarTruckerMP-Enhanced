using System;
using System.Collections.Generic;
using BepInEx.Logging;
using Il2CppInterop.Runtime;
using Steamworks;

namespace StarTruckMP.Client.Authentication;

/// <summary>
/// Steam's part of inviting: the "Join game" entry on a friend's list, the overlay's invite
/// dialog, and the list of friends who are on a server right now.
///
/// Like <see cref="SteamAuthHelper"/>, this class is the only place Steamworks types appear in,
/// and nothing calls into it unless the assembly was found at startup (<see cref="App.SteamAvailable"/>):
/// the CLR JIT-compiles a method's callees when the method is first run, and the Xbox build has
/// no Steamworks to compile against. Every call is expected on the game thread.
///
/// The mechanism is Steam Rich Presence. A player on a server publishes a <c>connect</c> key,
/// and from then on their friends see "Join game" next to their name in Steam. Accepting starts
/// the game with <c>+connect address:port</c> on its command line, or, when the game is already
/// running, raises <see cref="GameRichPresenceJoinRequested_t"/>. Neither needs anything set
/// up on Steam's side for the app, which the mod could not do.
/// </summary>
internal static class SteamPresence
{
    private static ManualLogSource Log => App.Log;

    /// <summary>Star Trucker on Steam, for telling which friends are in the game.</summary>
    private const uint AppId = 2380050;

    /// <summary>What Steam is told to put on the command line of a friend who joins.</summary>
    private const string ConnectPrefix = "+connect ";

    /// <summary>A friend who is on a StarTruckMP server, as their Rich Presence says.</summary>
    public class Friend
    {
        public string Name = string.Empty;

        /// <summary>The "address:port" they are on.</summary>
        public string Connect = string.Empty;
    }

    private static string _lastConnect;
    private static int _lastSize = -1;
    private static bool _cleared = true;

    private static Callback<GameRichPresenceJoinRequested_t> _joinCallback;
    private static volatile string _pendingJoin;
    private static bool _launchLineRead;

    // ---------------------------------------------------------------------------------------
    // Telling friends where we are
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Publishes the server we are on, so friends get "Join game". The group keys make Steam
    /// list everyone on the same server together, with a count, in the friends list.
    /// Repeated calls with the same values cost nothing.
    /// </summary>
    public static void Advertise(string connect, int groupSize)
    {
        if (connect == _lastConnect && groupSize == _lastSize) return;

        try
        {
            SteamFriends.SetRichPresence("connect", ConnectPrefix + connect);
            SteamFriends.SetRichPresence("steam_player_group", connect);
            SteamFriends.SetRichPresence("steam_player_group_size", groupSize.ToString());

            if (connect != _lastConnect)
                Log.LogInfo($"[Steam] Friends can now join {connect} from the Steam friends list.");

            _lastConnect = connect;
            _lastSize = groupSize;
            _cleared = false;
        }
        catch (Exception ex)
        {
            Log.LogWarning($"[Steam] Could not set Rich Presence: {ex.Message}");
        }
    }

    /// <summary>Takes "Join game" away again; after leaving a server or stopping the host.</summary>
    public static void Clear()
    {
        if (_cleared) return;

        try
        {
            SteamFriends.ClearRichPresence();
            Log.LogInfo("[Steam] Rich Presence cleared.");
        }
        catch (Exception ex)
        {
            Log.LogWarning($"[Steam] Could not clear Rich Presence: {ex.Message}");
        }

        _lastConnect = null;
        _lastSize = -1;
        _cleared = true;
    }

    /// <summary>
    /// Opens the Steam overlay's invite dialog: the player ticks friends, and each of them gets
    /// an invitation that lands them on this server.
    /// </summary>
    public static bool OpenInviteDialog(string connect)
    {
        try
        {
            SteamFriends.ActivateGameOverlayInviteDialogConnectString(ConnectPrefix + connect);
            Log.LogInfo($"[Steam] Invite dialog opened for {connect}.");
            return true;
        }
        catch (Exception ex)
        {
            Log.LogWarning($"[Steam] Could not open the invite dialog: {ex.Message}");
            return false;
        }
    }

    // ---------------------------------------------------------------------------------------
    // Finding friends to join
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Friends who are in Star Trucker and advertising a server. A friend in the game whose
    /// presence has not arrived yet is asked for it, so a second call a few seconds later sees them.
    /// </summary>
    public static List<Friend> FriendsOnServers()
    {
        var result = new List<Friend>();

        try
        {
            const EFriendFlags flags = EFriendFlags.k_EFriendFlagImmediate;
            var count = SteamFriends.GetFriendCount(flags);

            for (var i = 0; i < count; i++)
            {
                var id = SteamFriends.GetFriendByIndex(i, flags);

                if (!SteamFriends.GetFriendGamePlayed(id, out var game)) continue;

                // The low 24 bits of a game id are the app id; the rest marks mods and shortcuts.
                if ((game.m_gameID.m_GameID & 0xFFFFFF) != AppId) continue;

                var connect = ParseConnect(SteamFriends.GetFriendRichPresence(id, "connect"));
                if (connect == null)
                {
                    SteamFriends.RequestFriendRichPresence(id);
                    continue;
                }

                result.Add(new Friend
                {
                    Name = SteamFriends.GetFriendPersonaName(id) ?? "?",
                    Connect = connect
                });
            }
        }
        catch (Exception ex)
        {
            Log.LogWarning($"[Steam] Could not read the friends list: {ex.Message}");
        }

        return result;
    }

    // ---------------------------------------------------------------------------------------
    // Being joined
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Listens for a friend accepting an invitation while the game is already running. The
    /// callback is a generic Steamworks type over a struct, which IL2CPP only has compiled if
    /// the game itself uses it; if it does not, this logs once and the invitation is instead
    /// picked up at the next launch, or the friend joins from the Friends page.
    /// </summary>
    public static void ListenForJoinRequests()
    {
        if (_joinCallback != null) return;

        try
        {
            var handler = DelegateSupport.ConvertDelegate<Callback<GameRichPresenceJoinRequested_t>.DispatchDelegate>(
                new Action<GameRichPresenceJoinRequested_t>(OnJoinRequested));
            _joinCallback = Callback<GameRichPresenceJoinRequested_t>.Create(handler);
            Log.LogInfo("[Steam] Listening for join requests.");
        }
        catch (Exception ex)
        {
            Log.LogInfo($"[Steam] Join requests while the game is running are not available ({(ex.InnerException ?? ex).Message}); " +
                        "an invitation accepted now is picked up at the next launch, or use the Friends page.");
        }
    }

    private static void OnJoinRequested(GameRichPresenceJoinRequested_t request)
    {
        try
        {
            var connect = ParseConnect(request.m_rgchConnect);
            Log.LogInfo($"[Steam] Join request from {request.m_steamIDFriend.m_SteamID}: {connect ?? "(unreadable)"}");
            if (connect != null) _pendingJoin = connect;
        }
        catch (Exception ex)
        {
            Log.LogWarning($"[Steam] Could not read a join request: {ex.Message}");
        }
    }

    /// <summary>
    /// The server a Steam invitation asked for, once. Read from the command line the first time,
    /// which is where Steam puts the connect string when it launches the game, and from join
    /// requests after that.
    /// </summary>
    public static string TakePendingJoin()
    {
        if (!_launchLineRead)
        {
            _launchLineRead = true;
            var fromLaunch = ReadLaunchLine();
            if (fromLaunch != null) return fromLaunch;
        }

        var pending = _pendingJoin;
        if (pending != null) _pendingJoin = null;
        return pending;
    }

    private static string ReadLaunchLine()
    {
        try
        {
            var args = Environment.GetCommandLineArgs();
            var connect = ParseConnect(string.Join(" ", args));
            if (connect != null)
            {
                Log.LogInfo($"[Steam] Launched with a connect string: {connect}");
                return connect;
            }
        }
        catch (Exception ex)
        {
            Log.LogWarning($"[Steam] Could not read the command line: {ex.Message}");
        }

        try
        {
            // Where Steam puts it when the app is set to keep launch parameters off the OS command line.
            if (SteamApps.GetLaunchCommandLine(out var line, 1024) > 0)
            {
                var connect = ParseConnect(line);
                if (connect != null)
                {
                    Log.LogInfo($"[Steam] Steam launch line carries a connect string: {connect}");
                    return connect;
                }
            }
        }
        catch (Exception ex)
        {
            Log.LogDebug($"[Steam] No Steam launch line: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// The "address:port" out of a connect string, or null. Accepts the bare address as well as
    /// the "+connect address:port" Steam passes on, so a value typed by hand works too.
    /// </summary>
    public static string ParseConnect(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var parts = text.Split(new[] { ' ', '\t', '\r', '\n', '"' }, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            if (!string.Equals(parts[i], "+connect", StringComparison.OrdinalIgnoreCase)) continue;
            return i + 1 < parts.Length ? Sanitise(parts[i + 1]) : null;
        }

        // No keyword: a single token that looks like an address.
        return parts.Length == 1 ? Sanitise(parts[0]) : null;
    }

    /// <summary>Only what an address and a port are made of; the string came from another machine.</summary>
    private static string Sanitise(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 128) return null;

        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c) || c == '.' || c == ':' || c == '-' || c == '_' || c == '[' || c == ']') continue;
            return null;
        }

        return value;
    }
}
