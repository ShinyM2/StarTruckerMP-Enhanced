using System;
using HarmonyLib;
using StarTruckMP.Client.UI;

namespace StarTruckMP.Client.Patches;

/// <summary>
/// Keeps the multiplayer page on top when the game puts one of its own pages up without changing
/// channel — which it does the moment a trailer is hitched, for the trailer readout, and again
/// for the hitching page when a maglock target comes into range.
///
/// The postfix takes only the instance. The method's real parameter is a <c>MonitorChannel</c>,
/// and an earlier attempt that declared it as the overlay enum broke every monitor in the cab;
/// nothing here needs to know which page the game chose, only that it chose one.
/// </summary>
[HarmonyPatch(typeof(MonitorOverlaySwitcher), nameof(MonitorOverlaySwitcher.ShowOverlayType))]
public class MonitorOverlaySwitcher_Patch
{
    private static void Postfix(MonitorOverlaySwitcher __instance)
    {
        try
        {
            if (__instance == null || !MonitorPanel.Owns(__instance)) return;
            MonitorPanel.Reassert();
        }
        catch (Exception ex)
        {
            App.Log.LogError($"[Monitor] Could not reassert the page after an overlay change: {ex.Message}");
        }
    }
}
