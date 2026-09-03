using System;
using StarTruckMP.Client.Audio;
using UnityEngine;

namespace StarTruckMP.Client.Components;

/// <summary>
/// Decides when the player is on the air.
///
/// The CB radio in the cab is the push-to-talk: with the handset in hand, holding the game's own
/// talk button transmits to everyone on the server. The same handset carries the game's NPC
/// conversations, so the radio is handed back to the game whenever one of those is running — a
/// caller is speaking, or the player is being shown answers to pick from — and other players are
/// muted meanwhile if the player wants that. Sits on the player's truck and reads the game's
/// state every frame; the microphone itself is <see cref="VoiceInputComponent"/>.
/// </summary>
public class CbRadioPttComponent : MonoBehaviour
{
    private const float BindRetrySeconds = 1f;

    public CbRadioPttComponent(IntPtr ptr) : base(ptr) { }

    public static bool IsCbPttPressed { get; private set; }

    /// <summary>True while the game's own radio conversation owns the handset.</summary>
    public static bool IsDialogueBusy { get; private set; }

    public static event Action<bool> CbPttStateChanged;

    private CBRadioController _cbRadio;
    private float _nextBindAttempt;
    private bool _dialogueReadFailed;
    private bool _heldWhileBusyLogged;

    private void Update()
    {
        TryBindCbRadio();
        UpdateDialogueState();
        UpdateCbPttState();
    }

    private void OnDestroy()
    {
        SetCbPttState(false);
        IsDialogueBusy = false;
    }

    private void TryBindCbRadio()
    {
        if (_cbRadio != null || Time.unscaledTime < _nextBindAttempt)
            return;

        _nextBindAttempt = Time.unscaledTime + BindRetrySeconds;

        var truck = PlayerState.Truck;
        if (truck == null) return;

        _cbRadio = truck.GetComponentInChildren<CBRadioController>();
        if (_cbRadio == null) return;

        App.Log.LogInfo("[CB Radio] controller found");
        if (_cbRadio.gameObject.GetComponent<CbRadioSpeakerComponent>() == null)
        {
            var speaker = _cbRadio.gameObject.AddComponent<CbRadioSpeakerComponent>();
            speaker.SpeakerPosition = _cbRadio.transform.position;
        }
    }

    /// <summary>
    /// Whether the game is using the radio right now: an NPC line is on the panel, or the player
    /// has responses to choose from. Both come from the game's <c>RadioChatState</c> singleton.
    /// </summary>
    private void UpdateDialogueState()
    {
        var panels = 0;
        var responses = 0;
        string dialogueId = null;

        try
        {
            var chat = RadioChatState.instance;
            if (chat != null)
            {
                var ids = chat.availableResponseIds;
                responses = ids != null ? ids.Length : 0;

                var view = chat.dialogueView;
                var currentPanels = view != null ? view.currentPanels : null;
                panels = currentPanels != null ? currentPanels.Count : 0;

                dialogueId = chat.currentDialogueId;
            }
        }
        catch (Exception ex)
        {
            if (!_dialogueReadFailed)
            {
                _dialogueReadFailed = true;
                App.Log.LogWarning($"[CB Radio] Could not read the game's radio dialogue state; the radio will not yield to NPC calls. {ex.Message}");
            }
        }

        var busy = panels > 0 || responses > 0;
        if (busy == IsDialogueBusy) return;

        IsDialogueBusy = busy;
        App.Log.LogInfo($"[CB Radio] Game dialogue {(busy ? "took" : "released")} the radio (panels={panels}, responses={responses}, dialogue='{dialogueId}')");
    }

    private void UpdateCbPttState()
    {
        if (_cbRadio == null)
        {
            SetCbPttState(false);
            return;
        }

        var isMicHeld = _cbRadio.cbHeldBinding?.Get() ?? false;
        var isTalkHeld = ControlBindings.cbTalk != null && ControlBindings.cbTalk.Held();
        var wantsToTalk = isMicHeld && isTalkHeld;

        if (wantsToTalk && IsDialogueBusy)
        {
            if (!_heldWhileBusyLogged)
            {
                _heldWhileBusyLogged = true;
                App.Log.LogInfo("[CB Radio] Talk button held during a game dialogue; not transmitting.");
            }

            SetCbPttState(false);
            return;
        }

        if (!wantsToTalk) _heldWhileBusyLogged = false;
        SetCbPttState(wantsToTalk);
    }

    private void SetCbPttState(bool isPressed)
    {
        if (IsCbPttPressed == isPressed)
            return;

        IsCbPttPressed = isPressed;
        VoiceInputComponent.Transmitting = isPressed;

        var handsetTalking = _cbRadio != null && _cbRadio.handset != null && _cbRadio.handset.isTalking;
        App.Log.LogInfo($"[CB Radio] PTT => {(isPressed ? "DOWN" : "UP")} (handset talking={handsetTalking})");
        CbPttStateChanged?.Invoke(isPressed);
    }
}
