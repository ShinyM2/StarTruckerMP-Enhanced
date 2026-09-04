using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using LiteNetLib;
using Microsoft.Extensions.Logging;
using StarTruckMP.Server.Controllers.Services;
using StarTruckMP.Server.Crypto;
using StarTruckMP.Server.Entities;
using StarTruckMP.Server.Server.Services;
using StarTruckMP.Shared;
using StarTruckMP.Shared.Cmd;
using StarTruckMP.Shared.Dto;
using StarTruckMP.Shared.Movement;

namespace StarTruckMP.Server.Server;

public class ServerManager
{
    private const byte ReliableChannel = 0;
    private const byte VoiceChannel = 0;
    private const int MaxNameLength = 32;
    private const int MaxChatLength = 300;
    private const int MaxIncomingPacketsPerTick = 256;
    private const int MaxOutgoingPacketsPerTick = 512;

    /// <summary>
    /// How often the latency table goes out. Slow on purpose: it is a number people glance at,
    /// not one they steer by, and a faster table would only make it flicker.
    /// </summary>
    private const int PingBroadcastMs = 2000;

    private readonly EventBasedNetListener _listener;
    private readonly NetManager _server;
    private readonly ILogger _logger;
    private readonly ServerSettings _settings;
    private readonly PlayerContainer _playerContainer;
    private readonly AuthService _authService;
    private readonly ServerKeyPair _serverKeyPair;
    private readonly ConcurrentQueue<IncomingPacketWorkItem> _incomingPackets = new();
    private readonly ConcurrentQueue<OutgoingSendWorkItem> _outgoingPackets = new();

    /// <summary>Set by the socket thread whenever a packet is queued, so the relay loop wakes at once instead of on its next tick.</summary>
    private readonly AutoResetEvent _work = new(false);

    private long _nextPingBroadcast;

    /// <param name="PacketType">The type byte as it came off the wire: <see cref="PacketType.EncryptedPayload"/> until the frame is opened.</param>
    /// <param name="Encrypted">True when the packet arrived inside an EncryptedPayload frame.</param>
    private readonly record struct IncomingPacketWorkItem(int PeerId, PacketType PacketType, byte[] Raw, byte Channel, DeliveryMethod DeliveryMethod, bool Encrypted);

    /// <param name="IsPlaintext">When true the payload is sent as-is (before encryption is established, or for handshake packets).</param>
    private readonly record struct OutgoingSendWorkItem(byte[] Payload, byte Channel, DeliveryMethod DeliveryMethod, int? TargetPeerId = null, int? ExceptPeerId = null, bool DisconnectTarget = false, bool IsPlaintext = false, string? OnlySector = null);

    public ServerManager(ILogger<ServerManager> logger, ServerSettings settings, PlayerContainer playerContainer, AuthService authService, ServerKeyPair serverKeyPair)
    {
        _listener = new EventBasedNetListener();
        _server = new NetManager(_listener);

        // Packets are queued on the socket thread the instant they land and the relay loop is
        // woken for them, rather than both waiting for the next 15 ms tick. Everything else the
        // library reports still comes through PollEvents on the relay loop.
        _server.UnsyncedReceiveEvent = true;
        _logger = logger;
        _settings = settings;
        _playerContainer = playerContainer;
        _authService = authService;
        _serverKeyPair = serverKeyPair;

        _listener.ConnectionRequestEvent += ListenerOnConnectionRequestEvent;
        _listener.NetworkReceiveEvent += ListenerOnNetworkReceiveEvent;
        _listener.NetworkErrorEvent += ListenerOnNetworkErrorEvent;
        _listener.NetworkLatencyUpdateEvent += ListenerOnNetworkLatencyUpdateEvent;
        _listener.NetworkReceiveUnconnectedEvent += ListenerOnNetworkReceiveUnconnectedEvent;
        _listener.PeerConnectedEvent += ListenerOnPeerConnectedEvent;
        _listener.PeerAddressChangedEvent += ListenerOnPeerAddressChangedEvent;
        _listener.PeerDisconnectedEvent += ListenerOnPeerDisconnectedEvent;
        _listener.DeliveryEvent += ListenerOnDeliveryEvent;
    }

    #region Net Events

    #region Network

