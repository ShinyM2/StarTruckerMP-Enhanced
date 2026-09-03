using System;
using System.Collections.Concurrent;
using System.Text.Json;
using StarTruckMP.Client.Synchronization;
using StarTruckMP.Client.UI;
using StarTruckMP.Shared;
using StarTruckMP.Shared.Cmd;
using StarTruckMP.Shared.Dto;
using UnityEngine;
using Object = Il2CppSystem.Object;

namespace StarTruckMP.Client.Components;

/// <summary>
/// Everything the overlay menu needs from the game: chat in both directions, the connection
/// settings, and starting or stopping a dedicated server on this machine so a host never has to
/// leave the game to put one up.
///
/// The overlay is a browser, so all of this travels as small JSON messages over the existing
/// <see cref="OverlayManager"/> pipe rather than through any game UI.
/// </summary>
public class MultiplayerUiComponent : MonoBehaviour
{
    /// <summary>Chat lines kept for a client that opens the menu after the conversation started.</summary>
    private const int ChatHistoryLimit = 100;

    private readonly ConcurrentQueue<Action> _mainThreadWork = new();
    private readonly System.Collections.Generic.List<ChatLine> _chatHistory = new();

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private bool _lastConnected;

    public MultiplayerUiComponent(IntPtr ptr) : base(ptr) { }

    private void Awake()
    {
        Network.OnChatReceived += HandleChatReceived;
        OverlayManager.MessageReceived += HandleOverlayMessage;
        GameEventsComponent.ArrivedAtSector += HandleArrivedInWorld;
        App.Log.LogInfo("MultiplayerUiComponent ready");
    }

    private void OnDestroy()
    {
        Network.OnChatReceived -= HandleChatReceived;
        OverlayManager.MessageReceived -= HandleOverlayMessage;
        GameEventsComponent.ArrivedAtSector -= HandleArrivedInWorld;
        HostControl.Stop();
    }

    private void Update()
    {
        while (_mainThreadWork.TryDequeue(out var work))
        {
            try { work(); }
            catch (Exception ex) { App.Log.LogError($"[MP UI] {ex.Message}"); }
        }

        MonitorPanel.Tick();

        // The native page shows live state, and Esc backs out of it like the game's own screens.
        if (MultiplayerScreen.IsOpen)
        {
            MultiplayerScreen.Tick();
            if (Input.GetKeyDown(KeyCode.Escape)) MultiplayerScreen.Back();
        }

        // Let the menu grey out its Connect button and show the real state without polling.
        var connected = Network.NetId != -1;
        if (connected != _lastConnected)
        {
            _lastConnected = connected;
            PushStatus();
        }
    }

    #region Incoming: game -> overlay

    private static void HandleArrivedInWorld(string sector) =>
        OverlayManager.PostMessage("hud", new { inWorld = true });

    private void HandleChatReceived(ChatDto chat)
    {
        var line = new ChatLine
        {
            NetId = chat.NetId,
            Name = chat.Name,
            Message = chat.Message,
            SectorOnly = chat.SectorOnly,
            Mine = chat.NetId == Network.NetId
        };

        _chatHistory.Add(line);
        if (_chatHistory.Count > ChatHistoryLimit)
            _chatHistory.RemoveAt(0);

        MultiplayerState.AddChat(new MultiplayerState.ChatLine
        {
            Name = line.Name,
            Message = line.Message,
            SectorOnly = line.SectorOnly,
            Mine = line.Mine
        });

        OverlayManager.PostMessage("chat", line);
    }

    #endregion

    #region Outgoing: overlay -> game

    private void HandleOverlayMessage(string type, string payload)
    {
        // Raised on the pipe reader thread; anything touching game state has to wait for Update.
        switch (type)
        {
            case "chatSend":
                _mainThreadWork.Enqueue(() => SendChat(payload));
                break;
            case "menuOpened":
                _mainThreadWork.Enqueue(() =>
                {
                    PushSettings();
                    PushStatus();
                    PushChatHistory();
                });
                break;
            case "settingsSave":
                _mainThreadWork.Enqueue(() => SaveSettings(payload));
                break;
            case "hostStart":
                _mainThreadWork.Enqueue(() =>
                {
                    PushNotice(HostControl.Start());

                    // A host plays on their own machine, so point the client at it.
                    if (HostControl.IsHosting && App.ServerAddress.Value != "127.0.0.1")
                    {
                        App.ServerAddress.Value = "127.0.0.1";
                        Network.Reconnect();
                    }

                    PushStatus();
                });
                break;
            case "hostStop":
                _mainThreadWork.Enqueue(() =>
                {
                    PushNotice(HostControl.Stop());
                    PushStatus();
                });
                break;
            case "menuClose":
                _mainThreadWork.Enqueue(() => OverlayManager.SetInteractiveMode(false));
                break;
        }
    }

