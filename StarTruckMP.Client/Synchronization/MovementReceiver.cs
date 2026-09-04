using StarTruckMP.Client.Components;
using StarTruckMP.Shared.Movement;

namespace StarTruckMP.Client.Synchronization;

/// <summary>
/// A movement packet from one player, taken in on whatever thread it landed on.
///
/// One packet carries the cab and the trailer together, so the counting happens once here and
/// the states are then handed to the movers that exist: the cab's always, the trailer's once the
/// container has spawned. Ordering, loss and the player's clock are all judged from the
/// packet's counter and stamp; the movers only keep states and sample them.
///
/// Nothing here touches Unity, which is what lets it run on the socket thread.
/// </summary>
internal sealed class MovementReceiver
{
    private readonly RemoteTimeline _timeline;
    private readonly object _lock = new();

    private bool _seqKnown;
    private uint _lastSeq;

    public MovementReceiver(int netId)
    {
        _timeline = RemoteTimeline.For(netId);
    }

    /// <summary>Hands the packet's states to the movers. Either mover may be null. Any thread.</summary>
    public void Take(in MovementUpdate update, TruckControllerComponent cab, TruckControllerComponent trailer)
    {
        var current = update.Current;
        var sentAt = current.SentAt / 1000.0;

        int lostBefore;
        var late = false;

        lock (_lock)
        {
            if (!_seqKnown)
            {
                _seqKnown = true;
                _lastSeq = current.Seq;
                lostBefore = 0;
            }
            else
            {
                var gap = (int)(current.Seq - _lastSeq);
                if (gap <= 0)
                {
                    // Overtaken in flight. The packet that overtook it counted it as lost, but
                    // its state is still worth keeping: playback runs behind live, so a moment
                    // this old may not have been drawn yet.
                    late = true;
                    lostBefore = 0;
                }
                else
                {
                    lostBefore = gap - 1;
                    _lastSeq = current.Seq;
                }
            }
        }

        var inserted = Feed(current, cab, trailer);

        // A late packet is still a measurement of the link: it arrived that much after the
        // fastest one, and the delay should know.
        _timeline.Record(sentAt, update.ArrivedAt);

        if (late)
        {
            if (inserted) LinkStats.NoteRecovered(1);
            return;
        }

        var recovered = 0;
        if (lostBefore > 0)
        {
            for (var i = 0; i < update.HistoryCount; i++)
            {
                if (Feed(update.History[i], cab, trailer)) recovered++;
            }
        }

        _timeline.NoteBurst(lostBefore, update.ArrivedAt);
        LinkStats.NotePacket(lostBefore);
        LinkStats.NoteRecovered(recovered);
    }

    /// <summary>True when the cab took the state as new; the trailer's copy is best effort.</summary>
    private static bool Feed(in MovementEntry entry, TruckControllerComponent cab, TruckControllerComponent trailer)
    {
        var time = entry.SentAt / 1000.0;
        var inserted = false;

        if (entry.HasCab && cab != null) inserted = cab.Insert(entry.Seq, time, entry.Cab);
        if (entry.HasTrailer && trailer != null) trailer.Insert(entry.Seq, time, entry.Trailer);

        return inserted;
    }
}

/// <summary>A movement packet as it came off the wire, with our clock at the instant it landed.</summary>
public readonly struct MovementUpdate
{
    public readonly int NetId;
    public readonly MovementEntry Current;

    /// <summary>The earlier states the packet repeated, newest first; null when it carried none.</summary>
    public readonly MovementEntry[] History;
    public readonly int HistoryCount;

    /// <summary>Seconds on <see cref="NetClock"/> when the packet landed.</summary>
    public readonly double ArrivedAt;

    public MovementUpdate(int netId, in MovementEntry current, MovementEntry[] history, int historyCount, double arrivedAt)
    {
        NetId = netId;
        Current = current;
        History = history;
        HistoryCount = historyCount;
        ArrivedAt = arrivedAt;
    }
}