    private void ListenerOnNetworkReceiveUnconnectedEvent(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
    {
        if (_logger.IsEnabled(LogLevel.Trace))
            _logger.LogTrace("Received unconnected message from {EndPoint}, type: {MessageType}", remoteEndPoint, messageType);
    }

    /// <summary>
    /// LiteNetLib's own measurement, arriving about once a second per peer. Recorded here and
    /// published by <see cref="BroadcastPings"/>; nothing else in the server asks for it.
    /// </summary>
    private void ListenerOnNetworkLatencyUpdateEvent(NetPeer peer, int latency)
    {
        if (_playerContainer.TryGetPlayer(peer.Id, out var player) && player is not null)
            player.Ping = latency;
    }

    private void ListenerOnNetworkErrorEvent(IPEndPoint endPoint, SocketError socketError)
    {
        if (_logger.IsEnabled(LogLevel.Error))
            _logger.LogError("Network error from {EndPoint}, socket error: {SocketError}", endPoint, socketError);
    }

    /// <summary>
    /// On the socket thread. Nothing is opened here: the peer's cipher is used by the relay loop
    /// to encrypt what goes out, and one thread per cipher keeps that simple. The frame is queued
    /// as it came and the loop is woken to deal with it.
    /// </summary>
    private void ListenerOnNetworkReceiveEvent(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
    {
        try
        {
            var packetType = (PacketType)reader.GetByte();
            var raw = reader.GetRemainingBytes();
            _incomingPackets.Enqueue(new IncomingPacketWorkItem(peer.Id, packetType, raw, channel, deliveryMethod, Encrypted: packetType == PacketType.EncryptedPayload));
            _work.Set();
        }
        finally
        {
            reader.Recycle();
        }
    }

    #endregion

    #region Peer

    private void ListenerOnPeerDisconnectedEvent(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("Peer {PeerId} disconnected from {EndPoint}, reason: {Reason}", peer.Id, peer.Address, disconnectInfo.Reason);

        _playerContainer.RemovePlayer(peer.Id, out var removedPlayer);
        removedPlayer?.Cipher?.Dispose();

        if (removedPlayer is not { HandshakeCompleted: true })
            return;

        var payload = new PlayerDisconnectedDto { NetId = peer.Id };
        QueueSendReliableToAllExcept(payload.Serialize(PacketType.PlayerDisconnected), peer.Id);
    }

    private void ListenerOnPeerAddressChangedEvent(NetPeer peer, IPEndPoint previousAddress)
    {
        if (_logger.IsEnabled(LogLevel.Trace))
            _logger.LogTrace("Peer {PeerId} changed address from {PreviousAddress} to {CurrentAddress}", peer.Id, previousAddress, peer.Address);
    }

    private void ListenerOnPeerConnectedEvent(NetPeer peer)
    {
        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("Peer {PeerId} connected from {EndPoint}", peer.Id, peer.Address);

        _playerContainer.RegisterPlayer(peer.Id);
    }

    #endregion

    #region Other

    private void ListenerOnDeliveryEvent(NetPeer peer, object userData)
    {
        if (_logger.IsEnabled(LogLevel.Trace))
            _logger.LogTrace("Delivered data to peer {PeerId}", peer.Id);
    }

    private void ListenerOnConnectionRequestEvent(ConnectionRequest request)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Connection request from {EndPoint}", request.RemoteEndPoint);

        if (_server.ConnectedPeersCount >= _settings.MaxPlayers)
        {
            request.Reject();
            _logger.LogWarning("Rejected connection from {EndPoint}: the server is full ({MaxPlayers}).", request.RemoteEndPoint, _settings.MaxPlayers);
            return;
        }

        var raw = request.Data.GetRemainingBytesSpan();
        if (!PacketSerializer.TrySplitPacket<ProtocolAuthenticateCmd>(raw, out var packetType, out var authenticate))
        {
            request.RejectForce();
            _logger.LogWarning("Rejected connection from {EndPoint} due to invalid initial packet!", request.RemoteEndPoint);
            return;
        }

        if (packetType != PacketType.ProtocolAuthenticate ||
            !_authService.IsTokenValid(authenticate.Token))
        {
            request.Reject();
            _logger.LogWarning("Rejected connection from {EndPoint} due to invalid token: {token}.", request.RemoteEndPoint, authenticate.Token);
            return;
        }

        var peer = request.Accept();

        if (_logger.IsEnabled(LogLevel.Trace))
            _logger.LogTrace("Accepted connection, peer {peerId}", peer.Id);
    }

    #endregion

    #endregion

    public void Start()
    {
        _server.Start(_settings.Port);
        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("Server started on port {Port}", _settings.Port);
    }

    public void Polling()
    {
        _server.PollEvents();
        ProcessIncomingQueue();
        BroadcastPings();
        ProcessOutgoingQueue();
        ProcessKicks();
    }

    /// <summary>Blocks until a packet is queued or <paramref name="milliseconds"/> pass, whichever is first.</summary>
    public void WaitForWork(int milliseconds) => _work.WaitOne(milliseconds);

    /// <summary>
    /// Sends everyone the whole latency table, including their own entry.
    ///
    /// One packet for the lot rather than one per player: the table is a handful of small
    /// integers, and every client shows it as a column anyway. Players still in the handshake are
    /// left out — they have no cipher yet, so their entry could not be delivered.
    /// </summary>
    private void BroadcastPings()
    {
        var now = Environment.TickCount64;
        if (now < _nextPingBroadcast) return;
        _nextPingBroadcast = now + PingBroadcastMs;

        var players = _playerContainer.SnapshotPlayers();
        var entries = new List<PingEntryDto>(players.Length);

        foreach (var player in players)
        {
            if (!player.EncryptionReady) continue;
            entries.Add(new PingEntryDto { NetId = player.Id, Ping = player.Ping });
        }

        if (entries.Count == 0) return;

        var payload = new PingsDto { Players = entries.ToArray() };
        QueueSendToAllUnreliable(payload.Serialize(PacketType.Pings));
    }

    public void Stop()
    {
        ClearQueues();
        _server.Stop();
    }

    private void ProcessIncomingQueue()
    {
        var processed = 0;
        while (processed < MaxIncomingPacketsPerTick && _incomingPackets.TryDequeue(out var packet))
        {
            ProcessIncomingPacket(packet);
            processed++;
        }
    }

    private void ProcessIncomingPacket(IncomingPacketWorkItem packet)
    {
        if (packet.Encrypted && !TryOpen(ref packet))
            return;

        var player = GetKnownPlayerOrNull(packet.PeerId, packet.PacketType);
        if (player is null)
            return;

        if (packet.PacketType == PacketType.ProtocolHello)
        {
            HandleProtocolHello(packet.PeerId, player, packet.Raw);
            return;
        }

        if (!player.HandshakeCompleted)
        {
            _logger.LogWarning("Ignoring packet {PacketType} from peer {PeerId} before protocol handshake", packet.PacketType, packet.PeerId);
            return;
        }

        // Once a cipher exists, everything has to come through it. A plaintext packet at this
        // point is either a client that lost its key or someone else on the wire.
        if (!packet.Encrypted && player.Cipher is not null)
        {
            _logger.LogWarning("Ignoring plaintext packet {PacketType} from peer {PeerId} after encryption was established", packet.PacketType, packet.PeerId);
            return;
        }

        // Everything a client sends is relayed to others, so a client that floods is a client
        // that floods everyone. Over the limit its packets are simply not relayed.
        var limiter = packet.PacketType switch
        {
            PacketType.UpdatePosition => player.MovementRate,
            PacketType.Voice => player.VoiceRate,
            PacketType.Chat => player.ChatRate,
            _ => player.StateRate
        };

        if (!limiter.Allow())
        {
            NoteRefused(packet.PeerId, player, packet.PacketType);
            return;
        }

        try
        {
            switch (packet.PacketType)
            {
                case PacketType.UpdatePosition:
                    HandleMovement(packet.PeerId, player, packet.Raw, packet.Channel, packet.DeliveryMethod);
                    break;
                case PacketType.Voice:
                    HandleVoice(packet.PeerId, player, packet.Raw);
                    break;
                case PacketType.UpdateSector:
                    HandleUpdateSector(packet.PeerId, player, packet.Raw);
                    break;
                case PacketType.Chat:
                    HandleChat(packet.PeerId, player, packet.Raw);
                    break;
                case PacketType.UpdateLivery:
                    HandleUpdateLivery(packet.PeerId, player, packet.Raw);
                    break;
                case PacketType.UpdateTrailer:
                    HandleUpdateTrailer(packet.PeerId, player, packet.Raw);
                    break;
                case PacketType.TruckState:
                    HandleTruckState(packet.PeerId, player, packet.Raw);
                    break;
                default:
                    _logger.LogWarning("Unhandled packet type {PacketType} from peer {PeerId}", packet.PacketType, packet.PeerId);
                    break;
            }
        }
        catch (Exception ex)
        {
            // A packet that does not parse is one client's problem, not the relay loop's.
            if (_logger.IsEnabled(LogLevel.Warning))
                _logger.LogWarning("Dropped a malformed {PacketType} packet from peer {PeerId}: {Message}", packet.PacketType, packet.PeerId, ex.Message);
        }
    }

    /// <summary>One line per few seconds about a client over its limit, never one per packet.</summary>
    private void NoteRefused(int peerId, Player player, PacketType type)
    {
        player.Refused++;
        var now = Environment.TickCount64;
        if (now - player.RefusedLoggedAt < 5000) return;

        player.RefusedLoggedAt = now;
        _logger.LogWarning("Peer {PeerId} is over its rate limit; {Count} packet(s) not relayed, last {PacketType}", peerId, player.Refused, type);
        player.Refused = 0;
    }

    /// <summary>A client-supplied string cut down to something safe to store and relay.</summary>
    private static string Bound(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= max ? value : value[..max];
    }

    private const int MaxIdLength = 128;
    private const int MaxSectorLength = 64;
    private const int MaxLabelLength = 32;
    private const int MaxColours = 16;
    private const int MaxVoiceFrameBytes = 1500;

    /// <summary>Opens an EncryptedPayload frame in place: the real type and body replace the frame.</summary>
    private bool TryOpen(ref IncomingPacketWorkItem packet)
    {
        if (!_playerContainer.TryGetPlayer(packet.PeerId, out var player) || player?.Cipher is null)
        {
            if (_logger.IsEnabled(LogLevel.Warning))
                _logger.LogWarning("Received EncryptedPayload from peer {PeerId} before cipher is ready — dropping.", packet.PeerId);
            return false;
        }

        byte[] plaintext;
        try
        {
            plaintext = player.Cipher.Decrypt(packet.Raw);
        }
        catch (CryptographicException ex)
        {
            if (_logger.IsEnabled(LogLevel.Warning))
                _logger.LogWarning("Decryption failed for peer {PeerId}: {Message}", packet.PeerId, ex.Message);
            return false;
        }

        // plaintext = [1-byte real PacketType][body...]
        if (plaintext.Length < 1) return false;
        packet = packet with { PacketType = (PacketType)plaintext[0], Raw = plaintext[1..] };
        return true;
    }

    private void HandleVoice(int packetPeerId, Player player, byte[] packetRaw)
    {
        if (packetRaw.Length == 0 || packetRaw.Length > MaxVoiceFrameBytes) return;

        var dto = new VoiceDto { NetId = packetPeerId, OpusData = packetRaw };
        QueueSendToAllExcept(dto.Serialize(PacketType.Voice), packetPeerId, VoiceChannel, DeliveryMethod.Unreliable);
    }

    private void ProcessOutgoingQueue()
    {
        var processed = 0;
        while (processed < MaxOutgoingPacketsPerTick && _outgoingPackets.TryDequeue(out var send))
        {
            if (send.TargetPeerId.HasValue)
            {
                var targetPeer = _server.GetPeerById(send.TargetPeerId.Value);
                if (targetPeer is not null)
                {
                    var wirePayload = send.IsPlaintext
                        ? send.Payload
                        : TryEncryptForPeer(send.TargetPeerId.Value, send.Payload);

                    if (wirePayload is not null)
                        targetPeer.Send(wirePayload, send.DeliveryMethod);

                    if (send.DisconnectTarget)
                        targetPeer.Disconnect();
                }

                processed++;
                continue;
            }

            // Broadcast-except: iterate known players and encrypt individually.
            var knownPlayers = _playerContainer.SnapshotPlayers();
            foreach (var knownPlayer in knownPlayers)
            {
                if (send.ExceptPeerId.HasValue && knownPlayer.Id == send.ExceptPeerId.Value)
                    continue;

                if (send.OnlySector is not null && knownPlayer.Sector != send.OnlySector)
                    continue;

                var peer = _server.GetPeerById(knownPlayer.Id);
                if (peer is null) continue;

                var wirePayload = send.IsPlaintext
                    ? send.Payload
                    : TryEncryptForPeer(knownPlayer.Id, send.Payload);

                if (wirePayload is not null)
                    peer.Send(wirePayload, send.DeliveryMethod);
            }

            processed++;
        }

        // Send() only queues on the peer; the library's thread flushes on its own tick, up to
        // 15 ms later. Wake it so a relayed position leaves the moment it was relayed.
        if (processed > 0) _server.TriggerUpdate();
    }

    /// <summary>
    /// Wraps <paramref name="plainPayload"/> in an EncryptedPayload frame for the given peer.
    /// Returns null if the peer has no cipher yet (skips sending silently).
    /// </summary>
    private byte[]? TryEncryptForPeer(int peerId, byte[] plainPayload)
    {
        if (!_playerContainer.TryGetPlayer(peerId, out var player) || player?.Cipher is null)
            return null;

        var encrypted = player.Cipher.Encrypt(plainPayload);

        // Frame: [1-byte PacketType.EncryptedPayload][encrypted frame]
        var frame = new byte[1 + encrypted.Length];
        frame[0] = (byte)PacketType.EncryptedPayload;
        encrypted.CopyTo(frame, 1);
        return frame;
    }

    private void ClearQueues()
    {
        while (_incomingPackets.TryDequeue(out _)) { }
        while (_outgoingPackets.TryDequeue(out _)) { }
    }

    /// <summary>
    /// Reduces a client-supplied display name to one short, single-line string.
    /// Returns empty when nothing usable is left, so clients fall back to "Player #id".
    /// </summary>
    private static string SanitizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var cleaned = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return cleaned.Length <= MaxNameLength ? cleaned : cleaned[..MaxNameLength];
    }

