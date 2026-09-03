using MessagePack;

namespace StarTruckMP.Shared.Dto
{
    [MessagePackObject]
    public class UpdatePositionDto
    {
        [Key(0)]
        public int NetId { get; set; }
        [Key(1)]
        public Vector3 Position { get; set; }
        [Key(2)]
        public Quaternion Rotation { get; set; }
        [Key(3)]
        public Vector3 Velocity { get; set; }
        [Key(4)]
        public Vector3 AngVel { get; set; }
        [Key(5)]
        public bool IsTruck { get; set; }
        [Key(6)]
        public bool InSeat { get; set; }

        /// <summary>Sender's per-stream counter, relayed untouched so receivers can drop stale updates.</summary>
        [Key(7)]
        public uint Seq { get; set; }

        /// <summary>The sender's own clock at the time of the reading, relayed untouched; see the command.</summary>
        [Key(8)]
        public long SentAt { get; set; }

        /// <summary>0 player, 1 truck, 2 trailer; see the command.</summary>
        [Key(9)]
        public byte Kind { get; set; }

        [Key(10)]
        public byte Index { get; set; }
    }
}