using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Concentus;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using RNNoise.NET;
using StarTruckMP.Client.Components;
using StarTruckMP.Client.Synchronization;
using UnityEngine;

namespace StarTruckMP.Client.Audio;

/// <summary>
/// The microphone side of the radio: capture, input gain, noise suppression, Opus encoding and
/// the loopback test the settings page offers.
///
/// It lives on the plugin's own object for the whole session rather than on the truck, so the
/// player can pick a microphone and hear themselves from the main menu, before there is a truck
/// to hang a CB radio on. Who is allowed to transmit is not decided here: the CB radio component
/// raises <see cref="Transmitting"/> while the handset's talk button is down and the game's own
/// dialogue is not using the radio.
/// </summary>
public class VoiceInputComponent : MonoBehaviour
{
    private const int MicClipLengthSeconds = 1;
    private const int MicWarmupFrameCount = 3;
    private const int MicReadLagFrameCount = 1;
    private const float CaptureRetrySeconds = 5f;
    private const float LevelDecayPerSecond = 3f;

    /// <summary>The device name that means "let the mod choose".</summary>
    public const string AutoDevice = "";

    public VoiceInputComponent(IntPtr ptr) : base(ptr) { }

    public static VoiceInputComponent Instance { get; private set; }

    /// <summary>Raised by the CB radio while the player is talking into the handset; frames go to the server.</summary>
    public static bool Transmitting { get; set; }

    /// <summary>Set from the settings page; frames are played back locally instead of being sent.</summary>
    public static bool Testing { get; private set; }

    /// <summary>Peak of the most recent captured frame after gain, 0..1, for the level meter.</summary>
    public static float InputLevel { get; private set; }

    /// <summary>The device actually opened, for the settings page. Null while no microphone is running.</summary>
    public static string DeviceLabel => Instance?._deviceLabel;

    public static bool MicrophoneRunning => Instance != null && Instance._micClip != null;

    public static bool DenoiserAvailable => Instance != null && Instance._denoiser != null;

    private string _device;
    private string _deviceLabel;
    private AudioClip _micClip;
    private CancellationTokenSource _cts;
    private bool _enabled;
    private IOpusEncoder _encoder;
    private Denoiser _denoiser;
    private bool _denoiserWarned;
    private byte[] _encodeBuffer;
    private int _captureSamplesPerFrame;
    private int _captureSampleRate;
    private int _micChannels;
    private bool _micReady;
    private float _micStartTime;
    private float _nextCaptureAttempt;
    private bool _captureFailedLogged;
    private Il2CppStructArray<float> _micReadBuffer;

    // Mic read position — updated on the main thread only.
    private int _micReadPos;

    // Raw PCM frames queued by Update (main thread) → consumed by the encoder thread.
    private readonly ConcurrentQueue<float[]> _encodeQueue = new();

    /// <summary>Wakes the encoder the moment a frame is queued, rather than on its next five-millisecond look.</summary>
    private readonly AutoResetEvent _encodeSignal = new(false);

    private bool _wasCapturing;
    private readonly List<MicOpenCandidate> _micCandidates = new();

    private PcmStreamPlayer _loopback;
    private RadioVoiceEffectProcessor _loopbackEffect;

    private sealed class MicOpenCandidate
    {
        public string Device { get; init; }
        public string DeviceLabel { get; init; }
        public int RequestedSampleRate { get; init; }
        public string DeviceCaps { get; init; }

        public override string ToString() => $"{DeviceLabel} @ {RequestedSampleRate} Hz ({DeviceCaps})";
    }

