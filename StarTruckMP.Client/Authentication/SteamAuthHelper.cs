using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using BepInEx.Logging;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using StarTruckMP.Client.Http;
using StarTruckMP.Client.Synchronization;
using StarTruckMP.Shared.Cmd.Api;
using StarTruckMP.Shared.Dto.Api;
using StarTruckMP.Client.UI;

namespace StarTruckMP.Client.Authentication;

/// <summary>
/// Contains all Steamworks.NET-specific authentication logic.
/// This class is intentionally isolated so that the CLR never JIT-compiles
/// Steamworks types unless the assembly is confirmed to be present at runtime.
/// </summary>
internal static class SteamAuthHelper
{
    private static ManualLogSource Log => Plugin.Log;

    /// <summary>
    /// Initialises Steam and obtains an auth ticket, then posts it to the server.
    /// Must only be called after verifying that Steamworks.NET is loaded.
    /// </summary>
    public static void Run()
    {
        try
        {
            // Each sign-in supersedes the one before: a switch of server while the old one was
            // still being retried must not leave two loops posting tickets.
            var generation = Interlocked.Increment(ref _generation);

            // Steam may not be up yet, or not at all. Ask again, but with a growing pause: at
            // fifty milliseconds this loop was a busy wait for as long as Steam stayed away.
            var wait = 50;
            var explained = false;
            while (!Steamworks.SteamAPI.Init())
            {
                if (!explained && wait >= 2000)
                {
                    explained = true;
                    Log.LogWarning("[Auth] Steam is not answering; retrying every couple of seconds.");
                }

                Thread.Sleep(wait);
                wait = Math.Min(wait * 2, 2000);
            }

            if (!Steamworks.SteamAPI.IsSteamRunning())
            {
                Log.LogWarning("[Auth] Steam is not running, skipping Steam authentication.");
                return;
            }

            if (!Steamworks.SteamUser.BLoggedOn())
            {
                Log.LogWarning("[Auth] Steam user is not logged on, skipping Steam authentication.");
                return;
            }

            // Steam is up: presence and invites may use it from now on.
            Invites.SteamReady = true;

            PlayerState.Name = Steamworks.SteamFriends.GetPersonaName() ?? string.Empty;
            Log.LogInfo($"[Auth] Steam persona name: {PlayerState.Name}");

            var steamId = Steamworks.SteamUser.GetSteamID();

            // GetAuthSessionTicket requires an Il2CppStructArray<byte> buffer.
            // 1024 bytes is more than enough for a Steam auth ticket (~200 bytes typical).
            const int bufferSize = 1024;
            var buffer = new Il2CppStructArray<byte>(bufferSize);
            var handle = Steamworks.SteamUser.GetAuthSessionTicket(buffer, bufferSize, out var ticketSize);

            if (handle.m_HAuthTicket == 0 || ticketSize == 0)
            {
                Log.LogError("[Auth] GetAuthSessionTicket returned an invalid handle or empty ticket.");
                return;
            }

            // Copy only the valid bytes from the buffer.
            var ticketBytes = new byte[ticketSize];
            for (var i = 0; i < (int)ticketSize; i++)
                ticketBytes[i] = buffer[i];

            // Encode as lowercase hex — the format expected by ISteamUserAuth/AuthenticateUserTicket.
            var ticketHex = Convert.ToHexString(ticketBytes).ToLowerInvariant();
            var steamIdValue = steamId.m_SteamID;

            Log.LogInfo($"[Auth] Steam ticket obtained for SteamID={steamIdValue} ({ticketSize} bytes).");

            Plugin.StartAttachedThread(() => Send(steamIdValue, ticketHex, generation));
        }
        catch (Exception ex)
        {
            Log.LogError("[Auth] Failed to obtain Steam auth ticket:");
            Log.LogError(ex);
        }
    }

