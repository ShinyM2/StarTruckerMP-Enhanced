using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using StarTruckMP.Client.Synchronization;
using UnityEngine;
using UnityEngine.Rendering;

namespace StarTruckMP.Client.Components;

/// <summary>
/// Fades a remote player's truck out when it is standing where you need to be.
///
/// Gates and docking bays are single-file: one line through the ring, one shop, one fuel pump.
/// With several players in a sector they end up in the same metre of space, and a solid truck
/// there is a wall you cannot see past. Inside one of those places
/// (see <see cref="GhostZones"/>) and close enough to matter, another player's truck turns
/// translucent and says so above its name.
///
/// Three things this deliberately does not do. It never touches your own truck, so the view from
/// your cab is exactly as the game drew it. It leaves depth writing on, so a ghosted truck shows
/// its hull and not its interior — a plain alpha blend would let you see straight through the
/// bodywork into the cab and out the far side. And it changes only what it can put back: the
/// original materials are kept and restored the moment the truck leaves the zone.
/// </summary>
public class GhostComponent : MonoBehaviour
{
    /// <summary>How see-through a ghosted truck is. Enough to see past, enough to see it is there.</summary>
    private const float Alpha = 0.35f;

    /// <summary>Near enough to be in the way. Beyond this a truck at the same gate is scenery.</summary>
    private const float Proximity = 200f;

    private const float CheckSeconds = 0.25f;

    private Renderer[] _renderers;
    private Il2CppReferenceArray<Material>[] _solid;
    private Il2CppReferenceArray<Material>[] _ghost;

    private NameplateComponent _nameplate;
    private bool _isGhost;
    private float _nextCheck;

    public GhostComponent(IntPtr ptr) : base(ptr) { }

    private void Start()
    {
        _nameplate = GetComponent<NameplateComponent>();
    }

    private void Update()
    {
        if (Time.unscaledTime < _nextCheck) return;
        _nextCheck = Time.unscaledTime + CheckSeconds;

        try
        {
            Apply(ShouldGhost());
        }
        catch (Exception ex)
        {
            App.Log.LogError($"[Ghost] {ex.Message}");
            enabled = false;
        }
    }

    private void OnDestroy()
    {
        // The renderers may already be gone when the truck is torn down; nothing to put back.
        try { Apply(false); }
        catch (Exception) { }
    }

    private bool ShouldGhost()
    {
        if (!App.GhostMode.Value) return false;

        var mine = PlayerState.Truck;
        if (mine == null) return false;

        var here = transform.position;

        // Close enough to be in the way, and in a place where being in the way matters. Both
        // halves are needed: a truck alongside you in open space is not a problem, and one parked
        // at a bay on the far side of the station is not either.
        if ((here - mine.transform.position).sqrMagnitude > Proximity * Proximity) return false;

        return GhostZones.Contains(here);
    }

    private void Apply(bool ghost)
    {
        if (ghost == _isGhost) return;
        if (ghost && !Build()) return;
        if (_renderers == null) return;

        for (var i = 0; i < _renderers.Length; i++)
        {
            var renderer = _renderers[i];
            if (renderer == null) continue;

            renderer.sharedMaterials = ghost ? _ghost[i] : _solid[i];
        }

        _isGhost = ghost;
        _nameplate?.SetGhost(ghost);
    }

    /// <summary>
    /// The translucent copy of every material on the truck, made once and kept.
    ///
    /// Built on first use rather than at spawn: most trucks never come near a gate or a bay, and
    /// there is no reason to make a second copy of every material on a truck that will never need
    /// one.
    /// </summary>
    private bool Build()
    {
        if (_ghost != null) return true;

        var renderers = GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0) return false;

        _renderers = new Renderer[renderers.Length];
        _solid = new Il2CppReferenceArray<Material>[renderers.Length];
        _ghost = new Il2CppReferenceArray<Material>[renderers.Length];

        for (var i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            _renderers[i] = renderer;

            if (renderer == null) continue;

            var solid = renderer.sharedMaterials;
            _solid[i] = solid;

            var ghost = new Il2CppReferenceArray<Material>(solid.Length);
            for (var m = 0; m < solid.Length; m++)
                ghost[m] = solid[m] == null ? null : Translucent(solid[m]);

            _ghost[i] = ghost;
        }

        return true;
    }

    /// <summary>
    /// A copy of a material, turned translucent without letting the truck's insides show.
    ///
    /// Depth writing is the whole trick. Alpha blending on its own draws every surface the ray
    /// meets, so a see-through truck shows its own far wall, its cab furniture and the seams
    /// between panels all at once, and reads as a mess rather than a ghost. Writing depth keeps
    /// the nearest surface and drops the rest, so what is left is the shape of the hull.
    /// </summary>
    private static Material Translucent(Material solid)
    {
        var ghost = new Material(solid);

        // URP's lit shaders are configured by these, and the game is a URP title.
        Set(ghost, "_Surface", 1f);   // transparent
        Set(ghost, "_Blend", 0f);     // alpha
        Set(ghost, "_ZWrite", 1f);    // the insides stay inside
        Set(ghost, "_AlphaClip", 0f);

        ghost.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        ghost.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);

        ghost.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        ghost.DisableKeyword("_ALPHATEST_ON");
        ghost.DisableKeyword("_ALPHAPREMULTIPLY_ON");

        ghost.SetOverrideTag("RenderType", "Transparent");
        ghost.renderQueue = (int)RenderQueue.Transparent;

        Fade(ghost, "_BaseColor");
        Fade(ghost, "_Color");

        return ghost;
    }

    private static void Set(Material material, string property, float value)
    {
        if (material.HasProperty(property)) material.SetFloat(property, value);
    }

    private static void Fade(Material material, string property)
    {
        if (!material.HasProperty(property)) return;

        var colour = material.GetColor(property);
        colour.a = Alpha;
        material.SetColor(property, colour);
    }
}
