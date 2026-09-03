using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using LiteNetLib;
using LiteNetLib.Utils;
using StarTruckMP.Client.Components;
using StarTruckMP.Client.Crypto;
using StarTruckMP.Shared;
using StarTruckMP.Shared.Cmd;
using StarTruckMP.Shared.Dto;

namespace StarTruckMP.Client.Synchronization;

public class Network
{
    public static event Action<int> OnConnected;
    public static event Action OnDisconnected;
    public static event Action<UpdatePositionDto> OnPlayerPositionUpdate;
    public static event Action<UpdateLiveryDto> OnTruckLiveryUpdate;
    public static event Action<UpdateSectorDto> OnPlayerSectorUpdate;
    public static event Action<int> OnPlayerDisconnected;
    public static event Action<UpdateTrailerDto> OnTrailerUpdate;
    public static event Action<VoiceDto> OnVoiceReceived;
    public static event Action<int, string> OnPlayerNameUpdate;
    public static event Action<ChatDto> OnChatReceived;
    public static event Action<PingsDto> OnPingsUpdate;

    private static bool _isInitialized;
    private static NetManager _client;
    private static NetPeer _server;
    private static bool _handshakeCompleted = false;
    private static int _netId = -1;

    // ── Encryption ────────────────────────────────────────────────────────────
    /// <summary>Ephemeral ECDH key pair generated before connecting. Disposed after session key derivation.</summary>
    private static ECDiffieHellman? _ephemeralKey;
    /// <summary>ChaCha20-Poly1305 session cipher, ready after <see cref="HandleWelcome"/>.</summary>
    private static SessionCipher? _sessionCipher;

    public static int NetId => _netId;

    /// <summary>
    /// Why the server could not be reached at the last authentication attempt, or null once it
    /// answered. Written by the auth thread, read by the menu so "Connect" never looks like it
    /// did nothing.
    /// </summary>
    public static string AuthProblem;

    /// <summary>True once a save is loaded and the connection loop is running; before that the mod only authenticates.</summary>
    public static bool InWorld => _isInitialized;

    /// <summary>
    /// This should only run once, on plugin startup.
    /// </summary>
    /// <returns></returns>
    public static void SetupConnection()
    {
        GameEventsComponent.ArrivedAtSector += _ =>
        {
            if (_isInitialized) return;

            // Loaded into the map, start server connection
            _isInitialized = true;

            Plugin.StartAttachedThread(Polling);
        };
    }

    public static void SendServerMessage<T>(T data, PacketType packetType)
    {
        try
        {
            var deliveryMethod = packetType switch
            {
                PacketType.UpdatePosition => DeliveryMethod.Unreliable,
                PacketType.ProtocolHello  => DeliveryMethod.ReliableOrdered,
                _                         => DeliveryMethod.ReliableSequenced
            };

            var server = _server;
            if (server == null)
            {
                if (packetType != PacketType.UpdatePosition)
                    App.Log.LogWarning($"Packet:{packetType} dropped, not connected.");
                return;
            }

            var serialized = data.Serialize(packetType);

            // ProtocolHello is sent before the cipher exists - always plaintext.
            if (packetType == PacketType.ProtocolHello || _sessionCipher is null)
            {
                server.Send(serialized, deliveryMethod);
            }
            else
            {
                server.Send(BuildEncryptedPacket(serialized), deliveryMethod);
            }

            if (packetType != PacketType.UpdatePosition)
                App.Log.LogInfo($"Packet:{packetType} out {serialized.Length} bytes");
        }
        catch (Exception e)
        {
            App.Log.LogError("Failed to send message to server:");
            App.Log.LogError(e);
        }
    }

    /// <summary>Do not use this for normal messages.</summary>
    public static void SendOpusFrame(byte[] opusFrame)
    {
        var server = _server;
        if (_sessionCipher is null || server == null)
        {
            // Cipher not ready yet - drop the frame; it will be missed but that's acceptable.
            return;
        }

        // Build plaintext: [1-byte PacketType][opus bytes]
        var plain = new byte[1 + opusFrame.Length];
        plain[0] = (byte)PacketType.Voice;
        opusFrame.CopyTo(plain, 1);

        server.Send(BuildEncryptedPacket(plain), DeliveryMethod.Unreliable);
    }

