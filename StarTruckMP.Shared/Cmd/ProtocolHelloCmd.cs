using System;
using MessagePack;

namespace StarTruckMP.Shared.Cmd;

[MessagePackObject(true)]
public class ProtocolHelloCmd
{
    public ushort ProtocolVersion { get; set; } = NetProtocol.CurrentVersion;

    /// <summary>
    /// 32-byte X25519 ephemeral public key of the client.
    /// Sent in clear during the handshake so the server can derive the shared session key.
    /// </summary>
    public byte[] ClientPublicKey { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Display name of the player, used for nameplates and the player list.
    /// Empty when the platform gives us no name — receivers fall back to "Player #id".
    /// </summary>
    public string Name { get; set; } = string.Empty;
}
