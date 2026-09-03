using MessagePack;

namespace StarTruckMP.Shared.Dto;

[MessagePackObject(true)]
public class PlayerSnapshotDto
{
    public int NetId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Sector { get; set; } = "none";
    public string Livery { get; set; } = string.Empty;
    public TruckAppearance? Appearance { get; set; }
    public TransformDto Player { get; set; } = new();
    public TransformDto Truck { get; set; } = new();
    public int TrailersCount { get; set; }
    public string TrailerLivery { get; set; } = string.Empty;
    public string TrailerCargoTypeId { get; set; } = string.Empty;
}
