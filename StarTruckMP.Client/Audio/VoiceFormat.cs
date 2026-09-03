using Concentus.Enums;

namespace StarTruckMP.Client.Audio;

/// <summary>The one PCM and Opus shape every voice path agrees on, capture and playback alike.</summary>
public static class VoiceFormat
{
    public const int SampleRate = 48000;
    public const int Channels = 1;
    public const int FrameDurationMs = 20;
    public const int SamplesPerFrame = SampleRate * FrameDurationMs / 1000;

    public const int Bitrate = 24000;
    public const OpusApplication OpusApp = OpusApplication.OPUS_APPLICATION_VOIP;
}
