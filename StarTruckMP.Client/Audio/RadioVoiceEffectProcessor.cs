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
/// The chain is the one a real handset imposes on the voice itself: a narrow band, a hot input
/// stage that folds the loud syllables over, a compressor riding the level and a squelch gate
/// under it. It colours and distorts; it adds no noise of its own — an earlier version laid a
/// carrier hiss and crackle over the voice and that only read as a bad microphone. Every stage
/// keeps its state between frames, so a 20 ms frame boundary is inaudible.
///
/// The strength is the player's <c>RadioEffect</c> setting: off passes the voice through, light
/// keeps the band and the compressor, full adds the drive and the tighter band.
/// </summary>
public sealed class RadioVoiceEffectProcessor
{
    public enum Strength { Off = 0, Light = 1, Full = 2 }

    private const float CompressorThresholdDb = -24f;
    private const float CompressorRatio = 3.5f;
    private const float CompressorAttackSeconds = 0.02f;
    private const float CompressorReleaseSeconds = 0.18f;
    /// <summary>Anything quieter than this between words is shut off, so the compressor cannot lift the room.</summary>
    private const float NoiseGateThresholdDb = -40f;
    private const float NoiseGateRatio = 8f;
    private const float NoiseGateAttackSeconds = 0.003f;
    private const float NoiseGateReleaseSeconds = 0.08f;

    /// <summary>Input drive into the soft clipper. Higher is crunchier.</summary>
    private const float Drive = 3.4f;

    private readonly int _sampleRate;
    private readonly float _outputGain;
    private readonly FilterChain _bandLight;
    private readonly FilterChain _bandFull;
    private readonly DynamicsProcessor _compressor;
    private readonly DynamicsProcessor _noiseGate;
    private readonly Random _random = new();
    private readonly float _driveNorm;

    /// <summary>A touch of make-up after the clipper: driving a voice into tanh also squashes its peaks.</summary>
    private const float PostDriveGain = 1.15f;

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
                sample = MathF.Tanh(sample * Drive) * _driveNorm * PostDriveGain;

            sample = _compressor.Process(sample);
            sample = _noiseGate.Process(sample);

            pcm[i] = Math.Clamp(sample * _outputGain, -1f, 1f);
        }

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

        // Short and modest: a click and a tail that say "keyed" and "released", not a wash of static.
        var seconds = opening ? 0.03f : 0.09f;
        var amplitude = opening ? 0.14f : 0.09f;
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

}