    /// <summary>
    /// Wraps <paramref name="serializedPacket"/> (which already contains the 1-byte PacketType header)
    /// in an EncryptedPayload frame using the current session cipher.
    /// </summary>
    private static byte[] BuildEncryptedPacket(byte[] serializedPacket)
    {
        var encrypted = _sessionCipher!.Encrypt(serializedPacket);
        var frame = new byte[1 + encrypted.Length];
        frame[0] = (byte)PacketType.EncryptedPayload;
        encrypted.CopyTo(frame, 1);
        return frame;
    }

    /// <summary>Seconds to wait for the handshake before giving up on an attempt.</summary>
    private const int HandshakeTimeoutMs = 8000;

    /// <summary>Pause between connection attempts, so a server that is down is retried quietly.</summary>
    private const int RetryDelayMs = 5000;

    /// <summary>
    /// Owns the connection for the whole session: waits for a session token, connects, and
    /// keeps retrying. The order in which the server and the players start therefore does not
    /// matter — a client that loads into the world before the server exists simply keeps
    /// trying until it is there, and a dropped or rejected connection comes back on its own.
    /// </summary>
    private static void Polling()
    {
        var attempt = 0;

        while (true)
        {
            // Authentication runs on its own thread and retries by itself; there is nothing
            // useful to send until it has produced a token.
            if (string.IsNullOrEmpty(PlayerState.Token))
            {
                Thread.Sleep(500);
                continue;
            }

            // Read on every attempt, not once: the address can be changed from the menu while
            // the game is running, and a host name is as valid as a bare IP.
            var address = (App.ServerAddress.Value ?? string.Empty).Trim();
            if (address.Length == 0 || !int.TryParse(App.ServerPort.Value, out var port) || port <= 0 || port > 65535)
            {
                App.Log.LogError($"Invalid server address '{App.ServerAddress.Value}:{App.ServerPort.Value}', retrying in {RetryDelayMs / 1000}s.");
                Thread.Sleep(RetryDelayMs);
                continue;
            }

            attempt++;

            try
            {
                var listener = new EventBasedNetListener();
                _client = new NetManager(listener);

                listener.PeerConnectedEvent += ListenerOnPeerConnectedEvent;
                listener.PeerDisconnectedEvent += ListenerOnPeerDisconnectedEvent;
                listener.NetworkErrorEvent += ListenerOnNetworkErrorEvent;
                listener.NetworkReceiveEvent += ListenerOnNetworkReceiveEvent;

                _client.Start();

                // Generate ephemeral ECDH key pair before connecting.
                _ephemeralKey?.Dispose();
                _ephemeralKey = ClientKeyExchange.GenerateEphemeralKeyPair();

                var data = new ProtocolAuthenticateCmd
                {
                    Token = PlayerState.Token
                };

                _server = _client.Connect(address, port, NetDataWriter.FromBytes(data.Serialize(PacketType.ProtocolAuthenticate), true));
            }
            catch (Exception e)
            {
                App.Log.LogWarning($"Connection attempt {attempt} failed to start: {e.Message}");
                Cleanup();
                Thread.Sleep(RetryDelayMs);
                continue;
            }

            var deadline = Environment.TickCount64 + HandshakeTimeoutMs;
            while (!_handshakeCompleted && Environment.TickCount64 < deadline)
            {
                _client.PollEvents();
                Thread.Sleep(50);
            }

            if (!_handshakeCompleted)
            {
                // Either nobody is listening yet, or the token was refused — which is what a
                // server restart looks like, since it keeps its tokens in memory. Throw the
                // token away so the auth thread fetches a fresh one before the next attempt.
                App.Log.LogWarning($"Connection attempt {attempt} got no handshake, retrying in {RetryDelayMs / 1000}s.");
                Cleanup();
                PlayerState.Token = "";
                App.ReAuthenticate?.Invoke();
                Thread.Sleep(RetryDelayMs);
                continue;
            }

            attempt = 0;

            while ((_server.ConnectionState & ConnectionState.Connected) == ConnectionState.Connected)
            {
                _client.PollEvents();
                Thread.Sleep(50);
            }

            App.Log.LogWarning("Lost connection to server (" + _server.ConnectionState + "), reconnecting.");
            Cleanup();
            Thread.Sleep(RetryDelayMs);
        }
    }

    /// <summary>
    /// Drops the current connection. The polling loop owns reconnection, so it picks the new
    /// address up on its next pass — used after the server address is changed from the menu.
    /// </summary>
    public static void Reconnect()
    {
        App.Log.LogInfo("Reconnect requested.");
        try { _server?.Disconnect(); } catch { /* already gone */ }
    }