    /// <summary>
    /// POST /api/auth/steam  with the following JSON body:
    /// {
    ///   "steamId": 76561198000000000,
    ///   "ticket":  "0123abcdef..."
    /// }
    ///
    /// Server-side ticket validation:
    ///   1. Forward the hex-encoded ticket to the Steam Web API:
    ///        GET https://api.steampowered.com/ISteamUserAuth/AuthenticateUserTicket/v1/
    ///           ?key={SteamWebApiKey}&amp;appid={SteamAppId}&amp;ticket={hexTicket}
    ///   2. Verify response.params.result == "OK".
    ///   3. Compare response.params.steamid against the posted steamId field.
    /// </summary>
    private static void Send(ulong steamId, string ticketHex, int generation)
    {
        // Retries live in the caller loop below: recursing here added a stack frame every
        // five seconds, so a server that stayed down long enough took the thread with it.
        while (!SendOnce(steamId, ticketHex))
        {
            Thread.Sleep(5000);

            if (generation != Volatile.Read(ref _generation))
            {
                Log.LogInfo("[Auth] A newer sign-in has started; this one stops.");
                return;
            }
        }
    }

    /// <summary>Posts the ticket once. Returns false if the caller should try again.</summary>
    private static bool SendOnce(ulong steamId, string ticketHex)
    {
        var url = $"https://{App.ServerAddress.Value}:{App.ServerPort.Value}/api/auth/steam";
        try
        {
            var cmd = new SteamAuthCmd
            {
                SteamId = steamId,
                Ticket = ticketHex
            };

            using var content = new StringContent(JsonSerializer.Serialize(cmd), Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = content;
            using var response = HttpFactory.Create().Send(request);

            Log.LogInfo("[Auth] Steam server responded " + (int)response.StatusCode);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                Log.LogWarning("[Auth] Steam authentication failed, retrying in 5 seconds...");
                Network.AuthProblem = $"HTTP {(int)response.StatusCode}";
                return false;
            }

            // The server is there and answered; whatever the menu was showing about it is over.
            Network.AuthProblem = null;
            _lastFailure = null;

            using var stream = response.Content.ReadAsStream();
            var body = JsonSerializer.Deserialize<TicketAuthenticationDto>(stream, App.JsonReaderOptions);
            if (body == null)
            {
                var rawResult = response.Content.ReadAsStringAsync().Result;
                App.Log.LogError($"[Auth] Failed to parse Steam auth response body. Content: {rawResult}");
                return false;
            }
            
            App.Log.LogInfo($"[Auth] Steam token: {body.Token}");
            if (body.Token == null)
            {
                var rawResult = response.Content.ReadAsStringAsync().Result;
                App.Log.LogError($"[Auth] Token was empty. Content: {rawResult}");
            }

            if (body.Token == null) return false;
            
            PlayerState.Token = body.Token;

            if (!string.IsNullOrEmpty(body.ServerPublicKey))
            {
                try
                {
                    PlayerState.ServerPublicKey = Convert.FromBase64String(body.ServerPublicKey);
                    App.Log.LogInfo("[Auth] Steam - server public key stored for ECDH.");
                }
                catch (Exception ex)
                {
                    App.Log.LogError("[Auth] Steam - failed to decode server public key: " + ex.Message);
                }
            }
            else
            {
                App.Log.LogWarning("[Auth] Steam - server did not return a public key; UDP encryption will not be established.");
            }
            
            OverlayManager.SetSessionTokenAndNavigate(
                body.Token,
                $"https://{App.ServerAddress.Value}:{App.ServerPort.Value}/overlay");

            return true;
        }
        catch (Exception ex)
        {
            // A server that is not up yet is the normal case, not an error: the client keeps
            // trying every few seconds for as long as the game runs. The full trace is worth
            // having once; after that one line per new reason is enough, and a reason already
            // reported is repeated only now and then so the log stays readable.
            var reason = (ex.InnerException ?? ex).Message;
            Network.AuthProblem = reason;
            if (reason != _lastFailure)
            {
                _lastFailure = reason;
                _sameFailures = 0;
                Log.LogWarning($"[Auth] Cannot reach the server at {url}: {reason}. Retrying every 5 seconds until it answers.");
                if (!_traceLogged)
                {
                    _traceLogged = true;
                    Log.LogDebug(ex.ToString());
                }
            }
            else if (++_sameFailures % 24 == 0)
            {
                Log.LogWarning($"[Auth] Still cannot reach the server at {url} ({reason}); still retrying.");
            }

            return false;
        }
    }

    private static int _generation;
    private static string _lastFailure;
    private static int _sameFailures;
    private static bool _traceLogged;
}

