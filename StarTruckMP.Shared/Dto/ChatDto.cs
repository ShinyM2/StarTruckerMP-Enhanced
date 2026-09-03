using MessagePack;

namespace StarTruckMP.Shared.Dto
{
    [MessagePackObject]
    public class ChatDto
    {
        [Key(0)]
        public int NetId { get; set; }

        /// <summary>Sender's display name, resolved by the server so clients cannot spoof it.</summary>
        [Key(1)]
        public string Name { get; set; } = string.Empty;

        [Key(2)]
        public string Message { get; set; } = string.Empty;

        [Key(3)]
        public bool SectorOnly { get; set; }
    }
}
