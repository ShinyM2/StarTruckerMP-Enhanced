using MessagePack;

namespace StarTruckMP.Shared.Cmd
{
    [MessagePackObject]
    public class UpdatePositionCmd
    {
        [Key(0)]
        public Vector3 Position { get; set; }
        [Key(1)]
        public Quaternion Rotation { get; set; }
        [Key(2)]
        public Vector3 Velocity { get; set; }
        [Key(3)]
        public Vector3 AngVel { get; set; }
        [Key(4)]
        public bool IsTruck { get; set; }
        [Key(5)]
        public bool InSeat { get; set; }

        /// <summary>
        /// Per-stream counter, incremented by the sender on every packet. Position updates are
        /// sent unreliably, so they can arrive out of order; without this a late packet
        /// overwrites a newer one and the truck visibly snaps backwards.
        /// </summary>
        [Key(6)]
        public uint Seq { get; set; }

        /// <summary>
        /// The sender's clock when the state was read, in milliseconds of its own monotonic time.
        /// Receivers use the spacing between consecutive values to play the movement back at the
        /// pace it happened, whatever the network did to the packets on the way; the absolute
        /// value means nothing to anyone else.
        /// </summary>
        [Key(7)]
        public long SentAt { get; set; }

        /// <summary>What moved: 0 the player on foot, 1 the truck, 2 a hitched trailer (see <see cref="Index"/>).</summary>
        [Key(8)]
        public byte Kind { get; set; }

        /// <summary>For a trailer, its place in the train: 0 is hitched to the truck, 1 behind that, and so on.</summary>
        [Key(9)]
        public byte Index { get; set; }

        /// <summary>
        /// The states this stream sent just before this one, newest first, so a receiver that
        /// lost the packets they came in still gets them. Empty or null from an older client.
        /// </summary>
        [Key(10)]
        public MotionSample[]? History { get; set; }
    }
}