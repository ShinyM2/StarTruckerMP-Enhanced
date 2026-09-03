using StarTruckMP.Server.Crypto;

namespace StarTruckMP.Server.Entities;

/// <summary>
/// This contains the player information, it shouldn't be constructed.
/// </summary>
public class Player(int id)
{
    public int Id { get; set; } = id;
    public bool HandshakeCompleted { get; set; }
    public ushort ProtocolVersion { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Sector { get; set; } = "none";
    public string Livery { get; set; } = string.Empty;

    /// <summary>The whole look of the truck as the owner last reported it, or null before they did.</summary>
    public StarTruckMP.Shared.Dto.TruckAppearance? Appearance { get; set; }

    /// <summary>
    /// Round-trip time in milliseconds as LiteNetLib last measured it, or -1 before the first
    /// measurement. Only the server can know this for every player, so it is the server that
    /// tells everyone (see <see cref="StarTruckMP.Shared.Dto.PingsDto"/>).
    /// </summary>
    public int Ping { get; set; } = -1;

    /// <summary>
    /// Per-player ChaCha20-Poly1305 session cipher, available after the ECDH handshake.
    /// </summary>
    public SessionCipher? Cipher { get; set; }

    /// <summary>True when the network handshake and key exchange are both complete.</summary>
    public bool EncryptionReady => Cipher is not null && HandshakeCompleted;

    public StarTruckMP.Shared.Vector3 PlayerPosition { get; set; }
    public StarTruckMP.Shared.Quaternion PlayerRotation { get; set; }
    public StarTruckMP.Shared.Vector3 PlayerVelocity { get; set; }
    public StarTruckMP.Shared.Vector3 PlayerAngVel { get; set; }

    public StarTruckMP.Shared.Vector3 TruckPosition { get; set; }
    public StarTruckMP.Shared.Quaternion TruckRotation { get; set; }
    public StarTruckMP.Shared.Vector3 TruckVelocity { get; set; }
    public StarTruckMP.Shared.Vector3 TruckAngVel { get; set; }
    
    public int TrailerCount { get; set; }
    public string TrailerLivery { get; set; } = string.Empty;
    public string TrailerCargoTypeId { get; set; } = string.Empty;
}