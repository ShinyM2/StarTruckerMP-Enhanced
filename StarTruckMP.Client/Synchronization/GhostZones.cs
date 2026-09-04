using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace StarTruckMP.Client.Synchronization;

/// <summary>
/// The places where two trucks in the same spot are a problem rather than a coincidence.
///
/// There are only two kinds, and between them they cover every case:
///
/// <list type="bullet">
/// <item>A <c>WarpGate</c> — everyone entering or leaving a sector converges on the same line,
/// so the approach and the far side are exactly where trucks pile up.</item>
/// <item>A <c>DockingBay</c> — there is one shop, one fuel pump, one drop-off, and any number of
/// players wanting it. The bay's <c>m_amenityType</c> covers the lot: Shop, Repairs, PaintShop,
/// BodyShop, UpgradeShop, ItemDelivery, FuelPump, ParkingBay, VenturePickUp, VentureDropOff —
/// which is why fuel, parking and drop-offs need nothing of their own here.</item>
/// </list>
///
/// The scene is searched rather than the save: bays and gates belong to the loaded sector and
/// change with it. Searching a whole scene for two component types is not free, and doing it
/// every five seconds showed as a regular hitch whenever another truck was near; it now happens
/// when the sector changes and, as a safety net, once a minute.
/// </summary>
internal static class GhostZones
{
    /// <summary>A gate's queue forms along its approach, well before the ring itself.</summary>
    private const float GateRadius = 500f;

    /// <summary>A bay is a parking space, not a region; its crowd stands right on top of it.</summary>
    private const float BayRadius = 150f;

    /// <summary>Bays and gates do not move within a sector; this is only insurance against one loaded late.</summary>
    private const float RescanSeconds = 60f;

    private static readonly List<Vector3> Centres = new();
    private static readonly List<float> Radii = new();

    private static float _nextScan;
    private static bool _described;

    /// <summary>True when a point is inside one of the sector's crowded places.</summary>
    public static bool Contains(Vector3 position)
    {
        Rescan();

        for (var i = 0; i < Centres.Count; i++)
        {
            var radius = Radii[i];
            if ((position - Centres[i]).sqrMagnitude <= radius * radius) return true;
        }

        return false;
    }

    /// <summary>The sector changed: the list is rebuilt the next time anyone asks.</summary>
    public static void Invalidate()
    {
        _nextScan = 0f;
        _described = false;
    }

    private static void Rescan()
    {
        if (Time.unscaledTime < _nextScan) return;
        _nextScan = Time.unscaledTime + RescanSeconds;

        Centres.Clear();
        Radii.Clear();

        var gates = 0;
        var bays = 0;

        foreach (var gate in Object.FindObjectsOfType<WarpGate>())
        {
            if (gate == null) continue;

            Centres.Add(gate.transform.position);
            Radii.Add(GateRadius);
            gates++;
        }

        foreach (var bay in Object.FindObjectsOfType<DockingBay>())
        {
            if (bay == null) continue;

            Centres.Add(bay.transform.position);
            Radii.Add(BayRadius);
            bays++;
        }

        if (_described) return;
        _described = true;

        App.Log.LogInfo($"[Ghost] Zones in this sector: {gates} gates at {GateRadius} m, " +
                        $"{bays} bays at {BayRadius} m.");
    }
}
