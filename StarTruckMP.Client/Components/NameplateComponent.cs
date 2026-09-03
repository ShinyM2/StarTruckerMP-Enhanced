using System;
using StarTruckMP.Client.UI;
using TMPro;
using UnityEngine;

namespace StarTruckMP.Client.Components;

/// <summary>
/// Draws a floating name above a remote player's truck.
///
/// Attached to the truck object created by <see cref="NetworkEventsComponent"/>, so it lives
/// and dies with that truck — which already only exists while the player shares our sector.
/// The label is a world-space TextMeshPro mesh turned to face the camera every frame and
/// scaled with distance, so it stays readable from far away without dwarfing a nearby truck.
/// While the player is talking on the radio a small marker lights up beside the name.
/// </summary>
public class NameplateComponent : MonoBehaviour
{
    /// <summary>Metres above the truck origin to float the label at.</summary>
    private const float Height = 5.5f;

    /// <summary>Beyond this distance the label is hidden entirely. Space is roomy.</summary>
    private const float MaxDistance = 20000f;

    // Scaling with distance keeps the label about the same size on screen. The floor matters as
    // much as the ceiling: at arm's length a purely proportional label shrinks to nothing, which
    // is exactly where two truckers parked side by side need to read each other's name.
    private const float MinScale = 3.0f;
    private const float MaxScale = 400f;
    private const float ScalePerMetre = 0.035f;

    /// <summary>The on-air marker, in the pale tone of a lit indicator rather than the amber of the name.</summary>
    private const string SpeakingMark = " <color=#FFF4B8>((•))</color>";

    private static TMP_FontAsset _font;

    private GameObject _labelObj;
    private TextMeshPro _text;
    private string _pendingName = string.Empty;
    private bool _ghost;
    private bool _speaking;
    private string _language;

    public NameplateComponent(IntPtr ptr) : base(ptr) { }

    /// <summary>The player this plate belongs to, so it can tell when they are on the radio.</summary>
    public int NetId { get; set; } = -1;

    /// <summary>
    /// Sets the displayed name. Safe to call before the label mesh exists — the value is
    /// kept and applied once <see cref="Start"/> has built it.
    /// </summary>
    public void SetName(string name)
    {
        _pendingName = name ?? string.Empty;
        Apply();
    }

    /// <summary>
    /// Says, under the name, that this truck is currently being drawn through.
    ///
    /// Without it a translucent truck reads as a rendering fault rather than something the mod is
    /// doing on purpose.
    /// </summary>
    public void SetGhost(bool ghost)
    {
        if (_ghost == ghost) return;

        _ghost = ghost;
        Apply();
    }

    private void Apply()
    {
        if (_text == null) return;

        var line = _pendingName;
        if (_speaking) line += SpeakingMark;
        if (_ghost) line += "\n" + Strings.Get("nameplate.ghost");

        _text.text = line;
        _language = Strings.Language;
    }

    private void Start()
    {
        try
        {
            var font = ResolveFont();
            if (font == null)
            {
                App.Log.LogWarning("[Nameplate] No TMP font asset found in the game, nameplates disabled.");
                enabled = false;
                return;
            }

            _labelObj = new GameObject("Nameplate");
            _labelObj.transform.SetParent(transform, false);
            _labelObj.transform.localPosition = new Vector3(0f, Height, 0f);

            _text = _labelObj.AddComponent<TextMeshPro>();
            _text.font = font;
            _text.richText = true;
            Apply();
            _text.fontSize = 8f;
            _text.alignment = TextAlignmentOptions.Center;
            _text.characterSpacing = 6f;
            _text.fontStyle = FontStyles.UpperCase;

            // The game's own instrument amber, with a heavy dark edge so it stays legible
            // against a bright hull or the glare of a station.
            _text.color = new Color32(0xEF, 0xC8, 0x06, 0xFF);
            _text.outlineWidth = 0.28f;
            _text.outlineColor = new Color32(0, 0, 0, 255);
            _text.rectTransform.sizeDelta = new Vector2(24f, 4f);

            DrawThroughGeometry(_text);
        }
        catch (Exception ex)
        {
            App.Log.LogError($"[Nameplate] Failed to create label: {ex.Message}");
            enabled = false;
        }
    }

    private void LateUpdate()
    {
        if (_labelObj == null) return;

        var speaking = NetId >= 0 && MultiplayerState.IsSpeaking(NetId);
        if (speaking != _speaking || (_ghost && _language != Strings.Language))
        {
            _speaking = speaking;
            Apply();
        }

        var cam = Camera.main;
        if (cam == null) return;

        var labelPos = _labelObj.transform.position;
        var fromCam = labelPos - cam.transform.position;
        var distance = fromCam.magnitude;

        var visible = distance <= MaxDistance;
        if (_labelObj.activeSelf != visible) _labelObj.SetActive(visible);
        if (!visible) return;

        // TextMeshPro renders along +Z, so pointing that away from the camera faces us.
        _labelObj.transform.rotation = Quaternion.LookRotation(fromCam.normalized, Vector3.up);
        _labelObj.transform.localScale =
            Vector3.one * Mathf.Clamp(distance * ScalePerMetre, MinScale, MaxScale);
    }

    /// <summary>
    /// Makes the label ignore the depth buffer so it shows through the cab.
    ///
    /// Sitting in the driver's seat, the whole windscreen frame and dashboard stand between the
    /// camera and another truck, and a normally depth-tested label is simply swallowed by them —
    /// which is the one place a nameplate is most wanted.
    /// </summary>
    private static void DrawThroughGeometry(TextMeshPro text)
    {
        try
        {
            // fontMaterial is a per-instance copy; touching fontSharedMaterial here would
            // restyle every other piece of text in the game that uses this font.
            var material = text.fontMaterial;
            material.SetFloat("_ZTestMode", 8f); // UnityEngine.Rendering.CompareFunction.Always
            material.renderQueue = 4000;
        }
        catch (Exception ex)
        {
            App.Log.LogWarning($"[Nameplate] Could not make the label draw on top: {ex.Message}");
        }
    }

    private static TMP_FontAsset ResolveFont()
    {
        if (_font != null) return _font;

        // The game ships its own TMP fonts; any of them beats shipping one with the plugin.
        var candidates = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        if (candidates != null && candidates.Length > 0) _font = candidates[0];

        return _font;
    }
}
