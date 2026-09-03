using MessagePack;

namespace StarTruckMP.Shared.Dto
{
    /// <summary>
    /// Everything about a truck's outside that another player can see: the livery and the base
    /// material it is painted with, the colours the owner picked, the bolt-on parts, and how
    /// battered and dirty it is. Ids are the game's own customisation ids; colours are the packed
    /// ints the game itself stores, passed through untouched.
    /// </summary>
    [MessagePackObject(true)]
    public class TruckAppearance
    {
        public string Livery { get; set; } = string.Empty;
        public string BaseMaterial { get; set; } = string.Empty;

        /// <summary>The game's own packed colour ints, in its order: Base, Primary, Secondary, Tertiary, Chrome, Chassis. Empty when the livery's own colours apply.</summary>
        public uint[] Colors { get; set; } = [];

        public string Exhaust { get; set; } = string.Empty;
        public string Grill { get; set; } = string.Empty;
        public string Ornament { get; set; } = string.Empty;
        public string Sensors { get; set; } = string.Empty;
        public string LicensePlate { get; set; } = string.Empty;
        public string LicensePlateLabel { get; set; } = string.Empty;
        public string WindowDecal { get; set; } = string.Empty;
        public string MaglockTopper { get; set; } = string.Empty;

        /// <summary>0..1, how much of the damage layer shows.</summary>
        public float Damage { get; set; }

        /// <summary>0..1, how much of the dirt layer shows.</summary>
        public float Dirt { get; set; }
    }
}
