using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using Object = UnityEngine.Object;

namespace StarTruckMP.Client.Audio;

/// <summary>
/// Plays a live stream of PCM frames through one <see cref="AudioSource"/>.
///
/// Unity has no streaming clip to hand, so a looping two-second clip stands in for one: frames
/// are written a little ahead of the playhead and the playhead runs round behind them. A few
/// frames are held back before playback starts, so ordinary network jitter never reaches the
/// speaker, and silence is written whenever the network falls behind, so a late frame lands in
/// an audible gap rather than sliding the whole stream later and later.
///
/// Frames may be queued from any thread; <see cref="Pump"/> runs on the game thread once a
/// frame. The CB radio, the loopback test and voices heard across the sector all use this;
/// what makes them different is where the source sits and how it is configured, which the
/// owner does through <see cref="Source"/>.
/// </summary>
public sealed class PcmStreamPlayer
{
    private const int ClipSeconds = 2;
    private const int StartPlaybackFrames = 3;
    private const int TargetLeadFrames = 4;
    private const int MaxWriteFramesPerUpdate = 6;

    /// <summary>With nothing queued for this long the source stops, so the next burst starts with a fresh jitter buffer.</summary>
    private const long IdleStopMs = 1500;

    private readonly AudioSource _source;
    private readonly AudioClip _clip;
    private readonly int _clipSamples;
    private readonly int _samplesPerFrame;

    /// <summary>Two seconds of nothing, made once: every stop used to allocate and marshal it afresh.</summary>
    private static Il2CppStructArray<float> _silence;

    private static Il2CppStructArray<float> Silence =>
        _silence ??= new Il2CppStructArray<float>(new float[VoiceFormat.SampleRate * ClipSeconds * VoiceFormat.Channels]);

    private readonly ConcurrentQueue<float[]> _pending = new();
    private readonly Queue<float[]> _buffered = new();
    private int _bufferedOffset;
    private int _bufferedCount;
    private int _writePosition;
    private bool _playing;
    private long _lastEnqueueTicks;

    public AudioSource Source => _source;

    public bool IsPlaying => _playing;

    /// <summary>Milliseconds since the last frame was queued, or a very large number before the first.</summary>
    public long MillisecondsSinceLastFrame =>
        _lastEnqueueTicks == 0 ? long.MaxValue : Environment.TickCount64 - _lastEnqueueTicks;

    public PcmStreamPlayer(GameObject host, string clipName)
    {
        _clipSamples = VoiceFormat.SampleRate * ClipSeconds;
        _samplesPerFrame = VoiceFormat.SamplesPerFrame * VoiceFormat.Channels;

        _clip = AudioClip.Create(clipName, _clipSamples, VoiceFormat.Channels, VoiceFormat.SampleRate, false);
        _clip.SetData(Silence, 0);

        _source = host.AddComponent<AudioSource>();
        _source.playOnAwake = false;
        _source.loop = true;
        _source.dopplerLevel = 0f;
        _source.clip = _clip;
    }

    /// <summary>Queues a frame of samples. Safe from any thread.</summary>
    public void Enqueue(float[] samples)
    {
        if (samples == null || samples.Length == 0) return;

        _pending.Enqueue(samples);
        _lastEnqueueTicks = Environment.TickCount64;
    }

    /// <summary>Game thread, once a frame: moves queued audio into the clip ahead of the playhead.</summary>
    public void Pump()
    {
        if (_source == null || _clip == null) return;

        while (_pending.TryDequeue(out var frame))
        {
            _buffered.Enqueue(frame);
            _bufferedCount += frame.Length;
        }

        var startSamples = _samplesPerFrame * StartPlaybackFrames;
        var targetLead = _samplesPerFrame * TargetLeadFrames;

        if (!_playing)
        {
            if (_bufferedCount < startSamples) return;

            var initial = Math.Min(_bufferedCount, targetLead);
            initial -= initial % _samplesPerFrame;
            if (initial < _samplesPerFrame) return;

            WriteBuffered(initial, padWithSilence: false);
            _source.timeSamples = 0;
            _source.Play();
            _playing = true;
            return;
        }

        if (_bufferedCount == 0 && MillisecondsSinceLastFrame > IdleStopMs)
        {
            Stop();
            return;
        }

        var playhead = Mathf.Clamp(_source.timeSamples, 0, _clipSamples - 1);
        var lead = RingDistance(playhead, _writePosition, _clipSamples);
        if (lead >= targetLead) return;

        var needed = Mathf.Clamp(targetLead - lead, _samplesPerFrame, _samplesPerFrame * MaxWriteFramesPerUpdate);
        needed = RoundUpToFrame(needed);

        WriteBuffered(needed, padWithSilence: true);
    }

    /// <summary>Stops playback and forgets everything queued; the next frame starts a fresh burst.</summary>
    public void Stop()
    {
        if (_source != null) _source.Stop();

        while (_pending.TryDequeue(out _)) { }
        _buffered.Clear();
        _bufferedOffset = 0;
        _bufferedCount = 0;
        _writePosition = 0;
        _playing = false;

        if (_clip != null)
            _clip.SetData(Silence, 0);
    }

    /// <summary>Removes the source and the clip from the host object.</summary>
    public void Dispose()
    {
        Stop();
        if (_source != null) Object.Destroy(_source);
        if (_clip != null) Object.Destroy(_clip);
    }

    private void WriteBuffered(int requested, bool padWithSilence)
    {
        var count = requested;
        if (!padWithSilence)
        {
            count = Math.Min(count, _bufferedCount);
            count -= count % _samplesPerFrame;
            if (count < _samplesPerFrame) return;
        }

        var samples = new float[count];
        var copied = CopyBuffered(samples, count);

        if (copied == 0 && !padWithSilence) return;
        if (copied > 0 && copied < _samplesPerFrame) return;
        if (!padWithSilence && copied < count) count = copied;

        if (count != samples.Length)
        {
            var trimmed = new float[count];
            Array.Copy(samples, trimmed, count);
            samples = trimmed;
        }

        WriteRing(samples);
    }

    private int CopyBuffered(float[] destination, int requested)
    {
        var copied = 0;

        while (copied < requested && _buffered.Count > 0)
        {
            var frame = _buffered.Peek();
            var available = frame.Length - _bufferedOffset;
            var take = Math.Min(requested - copied, available);
            Array.Copy(frame, _bufferedOffset, destination, copied, take);

            copied += take;
            _bufferedOffset += take;
            _bufferedCount -= take;

            if (_bufferedOffset >= frame.Length)
            {
                _buffered.Dequeue();
                _bufferedOffset = 0;
            }
        }

        return copied;
    }

    private void WriteRing(float[] samples)
    {
        if (samples.Length == 0) return;

        var first = Math.Min(samples.Length, _clipSamples - _writePosition);
        if (first > 0)
        {
            var segment = new float[first];
            Array.Copy(samples, 0, segment, 0, first);
            _clip.SetData(new Il2CppStructArray<float>(segment), _writePosition);
        }

        var second = samples.Length - first;
        if (second > 0)
        {
            var segment = new float[second];
            Array.Copy(samples, first, segment, 0, second);
            _clip.SetData(new Il2CppStructArray<float>(segment), 0);
        }

        _writePosition = (_writePosition + samples.Length) % _clipSamples;
    }

    private int RoundUpToFrame(int count)
    {
        var remainder = count % _samplesPerFrame;
        return remainder == 0 ? count : count + (_samplesPerFrame - remainder);
    }

    private static int RingDistance(int from, int to, int length)
    {
        var distance = to - from;
        return distance < 0 ? distance + length : distance;
    }
}