    private void Awake()
    {
        Instance = this;
        _cts = new CancellationTokenSource();
        _encodeBuffer = new byte[4000];
        _enabled = true;

        _encoder = OpusCodecFactory.CreateEncoder(VoiceFormat.SampleRate, VoiceFormat.Channels, VoiceFormat.OpusApp);
        _encoder.Bitrate = VoiceFormat.Bitrate;
        _encoder.Complexity = 3; // 0-10; 3-5 best for real-time

        try
        {
            _denoiser = new Denoiser();
            App.Log.LogInfo("[CB Radio] RNNoise input denoiser ready");
        }
        catch (Exception ex)
        {
            App.Log.LogError($"[CB Radio] Failed to initialise RNNoise denoiser: {ex}");
            App.Log.LogWarning("[CB Radio] Voice input will be encoded without RNNoise noise reduction.");
            _denoiser = null;
        }

        // The encoder runs on a background thread; mic reading happens in Update().
        Plugin.StartAttachedThread(EncodeLoop);
        App.Log.LogInfo("[CB Radio] Voice input ready; the microphone opens on the first frame.");
    }

    private void Update()
    {
        if (_micClip == null && Time.unscaledTime >= _nextCaptureAttempt)
        {
            _nextCaptureAttempt = Time.unscaledTime + CaptureRetrySeconds;
            StartCapture("startup");
        }

        var active = Transmitting || Testing;
        if (active)
            ReadMicFrames();
        else if (_wasCapturing)
            ResetMicReadState(); // discard stale data and re-prime before the next talk burst
        _wasCapturing = active;

        if (InputLevel > 0f)
            InputLevel = Mathf.Max(0f, InputLevel - LevelDecayPerSecond * Time.unscaledDeltaTime);

        if (_loopback != null)
        {
            _loopback.Source.volume = CbRadioSpeakerComponent.OutputVolume;
            _loopback.Pump();
        }
    }

    #region Settings

