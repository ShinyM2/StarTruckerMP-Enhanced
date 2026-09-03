using System;
using NWaves.Filters.Base;
using NWaves.Filters.Fda;
using NWaves.Operations;
using BandPassFilter = NWaves.Filters.Butterworth.BandPassFilter;
using Random = System.Random;

namespace StarTruckMP.Client.Audio;

/// <summary>
/// Makes a clean voice sound like it came out of a CB set.
///
/// The chain is the one a real handset imposes: a narrow band, a hot input stage that clips
/// the loud syllables, a compressor riding the level, a squelch gate under it, and the carrier
/// hiss and the odd crackle the set adds of its own. Every stage keeps its state between
/// frames, so a 20 ms frame boundary is inaudible.
///
/// The strength is the player's <c>RadioEffect</c> setting: off passes the voice through, light
/// keeps only the band and the compressor, full is the whole set.
/// </summary>
public sealed class RadioVoiceEffectProcessor
{
    public enum Strength { Off = 0, Light = 1, Full = 2 }

    private const float CompressorThresholdDb = -24f;
    private const float CompressorRatio = 3.5f;
    private const float CompressorAttackSeconds = 0.02f;
    private const float CompressorReleaseSeconds = 0.18f;
    private const float NoiseGateThresholdDb = -46f;
    private const float NoiseGateRatio = 8f;
    private const float NoiseGateAttackSeconds = 0.003f;
    private const float NoiseGateReleaseSeconds = 0.08f;

    /// <summary>Input drive into the soft clipper. Higher is crunchier.</summary>
    private const float Drive = 2.6f;

    /// <summary>Carrier hiss under the voice, as a peak amplitude. Faint, but always there.</summary>
    private const float HissAmplitude = 0.0035f;

    private const float CrackleActivityThreshold = 0.018f;
    private const float CrackleBurstsPerSecond = 2.5f;
    private const float CrackleMinAmplitude = 0.008f;
    private const float CrackleMaxAmplitude = 0.02f;

    private readonly int _sampleRate;
    private readonly float _outputGain;
    private readonly FilterChain _bandLight;
    private readonly FilterChain _bandFull;
    private readonly DynamicsProcessor _compressor;
    private readonly DynamicsProcessor _noiseGate;
    private readonly Random _random = new();
    private readonly float _driveNorm;

    public RadioVoiceEffectProcessor(int sampleRate = VoiceFormat.SampleRate, float outputGain = 1f)
    {
        _sampleRate = sampleRate;
        _outputGain = outputGain;

        _bandLight = Band(300.0, 3400.0, sampleRate);
        _bandFull = Band(350.0, 2900.0, sampleRate);

        _compressor = new DynamicsProcessor(DynamicsMode.Compressor, sampleRate,
            CompressorThresholdDb, CompressorRatio, 0f, CompressorAttackSeconds, CompressorReleaseSeconds);
        _noiseGate = new DynamicsProcessor(DynamicsMode.NoiseGate, sampleRate,
            NoiseGateThresholdDb, NoiseGateRatio, 0f, NoiseGateAttackSeconds, NoiseGateReleaseSeconds);

        _driveNorm = 1f / MathF.Tanh(Drive);
    }

    private static FilterChain Band(double lowHz, double highHz, int sampleRate)
    {
        var tf = new BandPassFilter(lowHz / sampleRate, highHz / sampleRate, 4).Tf;
        return new FilterChain(DesignFilter.TfToSos(tf));
    }

    /// <summary>The player's chosen strength, read each frame so a change in the menu is heard at once.</summary>
    public static Strength Current
    {
        get
        {
            var value = App.RadioEffectStrength?.Value ?? (int)Strength.Full;
            return (Strength)Math.Clamp(value, (int)Strength.Off, (int)Strength.Full);
        }
    }

    /// <summary>Processes one frame in place and returns it.</summary>
    public float[] Process(float[] pcm)
    {
        var strength = Current;

        if (strength == Strength.Off)
        {
            if (Math.Abs(_outputGain - 1f) > 0.001f)
            {
                for (var i = 0; i < pcm.Length; i++)
                    pcm[i] = Math.Clamp(pcm[i] * _outputGain, -1f, 1f);
            }

            return pcm;
        }

        var band = strength == Strength.Full ? _bandFull : _bandLight;
        var full = strength == Strength.Full;

        for (var i = 0; i < pcm.Length; i++)
        {
            var sample = band.Process(pcm[i]);

            // A CB's input stage is driven hard; the loud syllables fold over rather than peak.
            if (full)
                sample = MathF.Tanh(sample * Drive) * _driveNorm;

            sample = _compressor.Process(sample);
            sample = _noiseGate.Process(sample);

            if (full)
                sample += ((float)_random.NextDouble() * 2f - 1f) * HissAmplitude;

            pcm[i] = Math.Clamp(sample * _outputGain, -1f, 1f);
        }

        if (full)
            AddRfCrackle(pcm);

        return pcm;
    }

    /// <summary>
    /// The burst of static a receiver makes when the carrier comes up or drops away, ready to be
    /// played through the same speaker as the voice. Loud and short to open, softer and longer
    /// to close — the "ksst" that tells a listener the other side has let go of the button.
    /// </summary>
    public float[] Squelch(bool opening)
    {
        if (Current != Strength.Full) return Array.Empty<float>();

        var seconds = opening ? 0.045f : 0.13f;
        var amplitude = opening ? 0.22f : 0.16f;
        var count = (int)(_sampleRate * seconds);
        var burst = new float[count];

        for (var i = 0; i < count; i++)
        {
            var t = i / (float)count;
            // Opening: a sharp attack that dies quickly. Closing: a longer exponential tail.
            var envelope = opening ? (1f - t) * (1f - t) : MathF.Exp(-4.5f * t);
            burst[i] = ((float)_random.NextDouble() * 2f - 1f) * amplitude * envelope;
        }

        // Through the band so it hisses like the set rather than like white noise.
        for (var i = 0; i < count; i++)
            burst[i] = Math.Clamp(_bandFull.Process(burst[i]) * _outputGain, -1f, 1f);

        return burst;
    }

    private void AddRfCrackle(float[] samples)
    {
        if (samples.Length == 0) return;

        var peak = 0f;
        for (var i = 0; i < samples.Length; i++)
        {
            var magnitude = MathF.Abs(samples[i]);
            if (magnitude > peak) peak = magnitude;
        }

        if (peak < CrackleActivityThreshold) return;

        var burstProbability = CrackleBurstsPerSecond * samples.Length / _sampleRate;
        if (_random.NextDouble() >= burstProbability) return;

        var burstStart = _random.Next(samples.Length);
        var burstLength = Math.Min(samples.Length - burstStart, _random.Next(6, 18));
        var burstAmplitude = CrackleMinAmplitude + (float)_random.NextDouble() * (CrackleMaxAmplitude - CrackleMinAmplitude);

        for (var i = 0; i < burstLength; i++)
        {
            var envelope = 1f - i / (float)burstLength;
            var polarity = _random.NextDouble() > 0.5 ? 1f : -1f;
            var crackle = polarity * burstAmplitude * envelope * (0.35f + (float)_random.NextDouble() * 0.65f);
            samples[burstStart + i] = Math.Clamp(samples[burstStart + i] + crackle, -1f, 1f);
        }
    }
}
