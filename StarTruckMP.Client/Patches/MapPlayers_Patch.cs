using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace StarTruckMP.Client.Patches;

/// <summary>
/// Writes the other players' names under their sectors on the galactic map.
///
/// The map is a set of <c>MapSectorButton</c>s, one per sector, each with a name label. A second,
/// smaller label is hung under each name and filled with whoever is in that sector, in their own
/// colours, refreshed once a second while the map tab is up. A sector button is matched to the
/// sector id the wire carries ("Sector_02_AtlasPrime") by the number in it, which is also what
/// the game's own string ids ("STR_SECTOR_02") and short ids carry.
/// </summary>
[HarmonyPatch(typeof(TabContent_MapPanel))]
public static class MapPlayers_Patch
{
    private const float RefreshSeconds = 1f;

    private static readonly Dictionary<int, TextMeshProUGUI> _labels = new();
    private static float _nextRefresh;
    private static bool _described;

    [HarmonyPatch("OnTabActivate")]
    [HarmonyPostfix]
    private static void AfterActivate(TabContent_MapPanel __instance) => Refresh(__instance, force: true);

    [HarmonyPatch("Update")]
    [HarmonyPostfix]
    private static void AfterUpdate(TabContent_MapPanel __instance) => Refresh(__instance, force: false);

    private static void Refresh(TabContent_MapPanel panel, bool force)
    {
        try
        {
            if (!force && Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + RefreshSeconds;

            var content = panel != null ? panel.m_mapContent : null;
            var buttons = content != null ? content.SectorButtons : null;
            if (buttons == null) return;

            // Who is where, grouped by the number in their sector id.
            var bySector = new Dictionary<string, StringBuilder>();
            foreach (var player in MultiplayerState.Players)
            {
                var key = SectorNumber(player.Sector);
                if (key == null) continue;

                if (!bySector.TryGetValue(key, out var names))
                {
                    names = new StringBuilder();
                    bySector[key] = names;
                }
                else
                {
                    names.Append(", ");
                }

                names.Append("<color=").Append(MultiplayerState.ColorHex(player.NetId)).Append('>')
                     .Append(player.Name).Append("</color>");
            }

            foreach (var button in buttons)
            {
                if (button == null) continue;

                var key = ButtonSectorNumber(button);
                if (!_described && key != null)
                {
                    _described = true;
                    App.Log.LogInfo($"[Map] Sector button '{button.name}': id={Safe(() => button.SectorId?.name)}, name={Safe(() => button.m_sectorMetadata?.sectorName)}, short={Safe(() => button.m_sectorMetadata?.shortSectorId)}, string={Safe(() => button.m_sectorMetadata?.displayNameId)} → number {key}");
                }

                var text = key != null && bySector.TryGetValue(key, out var names) ? names.ToString() : string.Empty;
                SetLabel(button, text);
            }
        }
        catch (Exception ex)
        {
            App.Log.LogError($"[Map] Could not place players on the map: {ex.Message}");
        }
    }

    private static void SetLabel(MapSectorButton button, string text)
    {
        var id = button.GetInstanceID();
        if (!_labels.TryGetValue(id, out var label) || label == null)
        {
            if (string.IsNullOrEmpty(text)) return;

            var model = button.m_sectorNameLabel;
            if (model == null) return;

            var clone = Object.Instantiate(model.gameObject, model.transform.parent);
            clone.name = "StarTruckMP_Players";
            foreach (var lookup in clone.GetComponentsInChildren<StringTableLookup>(true))
                Object.DestroyImmediate(lookup);

            label = clone.GetComponent<TextMeshProUGUI>();
            label.richText = true;
            label.fontSize = model.fontSize * 0.75f;
            label.enableAutoSizing = false;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
            label.raycastTarget = false;
            label.color = Color.white;

            // Just under the sector's own name.
            var rect = label.rectTransform;
            var modelRect = model.rectTransform;
            rect.anchoredPosition = modelRect.anchoredPosition + new Vector2(0f, -(modelRect.rect.height * 0.9f));

            _labels[id] = label;
        }

        if (label.text != text) label.text = text;
        if (label.gameObject.activeSelf != !string.IsNullOrEmpty(text)) label.gameObject.SetActive(!string.IsNullOrEmpty(text));
    }

    private static string ButtonSectorNumber(MapSectorButton button)
    {
        var metadata = button.m_sectorMetadata;
        if (metadata != null)
        {
            var number = SectorNumber(Safe(() => metadata.sectorName)) ??
                         SectorNumber(Safe(() => metadata.shortSectorId)) ??
                         SectorNumber(Safe(() => metadata.displayNameId));
            if (number != null) return number;
        }

        return SectorNumber(Safe(() => button.SectorId?.name));
    }

    /// <summary>The first run of digits in a sector id, without leading zeros: "Sector_02_AtlasPrime" and "STR_SECTOR_02" both give "2".</summary>
    private static string SectorNumber(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        var start = -1;
        for (var i = 0; i < id.Length; i++)
        {
            if (char.IsDigit(id[i]))
            {
                if (start < 0) start = i;
            }
            else if (start >= 0)
            {
                return id.Substring(start, i - start).TrimStart('0');
            }
        }

        return start >= 0 ? id.Substring(start).TrimStart('0') : null;
    }

    private static string Safe(Func<string> read)
    {
        try { return read(); }
        catch { return null; }
    }
}
