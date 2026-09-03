using MessagePack;
using StarTruckMP.Shared.Dto;

namespace StarTruckMP.Shared.Cmd
{
    [MessagePackObject]
    public class UpdateLiveryCmd
    {
        [Key(0)]
        public string Livery { get; set; } = string.Empty;

        /// <summary>The rest of the truck's look. Its own Livery, when set, agrees with the one above.</summary>
        [Key(1)]
        public TruckAppearance? Appearance { get; set; }
    }
}
