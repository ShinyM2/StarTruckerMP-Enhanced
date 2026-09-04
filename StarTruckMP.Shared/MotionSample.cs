using MessagePack;

namespace StarTruckMP.Shared
{
    /// <summary>
    /// One earlier state of a moving thing, carried inside a later position packet.
    ///
    /// Movement goes out unreliably, and on a poor link packets simply vanish. Each packet
    /// therefore repeats the last couple of states before it: a receiver that missed a packet
    /// gets its contents from the next one that does arrive, and the hole in the motion closes
    /// without anyone waiting for a resend. The extra bytes are cheap next to the stutter they
    /// prevent.
    /// </summary>
    [MessagePackObject]
    public struct MotionSample
    {
        [Key(0)]
        public Vector3 Position { get; set; }
        [Key(1)]
        public Quaternion Rotation { get; set; }
        [Key(2)]
        public Vector3 Velocity { get; set; }
        [Key(3)]
        public Vector3 AngVel { get; set; }

        /// <summary>The stream counter the state originally went out with.</summary>
        [Key(4)]
        public uint Seq { get; set; }

        /// <summary>The sender's clock at the time of the reading, same clock as the packet's own.</summary>
        [Key(5)]
        public long SentAt { get; set; }
    }
}