    /// <summary>Tears down the current attempt so the next one starts from a clean slate.</summary>
    private static void Cleanup()
    {
        _handshakeCompleted = false;
        _netId = -1;

        try { _client?.Stop(); } catch { /* already down */ }
        _client = null;
        _server = null;

        _sessionCipher?.Dispose();
        _sessionCipher = null;
    }

    private static void ListenerOnNetworkReceiveEvent(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
    {
        try
        {
            var firstByte = reader.GetByte();
            var packetType = (PacketType)firstByte;

            byte[] raw;

            if (packetType == PacketType.EncryptedPayload)
            {
                if (_sessionCipher is null)
                {
                    App.Log.LogError("Received EncryptedPayload but session cipher is not ready - dropping.");
                    return;
                }

                var encFrame = reader.GetRemainingBytes();
                try
                {
                    var plaintext = _sessionCipher.Decrypt(encFrame);
                    if (plaintext.Length < 1) return;
                    packetType = (PacketType)plaintext[0];
                    raw = plaintext[1..];
                }
                catch (CryptographicException ex)
                {
                    App.Log.LogError("Decryption failed: " + ex.Message);
                    return;
                }
            }
            else
            {
                raw = reader.GetRemainingBytes();
            }

            if (packetType is not PacketType.ProtocolWelcome and not PacketType.ProtocolMismatch &&
                !_handshakeCompleted)
                return;

            if (packetType is not PacketType.UpdatePosition and not PacketType.Voice)
                App.Log.LogInfo($"Packet:{packetType} in {raw.Length} bytes");

            switch (packetType)
            {
                // ordered by most common to less common
                case PacketType.UpdatePosition:
                    HandlePositionUpdate(raw.Deserialize<UpdatePositionDto>());
                    break;
                case PacketType.Voice:
                    HandleVoice(raw);
                    break;
                case PacketType.UpdateTrailer:
                    HandleTrailerUpdate(raw.Deserialize<UpdateTrailerDto>());
                    break;
                case PacketType.UpdateLivery:
                    HandleUpdateLivery(raw.Deserialize<UpdateLiveryDto>());
                    break;
                case PacketType.UpdateSector:
                    HandleSectorUpdate(raw.Deserialize<UpdateSectorDto>());
                    break;
                case PacketType.Chat:
                    OnChatReceived?.Invoke(raw.Deserialize<ChatDto>());
                    break;
                case PacketType.Pings:
                    OnPingsUpdate?.Invoke(raw.Deserialize<PingsDto>());
                    break;
                case PacketType.SyncPlayers:
                    HandleSyncPlayers(raw.Deserialize<SyncPlayersDto>());
                    break;
                case PacketType.PlayerConnected:
                    HandlePlayerConnected(raw.Deserialize<PlayerSnapshotDto>());
                    break;
                case PacketType.PlayerDisconnected:
                    HandlePlayerDisconnected(raw.Deserialize<PlayerDisconnectedDto>());
                    break;
                case PacketType.ProtocolWelcome:
                    HandleWelcome(raw.Deserialize<ProtocolWelcomeDto>());
                    break;
                case PacketType.ProtocolMismatch:
                    HandleMismatch(raw.Deserialize<ProtocolMismatchDto>());
                    break;
                default:
                    App.Log.LogError($"Received not handled packet type {packetType}");
                    break;
            }
        }
        catch (Exception ex)
        {
            App.Log.LogError("Error handling network message:");
            App.Log.LogError(ex);
        }
        finally
        {
            reader.Recycle();
        }
    }

    private static void HandleVoice(byte[] raw)
    {
        var dto = raw.Deserialize<VoiceDto>();
        OnVoiceReceived?.Invoke(dto);
    }

    private static void HandlePlayerDisconnected(PlayerDisconnectedDto disconnected)
    {
        OnPlayerDisconnected?.Invoke(disconnected.NetId);
    }

    private static void HandlePlayerConnected(PlayerSnapshotDto snapshot)
    {
        if (snapshot.NetId == _netId)
        {
            App.Log.LogInfo("Received own player snapshot, ignoring.");
            return;
        }

        OnPlayerNameUpdate?.Invoke(snapshot.NetId, snapshot.Name);

        OnPlayerSectorUpdate?.Invoke(new UpdateSectorDto
        {
            NetId = snapshot.NetId,
            Sector = snapshot.Sector
        });

        OnPlayerPositionUpdate?.Invoke(new UpdatePositionDto
        {
            NetId = snapshot.NetId,
            Position = snapshot.Player.Position,
            Rotation = snapshot.Player.Rotation,
            Velocity = snapshot.Player.Velocity,
            AngVel = snapshot.Player.AngVel,
            IsTruck = false,
            InSeat = false
        });

        OnTruckLiveryUpdate?.Invoke(new UpdateLiveryDto
        {
            NetId = snapshot.NetId,
            Livery = snapshot.Livery,
            Appearance = snapshot.Appearance
        });

        OnTrailerUpdate?.Invoke(new UpdateTrailerDto
        {
            NetId = snapshot.NetId,
            TrailerCount = snapshot.TrailersCount,
            LiveryId = snapshot.TrailerLivery,
            CargoTypeId = snapshot.TrailerCargoTypeId
        });
    }

    private static void HandleSyncPlayers(SyncPlayersDto syncPlayers)
    {
        foreach (var snapshot in syncPlayers.Players) HandlePlayerConnected(snapshot);
    }

    private static void HandleSectorUpdate(UpdateSectorDto sector)
    {
        OnPlayerSectorUpdate?.Invoke(sector);
    }

    private static void HandleUpdateLivery(UpdateLiveryDto livery)
    {
        OnTruckLiveryUpdate?.Invoke(livery);
    }

    private static void HandlePositionUpdate(UpdatePositionDto position)
    {
        OnPlayerPositionUpdate?.Invoke(position);
    }

    private static void HandleMismatch(ProtocolMismatchDto mismatch)
    {
        App.Log.LogError($"Protocol mismatch with server ({mismatch.ServerVersion}). Please update your client (min {mismatch.MinSupportedVersion}).");
        _handshakeCompleted = false;
        _server?.Disconnect();
    }

    private static void HandleWelcome(ProtocolWelcomeDto welcome)
    {
        // Derive session key from the server's public key received during HTTPS auth.
        if (_ephemeralKey is not null && PlayerState.ServerPublicKey is { Length: > 0 })
        {
            try
            {
                var sessionKey = ClientKeyExchange.DeriveSessionKey(_ephemeralKey, PlayerState.ServerPublicKey);
                _sessionCipher?.Dispose();
                _sessionCipher = new SessionCipher(sessionKey);
                App.Log.LogInfo("[Crypto] Session cipher established.");
            }
            catch (Exception ex)
            {
                App.Log.LogError("[Crypto] Failed to derive session key: " + ex.Message);
            }
            finally
            {
                // The server's key is kept: it is fixed for the lifetime of the server process,
                // and a reconnect after a dropped link reuses the same token without going
                // through HTTPS auth again. Clearing it here left every reconnect without a
                // cipher, so the server's encrypted packets were dropped and nobody appeared.
                _ephemeralKey.Dispose();
                _ephemeralKey = null;
            }
        }
        else
        {
            App.Log.LogWarning("[Crypto] No ephemeral key or server public key available - UDP traffic will be unencrypted.");
        }

        _handshakeCompleted = true;
        _netId = welcome.NetId;
        OnConnected?.Invoke(_netId);
        App.Log.LogInfo("Handshake completed with server. NetId: " + _netId);
    }

    private static void HandleTrailerUpdate(UpdateTrailerDto trailer)
    {
        OnTrailerUpdate?.Invoke(trailer);
    }

    private static void ListenerOnNetworkErrorEvent(IPEndPoint endPoint, SocketError socketError)
    {
        App.Log.LogError($"Network error: {socketError}");
    }

    private static void ListenerOnPeerDisconnectedEvent(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        App.Log.LogInfo($"Disconnected from server {peer.Address}:{peer.Port} ({disconnectInfo.Reason})");
        _handshakeCompleted = false;
        OnDisconnected?.Invoke();
    }

    private static void ListenerOnPeerConnectedEvent(NetPeer peer)
    {
        _handshakeCompleted = false;
        _sessionCipher?.Dispose();
        _sessionCipher = null;

        var hello = new ProtocolHelloCmd
        {
            ProtocolVersion = NetProtocol.CurrentVersion,
            ClientPublicKey = _ephemeralKey is not null
                ? ClientKeyExchange.ExportPublicKeyBytes(_ephemeralKey)
                : Array.Empty<byte>(),
            Name = PlayerState.Name
        };
        SendServerMessage(hello, PacketType.ProtocolHello);
        App.Log.LogInfo($"Connected to server {peer.Address}:{peer.Port}, waiting for handshake...");
    }
}