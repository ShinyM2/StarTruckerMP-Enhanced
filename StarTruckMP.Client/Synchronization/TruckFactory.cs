using System.Collections.Generic;
using StarTruckMP.Client.Components;
using StarTruckMP.Client.Patches;
using UnityEngine;

namespace StarTruckMP.Client.Synchronization;

/// <summary>
/// Makes the copy of another player's truck out of the game's own NPC cab.
///
/// The prefab comes with the whole NPC: a brain that plans routes, an engine that pushes, the
/// thrusters' effects, a horn, a place on the map. None of that is wanted on a truck whose every
/// position comes off the wire. The brain, the engine and the thrusters are switched off rather
/// than removed, because the customiser and the container slots that paint and load the cab may
/// hold references to them; what only registers the cab with the world — as an obstacle, a point
/// of interest, a fine to pay — is removed, so the world does not count it.
/// </summary>
public static class TruckFactory
{
    /// <summary>Behaviours that drive the NPC, switched off so they never run: their references stay valid.</summary>
    private static readonly string[] DisableTypes =
    [
        "AIVehicle_Truck",
        "AIVehicleEngine",
        "AIVehicleThrusters"
    ];

    /// <summary>Components that only announce the cab to the world, removed so the world forgets it.</summary>
    private static readonly string[] DestroyTypes =
    [
        "NavObstacle",
        "RegisterPointOfInterest",
        "CollisionFineReporter",
        "AITruckHorn",
        "DevCameraTarget"
    ];

    private static bool _described;

    public static GameObject CreatePlayerTruck(int nContainers, Vector3 spawnPos, Quaternion spawnRot)
    {
        var vehicleDef = AIVehicleDef_Patch.Instance;
        if (vehicleDef == null)
        {
            App.Log.LogError("Failed to find AIVehicleDef in scene");
            return null;
        }

        var prefab = vehicleDef.GetPrefab(nContainers);
        if (prefab == null)
        {
            App.Log.LogError($"Cannot get prefab of {nContainers} containers");
            return null;
        }

        var truckGo = Object.Instantiate(prefab, spawnPos, spawnRot);
        var truck = truckGo.GetComponent<AIVehicle_Truck>();
        if (truck == null)
        {
            App.Log.LogError("Failed to get AIVehicle_Truck component from prefab");
            return null;
        }
        truck.name = "PlayerTruck_Remote";

        Describe(truckGo);
        QuietenAi(truckGo);

        truckGo.AddComponent<TruckControllerComponent>();

        return truckGo;
    }

    /// <summary>Stops the NPC in the prefab from behaving like one. Also for a container spawned later.</summary>
    public static void QuietenAi(GameObject root)
    {
        var disabled = 0;
        var removed = 0;

        foreach (var t in root.GetComponentsInChildren<Transform>(includeInactive: true))
        {
            foreach (var typeName in DisableTypes)
            {
                var component = t.GetComponent(typeName);
                if (component == null) continue;

                var behaviour = component.TryCast<Behaviour>();
                if (behaviour != null && behaviour.enabled)
                {
                    behaviour.enabled = false;
                    disabled++;
                }
            }

            foreach (var typeName in DestroyTypes)
            {
                var component = t.GetComponent(typeName);
                if (component == null) continue;

                Object.Destroy(component);
                removed++;
            }
        }

        if (!_described)
            App.Log.LogInfo($"[Truck] Remote cab: {disabled} NPC behaviour(s) switched off, {removed} component(s) removed.");
    }

    /// <summary>
    /// The components on the cab's root, once per session, so what the prefab really carries
    /// can be read off the log rather than guessed.
    /// </summary>
    private static void Describe(GameObject truck)
    {
        if (_described) return;
        _described = true;

        var names = new List<string>();
        foreach (var component in truck.GetComponents<Component>())
        {
            if (component != null) names.Add(component.GetIl2CppType().Name);
        }

        App.Log.LogInfo($"[Truck] Remote cab root components: {string.Join(", ", names)}");
    }
}
