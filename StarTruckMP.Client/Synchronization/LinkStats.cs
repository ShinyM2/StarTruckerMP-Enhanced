using System.Threading;
using UnityEngine;

namespace StarTruckMP.Client.Synchronization;

/// <summary>
/// Counts what happened to the movement packets, for the health readout on the cab monitor.
///
/// Counted where the packets are taken in, on whatever thread that is, and published to
/// <see cref="MultiplayerState"/> every few seconds from the game thread. The loss figure is
/// what the wire lost: a state that a later packet carried in again still counts as a lost
/// packet, because the number is meant to say how the link is, not how well it is being hidden.
/// </summary>
internal static class LinkStats
{
    private const float WindowSeconds = 5f;

    private static long _received;
    private static long _lost;
    private static long _recovered;
    private static float _nextPublish;

    /// <summary>A packet landed; <paramref name="lostBefore"/> of the packets before it on its stream never did.</summary>
    public static void NotePacket(int lostBefore)
    {
        Interlocked.Increment(ref _received);
        if (lostBefore > 0) Interlocked.Add(ref _lost, lostBefore);
    }

    /// <summary>States that were lost with their own packet and came in again inside a later one.</summary>
    public static void NoteRecovered(int count)
    {
        if (count > 0) Interlocked.Add(ref _recovered, count);
    }

    /// <summary>Folds the window's counts into the monitor's figures. Game thread.</summary>
    public static void Publish()
    {
        if (Time.unscaledTime < _nextPublish) return;
        _nextPublish = Time.unscaledTime + WindowSeconds;

        var received = Interlocked.Exchange(ref _received, 0);
        var lost = Interlocked.Exchange(ref _lost, 0);
        var recovered = Interlocked.Exchange(ref _recovered, 0);

        var total = received + lost;
        MultiplayerState.PacketLossPercent = total > 0 ? Mathf.RoundToInt(100f * lost / total) : 0;
        MultiplayerState.RecoveredPercent = lost > 0 ? Mathf.RoundToInt(100f * recovered / lost) : 0;
    }
}
