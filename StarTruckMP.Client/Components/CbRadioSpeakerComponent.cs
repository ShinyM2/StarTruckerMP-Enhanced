using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Concentus;
using StarTruckMP.Client.Audio;
using StarTruckMP.Client.Synchronization;
using StarTruckMP.Shared.Dto;
using UnityEngine;

namespace StarTruckMP.Client.Components;

/// <summary>
/// The loudspeaker of the CB radio in the cab: every other player's voice comes out of it.
///
/// Each sender gets its own decoder, its own radio colouring and its own stream player, all
/// hung on the radio's object so the sound comes from the dash. A transmission is bracketed the
/// way a real set brackets it — a burst of static as the carrier comes up, a softer "ksst" as it
/// drops — and the roster and nameplates are told who is on the air. While the game's own radio
/// conversation is running the speaker can be muted, so a caller is not talked over.
/// </summary>
public class CbRadioSpeakerComponent : MonoBehaviour
{
    /// <summary>Frames further apart than this belong to separate transmissions.</summary>
    private const long BurstGapMs = 300;

    /// <summary>A sender heard nothing from for this long is forgotten, decoder and all.</summary>
    private const long ForgetAfterMs = 60_000;

    public CbRadioSpeakerComponent(IntPtr ptr) : base(ptr) { }

    /// <summary>World-space position of the CB radio speaker.</summary>
    public Vector3 SpeakerPosition
    {
        get => transform.position;
        set => transform.position = value;
    }

    private const float MinDistance = 1f;
    private const float MaxDistance = 30f;

    private int _maxFrameSizePerChannel;

    private readonly Dictionary<int, Sender> _senders = new();
    private readonly object _sendersLock = new();
    private readonly ConcurrentQueue<Sender> _retired = new();

    private sealed class Sender
    {
        public int NetId;
        public IOpusDecoder Decoder;
        public float[] DecodeBuffer;

        /// <summary>Colours the frames; used on the network thread only.</summary>
        public RadioVoiceEffectProcessor Effect;

        /// <summary>Makes the closing burst; used on the game thread only.</summary>
        public RadioVoiceEffectProcessor SquelchEffect;

        /// <summary>Frames waiting for the game thread to hand them to the player.</summary>
        public readonly ConcurrentQueue<float[]> Pending = new();

        public PcmStreamPlayer Player;
        public long LastFrameTicks;
        public bool OnAir;

        /// <summary>The same voice again, from the speaker's own truck when it is near, so a transmission from the cab beside you sounds like it comes from there too.</summary>
        public PcmStreamPlayer TruckPlayer;
        public GameObject TruckHost;
    }

    /// <summary>How far the voice from another truck's cab carries, in metres.</summary>
    private const float TruckVoiceRange = 60f;
    private const float TruckVoiceVolume = 0.6f;

    private void Awake()
    {
        // Max Opus frame is 120 ms
        _maxFrameSizePerChannel = VoiceFormat.SampleRate * 120 / 1000;

        Network.OnVoiceReceived += HandleVoiceReceived;
        Network.OnPlayerDisconnected += HandlePlayerDisconnected;

        App.Log.LogInfo("[CB Radio] Speaker ready");
    }

    private void OnDestroy()
    {
        Network.OnVoiceReceived -= HandleVoiceReceived;
        Network.OnPlayerDisconnected -= HandlePlayerDisconnected;

        while (_retired.TryDequeue(out var retired)) Cleanup(retired);

        List<Sender> active;
        lock (_sendersLock)
        {
            active = new List<Sender>(_senders.Values);
            _senders.Clear();
        }

        foreach (var sender in active) Cleanup(sender);
    }

    private void Update()
    {
        while (_retired.TryDequeue(out var retired)) Cleanup(retired);

        List<Sender> senders;
        lock (_sendersLock)
        {
            if (_senders.Count == 0) return;
            senders = new List<Sender>(_senders.Values);
        }

        // Muted rather than paused during a game dialogue: the stream keeps flowing and the
        // player rejoins it live the moment the call ends, instead of hearing a backlog.
        var mute = CbRadioPttComponent.IsDialogueBusy && App.MuteRadioDuringDialogue.Value;
        var volume = OutputVolume;
        var now = Environment.TickCount64;

        foreach (var sender in senders)
        {
            if (sender.Player == null)
            {
                sender.Player = new PcmStreamPlayer(gameObject, $"voice_sender_{sender.NetId}_ring");
                Configure(sender.Player.Source);
            }

            FollowTruck(sender);

            while (sender.Pending.TryDequeue(out var frame))
            {
                sender.Player.Enqueue(frame);
                sender.TruckPlayer?.Enqueue((float[])frame.Clone());
            }

            // The other side let go of the button: close the squelch behind them.
            if (sender.OnAir && now - sender.LastFrameTicks > BurstGapMs)
            {
                sender.OnAir = false;
                var tail = sender.SquelchEffect.Squelch(opening: false);
                sender.Player.Enqueue(tail);
                sender.TruckPlayer?.Enqueue((float[])tail.Clone());
            }

            sender.Player.Source.mute = mute;
            sender.Player.Source.volume = volume;
            sender.Player.Pump();

            if (sender.TruckPlayer != null)
            {
                sender.TruckPlayer.Source.mute = mute;
                sender.TruckPlayer.Source.volume = volume * TruckVoiceVolume;
                sender.TruckPlayer.Pump();
            }

            if (!sender.OnAir && now - sender.LastFrameTicks > ForgetAfterMs)
                Retire(sender.NetId);
        }
    }