    private static PlayerSnapshotDto ToSnapshot(Player player)
    {
        return new PlayerSnapshotDto
        {
            NetId = player.Id,
            Name = player.Name,
            Sector = player.Sector,
            Livery = player.Livery,
            Appearance = player.Appearance,
            Truck = new TransformDto
            {
                Position = player.TruckPosition,
                Rotation = player.TruckRotation,
                Velocity = player.TruckVelocity,
                AngVel = player.TruckAngVel
            },
            TrailerLivery = player.TrailerLivery,
            TrailersCount = player.TrailerCount,
            TrailerCargoTypeId = player.TrailerCargoTypeId,
            Headlights = player.Headlights
        };
    }

    private void HandleTruckState(int peerId, Player player, byte[] raw)
    {
        var state = PacketSerializer.Deserialize<TruckStateCmd>(raw);
        player.Headlights = state.Headlights;

        var update = new TruckStateDto { NetId = peerId, Headlights = state.Headlights };
        QueueSendReliableToAllExcept(update.Serialize(PacketType.TruckState), peerId);
    }

    // ---------------------------------------------------------------------------------------
    // For the admin page
    // ---------------------------------------------------------------------------------------

    public sealed record ChatRecord(DateTime At, int NetId, string Name, string Message, bool SectorOnly);

