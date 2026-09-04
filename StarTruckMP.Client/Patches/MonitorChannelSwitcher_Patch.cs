using System;
using System.Collections.Generic;
using HarmonyLib;
using StarTruckMP.Client.UI;
using UnityEngine;

namespace StarTruckMP.Client.Patches;

/// <summary>
/// Puts the multiplayer page onto the right-hand monitor's docking-camera channel.
///
/// The channel is the unit the player actually switches between with the arrows under the
/// screen — the six mirror and approach cameras, the docking camera, and the interface pages.
/// The game knows which one is the docking camera without any guessing on our part:
/// <c>MonitorChannelSwitcher.dockingCameraChannel</c> is its index into <c>channels</c>. That
/// slot has several views — the trailer readout and the docked pages ride on it too — and
/// <see cref="MonitorPanel.Claims"/> takes all of them, since the game moves between them on
/// its own whenever a trailer is hitched or the truck docks.
///
/// An earlier version hooked <c>MonitorOverlaySwitcher.ShowOverlayType</c> and replaced the
/// <c>DockedStatus</c> page instead. That page only appears while the truck is actually docked at
/// a station, which is why the multiplayer page was never on screen: the target was wrong, not
/// the mechanism.
///
/// This patch decides only which channel the page belongs to. Holding the screen once the page is
/// up — blanking the camera feed, keeping the game's own overlays down — belongs to
/// <see cref="MonitorPanel"/>, because it has to happen every frame rather than once per change
/// of channel.
/// </summary>
[HarmonyPatch(typeof(MonitorChannelSwitcher), "ActivateChannel")]
public class MonitorChannelSwitcher_Patch
{
    private static readonly HashSet<int> Described = new();

    private static void Postfix(MonitorChannelSwitcher __instance, MonitorChannel __0)
    {
        try
        {
            if (__instance == null) return;

            Describe(__instance);

            if (MonitorPanel.Exists)
            {
                if (!MonitorPanel.Owns(__instance)) return;
            }
            else
            {
                if (!IsRightMonitor(__instance)) return;
                MonitorPanel.Install(__instance);
                if (!MonitorPanel.Exists) return;
            }

            MonitorPanel.SetVisible(MonitorPanel.Claims(__0));
        }
        catch (Exception ex)
        {
            App.Log.LogError($"[Monitor] Channel swap failed: {ex.Message}");
        }
    }

    /// <summary>
    /// The cab has more than one monitor. The overlay switcher's own object is the one whose
    /// path names the side — <c>Camera_RenderToTexture_Right</c> — so the side is read from
    /// there rather than from the channel switcher, which sits elsewhere in the truck.
    /// </summary>
    private static bool IsRightMonitor(MonitorChannelSwitcher switcher)
    {
        var overlays = switcher.monitorOverlaySwitcher;
        var path = Path(overlays != null ? overlays.transform : switcher.transform);

        if (path.IndexOf("right", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (path.IndexOf("left", StringComparison.OrdinalIgnoreCase) >= 0) return false;

        // No side in the name: fall back to which side of the cab it physically sits on.
        var truck = PlayerState.Truck;
        if (truck == null) return false;

        var local = truck.transform.InverseTransformPoint(switcher.transform.position);
        return local.x >= 0f;
    }

    /// <summary>
    /// Each monitor's channel table, written to the log once. What a channel is made of — its
    /// name, whether it carries a camera, which interface page rides on it — cannot be read off
    /// the source, and this is what the next change to the page will be laid out against.
    /// </summary>
    private static void Describe(MonitorChannelSwitcher switcher)
    {
        var id = switcher.GetInstanceID();
        if (!Described.Add(id)) return;

        var overlays = switcher.monitorOverlaySwitcher;
        App.Log.LogInfo($"[Monitor] Channels of {Path(overlays != null ? overlays.transform : switcher.transform)}, " +
                        $"docking camera is #{switcher.dockingCameraChannel}:");

        var channels = switcher.channels;
        if (channels == null)
        {
            App.Log.LogInfo("[Monitor]   (no channel list)");
            return;
        }

        for (var i = 0; i < channels.Count; i++)
        {
            var channel = channels[i];
            if (channel == null)
            {
                App.Log.LogInfo($"[Monitor]   #{i} (null)");
                continue;
            }

            // The camera is deliberately not touched here: reading it would drag a Cinemachine
            // reference into the plugin, and the name already says what a channel is —
            // STR_MONITORCAM_* is a camera feed, anything else an interface page.
            App.Log.LogInfo($"[Monitor]   #{i} {channel.channelNameStringId}  overlay={channel.overlayType}  " +
                            $"idx={channel.channelIdx}  view={channel.channelViewIdx}  mirrored={channel.mirrored}");
        }
    }

    private static string Path(Transform t)
    {
        var parts = new List<string>();
        while (t != null && parts.Count < 8)
        {
            parts.Insert(0, t.name);
            t = t.parent;
        }

        return string.Join("/", parts);
    }
}
