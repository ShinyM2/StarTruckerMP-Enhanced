using UnityEngine;

namespace StarTruckMP.Client.Synchronization;

/// <summary>
/// Star Trucker recentres the scene around the player as they travel, so that float precision
/// stays usable across a sector. The consequence is that a Unity position is meaningless outside
/// the client that produced it: two trucks parked side by side can hold completely different
/// local coordinates, and the offset between two players grows without bound as they move apart.
///
/// Everything that goes on the wire is therefore expressed in the game's absolute world space
/// and converted back to the receiver's scene space on arrival. Rotations and velocities are
/// unaffected — an origin shift is a pure translation.
/// </summary>
internal static class FloatingOrigin
{
    /// <summary>Local scene coordinates to absolute world coordinates, for sending.</summary>
    public static Vector3 ToWorld(Vector3 scenePosition) =>
        Available ? FloatingOriginManager.ToWorldPosition(scenePosition) : scenePosition;

    /// <summary>Absolute world coordinates to local scene coordinates, for rendering.</summary>
    public static Vector3 ToScene(Vector3 worldPosition) =>
        Available ? FloatingOriginManager.ToScenePosition(worldPosition) : worldPosition;

    /// <summary>
    /// Logs where the scene currently sits in world space. Worth having in the log: if two
    /// players ever report positions that disagree, this is the number that explains it.
    /// </summary>
    public static void LogState(string context)
    {
        if (!Available)
        {
            App.Log.LogInfo($"[Origin] {context}: no floating origin active, positions pass through unchanged.");
            return;
        }

        App.Log.LogInfo($"[Origin] {context}: scene origin in world = {FloatingOriginManager.SceneOriginInWorldSpace}, " +
                        $"ToWorld(0,0,0) = {FloatingOriginManager.ToWorldPosition(Vector3.zero)}");
    }

    /// <summary>
    /// False before a sector has loaded, and in sectors that do not use a floating origin.
    /// Both conversions then pass positions through unchanged, which is correct: with no
    /// origin in play, scene space and world space are the same thing.
    /// </summary>
    private static bool Available
    {
        get
        {
            try { return FloatingOriginManager.Instance != null; }
            catch { return false; }
        }
    }
}
