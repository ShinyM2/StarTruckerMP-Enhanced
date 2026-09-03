namespace StarTruckMP.Shared
{
    public enum PacketType : byte
    {
        SyncPlayers,
        PlayerConnected,
        PlayerDisconnected,
        UpdatePosition,
        UpdateLivery,
        UpdateSector,
        ProtocolHello,
        ProtocolWelcome,
        ProtocolMismatch,
        ProtocolAuthenticate,
        UpdateTrailer,
        Voice,
        EncryptedPayload,
        Chat,

        /// <summary>The server's table of everyone's latency, broadcast on a slow timer.</summary>
        Pings
    }
}