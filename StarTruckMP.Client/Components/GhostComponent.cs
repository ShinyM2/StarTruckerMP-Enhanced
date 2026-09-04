using System;
using System.Collections.Generic;
using System.Text;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using StarTruckMP.Client.Synchronization;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace StarTruckMP.Client.Components;

/// <summary>
/// Fades a remote player's truck out when it is standing where you need to be.
///
/// Gates and docking bays are single-file: one line through the ring, one shop, one fuel pump.
/// With several players in a sector they end up in the same metre of space, and a solid truck
/// there is a wall you cannot see past. Inside one of those places
/// (see <see cref="GhostZones"/>) and close enough to matter, another player's truck, and the
/// trailer behind it, turn into a pale translucent hologram and say so above the name; a line
/// at the top of your own screen says the same while any truck around you is drawn that way.
///
/// The look does not depend on the truck's own shaders. The first version copied each hull
/// material and asked it to go transparent through the properties a URP surface has, and on the
/// game's hull shaders that changed nothing anybody could see. The ghost is now drawn with one
/// of the engine's own always-present unlit shaders — the hull's texture through a pale tint at
/// a third of the alpha — so it is see-through on every truck, whatever it is painted with.
///
/// Two things this deliberately does not do. It never touches your own truck, so the view from
/// your cab is exactly as the game drew it. And it changes only what it can put back: the
/// materials found on the truck at the moment it fades are kept and restored the moment it
/// leaves the zone, so a livery that landed in between is not lost.
/// </summary>
public class GhostComponent : MonoBehaviour
{
    /// <summary>How see-through a ghosted truck is. Enough to see past, enough to see it is there.</summary>
    private const float Alpha = 0.32f;

    /// <summary>A pale blue-white, like a projection: unmistakably a ghost, not a rendering fault.</summary>
    private static readonly Color Tint = new(0.72f, 0.90f, 1f, Alpha);

    /// <summary>Near enough to be in the way. Beyond this a truck at the same gate is scenery.</summary>
    private const float Proximity = 200f;

    private const float CheckSeconds = 0.25f;

    /// <summary>
    /// Shaders that ship in every Unity build and blend by alpha whatever the project does, in
    /// the order they are tried. Sprites/Default is unlit, double-sided and premultiplied, which
    /// is exactly the flat see-through look wanted here; the others are fallbacks.
    /// </summary>
    private static readonly string[] ShaderNames =
    {
        "Sprites/Default",
        "Universal Render Pipeline/Unlit",
        "UI/Default"
    };

    private static Shader _shader;
    private static bool _shaderResolved;
    private static bool _hullsDescribed;

    /// <summary>How many remote bodies are drawn as ghosts right now, for the notice on the player's screen. Game thread.</summary>
    public static int ActiveCount { get; private set; }

    private Renderer[] _renderers;
    private Il2CppReferenceArray<Material>[] _solid;

    /// <summary>The ghost copy of each hull material, by the material's instance id, made once per truck and kept.</summary>
    private readonly Dictionary<int, Material> _ghosts = new();

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

        // The translucent copies are assets of our own making and outlive the truck unless
        // destroyed here: one set per truck that ever came near a gate, for the whole session.
        try
        {
            foreach (var ghost in _ghosts.Values)
            {
                if (ghost != null) Destroy(ghost);
            }
        }
        catch (Exception) { }
        finally
        {
            _ghosts.Clear();
        }
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

        if (ghost)
        {
            if (!Fade()) return;
        }
        else
        {
            Restore();
        }

