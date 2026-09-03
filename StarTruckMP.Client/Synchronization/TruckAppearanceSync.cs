using System;
using System.Globalization;
using System.Text;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using StarTruckMP.Shared.Dto;
using UnityEngine;

namespace StarTruckMP.Client.Synchronization;

/// <summary>
/// Reads the look of the player's own truck and paints it onto a remote copy.
///
/// The game keeps the owner's choices in <c>CustomizationState</c> — livery, base material, the
/// six colours, the bolt-on parts — and the wear in <c>DamageState</c>. A remote truck is an NPC
/// cab, and an NPC cab carries a livery applier that accepts a livery, a material override and
/// colour overrides, plus a damage and dirt level; that covers the paint completely. The bolt-on
/// parts need a <c>CustomizationApplier</c>, which the NPC cab may or may not carry — this logs
/// which, once, rather than guessing.
/// </summary>
internal static class TruckAppearanceSync
{
    private static bool _readFailureLogged;
    private static bool _noApplierLogged;
    private static bool _noPartsLogged;
    private static bool _partsFailureLogged;

    // ---------------------------------------------------------------------------------------
    // Reading our own truck
    // ---------------------------------------------------------------------------------------

    /// <summary>The player's truck as others should see it, or null when the game has nothing to read yet.</summary>
    public static TruckAppearance Read(GameObject truck)
    {
        if (truck == null) return null;

        try
        {
            var appearance = new TruckAppearance();

            var exterior = truck.GetComponentInChildren<LiveryAndDamageApplierTruckExterior>(true);
            var customisation = truck.GetComponentInChildren<CustomizationState>(true);
            var damage = truck.GetComponentInChildren<DamageState>(true);

            var state = customisation != null ? customisation.CurrentCustomizationState : null;

            if (state != null)
            {
                appearance.Livery = state.equippedLivery ?? string.Empty;
                appearance.BaseMaterial = state.equippedMaterial ?? string.Empty;
                appearance.Colors = Pack(state.equippedColors);
                appearance.Exhaust = state.equippedExhaust ?? string.Empty;
                appearance.Grill = state.equippedGrill ?? string.Empty;
                appearance.Ornament = state.equippedOrnament ?? string.Empty;
                appearance.Sensors = state.equippedSensors ?? string.Empty;
                appearance.LicensePlate = state.equippedLicensePlate ?? string.Empty;
                appearance.LicensePlateLabel = state.licensePlateLabel ?? string.Empty;
                appearance.WindowDecal = state.equippedWindowDecal ?? string.Empty;
                appearance.MaglockTopper = state.equippedMaglockTopper ?? string.Empty;
            }

            // The applier knows what is actually on the hull, which is the better answer for the
            // livery itself when the state has not been restored yet.
            if (string.IsNullOrEmpty(appearance.Livery) && exterior != null)
                appearance.Livery = exterior.AppliedLiveryId ?? exterior.CurrentLiveryId ?? string.Empty;

            if (damage != null)
            {
                appearance.Damage = Mathf.Clamp01(damage.OverallDamagePercent);
                appearance.Dirt = Mathf.Clamp01(damage.OverallDirtPercent);
            }
            else if (exterior != null)
            {
                appearance.Damage = Mathf.Clamp01(exterior.DamageRevealPercent);
            }

            if (string.IsNullOrEmpty(appearance.Livery)) return null;
            return appearance;
        }
        catch (Exception ex)
        {
            if (!_readFailureLogged)
            {
                _readFailureLogged = true;
                App.Log.LogWarning($"[Appearance] Could not read the truck's customisation; only the livery id will be shared. {ex.Message}");
            }

            return ReadLiveryOnly(truck);
        }
    }

    private static TruckAppearance ReadLiveryOnly(GameObject truck)
    {
        var info = Utils.ExtractTruckInfo(truck);
        if (string.IsNullOrEmpty(info.LiveryId)) return null;
        return new TruckAppearance { Livery = info.LiveryId };
    }

