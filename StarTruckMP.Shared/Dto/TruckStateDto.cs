using MessagePack;

namespace StarTruckMP.Shared.Dto
{
    [MessagePackObject(true)]
    public class TruckStateDto
    {
        public int NetId { get; set; }
        public bool Headlights { get; set; }
    }
}
