using System;
using HarmonyLib;
using StarTruckMP.Client.UI;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace StarTruckMP.Client.Patches;

/// <summary>
/// Puts a Multiplayer entry into the game's own main menu, between the buttons that start a
/// game and Options, and stamps the mod version next to the game's.
///
/// The button is a clone of the Options button rather than anything hand-built, so it inherits
/// the game's exact visuals, sounds, hover and selection animations — nothing to keep in sync
/// with the game's art, and nothing that looks bolted on.
/// </summary>
[HarmonyPatch(typeof(MainMenuScreen), nameof(MainMenuScreen.Start))]
public class MainMenuScreen_Patch
{
    private const string ButtonName = "StarTruckMP_MultiplayerButton";

    private static void Postfix(MainMenuScreen __instance)
    {
        try
        {
            AddMultiplayerButton(__instance);
            StampVersion(__instance);
        }
        catch (Exception ex)
        {
            // A broken main menu would make the game unplayable, so never let this throw out.
            App.Log.LogError($"[MainMenu] Could not add the multiplayer button: {ex.Message}");
        }
    }

    private static void AddMultiplayerButton(MainMenuScreen screen)
    {
        var template = screen.optionsButton;
        if (template == null)
        {
            App.Log.LogWarning("[MainMenu] No options button to clone, skipping.");
            return;
        }

        var parent = template.transform.parent;
        if (parent == null) return;

        // Start runs again every time the player returns to the menu.
        if (parent.Find(ButtonName) != null) return;

        // Read the game's own label before overwriting the clone's: it tells us which language
        // the player is in, which is more reliable than guessing from the font.
        Localisation.LearnFrom(SampleText(template.gameObject));
        var label = LabelFor(template.gameObject);

        var clone = Object.Instantiate(template.gameObject, parent);
        clone.name = ButtonName;
        clone.SetActive(true);

        // Sit directly above Options, i.e. between starting a game and the settings.
        clone.transform.SetSiblingIndex(template.transform.GetSiblingIndex());

        // The clone carries the Options label's localisation binding, which would overwrite
        // whatever we set on the next language refresh.
        foreach (var lookup in clone.GetComponentsInChildren<StringTableLookup>(true))
            Object.Destroy(lookup);

        // Both label objects: a menu entry keeps one for its resting state and another for the
        // highlighted one, and filling only the first leaves the original text showing the
        // moment the entry is selected.
        foreach (var text in clone.GetComponentsInChildren<TextMeshProUGUI>(true))
            text.text = label;

        var button = clone.GetComponent<MenuButton>();
        if (button == null)
        {
            App.Log.LogWarning("[MainMenu] Clone has no MenuButton component.");
            return;
        }

        button.isInteractable = true;

        // RemoveAllListeners only drops listeners added at runtime. The Options button's real
        // action is a persistent call baked into the prefab, which survived the clone and kept
        // opening the settings screen underneath our own; those have to be switched off by index.
        var click = button.m_OnClick;
        for (var i = 0; i < click.GetPersistentEventCount(); i++)
            click.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.Off);

        click.RemoveAllListeners();
        click.AddListener(new Action(OpenMultiplayerMenu));

        // The roster panel belongs to the cockpit, not the menus.
        OverlayManager.PostMessage("hud", new { inWorld = false });

        // The page builds itself from this very column, using the same button as its rows.
        MultiplayerScreen.Prepare(parent, template);

        App.Log.LogInfo($"[MainMenu] Multiplayer button added at index {clone.transform.GetSiblingIndex()}.");
    }

    /// <summary>
    /// Matches the language the game is already displaying, judged by whether the button we are
    /// cloning is showing Cyrillic. Asking the font instead does not work: the menu face carries
    /// no Cyrillic at all and TMP draws Russian through a fallback, so a font query says "no"
    /// while the screen is plainly full of Russian — and answering Latin also picks up the
    /// primary face, which is why the entry stood out in bold among its neighbours.
    /// </summary>
    private static string LabelFor(GameObject template) => Strings.Get("menu.multiplayer");

    /// <summary>Any visible text from the game, used only to tell which language is on screen.</summary>
    private static string SampleText(GameObject template)
    {
        foreach (var text in template.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (!string.IsNullOrWhiteSpace(text.text)) return text.text;
        }

        return string.Empty;
    }

    private static void StampVersion(MainMenuScreen screen)
    {
        var field = screen.versionField;
        if (field == null) return;

        var stamp = $"StarTruckMP {PluginInfo.PLUGIN_VERSION}";
        if (UpdateCheck.Available != null)
            stamp += "  <color=#EFC806>" + Strings.Get("update.short", UpdateCheck.Available) + "</color>";

        if (field.text != null && field.text.Contains("StarTruckMP")) return;

        field.text = string.IsNullOrWhiteSpace(field.text) ? stamp : $"{field.text}    {stamp}";
    }

    private static void OpenMultiplayerMenu()
    {
        App.Log.LogInfo("[MainMenu] Multiplayer button pressed.");

        // The page replaces the button column while it is up, the way the game's own screens do.
        MultiplayerScreen.Show();

        // Nothing native was available to clone — fall back to the overlay so the entry still works.
        if (!MultiplayerScreen.IsOpen)
            OverlayManager.SetInteractiveMode(true);
    }
}
