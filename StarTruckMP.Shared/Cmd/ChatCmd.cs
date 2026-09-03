using MessagePack;

namespace StarTruckMP.Shared.Cmd
{
    [MessagePackObject]
    public class ChatCmd
    {
        [Key(0)]
        public string Message { get; set; } = string.Empty;

        /// <summary>True to reach only the sender's sector, false for everyone on the server.</summary>
        [Key(1)]
        public bool SectorOnly { get; set; }
    }
}
