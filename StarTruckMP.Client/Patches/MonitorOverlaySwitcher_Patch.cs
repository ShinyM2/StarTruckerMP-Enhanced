using System;
using HarmonyLib;
using StarTruckMP.Client.UI;

namespace StarTruckMP.Client.Patches;

/// <summary>
/// Keeps the multiplayer page in step with the game's own page switching, which happens inside
/// <c>ActivateChannel</c> and on its own account as well: the hitching readout goes up the moment
/// a maglock target comes into range, with no channel change at all.
///
/// The channel the game is showing a page for decides everything. If it is a view of our slot,
/// the game's call is declined before its page goes up and ours is shown instead; if it is
/// anything else, the game's page goes up as it always did and ours comes down at once.
///
/// Declining, rather than taking the page down afterwards, is what stopped the stutter with a
/// trailer hitched. The game asks for the trailer readout on our slot, and it asks again as long
/// as it does not see it: with the page switched off after every request, each frame switched a
/// page with a dozen labels on and off again, and a hitched trailer is the normal state of a
/// truck. Nothing is switched at all now.
///
/// An earlier version only reasserted the page whenever it happened to be active, and that was
/// the black screen on the way out: the game had already put the next channel's page up, our
/// page was still active because the channel postfix had not run yet, and the reassert took the
/// new page down again.
///
/// The parameter is declared as the <c>MonitorChannel</c> it really is. Declaring it as the
/// overlay enum, which is what an earlier attempt did, broke every monitor in the cab.
/// </summary>
[HarmonyPatch(typeof(MonitorOverlaySwitcher), nameof(MonitorOverlaySwitcher.ShowOverlayType))]
public class MonitorOverlaySwitcher_Patch
{
    private static bool Prefix(MonitorOverlaySwitcher __instance, MonitorChannel __0)
    {
        try
        {
            if (__instance == null || !MonitorPanel.Owns(__instance)) return true;
            if (!MonitorPanel.Claims(__0)) return true;

            MonitorPanel.SetVisible(true);
            return false;
        }
        catch (Exception ex)
        {
            App.Log.LogError($"[Monitor] Could not decline the game's page: {ex.Message}");
            return true;
        }
    }

    private static void Postfix(MonitorOverlaySwitcher __instance, MonitorChannel __0)
    {
        try
        {
            if (__instance == null || !MonitorPanel.Owns(__instance)) return;
            if (!MonitorPanel.Claims(__0)) MonitorPanel.SetVisible(false);
        }
        catch (Exception ex)
        {
            App.Log.LogError($"[Monitor] Could not follow the overlay change: {ex.Message}");
        }
    }
}