    /// <summary>
    /// A string that changes whenever anything visible changes, so the truck is only re-sent when
    /// it needs to be. Wear is rounded to a few percent: a fresh scratch is not worth a packet.
    /// </summary>
    public static string Signature(TruckAppearance a)
    {
        if (a == null) return string.Empty;

        var sb = new StringBuilder();
        sb.Append(a.Livery).Append('|').Append(a.BaseMaterial).Append('|');
        foreach (var colour in a.Colors) sb.Append(colour.ToString("X8")).Append(',');
        sb.Append('|').Append(a.Exhaust).Append('|').Append(a.Grill).Append('|').Append(a.Ornament)
          .Append('|').Append(a.Sensors).Append('|').Append(a.LicensePlate).Append('|').Append(a.LicensePlateLabel)
          .Append('|').Append(a.WindowDecal).Append('|').Append(a.MaglockTopper)
          .Append('|').Append(Mathf.RoundToInt(a.Damage * 25f).ToString(CultureInfo.InvariantCulture))
          .Append('|').Append(Mathf.RoundToInt(a.Dirt * 25f).ToString(CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    // The game keeps its colours as packed ints already (EquipColor takes "colorAsInt"), so they
    // travel as they are and come back the same; the mod never has to know the byte order.
    private static uint[] Pack(Il2CppStructArray<int> colours)
    {
        if (colours == null || colours.Length == 0) return Array.Empty<uint>();

        var packed = new uint[colours.Length];
        for (var i = 0; i < colours.Length; i++)
            packed[i] = unchecked((uint)colours[i]);

        return packed;
    }

    private static Il2CppStructArray<int> Unpack(uint[] packed)
    {
        var colours = new Il2CppStructArray<int>(packed.Length);
        for (var i = 0; i < packed.Length; i++)
            colours[i] = unchecked((int)packed[i]);

        return colours;
    }

    // ---------------------------------------------------------------------------------------
    // Painting a remote truck
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Applies what is known about a player's truck to their remote copy. With only a livery id
    /// — an older client, or a state not yet read — the livery alone is applied, as before.
    /// </summary>
    public static void Apply(GameObject remoteTruck, string livery, TruckAppearance appearance)
    {
        if (remoteTruck == null) return;

        var liveryId = appearance != null && !string.IsNullOrEmpty(appearance.Livery) ? appearance.Livery : livery;
        if (string.IsNullOrEmpty(liveryId) && appearance == null) return;

        try
        {
            var customiser = remoteTruck.GetComponentInChildren<AIVehicleCustomiser>(true);
            var applier = customiser != null ? customiser.m_cabLiveryApplier : null;
            if (applier == null) applier = remoteTruck.GetComponentInChildren<LiveryAndDamageApplierBase>(true);

            if (applier == null)
            {
                if (!_noApplierLogged)
                {
                    _noApplierLogged = true;
                    App.Log.LogWarning("[Appearance] The remote truck has no livery applier; it stays unpainted.");
                }

                return;
            }

            // Overrides first: the livery loads asynchronously and reads them when it lands.
            if (appearance != null)
            {
                if (appearance.Colors != null && appearance.Colors.Length > 0)
                    applier.SetColorOverrides(Unpack(appearance.Colors));

                if (!string.IsNullOrEmpty(appearance.BaseMaterial))
                    applier.SetBaseMaterialOverride(appearance.BaseMaterial);
            }

            var damage = appearance?.Damage ?? 0f;

            if (!string.IsNullOrEmpty(liveryId))
            {
                if (customiser != null) customiser.AssignCabLivery(liveryId, damage);
                else applier.LoadAndApplyLiveryById(liveryId);
            }

            applier.SetOverallDamagePercent(damage, true);

            var exterior = applier.TryCast<LiveryAndDamageApplierTruckExterior>();
            if (exterior != null && appearance != null)
                exterior.SetOverallDirtPercent(appearance.Dirt, true);

            if (appearance != null) ApplyParts(remoteTruck, appearance);

            App.Log.LogInfo($"[Appearance] Applied livery '{liveryId}'" +
                            (appearance != null
                                ? $", material '{appearance.BaseMaterial}', {appearance.Colors?.Length ?? 0} colour(s), damage {appearance.Damage:0.00}, dirt {appearance.Dirt:0.00}"
                                : " (livery only)"));
        }
        catch (Exception ex)
        {
            App.Log.LogError($"[Appearance] Applying the truck's look failed: {ex.Message}");
        }
    }

    /// <summary>The bolt-on parts, through the game's own customisation applier if the NPC cab has one.</summary>
    private static void ApplyParts(GameObject remoteTruck, TruckAppearance a)
    {
        var parts = remoteTruck.GetComponentInChildren<CustomizationApplier>(true);
        if (parts == null)
        {
            if (!_noPartsLogged)
            {
                _noPartsLogged = true;
                App.Log.LogInfo("[Appearance] The remote truck has no CustomizationApplier; exhausts, grills, ornaments and plates are not shown on other players' trucks.");
            }

            return;
        }

        try
        {
            Part(parts, CustomizationDef.Type.Exhausts, a.Exhaust);
            Part(parts, CustomizationDef.Type.HoodGrill, a.Grill);
            Part(parts, CustomizationDef.Type.HoodOrnament, a.Ornament);
            Part(parts, CustomizationDef.Type.HoodSensors, a.Sensors);
            Part(parts, CustomizationDef.Type.LicensePlate, a.LicensePlate, a.LicensePlateLabel);
            Part(parts, CustomizationDef.Type.WindscreenDecal, a.WindowDecal);
            Part(parts, CustomizationDef.Type.MaglockTopper, a.MaglockTopper);
        }
        catch (Exception ex)
        {
            if (!_partsFailureLogged)
            {
                _partsFailureLogged = true;
                App.Log.LogWarning($"[Appearance] Applying bolt-on parts failed; the paint still applies. {ex.Message}");
            }
        }
    }

    private static void Part(CustomizationApplier parts, CustomizationDef.Type type, string id, string content = "")
    {
        if (string.IsNullOrEmpty(id)) return;
        parts.LoadAndApplyCustomization(CustomizationSlotKey.SingleIndexSlot(type), id, content ?? string.Empty);
    }
}