    /// <summary>
    /// Keeps a second player on the sender's truck while that truck is in our sector and the
    /// setting is on; drops it when the truck goes, or is rebuilt, or the setting goes off.
    /// </summary>
    private void FollowTruck(Sender sender)
    {
        var truck = App.HearNearbyRadios.Value ? NetworkEventsComponent.RemoteTruck(sender.NetId) : null;

        // Compared by reference on purpose: Unity's own equality calls a destroyed host equal to
        // null, and a player whose truck was just destroyed would never get their clip released.
        if (ReferenceEquals(truck, sender.TruckHost)) return;

        sender.TruckPlayer?.Dispose();
        sender.TruckPlayer = null;
        sender.TruckHost = truck;

        if (truck == null) return;

        sender.TruckPlayer = new PcmStreamPlayer(truck, $"voice_truck_{sender.NetId}_ring");
        var source = sender.TruckPlayer.Source;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.minDistance = 4f;
        source.maxDistance = TruckVoiceRange;
        source.spread = 30f;
        source.dopplerLevel = 0f;
    }

    /// <summary>The player's radio volume as far as an <see cref="AudioSource"/> can take it (0..1).</summary>
    public static float OutputVolume => Mathf.Clamp01(App.RadioVolume?.Value ?? 1f);

    /// <summary>The part of the radio volume above 100%, which a source cannot do and the samples must.</summary>
    public static void ApplyOutputGain(float[] samples)
    {
        var gain = App.RadioVolume?.Value ?? 1f;
        if (gain <= 1f || samples == null) return;

        for (var i = 0; i < samples.Length; i++)
            samples[i] = Mathf.Clamp(samples[i] * gain, -1f, 1f);
    }

    // Called from the network polling thread.
    private void HandleVoiceReceived(VoiceDto voice)
    {
        if (voice.OpusData == null || voice.OpusData.Length == 0) return;

        var sender = GetOrCreate(voice.NetId);
        var now = Environment.TickCount64;

        // A fresh transmission: the receiver's squelch opens with a burst of static.
        if (!sender.OnAir || now - sender.LastFrameTicks > BurstGapMs)
        {
            var burst = sender.Effect.Squelch(opening: true);
            if (burst.Length > 0) sender.Pending.Enqueue(burst);
        }

        sender.LastFrameTicks = now;
        sender.OnAir = true;
        MultiplayerState.MarkSpeaking(voice.NetId);

        int decoded;
        try
        {
            decoded = sender.Decoder.Decode(voice.OpusData.AsSpan(), sender.DecodeBuffer.AsSpan(), _maxFrameSizePerChannel);
        }
        catch (Exception e)
        {
            App.Log.LogWarning($"[CB Radio] Decode error (sender {voice.NetId}): {e.Message}");
            return;
        }

        if (decoded <= 0) return;

        // A fresh buffer per frame: the network thread moves on while the game thread plays this one.
        var samples = new float[decoded * VoiceFormat.Channels];
        Array.Copy(sender.DecodeBuffer, samples, samples.Length);

        var processed = sender.Effect.Process(samples);
        ApplyOutputGain(processed);
        sender.Pending.Enqueue(processed);
    }

    // Called from the network polling thread when a player leaves.
    private void HandlePlayerDisconnected(int netId)
    {
        Retire(netId);
        MultiplayerState.ForgetSpeaker(netId);
    }

    private void Retire(int netId)
    {
        lock (_sendersLock)
        {
            if (_senders.TryGetValue(netId, out var sender))
            {
                _senders.Remove(netId);
                _retired.Enqueue(sender);
            }
        }
    }

    private Sender GetOrCreate(int netId)
    {
        lock (_sendersLock)
        {
            if (_senders.TryGetValue(netId, out var existing)) return existing;

            var sender = new Sender
            {
                NetId = netId,
                Decoder = OpusCodecFactory.CreateDecoder(VoiceFormat.SampleRate, VoiceFormat.Channels),
                DecodeBuffer = new float[_maxFrameSizePerChannel * VoiceFormat.Channels],
                Effect = new RadioVoiceEffectProcessor(VoiceFormat.SampleRate, 2.0f),
                SquelchEffect = new RadioVoiceEffectProcessor(VoiceFormat.SampleRate, 2.0f)
            };

            _senders[netId] = sender;
            return sender;
        }
    }

    private static void Configure(AudioSource source)
    {
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = MinDistance;
        source.maxDistance = MaxDistance;
        source.dopplerLevel = 0f;
    }

    private static void Cleanup(Sender sender)
    {
        sender.Player?.Dispose();
        sender.Player = null;
        sender.TruckPlayer?.Dispose();
        sender.TruckPlayer = null;
        sender.TruckHost = null;

        if (sender.Decoder is IDisposable disposable)
            disposable.Dispose();
    }
}
