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
        GhostNotice.Tick();
        KeepRunning();
        Invites.Tick();

        // The native page shows live state, and Esc backs out of it like the game's own screens.
        // It ticks while closed as well: for a moment after closing it is still putting the
        // game's own menu entries back the way it found them.
        var open = MultiplayerScreen.IsOpen;

        // Escape closes a text box first; only with none open does it back out of the page.
        var typing = open && MultiplayerScreen.IsTyping;
        MultiplayerScreen.Tick();
        if (open && !typing && Input.GetKeyDown(KeyCode.Escape)) MultiplayerScreen.Back();

        // Let the menu grey out its Connect button and show the real state without polling.
        var connected = Network.NetId != -1;
        if (connected != _lastConnected)
        {
            _lastConnected = connected;
            PushStatus();
        }
    }

    /// <summary>
    /// The safety net under <see cref="Patches.PauseController_Patch"/>: whatever path froze time
    /// or stopped the window updating in the background is undone every frame while on a server.
    /// </summary>
    private static void KeepRunning()
    {
        if (!Patches.PauseController_Patch.Suppressing) return;

        if (!Application.runInBackground) Application.runInBackground = true;
        if (Time.timeScale == 0f) Time.timeScale = 1f;
        if (AudioListener.pause) AudioListener.pause = false;
    }

    /// <summary>
    /// After every script's Update, including the game's: the monitor page reasserts itself here
    /// so a page the game switched on this frame — the trailer readout, once a trailer is hitched —
    /// is taken down again before anything is drawn.
    /// </summary>
    private void LateUpdate() => MonitorPanel.LateTick();

    #region Incoming: game -> overlay

    private static void HandleArrivedInWorld(string sector) =>
        OverlayManager.PostMessage("hud", new { inWorld = true });

    /// <summary>On the socket thread: the chat lists belong to the game thread, so the line waits for Update.</summary>
    private void HandleChatReceived(ChatDto chat) => _mainThreadWork.Enqueue(() => AppendChat(chat));

    private void AppendChat(ChatDto chat)
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
            case "overlayLoaded":
                // The page has just (re)started its scripts: give it its words before anything else.
                _mainThreadWork.Enqueue(PushStrings);
                break;
            case "menuOpened":
                _mainThreadWork.Enqueue(() =>
                {
                    PushStrings();
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
                        Network.SwitchServer("127.0.0.1", App.ServerPort.Value);
                        ServerStatus.Reset();
                        MultiplayerScreen.AddressChanged();
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
            PushNotice(Strings.Get("notice.notconnected"));
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
        var newAddress = App.ServerAddress.Value;
        var newPort = App.ServerPort.Value;

        if (!string.IsNullOrWhiteSpace(settings.ServerAddress) && settings.ServerAddress != App.ServerAddress.Value)
        {
            newAddress = settings.ServerAddress.Trim();
            addressChanged = true;
        }

        if (!string.IsNullOrWhiteSpace(settings.ServerPort) && settings.ServerPort != App.ServerPort.Value)
        {
            newPort = settings.ServerPort.Trim();
            addressChanged = true;
        }

        if (settings.IgnoreSslValidation.HasValue)
            App.IgnoreSslValidation.Value = settings.IgnoreSslValidation.Value;

        if (settings.ShowNameplates.HasValue)
            App.ShowNameplates.Value = settings.ShowNameplates.Value;

        if (settings.RemoteCollisions.HasValue)
            App.RemoteCollisions.Value = settings.RemoteCollisions.Value;

        if (settings.GhostMode.HasValue)
            App.GhostMode.Value = settings.GhostMode.Value;

        if (settings.MicrophoneDevice != null && settings.MicrophoneDevice != (App.MicrophoneDeviceName.Value ?? string.Empty))
            Audio.VoiceInputComponent.SelectDevice(settings.MicrophoneDevice);

        if (settings.MicrophoneGain.HasValue)
            App.MicrophoneGain.Value = Mathf.Clamp(settings.MicrophoneGain.Value, 0.25f, 4f);

        if (settings.NoiseSuppression.HasValue)
            App.NoiseSuppression.Value = settings.NoiseSuppression.Value;

        if (settings.RadioVolume.HasValue)
            App.RadioVolume.Value = Mathf.Clamp(settings.RadioVolume.Value, 0f, 2f);

        if (settings.RadioEffect.HasValue)
            App.RadioEffectStrength.Value = Mathf.Clamp(settings.RadioEffect.Value, 0, 2);

        if (settings.MuteRadioDuringDialogue.HasValue)
            App.MuteRadioDuringDialogue.Value = settings.MuteRadioDuringDialogue.Value;

        if (settings.HearNearbyRadios.HasValue)
            App.HearNearbyRadios.Value = settings.HearNearbyRadios.Value;

        if (settings.CheckForUpdates.HasValue)
            App.CheckForUpdates.Value = settings.CheckForUpdates.Value;

        if (settings.NoPauseInMultiplayer.HasValue)
            App.NoPauseInMultiplayer.Value = settings.NoPauseInMultiplayer.Value;

        App.Log.LogInfo($"[MP UI] Settings saved (address changed: {addressChanged})");
        PushSettings();

        if (addressChanged)
        {
            PushNotice(Strings.Get("notice.addresssaved"));

            // The sign-in belongs to the old server; SwitchServer redoes it for the new one.
            Network.SwitchServer(newAddress, newPort);
            ServerStatus.Reset();
            MultiplayerScreen.AddressChanged();
        }
        else
        {
            PushNotice(Strings.Get("notice.saved"));
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
        RemoteCollisions = App.RemoteCollisions.Value,
        GhostMode = App.GhostMode.Value,

        // Read-only here: rebinding needs a key press, which the game's own page reads and a
        // browser overlay cannot.
        ChatKey = MonitorPanel.KeyName(App.ChatKey.Value),
        MicrophoneDevice = App.MicrophoneDeviceName.Value ?? string.Empty,
        MicrophoneDevices = Audio.VoiceInputComponent.Devices(),
        MicrophoneGain = App.MicrophoneGain.Value,
        NoiseSuppression = App.NoiseSuppression.Value,
        RadioVolume = App.RadioVolume.Value,
        RadioEffect = App.RadioEffectStrength.Value,
        MuteRadioDuringDialogue = App.MuteRadioDuringDialogue.Value,
        HearNearbyRadios = App.HearNearbyRadios.Value,
        CheckForUpdates = App.CheckForUpdates.Value,
        NoPauseInMultiplayer = App.NoPauseInMultiplayer.Value
    });

    private void PushStatus() => OverlayManager.PostMessage("status", new StatusState
    {
        Connected = Network.NetId != -1,
        NetId = Network.NetId,
        Sector = PlayerState.Sector,
        Name = PlayerState.Name,
        Hosting = HostControl.IsHosting,
        ServerAvailable = HostControl.ServerExe != null,
        UpdateAvailable = UpdateCheck.Available
    });

    private void PushChatHistory() => OverlayManager.PostMessage("chatHistory", _chatHistory);

    /// <summary>
    /// Every word the overlay shows, in the game's language. The overlay has English built in and
    /// swaps in whatever arrives here, so the one translation table lives on this side.
    /// </summary>
    private void PushStrings() => OverlayManager.PostMessage("strings", Strings.All("overlay."));

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
        public bool? GhostMode { get; set; }
        public string MicrophoneDevice { get; set; }
        public float? MicrophoneGain { get; set; }
        public bool? NoiseSuppression { get; set; }
        public float? RadioVolume { get; set; }
        public int? RadioEffect { get; set; }
        public bool? MuteRadioDuringDialogue { get; set; }
        public bool? HearNearbyRadios { get; set; }
        public bool? CheckForUpdates { get; set; }
        public bool? NoPauseInMultiplayer { get; set; }
    }

    private class SettingsState
    {
        public string ServerAddress { get; set; }
        public string ServerPort { get; set; }
        public bool IgnoreSslValidation { get; set; }
        public bool ShowNameplates { get; set; }
        public bool RemoteCollisions { get; set; }
        public bool GhostMode { get; set; }

        /// <summary>The chat key's name, shown but not changeable from the overlay.</summary>
        public string ChatKey { get; set; }

        public string MicrophoneDevice { get; set; }
        public string[] MicrophoneDevices { get; set; }
        public float MicrophoneGain { get; set; }
        public bool NoiseSuppression { get; set; }
        public float RadioVolume { get; set; }
        public int RadioEffect { get; set; }
        public bool MuteRadioDuringDialogue { get; set; }
        public bool HearNearbyRadios { get; set; }
        public bool CheckForUpdates { get; set; }
        public bool NoPauseInMultiplayer { get; set; }
    }

    private class StatusState
    {
        public bool Connected { get; set; }
        public int NetId { get; set; }
        public string Sector { get; set; }
        public string Name { get; set; }
        public bool Hosting { get; set; }
        public bool ServerAvailable { get; set; }

        /// <summary>A newer release's version, or null when this build is current or the check is off.</summary>
        public string UpdateAvailable { get; set; }
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