        _isGhost = ghost;
        ActiveCount = Math.Max(0, ActiveCount + (ghost ? 1 : -1));
        _nameplate?.SetGhost(ghost);
    }

    /// <summary>
    /// Swaps every hull material for its ghost. The materials are read now rather than at spawn:
    /// the livery lands asynchronously, and a set captured before it did would paint the truck
    /// back to bare metal when the ghost lifts.
    /// </summary>
    private bool Fade()
    {
        var found = GetComponentsInChildren<Renderer>(true);
        if (found == null || found.Length == 0) return false;

        var renderers = new List<Renderer>(found.Length);
        var solids = new List<Il2CppReferenceArray<Material>>(found.Length);

        foreach (var renderer in found)
        {
            if (renderer == null || !IsHull(renderer)) continue;

            var solid = renderer.sharedMaterials;
            if (solid == null || solid.Length == 0) continue;

            renderers.Add(renderer);
            solids.Add(solid);
        }

        if (renderers.Count == 0) return false;

        DescribeHulls(solids);

        for (var i = 0; i < renderers.Count; i++)
        {
            var solid = solids[i];
            var ghost = new Il2CppReferenceArray<Material>(solid.Length);
            for (var m = 0; m < solid.Length; m++)
                ghost[m] = solid[m] == null ? null : GhostOf(solid[m]);

            renderers[i].sharedMaterials = ghost;
        }

        _renderers = renderers.ToArray();
        _solid = solids.ToArray();
        return true;
    }

    private void Restore()
    {
        if (_renderers == null) return;

        for (var i = 0; i < _renderers.Length; i++)
        {
            var renderer = _renderers[i];
            if (renderer == null) continue;

            try { renderer.sharedMaterials = _solid[i]; }
            catch (Exception) { /* the renderer went with its truck */ }
        }

        _renderers = null;
        _solid = null;
    }

    /// <summary>
    /// Whether a renderer is part of the truck's body: a mesh, plain or skinned. Particles,
    /// trails and lines are effects, not hull, and the nameplate is a mesh under the same root
    /// that must keep its font material.
    /// </summary>
    private static bool IsHull(Renderer renderer)
    {
        var kind = renderer.GetIl2CppType().Name;
        if (kind != "MeshRenderer" && kind != "SkinnedMeshRenderer") return false;

        var go = renderer.gameObject;
        if (go.name == "Nameplate") return false;
        if (go.GetComponent<TMP_Text>() != null) return false;

        return true;
    }

    private Material GhostOf(Material solid)
    {
        var id = solid.GetInstanceID();
        if (_ghosts.TryGetValue(id, out var ghost) && ghost != null) return ghost;

        ghost = Translucent(solid);
        _ghosts[id] = ghost;
        return ghost;
    }

    /// <summary>
    /// The ghost of a hull material: its texture, through the tint, on a shader that blends.
    ///
    /// Depth is deliberately not written: the engine's unlit shaders do not, and a hull drawn
    /// that way shows its panels through one another, which is the look of a projection rather
    /// than of glass. With the tint and the notice, nobody takes it for a fault.
    /// </summary>
    private static Material Translucent(Material solid)
    {
        var shader = ResolveShader();
        if (shader == null) return TranslucentCopy(solid);

        var ghost = new Material(shader) { name = solid.name + " (ghost)" };

        try
        {
            var texture = solid.mainTexture;
            if (texture != null) ghost.mainTexture = texture;
        }
        catch (Exception) { /* a material without a main texture is drawn as plain tint */ }

        ghost.color = Tint;

        // URP's unlit shader is configured by these when it is the one in use; the others ignore them.
        Set(ghost, "_Surface", 1f);
        Set(ghost, "_Blend", 0f);
        Set(ghost, "_ZWrite", 0f);
        Set(ghost, "_AlphaClip", 0f);
        if (ghost.HasProperty("_SrcBlend")) ghost.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        if (ghost.HasProperty("_DstBlend")) ghost.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        ghost.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        Fade(ghost, "_BaseColor");

        ghost.renderQueue = (int)RenderQueue.Transparent;
        return ghost;
    }

    /// <summary>The previous approach, kept for a build with none of the engine shaders: a copy asked to blend.</summary>
    private static Material TranslucentCopy(Material solid)
    {
        var ghost = new Material(solid) { name = solid.name + " (ghost)" };

        Set(ghost, "_Surface", 1f);
        Set(ghost, "_Blend", 0f);
        Set(ghost, "_ZWrite", 1f);
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

    /// <summary>The first of the engine's blending shaders this build carries, found once.</summary>
    private static Shader ResolveShader()
    {
        if (_shaderResolved) return _shader;
        _shaderResolved = true;

        foreach (var name in ShaderNames)
        {
            try
            {
                var shader = Shader.Find(name);
                if (shader == null || !shader.isSupported) continue;

                _shader = shader;
                App.Log.LogInfo($"[Ghost] Ghost trucks are drawn with '{name}'.");
                return shader;
            }
            catch (Exception ex)
            {
                App.Log.LogWarning($"[Ghost] Could not look up '{name}': {ex.Message}");
            }
        }

        App.Log.LogWarning("[Ghost] None of the engine's blending shaders is in this build; ghost trucks are copies of their own materials asked to blend.");
        return null;
    }

    /// <summary>The hull's shaders, once, so the next change to the look can be made against what the game really uses.</summary>
    private static void DescribeHulls(List<Il2CppReferenceArray<Material>> solids)
    {
        if (_hullsDescribed) return;
        _hullsDescribed = true;

        try
        {
            var names = new HashSet<string>();
            foreach (var set in solids)
            {
                foreach (var material in set)
                {
                    if (material == null) continue;
                    var shader = material.shader;
                    names.Add(shader != null ? shader.name : "(no shader)");
                }
            }

            var text = new StringBuilder();
            foreach (var name in names) text.Append(name).Append(", ");
            App.Log.LogInfo($"[Ghost] Hull shaders on a remote truck: {text}");
        }
        catch (Exception ex)
        {
            App.Log.LogInfo($"[Ghost] Could not list the hull shaders: {ex.Message}");
        }
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
