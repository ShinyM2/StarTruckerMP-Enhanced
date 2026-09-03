using System;
using StarTruckMP.Client.Synchronization;
using StarTruckMP.Shared;
using StarTruckMP.Shared.Cmd;
using UnityEngine;

namespace StarTruckMP.Client.Components;

/// <summary>
/// Tells the server what the player's truck looks like, and again whenever that changes.
///
/// A repaint at a station, a new grill, a warp's worth of scorching — none of it raises an event
/// the mod can hear, so the truck is read every few seconds and sent only when its signature
/// moved. On every new connection the signature is forgotten, so the server always learns the
/// current look, whichever order the players started in.
/// </summary>
public class AppearanceSyncComponent : MonoBehaviour
{
    private const float PollSeconds = 3f;

    public AppearanceSyncComponent(IntPtr ptr) : base(ptr) { }

    private float _nextPoll;
    private string _lastSignature;

    private void Awake()
    {
        Network.OnConnected += HandleConnected;
        Network.OnDisconnected += HandleDisconnected;
    }

    private void OnDestroy()
    {
        Network.OnConnected -= HandleConnected;
        Network.OnDisconnected -= HandleDisconnected;
    }

    // Network thread; a reference write is enough to make the next poll send.
    private void HandleConnected(int netId) => _lastSignature = null;

    private void HandleDisconnected() => _lastSignature = null;

    private void Update()
    {
        if (Time.unscaledTime < _nextPoll) return;
        _nextPoll = Time.unscaledTime + PollSeconds;

        if (Network.NetId == -1) return;

        var truck = PlayerState.Truck;
        if (truck == null) return;

        var appearance = TruckAppearanceSync.Read(truck);
        if (appearance == null) return;

        var signature = TruckAppearanceSync.Signature(appearance);
        if (signature == _lastSignature) return;
        _lastSignature = signature;

        Network.SendServerMessage(new UpdateLiveryCmd { Livery = appearance.Livery, Appearance = appearance }, PacketType.UpdateLivery);
        App.Log.LogInfo($"[Appearance] Sent: livery '{appearance.Livery}', material '{appearance.BaseMaterial}', " +
                        $"{appearance.Colors?.Length ?? 0} colour(s), parts [{appearance.Exhaust}|{appearance.Grill}|{appearance.Ornament}|{appearance.Sensors}|{appearance.LicensePlate}|{appearance.WindowDecal}|{appearance.MaglockTopper}], " +
                        $"damage {appearance.Damage:0.00}, dirt {appearance.Dirt:0.00}");
    }
}
