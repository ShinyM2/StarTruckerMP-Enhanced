using MessagePack;

namespace StarTruckMP.Shared.Cmd
{
    /// <summary>The switchable state of the sender's truck, sent whenever any of it changes.</summary>
    [MessagePackObject]
    public class TruckStateCmd
    {
        [Key(0)]
        public bool Headlights { get; set; }
    }
}
