using MessagePack;

namespace StarTruckMP.Shared.Dto;

/// <summary>
/// Everyone's round-trip time, as the server measures it.
///
/// The server is the only party that can answer this: LiteNetLib keeps a latency estimate per
/// peer, and a client has no way to time another client it never talks to directly. So the whole
/// table is broadcast on a slow timer rather than each client measuring its own.
///
/// It is sent unreliably. A dropped table costs one refresh of a number that changes on its own
/// anyway, and a queue of stale latencies is worth less than the next measurement.
/// </summary>
[MessagePackObject(true)]
public class PingsDto
{
    public PingEntryDto[] Players { get; set; } = [];
}

[MessagePackObject(true)]
public class PingEntryDto
{
    public int NetId { get; set; }

    /// <summary>Milliseconds, as LiteNetLib reports the peer's latency. Negative until measured.</summary>
    public int Ping { get; set; } = -1;
}
