namespace Phrasor.Core.Voice;

public enum AudioLevel
{
    /// <summary>Every sample is zero: the microphone is muted or turned off in Windows.</summary>
    Muted,

    /// <summary>The microphone works but nothing loud enough to be speech was recorded.</summary>
    Silent,

    /// <summary>Enough sound to be worth transcribing.</summary>
    HasSound,
}

/// <summary>
/// Decides whether a recording is worth sending for transcription. Speech-to-text models such as
/// Whisper invent text from silence ("Thank you.", "Thanks for watching"), so silent clips must
/// be caught before the call, not filtered after it.
/// </summary>
public static class SpeechDetector
{
    /// <summary>Analysis frame length. 20 ms is the usual frame size for voice activity checks.</summary>
    private const int FrameMs = 20;

    /// <summary>
    /// Frames quieter than this don't count as sound. Normal speech at a laptop mic is roughly
    /// -35 to -15 dBFS; a quiet room's background noise is usually below -55 dBFS.
    /// </summary>
    public const double SoundThresholdDbfs = -50;

    /// <summary>How much sound is needed before a clip counts as possibly containing speech.</summary>
    public static readonly TimeSpan MinSound = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// The speech model decides whether there is speech. Loudness can't: typing, clicks and breathing are loud
    /// enough, and quiet words are not (#66). Only a muted microphone is settled by loudness, and audio the
    /// model can't take (another sample rate, or no model) keeps the loudness verdict.
    /// </summary>
    public static AudioLevel Analyze(ReadOnlySpan<byte> pcm16, int sampleRate, ISpeechModel? model)
    {
        var level = Analyze(pcm16, sampleRate);
        if (level == AudioLevel.Muted || model is null || sampleRate != model.SampleRate) return level;
        var boosted = Normalize(pcm16, sampleRate);
        return SpeechRuns.HasSustainedSpeech(model.Probabilities(boosted)) ? AudioLevel.HasSound : AudioLevel.Silent;
    }

    /// <summary>Loudest frames below this are background noise, never boosted. Quiet speech on a weak mic sits above it.</summary>
    public const double NoiseFloorDbfs = -60;

    /// <summary>The loudest frame is brought up to this level before the model hears it; the model is trained on normal-level speech.</summary>
    public const double TargetDbfs = -20;

    /// <summary>Most gain ever applied, so a recording that is nearly all noise isn't turned into something speech-like.</summary>
    public const double MaxGainDb = 30;

    /// <summary>Returns the clip amplified so its loudest frame reaches <see cref="TargetDbfs"/>. Never makes it quieter, never boosts noise-floor clips.</summary>
    internal static byte[] Normalize(ReadOnlySpan<byte> pcm16, int sampleRate)
    {
        var loudest = LoudestFrameDbfs(pcm16, sampleRate);
        var gainDb = Math.Min(TargetDbfs - loudest, MaxGainDb);
        if (loudest < NoiseFloorDbfs || gainDb <= 0) return pcm16.ToArray();

        var gain = Math.Pow(10, gainDb / 20);
        var result = new byte[pcm16.Length / 2 * 2];
        for (var i = 0; i + 1 < pcm16.Length; i += 2)
        {
            var sample = (short)(pcm16[i] | (pcm16[i + 1] << 8));
            var scaled = (short)Math.Clamp(Math.Round(sample * gain), short.MinValue, short.MaxValue);
            result[i] = (byte)scaled;
            result[i + 1] = (byte)(scaled >> 8);
        }
        return result;
    }

    /// <summary>RMS of the loudest 20 ms frame in dBFS (-infinity for silence). Levels only, safe to log.</summary>
    public static double LoudestFrameDbfs(ReadOnlySpan<byte> pcm16, int sampleRate)
    {
        var samples = pcm16.Length / 2;
        var frameSamples = Math.Max(1, sampleRate * FrameMs / 1000);
        double loudest = 0;
        for (var start = 0; start < samples; start += frameSamples)
        {
            var count = Math.Min(frameSamples, samples - start);
            double sumSquares = 0;
            for (var i = 0; i < count; i++)
            {
                var offset = (start + i) * 2;
                var sample = (short)(pcm16[offset] | (pcm16[offset + 1] << 8));
                sumSquares += (double)sample * sample;
            }
            loudest = Math.Max(loudest, sumSquares / count);
        }
        return 10 * Math.Log10(loudest / (32768.0 * 32768.0));
    }

    /// <summary>Classifies 16-bit little-endian mono PCM recorded at <paramref name="sampleRate"/>.</summary>
    public static AudioLevel Analyze(ReadOnlySpan<byte> pcm16, int sampleRate)
    {
        var samples = pcm16.Length / 2;
        if (samples == 0) return AudioLevel.Muted;

        var frameSamples = Math.Max(1, sampleRate * FrameMs / 1000);
        var threshold = 32768.0 * Math.Pow(10, SoundThresholdDbfs / 20);
        var anyNonZero = false;
        var loudFrames = 0;

        for (var start = 0; start < samples; start += frameSamples)
        {
            var count = Math.Min(frameSamples, samples - start);
            double sumSquares = 0;
            for (var i = 0; i < count; i++)
            {
                var offset = (start + i) * 2;
                var sample = (short)(pcm16[offset] | (pcm16[offset + 1] << 8));
                if (sample != 0) anyNonZero = true;
                sumSquares += (double)sample * sample;
            }
            if (Math.Sqrt(sumSquares / count) >= threshold) loudFrames++;
        }

        if (!anyNonZero) return AudioLevel.Muted;
        return TimeSpan.FromMilliseconds(loudFrames * FrameMs) >= MinSound ? AudioLevel.HasSound : AudioLevel.Silent;
    }
}
