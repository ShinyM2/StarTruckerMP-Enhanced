using System;
using StarTruckMP.Client.Components;
using StarTruckMP.Client.Synchronization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace StarTruckMP.Client.UI;

/// <summary>
/// The line at the top of the screen that says ghost mode is in effect around you.
///
/// A translucent truck says what it is under its own name, but the player it matters most to
/// is the one it is drawn for, who may be looking at the bay and not at the label: this tells
/// them, in the game's language, that the trucks here are see-through and cannot hit them. It
/// fades in while any remote truck or trailer is ghosted and out again the moment none is, and
/// costs nothing while no truck is near a gate or a bay.
/// </summary>
internal static class GhostNotice
{
    /// <summary>The game's own amber, the colour of every other caption of the mod.</summary>
    private static readonly Color Amber = new(0.937f, 0.784f, 0.024f, 1f);

    private const float FadePerSecond = 4f;

    private static GameObject _root;
    private static CanvasGroup _group;
    private static TextMeshProUGUI _text;
    private static float _alpha;
    private static string _language;
    private static bool _failed;

    /// <summary>Game thread, every frame.</summary>
    public static void Tick()
    {
        var wanted = App.GhostMode.Value && Network.NetId != -1 && GhostComponent.ActiveCount > 0;

        if (_root == null)
        {
            if (!wanted || _failed) return;
            if (!Build()) return;
        }

        var target = wanted ? 1f : 0f;
        if (Mathf.Approximately(_alpha, target) && !_root.activeSelf) return;

        _alpha = Mathf.MoveTowards(_alpha, target, FadePerSecond * Time.unscaledDeltaTime);
        _group.alpha = _alpha;

        var visible = _alpha > 0f;
        if (_root.activeSelf != visible) _root.SetActive(visible);
        if (!visible) return;

        if (_language != Strings.Language)
        {
            _language = Strings.Language;
            _text.text = Strings.Get("ghost.notice");
        }
    }

    private static bool Build()
    {
        try
        {
            var font = NameplateComponent.ResolveFont();
            if (font == null)
            {
                _failed = true;
                App.Log.LogWarning("[Ghost] No font for the ghost notice; it is not shown.");
                return false;
            }

            _root = new GameObject("StarTruckMP_GhostNotice");
            Object.DontDestroyOnLoad(_root);

            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 400;

            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            _group = _root.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.interactable = false;
            _group.blocksRaycasts = false;

            var label = new GameObject("Text");
            label.transform.SetParent(_root.transform, false);

            _text = label.AddComponent<TextMeshProUGUI>();
            _text.font = font;
            _text.richText = true;
            _text.fontSize = 24f;
            _text.alignment = TextAlignmentOptions.Center;
            _text.characterSpacing = 3f;
            _text.fontStyle = FontStyles.UpperCase;
            _text.enableWordWrapping = true;
            _text.overflowMode = TextOverflowModes.Overflow;
            _text.color = Amber;
            _text.outlineWidth = 0.2f;
            _text.outlineColor = new Color32(0, 0, 0, 220);
            _text.raycastTarget = false;

            // Across the upper part of the screen, under the game's own top-edge readouts and
            // above the middle of the windscreen, where the eye is while docking.
            var rect = _text.rectTransform;
            rect.anchorMin = new Vector2(0.15f, 0.80f);
            rect.anchorMax = new Vector2(0.85f, 0.90f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            _language = Strings.Language;
            _text.text = Strings.Get("ghost.notice");
            _root.SetActive(false);
            return true;
        }
        catch (Exception ex)
        {
            _failed = true;
            App.Log.LogWarning($"[Ghost] Could not build the ghost notice: {ex.Message}");
            if (_root != null) Object.Destroy(_root);
            _root = null;
            return false;
        }
    }
}
