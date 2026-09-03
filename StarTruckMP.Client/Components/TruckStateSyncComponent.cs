using System;
using StarTruckMP.Client.Synchronization;
using StarTruckMP.Shared;
using StarTruckMP.Shared.Cmd;
using UnityEngine;

namespace StarTruckMP.Client.Components;

/// <summary>
/// Reports the switchable state of the player's truck — the headlights, for now — whenever it
/// changes, so the copy other players see lights up when the owner flicks the switch.
///
/// The game keeps the switch in a <c>TruckSystemsBindingBool</c> that every exterior light
/// switcher on the truck reads; the first switcher found is the one asked here.
/// </summary>
public class TruckStateSyncComponent : MonoBehaviour
{
    private const float PollSeconds = 0.25f;

    public TruckStateSyncComponent(IntPtr ptr) : base(ptr) { }

    private float _nextPoll;
    private bool? _sentHeadlights;
    private GameObject _truck;
    private ExteriorLightSwitcher _switcher;
    private bool _warned;

    private void Awake()
    {
        Network.OnConnected += HandleConnected;
    }

    private void OnDestroy()
    {
        Network.OnConnected -= HandleConnected;
    }

    private void HandleConnected(int netId) => _sentHeadlights = null;

    private void Update()
    {
        if (Time.unscaledTime < _nextPoll) return;
        _nextPoll = Time.unscaledTime + PollSeconds;

        if (Network.NetId == -1) return;

        var truck = PlayerState.Truck;
        if (truck == null) return;

        if (_truck != truck || _switcher == null)
        {
            _truck = truck;
            _switcher = truck.GetComponentInChildren<ExteriorLightSwitcher>(true);
            if (_switcher == null && !_warned)
            {
                _warned = true;
                App.Log.LogWarning("[TruckState] No ExteriorLightSwitcher on the player's truck; headlights will not be shared.");
            }
        }

        if (_switcher == null) return;

        bool headlights;
        try
        {
            var binding = _switcher.m_binding;
            headlights = binding != null ? binding.Get() : _switcher.m_enabled;
        }
        catch (Exception ex)
        {
            if (!_warned)
            {
                _warned = true;
                App.Log.LogWarning($"[TruckState] Could not read the headlight switch: {ex.Message}");
            }

            return;
        }

        if (_sentHeadlights == headlights) return;
        _sentHeadlights = headlights;

        Network.SendServerMessage(new TruckStateCmd { Headlights = headlights }, PacketType.TruckState);
        App.Log.LogInfo($"[TruckState] Headlights {(headlights ? "on" : "off")}");
    }

    /// <summary>
    /// Sets a remote truck's headlights. The NPC cab carries the same light switchers; their own
    /// Update is stopped so it cannot argue, and the parts it would have toggled are toggled here.
    /// </summary>
    public static void ApplyHeadlights(GameObject remoteTruck, bool on)
    {
        if (remoteTruck == null) return;

        try
        {
            var switchers = remoteTruck.GetComponentsInChildren<ExteriorLightSwitcher>(true);
            if (switchers == null || switchers.Length == 0)
            {
                if (!_noSwitcherLogged)
                {
                    _noSwitcherLogged = true;
                    App.Log.LogInfo("[TruckState] The remote truck has no ExteriorLightSwitcher; other players' headlights are not shown.");
                }

                return;
            }

            foreach (var switcher in switchers)
            {
                switcher.enabled = false;
                switcher.m_enabled = on;

                var objects = switcher.m_objectsToToggle;
                if (objects != null)
                {
                    foreach (var go in objects)
                    {
                        if (go != null) go.SetActive(on);
                    }
                }

                var material = switcher.m_glowMaterialInst;
                if (material != null)
                    material.SetColor("_EmissionColor", on ? switcher.m_emissionColor : Color.black);

                var glow = switcher.m_glowRenderers;
                if (glow != null)
                {
                    foreach (var renderer in glow)
                    {
                        if (renderer != null) renderer.enabled = on;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (!_applyFailedLogged)
            {
                _applyFailedLogged = true;
                App.Log.LogWarning($"[TruckState] Could not set a remote truck's headlights: {ex.Message}");
            }
        }
    }

    private static bool _noSwitcherLogged;
    private static bool _applyFailedLogged;
}