    /// <summary>Every microphone Windows offers, as the game sees them.</summary>
    public static string[] Devices()
    {
        try
        {
            var devices = Microphone.devices;
            if (devices == null) return Array.Empty<string>();

            var result = new List<string>();
            foreach (var device in devices)
            {
                if (!string.IsNullOrWhiteSpace(device)) result.Add(device);
            }

            return result.ToArray();
        }
        catch (Exception ex)
        {
            App.Log.LogWarning($"[CB Radio] Could not list microphones: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    /// <summary>Switches to the named device, or back to automatic choice for <see cref="AutoDevice"/>.</summary>
    public static void SelectDevice(string deviceName)
    {
        App.MicrophoneDeviceName.Value = deviceName ?? AutoDevice;
        Instance?.StartCapture("device changed in settings");
    }

    /// <summary>Starts or stops hearing your own microphone the way other players will.</summary>
    public static void SetTesting(bool testing)
    {
        if (Testing == testing) return;

        Testing = testing;
        var instance = Instance;
        if (instance == null) return;

        if (testing)
        {
            // Flat and non-spatial, but through the same radio colouring other players hear,
            // so what the test plays back is what they will get.
            if (instance._loopback == null)
            {
                instance._loopback = new PcmStreamPlayer(instance.gameObject, "voice_loopback_ring");
                instance._loopback.Source.spatialBlend = 0f;
                instance._loopbackEffect = new RadioVoiceEffectProcessor(VoiceFormat.SampleRate, 2.0f);
            }

            App.Log.LogInfo("[CB Radio] Microphone test started");
        }
        else
        {
            instance._loopback?.Stop();
            InputLevel = 0f;
            App.Log.LogInfo("[CB Radio] Microphone test stopped");
        }
    }

    #endregion

    #region Capture

    /// <summary>
    /// Reads microphone samples into complete 20 ms frames.
    /// Must run on the main thread — AudioClip.GetData requires it.
    /// </summary>
    private void ReadMicFrames()
    {
        if (_micClip == null) return;
        if (!_micReady && !TryPrimeMicrophone()) return;

        var writePos = Microphone.GetPosition(_device);
        if (writePos < 0)
            return;

        var available = GetRingDistance(_micReadPos, writePos, _micClip.samples);
        var readable = available - (_captureSamplesPerFrame * MicReadLagFrameCount);

        while (readable >= _captureSamplesPerFrame)
        {
            if (!_micClip.GetData(_micReadBuffer, _micReadPos))
            {
                App.Log.LogWarning($"[CB Radio] AudioClip.GetData failed at readPos={_micReadPos} (writePos={writePos}, available={available}, channels={_micChannels}, clipSamples={_micClip.samples})");
                _micReady = false;
                return;
            }

            var frame = ConvertCaptureBufferToOutputFrame();
            _micReadPos = (_micReadPos + _captureSamplesPerFrame) % _micClip.samples;
            readable -= _captureSamplesPerFrame;

            ApplyGain(frame);

            if (App.NoiseSuppression.Value)
                TryDenoiseFrame(frame);

            InputLevel = Mathf.Max(InputLevel, Peak(frame));

            if (Testing)
            {
                if (_loopback != null && _loopbackEffect != null)
                {
                    var heard = _loopbackEffect.Process(frame);
                    CbRadioSpeakerComponent.ApplyOutputGain(heard);
                    _loopback.Enqueue(heard);
                }
            }
            else if (Transmitting)
            {
                _encodeQueue.Enqueue(frame);
                _encodeSignal.Set();
            }
        }
    }

    private static void ApplyGain(float[] frame)
    {
        var gain = Mathf.Clamp(App.MicrophoneGain.Value, 0f, 4f);
        if (Mathf.Approximately(gain, 1f)) return;

        for (var i = 0; i < frame.Length; i++)
            frame[i] = Mathf.Clamp(frame[i] * gain, -1f, 1f);
    }

    private static float Peak(float[] frame)
    {
        var peak = 0f;
        for (var i = 0; i < frame.Length; i++)
        {
            var magnitude = Math.Abs(frame[i]);
            if (magnitude > peak) peak = magnitude;
        }

        return peak;
    }

    /// <summary>
    /// Background thread: dequeues raw PCM frames, encodes them with Opus and sends over the network.
    /// </summary>
    private void EncodeLoop()
    {
        while (_enabled && !_cts.IsCancellationRequested)
        {
            if (_encodeQueue.TryDequeue(out var frame))
            {
                if (_encoder == null) continue;

                var written = _encoder.Encode(frame, VoiceFormat.SamplesPerFrame, _encodeBuffer, _encodeBuffer.Length);
                if (written <= 0) continue;

                var packet = new byte[written];
                Buffer.BlockCopy(_encodeBuffer, 0, packet, 0, written);
                Network.SendOpusFrame(packet);
            }
            else
            {
                _encodeSignal.WaitOne(20);
            }
        }
    }

    private void TryDenoiseFrame(float[] frame)
    {
        if (_denoiser == null || frame == null || frame.Length == 0)
            return;

        try
        {
            var processed = _denoiser.Denoise(frame.AsSpan(), false);
            if (processed <= 0 && !_denoiserWarned)
            {
                _denoiserWarned = true;
                App.Log.LogWarning("[CB Radio] RNNoise returned no samples; using the raw frame.");
            }
        }
        catch (Exception ex)
        {
            App.Log.LogError($"[CB Radio] RNNoise denoise failed: {ex}");
            App.Log.LogWarning("[CB Radio] Disabling RNNoise for the rest of this session.");
            _denoiser.Dispose();
            _denoiser = null;
        }
    }

    private bool TryPrimeMicrophone()
    {
        if (_micClip == null || !Microphone.IsRecording(_device))
            return false;

        var writePos = Microphone.GetPosition(_device);
        if (writePos <= 0)
            return false;

        var warmupSamples = _captureSamplesPerFrame * MicWarmupFrameCount;
        var warmupSeconds = (float)warmupSamples / Math.Max(1, _captureSampleRate);
        if (Time.realtimeSinceStartup - _micStartTime < warmupSeconds)
            return false;

        _micReadPos = (writePos - warmupSamples + _micClip.samples) % _micClip.samples;
        _micReady = true;
        return true;
    }

    private void ResetMicReadState()
    {
        if (_micClip == null)
            return;

        _micReady = false;
        _micReadPos = Math.Max(0, Microphone.GetPosition(_device));
    }

    private void StartCapture(string reason)
    {
        try
        {
            if (Microphone.devices.Length == 0)
            {
                if (!_captureFailedLogged)
                {
                    _captureFailedLogged = true;
                    App.Log.LogError("[CB Radio] No microphone devices found.");
                }

                StopMicrophoneCapture();
                return;
            }

            App.Log.LogInfo($"[CB Radio] Available microphone devices: {string.Join(", ", Microphone.devices)}");

            BuildMicrophoneCandidates();
            if (!TryStartMicrophoneCandidate(0, reason))
            {
                if (!_captureFailedLogged)
                {
                    _captureFailedLogged = true;
                    App.Log.LogError("[CB Radio] Failed to start any microphone candidate.");
                }

                return;
            }

            _captureFailedLogged = false;
            App.Log.LogInfo($"[CB Radio] Capturing from {_deviceLabel}");
        }
        catch (Exception ex)
        {
            App.Log.LogError($"[CB Radio] Microphone start failed: {ex}");
            StopMicrophoneCapture();
        }
    }

    private void BuildMicrophoneCandidates()
    {
        _micCandidates.Clear();

        foreach (var device in BuildPreferredDeviceOrder())
        {
            Microphone.GetDeviceCaps(device, out var minFreq, out var maxFreq);
            var caps = DescribeDeviceCaps(minFreq, maxFreq);
            var label = string.IsNullOrWhiteSpace(device) ? "<system default>" : device;

            foreach (var sampleRate in BuildCaptureSampleRateCandidates(minFreq, maxFreq))
            {
                if (_micCandidates.Any(candidate => string.Equals(candidate.Device, device, StringComparison.OrdinalIgnoreCase) && candidate.RequestedSampleRate == sampleRate))
                    continue;

                _micCandidates.Add(new MicOpenCandidate
                {
                    Device = device,
                    DeviceLabel = label,
                    RequestedSampleRate = sampleRate,
                    DeviceCaps = caps
                });
            }
        }

        App.Log.LogInfo($"[CB Radio] Microphone candidate order: {string.Join(" | ", _micCandidates.Select((candidate, index) => $"#{index}: {candidate}"))}");
    }

    private IEnumerable<string> BuildPreferredDeviceOrder()
    {
        var availableDevices = Microphone.devices ?? Array.Empty<string>();
        var configuredDeviceName = App.MicrophoneDeviceName?.Value?.Trim();
        var preferSystemDefault = App.PreferSystemDefaultMicrophone?.Value ?? true;
        var orderedDevices = new List<string>();

        if (!string.IsNullOrWhiteSpace(configuredDeviceName))
        {
            var configuredDevice = availableDevices.FirstOrDefault(device => string.Equals(device, configuredDeviceName, StringComparison.OrdinalIgnoreCase));
            if (configuredDevice != null)
                AddDeviceIfMissing(orderedDevices, configuredDevice);
            else
                App.Log.LogWarning($"[CB Radio] Configured microphone '{configuredDeviceName}' was not found. Falling back to auto-selection.");
        }

        if (preferSystemDefault)
            AddDeviceIfMissing(orderedDevices, null);

        var scoredDevices = new List<DeviceScore>(availableDevices.Length);
        for (var index = 0; index < availableDevices.Length; index++)
            scoredDevices.Add(new DeviceScore(availableDevices[index], index, ScoreMicrophoneDeviceName(availableDevices[index])));

        scoredDevices.Sort((left, right) =>
        {
            var scoreComparison = right.Score.CompareTo(left.Score);
            return scoreComparison != 0 ? scoreComparison : left.Index.CompareTo(right.Index);
        });

        foreach (var scoredDevice in scoredDevices)
            AddDeviceIfMissing(orderedDevices, scoredDevice.Name);

        AddDeviceIfMissing(orderedDevices, null);
        return orderedDevices;
    }

    private static IEnumerable<int> BuildCaptureSampleRateCandidates(int minFreq, int maxFreq)
    {
        var rates = new List<int>();

        void AddRate(int rate)
        {
            if (rate > 0 && !rates.Contains(rate))
                rates.Add(rate);
        }

        if (minFreq > 0 && minFreq == maxFreq)
        {
            AddRate(minFreq);
            AddRate(44100);
            AddRate(VoiceFormat.SampleRate);
        }
        else
        {
            if (IsSampleRateSupported(VoiceFormat.SampleRate, minFreq, maxFreq))
                AddRate(VoiceFormat.SampleRate);
            if (IsSampleRateSupported(44100, minFreq, maxFreq))
                AddRate(44100);
            AddRate(maxFreq);
            AddRate(minFreq);
        }

        if (rates.Count == 0)
        {
            AddRate(44100);
            AddRate(VoiceFormat.SampleRate);
        }

        return rates;
    }

    private bool TryStartMicrophoneCandidate(int startIndex, string reason)
    {
        for (var index = Math.Max(0, startIndex); index < _micCandidates.Count; index++)
        {
            if (TryStartMicrophone(_micCandidates[index], reason))
                return true;
        }

        return false;
    }

    private bool TryStartMicrophone(MicOpenCandidate candidate, string reason)
    {
        StopMicrophoneCapture();

        _device = candidate.Device;
        _deviceLabel = candidate.DeviceLabel;

        App.Log.LogInfo($"[CB Radio] Opening microphone {candidate.DeviceLabel} (caps: {candidate.DeviceCaps}, requestedRate={candidate.RequestedSampleRate}) [{reason}]");
        if (!IsSampleRateSupported(candidate.RequestedSampleRate, candidate.Device))
        {
            App.Log.LogWarning($"[CB Radio] Requested {candidate.RequestedSampleRate} Hz is outside the device caps {candidate.DeviceCaps}. Skipping candidate {candidate.DeviceLabel}.");
            return false;
        }

        _micClip = Microphone.Start(_device, true, MicClipLengthSeconds, candidate.RequestedSampleRate);
        if (_micClip == null)
        {
            App.Log.LogWarning($"[CB Radio] Failed to start microphone {candidate.DeviceLabel} @ {candidate.RequestedSampleRate} Hz.");
            return false;
        }

        _micChannels = Math.Max(1, _micClip.channels);
        _captureSampleRate = _micClip.frequency > 0 ? _micClip.frequency : candidate.RequestedSampleRate;
        _captureSamplesPerFrame = Math.Max(1, _captureSampleRate * VoiceFormat.FrameDurationMs / 1000);
        _micReadBuffer = new Il2CppStructArray<float>(_captureSamplesPerFrame * _micChannels);
        _micReadPos = 0;
        _micReady = false;
        _micStartTime = Time.realtimeSinceStartup;

        App.Log.LogInfo(
            $"[CB Radio] Microphone started on {candidate.DeviceLabel}: requestedRate={candidate.RequestedSampleRate}, clipFrequency={_captureSampleRate}, clipChannels={_micChannels}, clipSamples={_micClip.samples}, outputFrameSamples={VoiceFormat.SamplesPerFrame}, captureFrameSamples={_captureSamplesPerFrame}");
        return true;
    }

    private void StopMicrophoneCapture()
    {
        _micReady = false;
        _micReadPos = 0;
        _micReadBuffer = null;
        _captureSamplesPerFrame = 0;
        _captureSampleRate = 0;

        try
        {
            if (_micClip != null)
                Microphone.End(_device);
        }
        catch (Exception ex)
        {
            App.Log.LogWarning($"[CB Radio] Failed to stop microphone {_deviceLabel}: {ex.Message}");
        }

        _micClip = null;
    }

    private float[] ConvertCaptureBufferToOutputFrame()
    {
        var monoFrame = new float[_captureSamplesPerFrame];
        if (_micChannels == 1)
        {
            for (int sampleIndex = 0; sampleIndex < _captureSamplesPerFrame; sampleIndex++)
                monoFrame[sampleIndex] = _micReadBuffer[sampleIndex];
        }
        else
        {
            for (int sampleIndex = 0; sampleIndex < _captureSamplesPerFrame; sampleIndex++)
            {
                float sample = 0f;
                var baseIndex = sampleIndex * _micChannels;
                for (int channel = 0; channel < _micChannels; channel++)
                    sample += _micReadBuffer[baseIndex + channel];
                monoFrame[sampleIndex] = sample / _micChannels;
            }
        }

        if (monoFrame.Length == VoiceFormat.SamplesPerFrame)
            return monoFrame;

        return ResampleFrame(monoFrame, VoiceFormat.SamplesPerFrame);
    }

    private static int GetRingDistance(int from, int to, int length)
    {
        var distance = to - from;
        return distance < 0 ? distance + length : distance;
    }

    private static int ScoreMicrophoneDeviceName(string deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
            return int.MinValue;

        var name = deviceName.ToLowerInvariant();
        var score = 0;

        if (name.Contains("micrófono") || name.Contains("microphone") || name.Contains("микрофон") || name.Contains(" headset") || name.StartsWith("mic") || name.Contains(" mic ") || name.Contains("headset"))
            score += 100;
        if (name.Contains("line") || name.Contains("analogue") || name.Contains("stereo mix") || name.Contains("loopback") || name.Contains("monitor"))
            score -= 100;

        return score;
    }

    private static void AddDeviceIfMissing(ICollection<string> devices, string device)
    {
        foreach (var existingDevice in devices)
        {
            if (string.Equals(existingDevice, device, StringComparison.OrdinalIgnoreCase))
                return;
        }

        devices.Add(device);
    }

    private sealed class DeviceScore
    {
        public DeviceScore(string name, int index, int score)
        {
            Name = name;
            Index = index;
            Score = score;
        }

        public string Name { get; }
        public int Index { get; }
        public int Score { get; }
    }

    private static bool IsSampleRateSupported(int sampleRate, string device)
    {
        Microphone.GetDeviceCaps(device, out var minFreq, out var maxFreq);
        return IsSampleRateSupported(sampleRate, minFreq, maxFreq);
    }

    private static bool IsSampleRateSupported(int sampleRate, int minFreq, int maxFreq)
    {
        return minFreq == 0 && maxFreq == 0 || sampleRate >= minFreq && sampleRate <= maxFreq;
    }

    private static float[] ResampleFrame(float[] source, int targetLength)
    {
        var output = new float[targetLength];
        if (source.Length == 0 || targetLength == 0)
            return output;

        if (source.Length == 1)
        {
            for (int i = 0; i < targetLength; i++)
                output[i] = source[0];
            return output;
        }

        if (targetLength == 1)
        {
            output[0] = source[0];
            return output;
        }

        var step = (source.Length - 1f) / (targetLength - 1f);
        for (int i = 0; i < targetLength; i++)
        {
            var sourcePos = i * step;
            var left = Mathf.Clamp((int)sourcePos, 0, source.Length - 1);
            var right = Mathf.Min(left + 1, source.Length - 1);
            var fraction = sourcePos - left;
            output[i] = Mathf.Lerp(source[left], source[right], fraction);
        }

        return output;
    }

    private static string DescribeDeviceCaps(int minFreq, int maxFreq)
    {
        return minFreq == 0 && maxFreq == 0 ? "any/unknown" : $"{minFreq}-{maxFreq} Hz";
    }

    #endregion

    private void OnDestroy()
    {
        _enabled = false;
        if (!_cts.IsCancellationRequested)
            _cts.Cancel();
        _encodeSignal.Set();

        SetTesting(false);

        try
        {
            StopMicrophoneCapture();
        }
        catch (Exception ex)
        {
            App.Log.LogError($"[CB Radio] Failed to end microphone: {ex}");
        }

        if (_encoder is IDisposable d)
        {
            d.Dispose();
            _encoder = null;
        }

        if (_denoiser != null)
        {
            _denoiser.Dispose();
            _denoiser = null;
        }

        if (Instance == this) Instance = null;
    }
}
