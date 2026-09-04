using System;
using HarmonyLib;
using StarTruckMP.Client.Synchronization;
using UnityEngine;

namespace StarTruckMP.Client.Patches;

/// <summary>
/// Keeps the world running while a menu is up, once the player is on a server.
///
/// Star Trucker freezes time for the pause menu, the map, the shop screens and the moment the
/// window loses focus. Alone that is fine; with others on the server it is not: a frozen player
/// stops sending positions, so their truck stands still for everyone else and then leaps to
/// wherever they really are when they come back — and while frozen they see the same happen to
/// everyone else. So the game's own pause is declined for as long as a session is joined. The
/// screens still open and still take the controls; only time keeps going.
/// </summary>
[HarmonyPatch(typeof(PauseController))]
public static class PauseController_Patch
{
    private static bool _explained;

    /// <summary>True while the game's pause should be refused.</summary>
    public static bool Suppressing => App.NoPauseInMultiplayer.Value && Network.NetId != -1;

    [HarmonyPatch("Internal_GamePause")]
    [HarmonyPrefix]
    private static bool BeforePause()
    {
        try
        {
            if (!Suppressing) return true;

            if (!_explained)
            {
                _explained = true;
                App.Log.LogInfo("[Pause] The game asked to pause; declined while on a server (NoPauseInMultiplayer).");
            }

            // Whatever got as far as freezing time before this ran, undo.
            if (Time.timeScale == 0f) Time.timeScale = 1f;
            AudioListener.pause = false;
            return false;
        }
        catch (Exception ex)
        {
            App.Log.LogWarning($"[Pause] Could not decline the pause: {ex.Message}");
            return true;
        }
    }
}