    private const int ChatHistoryLimit = 200;
    private readonly Queue<ChatRecord> _chatHistory = new();
    private readonly object _chatLock = new();
    private readonly ConcurrentQueue<int> _kicks = new();

    /// <summary>The last couple of hundred chat lines, oldest first.</summary>
    public ChatRecord[] RecentChat()
    {
        lock (_chatLock) return _chatHistory.ToArray();
    }

    /// <summary>Drops a player on the next poll; the disconnect has to happen on the network thread.</summary>
    public void Kick(int peerId) => _kicks.Enqueue(peerId);

    private void ProcessKicks()
    {
        while (_kicks.TryDequeue(out var peerId))
        {
            var peer = _server.GetPeerById(peerId);
            if (peer is null) continue;

            _logger.LogInformation("Peer {PeerId} kicked from the admin page", peerId);
            peer.Disconnect();
        }
    }

    private void RememberChat(int netId, string name, string message, bool sectorOnly)
    {
        lock (_chatLock)
        {
            _chatHistory.Enqueue(new ChatRecord(DateTime.UtcNow, netId, name, message, sectorOnly));
            while (_chatHistory.Count > ChatHistoryLimit) _chatHistory.Dequeue();
        }
    }

    private void HandleProtocolHello(int peerId, Player player, byte[] raw)
    {
        var hello = PacketSerializer.Deserialize<ProtocolHelloCmd>(raw);
        if (!IsVersionSupported(hello.ProtocolVersion))
        {
            var mismatch = new ProtocolMismatchDto
            {
                ClientVersion = hello.ProtocolVersion,
                MinSupportedVersion = NetProtocol.MinSupportedVersion,
                ServerVersion = NetProtocol.CurrentVersion
            };

            // ProtocolMismatch is sent before the cipher exists — always plaintext.
            QueueSendToPeer(mismatch.Serialize(PacketType.ProtocolMismatch), peerId, ReliableChannel, DeliveryMethod.ReliableOrdered, disconnectAfterSend: true, plaintext: true);
            return;
        }

        // The name is cosmetic and comes straight from the client, so bound it here;
        // an empty one falls back to "Player #id" on the receiving side.
        player.Name = SanitizeName(hello.Name);

        if (player.HandshakeCompleted)
            return;

        // ECDH key exchange
        if (hello.ClientPublicKey is not { Length: > 0 })
        {
            _logger.LogWarning("Peer {PeerId} sent ProtocolHello without a ClientPublicKey — rejecting.", peerId);
            _server.GetPeerById(peerId)?.Disconnect();
            return;
        }

        try
        {
            var sessionKey = _serverKeyPair.DeriveSessionKey(hello.ClientPublicKey);
            player.Cipher = new SessionCipher(sessionKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("ECDH key derivation failed for peer {PeerId}: {Message}", peerId, ex.Message);
            _server.GetPeerById(peerId)?.Disconnect();
            return;
        }

        player.HandshakeCompleted = true;
        player.ProtocolVersion = hello.ProtocolVersion;

        // ProtocolWelcome is sent in plaintext - the client will use its own local key derivation.
        // From this point on all other packets are encrypted.
        var welcome = new ProtocolWelcomeDto
        {
            NetId = peerId,
            ProtocolVersion = NetProtocol.CurrentVersion
        };
        QueueSendToPeer(welcome.Serialize(PacketType.ProtocolWelcome), peerId, ReliableChannel, DeliveryMethod.ReliableOrdered, plaintext: true);

        var existingPlayers = _playerContainer
            .SnapshotPlayers()
            .Where(x => x.Id != peerId && x.HandshakeCompleted)
            .Select(ToSnapshot)
            .ToArray();

        var sync = new SyncPlayersDto { Players = existingPlayers };
        QueueSendToPeer(sync.Serialize(PacketType.SyncPlayers), peerId, ReliableChannel, DeliveryMethod.ReliableOrdered);

        var connectedPayload = ToSnapshot(player);
        QueueSendReliableToAllExcept(connectedPayload.Serialize(PacketType.PlayerConnected), peerId);
    }

    private static bool IsVersionSupported(ushort version)
    {
        return version >= NetProtocol.MinSupportedVersion && version <= NetProtocol.CurrentVersion;
    }

    private Player? GetKnownPlayerOrNull(int peerId, PacketType packetType)
    {
        if (_playerContainer.TryGetPlayer(peerId, out var player) && player is not null)
            return player;

        _logger.LogWarning("Ignoring packet {PacketType} from unknown peer {PeerId}", packetType, peerId);
        return null;
    }

    /// <summary>
    /// A movement packet: the cab is remembered for the snapshot a late joiner gets, and the
    /// client's bytes go out behind the sender's net id exactly as they came. Nothing is
    /// re-serialised, and a packet that does not parse is dropped here rather than relayed.
    /// </summary>
    private void HandleMovement(int peerId, Player player, byte[] raw, byte channel, DeliveryMethod deliveryMethod)
    {
        // Parsed whole, not just the head: a packet the receivers would reject is not worth
        // relaying to a sector of them, each of which would log it.
        Span<MovementEntry> history = stackalloc MovementEntry[MovementCodec.MaxHistory];
        if (!MovementCodec.TryRead(raw, out var current, history, out _))
        {
            NoteRefused(peerId, player, PacketType.UpdatePosition);
            return;
        }

        // A trailer's position is relayed, not remembered: a late joiner rebuilds the train from
        // the trailer update and the stream that follows.
        if (current.HasCab)
        {
            player.TruckPosition = current.Cab.Position;
            player.TruckRotation = current.Cab.Rotation;
            player.TruckVelocity = current.Cab.Velocity;
            player.TruckAngVel = current.Cab.AngVel;
        }

        // Only players sharing this sector can see the sender, so there is no point paying for
        // the encryption and bandwidth of shipping their movement to everyone else.
        QueueSendToSectorExcept(MovementCodec.WriteRelayed(PacketType.UpdatePosition, peerId, raw), peerId, player.Sector, channel, deliveryMethod);
    }

    /// <summary>
    /// Relays a chat line. The sender's name is taken from the server's own record rather than
    /// the packet, so nobody can post under someone else's name, and the text is bounded here
    /// because it is echoed to every other client.
    /// </summary>
    private void HandleChat(int peerId, Player player, byte[] raw)
    {
        var chat = PacketSerializer.Deserialize<ChatCmd>(raw);

        var text = new string((chat.Message ?? string.Empty).Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (text.Length == 0) return;
        if (text.Length > MaxChatLength) text = text[..MaxChatLength];

        RememberChat(peerId, string.IsNullOrWhiteSpace(player.Name) ? $"Player #{peerId}" : player.Name, text, chat.SectorOnly);

        var payload = new ChatDto
        {
            NetId = peerId,
            Name = string.IsNullOrWhiteSpace(player.Name) ? $"Player #{peerId}" : player.Name,
            Message = text,
            SectorOnly = chat.SectorOnly
        }.Serialize(PacketType.Chat);

        // The sender gets their own line back too, so every client renders one consistent log.
        if (chat.SectorOnly)
            QueueSendToSector(payload, player.Sector);
        else
            QueueSendToAll(payload);

        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("[Chat] {Name}: {Message}", player.Name, text);
    }

    private void HandleUpdateSector(int peerId, Player player, byte[] raw)
    {
        var sectorData = PacketSerializer.Deserialize<UpdateSectorCmd>(raw);
        player.Sector = string.IsNullOrWhiteSpace(sectorData.Sector) ? "none" : Bound(sectorData.Sector, MaxSectorLength);

        var update = new UpdateSectorDto { NetId = peerId, Sector = player.Sector };
        QueueSendReliableToAllExcept(update.Serialize(PacketType.UpdateSector), peerId);

        if (_logger.IsEnabled(LogLevel.Trace))
            _logger.LogTrace("Peer {peerId} updated sector to '{sector}'", peerId, sectorData.Sector);
    }

    private void HandleUpdateLivery(int peerId, Player player, byte[] raw)
    {
        var liveryData = PacketSerializer.Deserialize<UpdateLiveryCmd>(raw);
        player.Livery = Bound(liveryData.Livery, MaxIdLength);
        player.Appearance = BoundAppearance(liveryData.Appearance);

        var update = new UpdateLiveryDto { NetId = peerId, Livery = player.Livery, Appearance = player.Appearance };
        QueueSendReliableToAllExcept(update.Serialize(PacketType.UpdateLivery), peerId);

        if (_logger.IsEnabled(LogLevel.Trace))
            _logger.LogTrace("Peer {peerId} updated livery to '{livery}'", peerId, liveryData.Livery);
    }

    private void HandleUpdateTrailer(int packetPeerId, Player player, byte[] packetRaw)
    {
        var trailerData = PacketSerializer.Deserialize<UpdateTrailerCmd>(packetRaw);
        player.TrailerCount = Math.Clamp(trailerData.TrailerCount, 0, 8);
        player.TrailerLivery = Bound(trailerData.LiveryId, MaxIdLength);
        player.TrailerCargoTypeId = Bound(trailerData.CargoTypeId, MaxIdLength);

        var update = new UpdateTrailerDto
        {
            NetId = packetPeerId,
            TrailerCount = player.TrailerCount,
            LiveryId = player.TrailerLivery,
            CargoTypeId = player.TrailerCargoTypeId
        };
        QueueSendReliableToAllExcept(update.Serialize(PacketType.UpdateTrailer), packetPeerId);

        if (_logger.IsEnabled(LogLevel.Trace))
            _logger.LogTrace("Peer {peerId} updated trailer, count {count} with livery {livery}", packetPeerId, trailerData.TrailerCount, trailerData.LiveryId);
    }

    /// <summary>The look of a truck with every id cut to a sane length; it is stored for the session and relayed to everyone.</summary>
    private static TruckAppearance? BoundAppearance(TruckAppearance? a)
    {
        if (a is null) return null;

        return new TruckAppearance
        {
            Livery = Bound(a.Livery, MaxIdLength),
            BaseMaterial = Bound(a.BaseMaterial, MaxIdLength),
            Colors = a.Colors is { Length: > MaxColours } ? a.Colors[..MaxColours] : a.Colors ?? [],
            Exhaust = Bound(a.Exhaust, MaxIdLength),
            Grill = Bound(a.Grill, MaxIdLength),
            Ornament = Bound(a.Ornament, MaxIdLength),
            Sensors = Bound(a.Sensors, MaxIdLength),
            LicensePlate = Bound(a.LicensePlate, MaxIdLength),
            LicensePlateLabel = Bound(a.LicensePlateLabel, MaxLabelLength),
            WindowDecal = Bound(a.WindowDecal, MaxIdLength),
            MaglockTopper = Bound(a.MaglockTopper, MaxIdLength),
            Damage = float.IsFinite(a.Damage) ? Math.Clamp(a.Damage, 0f, 1f) : 0f,
            Dirt = float.IsFinite(a.Dirt) ? Math.Clamp(a.Dirt, 0f, 1f) : 0f
        };
    }

    private void QueueSendReliableToAllExcept(byte[] payload, int exceptPeerId)
    {
        QueueSendToAllExcept(payload, exceptPeerId, ReliableChannel, DeliveryMethod.ReliableOrdered);
    }

    private void QueueSendToPeer(byte[] payload, int targetPeerId, byte channel, DeliveryMethod deliveryMethod, bool disconnectAfterSend = false, bool plaintext = false)
    {
        _outgoingPackets.Enqueue(new OutgoingSendWorkItem(payload, channel, deliveryMethod, TargetPeerId: targetPeerId, DisconnectTarget: disconnectAfterSend, IsPlaintext: plaintext));
    }

    private void QueueSendToAllExcept(byte[] payload, int exceptPeerId, byte channel, DeliveryMethod deliveryMethod)
    {
        _outgoingPackets.Enqueue(new OutgoingSendWorkItem(payload, channel, deliveryMethod, ExceptPeerId: exceptPeerId));
    }

    /// <summary>
    /// A broadcast that is allowed to go missing. For state the next packet replaces outright,
    /// redelivering a stale copy is worse than skipping it.
    /// </summary>
    private void QueueSendToAllUnreliable(byte[] payload)
    {
        _outgoingPackets.Enqueue(new OutgoingSendWorkItem(payload, ReliableChannel, DeliveryMethod.Unreliable));
    }

    private void QueueSendToAll(byte[] payload)
    {
        _outgoingPackets.Enqueue(new OutgoingSendWorkItem(payload, ReliableChannel, DeliveryMethod.ReliableOrdered));
    }

    private void QueueSendToSector(byte[] payload, string sector)
    {
        _outgoingPackets.Enqueue(new OutgoingSendWorkItem(payload, ReliableChannel, DeliveryMethod.ReliableOrdered, OnlySector: sector));
    }

    /// <summary>
    /// Broadcast limited to the players currently in <paramref name="sector"/>. Used for movement,
    /// which nobody outside that sector renders anyway.
    /// </summary>
    private void QueueSendToSectorExcept(byte[] payload, int exceptPeerId, string sector, byte channel, DeliveryMethod deliveryMethod)
    {
        _outgoingPackets.Enqueue(new OutgoingSendWorkItem(payload, channel, deliveryMethod, ExceptPeerId: exceptPeerId, OnlySector: sector));
    }
}