    private void SendChat(string payload)
    {
        var msg = Deserialize<ChatSendRequest>(payload);
        if (msg == null || string.IsNullOrWhiteSpace(msg.Message)) return;

        if (Network.NetId == -1)
        {
            PushNotice("Не подключено к серверу.");
            return;
        }

        Network.SendServerMessage(
            new ChatCmd { Message = msg.Message, SectorOnly = msg.SectorOnly },
            PacketType.Chat);
    }

    private void SaveSettings(string payload)
    {
        var settings = Deserialize<SettingsRequest>(payload);
        if (settings == null) return;

        var addressChanged = false;

        if (!string.IsNullOrWhiteSpace(settings.ServerAddress) && settings.ServerAddress != App.ServerAddress.Value)
        {
            App.ServerAddress.Value = settings.ServerAddress.Trim();
            addressChanged = true;
        }

        if (!string.IsNullOrWhiteSpace(settings.ServerPort) && settings.ServerPort != App.ServerPort.Value)
        {
            App.ServerPort.Value = settings.ServerPort.Trim();
            addressChanged = true;
        }

        if (settings.IgnoreSslValidation.HasValue)
            App.IgnoreSslValidation.Value = settings.IgnoreSslValidation.Value;

        if (settings.ShowNameplates.HasValue)
            App.ShowNameplates.Value = settings.ShowNameplates.Value;

        if (settings.RemoteCollisions.HasValue)
            App.RemoteCollisions.Value = settings.RemoteCollisions.Value;

        App.Log.LogInfo($"[MP UI] Settings saved (address changed: {addressChanged})");
        PushSettings();

        if (addressChanged)
        {
            PushNotice("Адрес сохранён. Переподключаюсь…");
            Network.Reconnect();
        }
        else
        {
            PushNotice("Настройки сохранены.");
        }
    }

    #endregion

    #region Pushes to the overlay

    private void PushSettings() => OverlayManager.PostMessage("settings", new SettingsState
    {
        ServerAddress = App.ServerAddress.Value,
        ServerPort = App.ServerPort.Value,
        IgnoreSslValidation = App.IgnoreSslValidation.Value,
        ShowNameplates = App.ShowNameplates.Value,
        RemoteCollisions = App.RemoteCollisions.Value
    });

    private void PushStatus() => OverlayManager.PostMessage("status", new StatusState
    {
        Connected = Network.NetId != -1,
        NetId = Network.NetId,
        Sector = PlayerState.Sector,
        Name = PlayerState.Name,
        Hosting = HostControl.IsHosting,
        ServerAvailable = HostControl.ServerExe != null
    });

    private void PushChatHistory() => OverlayManager.PostMessage("chatHistory", _chatHistory);

    private void PushNotice(string text) => OverlayManager.PostMessage("notice", new NoticeState { Text = text });

    #endregion

    #region Wire shapes

    private static T Deserialize<T>(string payload) where T : class
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;
        try { return JsonSerializer.Deserialize<T>(payload, Json); }
        catch (Exception ex)
        {
            App.Log.LogWarning($"[MP UI] Bad payload for {typeof(T).Name}: {ex.Message}");
            return null;
        }
    }

    private class ChatSendRequest
    {
        public string Message { get; set; }
        public bool SectorOnly { get; set; }
    }

    private class SettingsRequest
    {
        public string ServerAddress { get; set; }
        public string ServerPort { get; set; }
        public bool? IgnoreSslValidation { get; set; }
        public bool? ShowNameplates { get; set; }
        public bool? RemoteCollisions { get; set; }
    }

    private class SettingsState
    {
        public string ServerAddress { get; set; }
        public string ServerPort { get; set; }
        public bool IgnoreSslValidation { get; set; }
        public bool ShowNameplates { get; set; }
        public bool RemoteCollisions { get; set; }
    }

    private class StatusState
    {
        public bool Connected { get; set; }
        public int NetId { get; set; }
        public string Sector { get; set; }
        public string Name { get; set; }
        public bool Hosting { get; set; }
        public bool ServerAvailable { get; set; }
    }

    private class NoticeState
    {
        public string Text { get; set; }
    }

    private class ChatLine
    {
        public int NetId { get; set; }
        public string Name { get; set; }
        public string Message { get; set; }
        public bool SectorOnly { get; set; }
        public bool Mine { get; set; }
    }

    #endregion
}
