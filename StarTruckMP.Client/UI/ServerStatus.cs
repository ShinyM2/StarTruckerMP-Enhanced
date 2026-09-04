using System;
using System.Net.Http;
using System.Text.Json;
using StarTruckMP.Client.Http;
using StarTruckMP.Shared.Dto.Api;
using UnityEngine;

namespace StarTruckMP.Client.UI;

/// <summary>
/// The lobby line: asks the server who is on it while the multiplayer page is up.
///
/// The mod only joins a server once a save is loaded, so from the main menu the player used to
/// see nothing but "server found" and had to take it on trust. <c>GET /api/status</c> needs no
/// sign-in and answers with the count and the names, which is what a player wants to see
/// before loading a save: that the server is there and that their friends are already on it.
/// </summary>
internal static class ServerStatus
{
    /// <summary>How often the server is asked while a page is open.</summary>
    private const float PollSeconds = 4f;

    /// <summary>The last answer, or null when the server has not answered for the current address.</summary>
    public static ServerStatusDto Result { get; private set; }

    /// <summary>Why the last poll failed, or null.</summary>
    public static string Error { get; private set; }

    /// <summary>The "address:port" the current <see cref="Result"/> and <see cref="Error"/> are about.</summary>
    public static string For { get; private set; }

    /// <summary>True until the first answer about the current address has landed.</summary>
    public static bool Checking => For != Target;

    private static float _nextPoll;
    private static volatile bool _running;

    /// <summary>The server the menu is pointed at right now.</summary>
    public static string Target => $"{(App.ServerAddress.Value ?? string.Empty).Trim()}:{(App.ServerPort.Value ?? string.Empty).Trim()}";

    /// <summary>
    /// Asks the server if it is time to. Safe to call every frame; only one request is in
    /// flight at a time and the answer lands on a background thread.
    /// </summary>
    public static void Poll()
    {
        var target = Target;

        // A new address: whatever was known is about a different server.
        if (For != null && For != target)
        {
            Result = null;
            Error = null;
            For = null;
            _nextPoll = 0f;
        }

        if (_running || Time.unscaledTime < _nextPoll) return;
        _nextPoll = Time.unscaledTime + PollSeconds;

        var address = (App.ServerAddress.Value ?? string.Empty).Trim();
        if (address.Length == 0 || !int.TryParse(App.ServerPort.Value, out var port) || port <= 0 || port > 65535)
        {
            For = target;
            Result = null;
            Error = "invalid address";
            return;
        }

        _running = true;
        Plugin.StartAttachedThread(() => Fetch(target, $"https://{address}:{port}/api/status"));
    }

    /// <summary>Forgets the answer so the next poll is immediate; after the address changes.</summary>
    public static void Reset()
    {
        Result = null;
        Error = null;
        For = null;
        _nextPoll = 0f;
    }

    private static void Fetch(string target, string url)
    {
        try
        {
            using var http = HttpFactory.Create();
            http.Timeout = TimeSpan.FromSeconds(4);

            using var response = http.Send(new HttpRequestMessage(HttpMethod.Get, url));
            if (!response.IsSuccessStatusCode)
            {
                // An older server has no status page but is otherwise fine; say so rather than "down".
                Publish(target, null, $"HTTP {(int)response.StatusCode}");
                return;
            }

            using var stream = response.Content.ReadAsStream();
            var status = JsonSerializer.Deserialize<ServerStatusDto>(stream, App.JsonReaderOptions);
            Publish(target, status, status == null ? "empty answer" : null);
        }
        catch (Exception ex)
        {
            Publish(target, null, (ex.InnerException ?? ex).Message);
        }
        finally
        {
            _running = false;
        }
    }

    private static void Publish(string target, ServerStatusDto status, string error)
    {
        // The address may have changed while the request was out; an answer about the old
        // server must not be shown as if it were about the new one.
        if (target != Target) return;

        Result = status;
        Error = error;
        For = target;
    }
}
