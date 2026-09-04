using System;
using StarTruckMP.Client.UI;
using TMPro;
using UnityEngine;

namespace StarTruckMP.Client.Components;

/// <summary>
/// Draws a floating name above a remote player's truck, with how far away it is beside it.
///
/// Attached to the truck object created by <see cref="NetworkEventsComponent"/>, so it lives
/// and dies with that truck — which already only exists while the player shares our sector.
/// The label is a world-space TextMeshPro mesh turned to face the camera every frame and
/// scaled with distance, so it stays readable from far away without dwarfing a nearby truck.
/// The name takes the player's own colour, the one the monitor and the overlay use for them
/// too; the distance is quieter, and a marker lights up while they talk on the radio.
/// </summary>
public class NameplateComponent : MonoBehaviour
{
    /// <summary>Metres above the truck origin to float the label at: clear of the roof and the trailer stack.</summary>
    private const float Height = 8f;

    /// <summary>Beyond this distance the label is hidden entirely. Space is roomy.</summary>
    private const float MaxDistance = 20000f;

    // Scaling with distance keeps the label about the same size on screen. The floor matters as
    // much as the ceiling: at arm's length a purely proportional label shrinks to nothing, which
    // is exactly where two truckers parked side by side need to read each other's name.
    private const float MinScale = 3.5f;
    private const float MaxScale = 400f;
    private const float ScalePerMetre = 0.035f;

    /// <summary>How often the distance is rewritten. A label rebuild is not free, and metres tick fast.</summary>
    private const float DistanceRefreshSeconds = 0.2f;

    /// <summary>The on-air marker, in the pale tone of a lit indicator.</summary>
    private const string SpeakingMark = " <color=#FFF4B8>((•))</color>";

    private const string DistanceColour = "#E9E7DE";

    private static TMP_FontAsset _font;

    private GameObject _labelObj;
    private TextMeshPro _text;
    private string _pendingName = string.Empty;
    private string _distance = string.Empty;
    private bool _ghost;
    private bool _speaking;
    private string _language;
    private float _nextDistance;

    public NameplateComponent(IntPtr ptr) : base(ptr) { }

    /// <summary>The player this plate belongs to: for their colour, and to tell when they are on the radio.</summary>
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

        var colour = NetId >= 0 ? MultiplayerState.ColorHex(NetId) : "#EFC806";
        var line = $"<color={colour}>{_pendingName}</color>";
        if (_speaking) line += SpeakingMark;
        if (!string.IsNullOrEmpty(_distance)) line += $"<color={DistanceColour}><size=70%>   {_distance}</size></color>";
        if (_ghost) line += "\n<size=65%>" + Strings.Get("nameplate.ghost") + "</size>";

        if (_text.text != line) _text.text = line;
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
            _text.fontSize = 8f;
            _text.alignment = TextAlignmentOptions.Center;
            _text.characterSpacing = 4f;
            _text.fontStyle = FontStyles.UpperCase | FontStyles.Bold;
            _text.enableWordWrapping = false;
            _text.overflowMode = TextOverflowModes.Overflow;

            // White base so the rich-text colours come through unchanged, with a heavy dark edge so
            // the label stays legible against a bright hull or the glare of a station.
            _text.color = Color.white;
            _text.outlineWidth = 0.25f;
            _text.outlineColor = new Color32(0, 0, 0, 255);
            _text.rectTransform.sizeDelta = new Vector2(40f, 4f);

            DrawThroughGeometry(_text);
            Apply();
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

        var cam = Camera.main;
        if (cam == null) return;

        var labelPos = _labelObj.transform.position;
        var fromCam = labelPos - cam.transform.position;
        var distance = fromCam.magnitude;

        var visible = distance <= MaxDistance;
        if (_labelObj.activeSelf != visible) _labelObj.SetActive(visible);
        if (!visible) return;

        var speaking = NetId >= 0 && MultiplayerState.IsSpeaking(NetId);
        var redraw = speaking != _speaking || (_ghost && _language != Strings.Language);
        _speaking = speaking;

        if (Time.unscaledTime >= _nextDistance)
        {
            _nextDistance = Time.unscaledTime + DistanceRefreshSeconds;
            var text = FormatDistance(DistanceFromPlayer());
            if (text != _distance)
            {
                _distance = text;
                redraw = true;
            }
        }

        if (redraw) Apply();

        // TextMeshPro renders along +Z, so pointing that away from the camera faces us.
        _labelObj.transform.rotation = Quaternion.LookRotation(fromCam.normalized, Vector3.up);
        _labelObj.transform.localScale =
            Vector3.one * Mathf.Clamp(distance * ScalePerMetre, MinScale, MaxScale);
    }

    /// <summary>Metres from our own truck to this one — not from the camera, which may be looking out of a mirror.</summary>
    private float DistanceFromPlayer()
    {
        var mine = PlayerState.Truck;
        var from = mine != null ? mine.transform.position : (Camera.main != null ? Camera.main.transform.position : transform.position);
        return Vector3.Distance(from, transform.position);
    }

    /// <summary>"85 m", "1.2 km", "14 km": as a road sign would put it.</summary>
    public static string FormatDistance(float metres)
    {
        if (metres < 1000f) return $"{Mathf.RoundToInt(metres)} {Strings.Get("unit.m")}";
        if (metres < 10000f) return $"{metres / 1000f:0.0} {Strings.Get("unit.km")}";
        return $"{Mathf.RoundToInt(metres / 1000f)} {Strings.Get("unit.km")}";
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

    /// <summary>
    /// The game's own menu face, by preference. Whatever font asset happened to be first in memory
    /// was taken before, and it was not always a face meant to be read at a glance; the menu's
    /// carries the fallbacks that make Cyrillic and CJK names appear, too.
    /// </summary>
    internal static TMP_FontAsset ResolveFont()
    {
        if (_font != null) return _font;

        var candidates = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        if (candidates == null || candidates.Length == 0) return null;

        var names = new System.Text.StringBuilder();
        TMP_FontAsset preferred = null;
        TMP_FontAsset fallback = null;

        foreach (var candidate in candidates)
        {
            if (candidate == null) continue;
            names.Append(candidate.name).Append(", ");

            var name = candidate.name ?? string.Empty;
            if (preferred == null && name.IndexOf("Davis", StringComparison.OrdinalIgnoreCase) >= 0 &&
                name.IndexOf("Bold", StringComparison.OrdinalIgnoreCase) >= 0)
                preferred = candidate;
            else if (fallback == null && name.IndexOf("Davis", StringComparison.OrdinalIgnoreCase) >= 0)
                fallback = candidate;
        }

        _font = preferred ?? fallback ?? candidates[0];
        App.Log.LogInfo($"[Nameplate] Fonts available: {names}using '{_font.name}'.");
        return _font;
    }
}
