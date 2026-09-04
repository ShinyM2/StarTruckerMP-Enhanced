using System;
using System.Collections.Generic;
using HarmonyLib;
using StarTruckMP.Client.UI;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace StarTruckMP.Client.Patches;

/// <summary>
/// The same multiplayer entry in the in-game pause menu.
///
/// Reaching the page only from the title screen meant leaving the session to change anything, so
/// the pause menu gets its own copy — built the same way, from a clone of one of its own entries.
/// </summary>
[HarmonyPatch(typeof(PauseScreen), nameof(PauseScreen.Awake))]
public class PauseScreen_Patch
{
    private const string ButtonName = "StarTruckMP_MultiplayerButton";

    private static void Postfix(PauseScreen __instance)
    {
        try
        {
            AddMultiplayerButton(__instance);
        }
        catch (Exception ex)
        {
            App.Log.LogError($"[Pause] Could not add the multiplayer button: {ex.Message}");
        }
    }

    private static void AddMultiplayerButton(PauseScreen screen)
    {
        // The pause menu exposes no button fields, so take one of its own entries as the model.
        var template = FirstEntry(screen);
        if (template == null)
        {
            App.Log.LogWarning("[Pause] No menu entry to clone.");
            return;
        }

        var parent = template.transform.parent;
        if (parent == null || parent.Find(ButtonName) != null) return;

        Localisation.LearnFrom(SampleText(template.gameObject));

        var clone = Object.Instantiate(template.gameObject, parent);
        clone.name = ButtonName;
        clone.SetActive(true);

        // Second in the list, right under the entry it was cloned from.
        clone.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);

        foreach (var lookup in clone.GetComponentsInChildren<StringTableLookup>(true))
            Object.DestroyImmediate(lookup);

        var label = Strings.Get("menu.multiplayer");
        foreach (var text in clone.GetComponentsInChildren<TextMeshProUGUI>(true))
            text.text = label;

        var button = clone.GetComponent<MenuButton>();
        if (button == null)
        {
            App.Log.LogWarning("[Pause] The cloned entry has no MenuButton.");
            return;
        }

        var click = button.m_OnClick;
        for (var i = 0; i < click.GetPersistentEventCount(); i++)
            click.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.Off);

        click.RemoveAllListeners();
        click.AddListener(new Action(Open));
        button.isInteractable = true;

        // Never let it come back from a page change in its faded Disabled state.
        button.disableOnEnable = false;

        // The page builds itself into this column, from this very entry.
        MultiplayerScreen.Prepare(parent, button);

        App.Log.LogInfo($"[Pause] Multiplayer button added at index {clone.transform.GetSiblingIndex()}, " +
                        $"parent {Describe(parent)}, active={clone.activeInHierarchy}, " +
                        $"siblings={parent.childCount}, model={Describe(template.transform)}");

        foreach (var sibling in parent)
        {
            var t = sibling.Cast<Transform>();
            if (t == null) continue;
            App.Log.LogInfo($"[Pause]   child {t.GetSiblingIndex()}: {t.name} active={t.gameObject.activeSelf}");
        }
    }

    /// <summary>
    /// An entry from the pause menu's own list, to serve as the model.
    ///
    /// The screen holds two kinds of button: the list the player reads down, and the row of
    /// input hints along the bottom. Both are <see cref="MenuButton"/>s, and simply taking the
    /// first one landed our entry among the hints. The list is identified the way the game names
    /// it — its entries are "Button_Option [...]", the hints "Button_Helper_Input_...".
    /// </summary>
    private static MenuButton FirstEntry(PauseScreen screen)
    {
        var buttons = screen.GetComponentsInChildren<MenuButton>(true);

        var best = (MenuButton)null;
        var bestScore = -1;
        var bestIndex = int.MaxValue;

        foreach (var button in buttons)
        {
            if (button == null || button.gameObject.name == ButtonName) continue;

            var name = button.gameObject.name;
            if (name.IndexOf("Helper", StringComparison.OrdinalIgnoreCase) >= 0) continue;

            // Prefer a real list entry; among equals, the topmost one.
            var score = name.IndexOf("Option", StringComparison.OrdinalIgnoreCase) >= 0 ? 1 : 0;
            var index = button.transform.GetSiblingIndex();

            if (score < bestScore) continue;
            if (score == bestScore && index >= bestIndex) continue;

            best = button;
            bestScore = score;
            bestIndex = index;
        }

        if (best == null)
            App.Log.LogWarning("[Pause] Found no list entry to clone; only input hints.");

        return best;
    }

    private static string Describe(Transform t)
    {
        var parts = new List<string>();
        while (t != null && parts.Count < 8)
        {
            parts.Insert(0, t.name);
            t = t.parent;
        }

        return string.Join("/", parts);
    }

    private static string SampleText(GameObject template)
    {
        foreach (var text in template.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (!string.IsNullOrWhiteSpace(text.text)) return text.text;
        }

        return string.Empty;
    }

    private static void Open()
    {
        App.Log.LogInfo("[Pause] Multiplayer button pressed.");
        MultiplayerScreen.Show();

        if (!MultiplayerScreen.IsOpen)
            OverlayManager.SetInteractiveMode(true);
    }
}
