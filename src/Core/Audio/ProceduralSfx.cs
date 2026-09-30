using Godot;
using System;
using System.Collections.Generic;

namespace ZombieApocalypse.Core.Audio;

/// <summary>
/// Sound effect identifiers generated procedurally at runtime.
/// </summary>
public enum SfxId
{
    Gunshot,
    SuppressedShot,
    MeleeSwing,
    Footstep,
    FootstepSprint,
    ZombieGroan,
    ZombieScream,
    HumanVoice,
    Explosion,
    ElectricZap,
    Blip,
    GeneratorHum,
    AmbientWind,
}

/// <summary>
/// Procedurally synthesised sound bank.
///
/// The project intentionally ships no audio files yet, so every effect is
/// generated as 16-bit PCM (<see cref="AudioStreamWav"/>) on first use and
/// cached. Clips use a fixed RNG seed per effect, so the same sound is produced
/// on every run (important for reproducible tests and balance passes).
///
/// Replacing these with real assets later only requires mapping
/// <see cref="SfxId"/> to imported streams in <see cref="AudioManager"/>.
/// </summary>
public static class ProceduralSfx
{
    private const int SampleRate = 22050;
    private static readonly Dictionary<SfxId, AudioStreamWav> Cache = new();

    /// <summary>Fetch (and lazily synthesise) the clip for an effect.</summary>
    public static AudioStreamWav Get(SfxId id)
    {
        if (Cache.TryGetValue(id, out var cached)) return cached;
        var built = Build(id);
        Cache[id] = built;
        return built;
    }

    /// <summary>Number of clips currently synthesised (test hook).</summary>
    public static int CachedClipCount => Cache.Count;

    /// <summary>
    /// Releases every synthesised clip. Called when the <see cref="Autoloads.AudioManager"/>
    /// leaves the tree so the static cache does not leak native resources at exit.
    /// Players must have stopped/cleared their streams first.
    /// </summary>
    public static void ClearCache()
    {
        foreach (var stream in Cache.Values)
            stream?.Dispose();
        Cache.Clear();
    }

    /// <summary>Lazily synthesises a clip from a per-sample generator function.</summary>
    private static AudioStreamWav Make(float seconds, bool loop, Func<float, Random, float> sample, int seed)
    {
        var rng = new Random(seed);
        int frames = Mathf.Max(1, (int)(seconds * SampleRate));
        var data = new byte[frames * 2];

        for (int i = 0; i < frames; i++)
        {
            float t = (float)i / SampleRate;
            float value = Mathf.Clamp(sample(t, rng), -1f, 1f);
            short pcm = (short)(value * 30000f);
            data[i * 2] = (byte)(pcm & 0xFF);
            data[i * 2 + 1] = (byte)((pcm >> 8) & 0xFF);
        }

        var wav = new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = SampleRate,
            Stereo = false,
            Data = data,
        };

        if (loop)
        {
            wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            wav.LoopBegin = 0;
            wav.LoopEnd = frames;
        }

        return wav;
    }

    private static AudioStreamWav Build(SfxId id) => id switch
    {
        SfxId.Gunshot => Make(0.30f, false, (t, r) =>
            (float)(r.NextDouble() * 2 - 1) * Mathf.Exp(-t * 14f) + 0.6f * Mathf.Sin(t * Mathf.Tau * 90f) * Mathf.Exp(-t * 9f), 101),

        SfxId.SuppressedShot => Make(0.16f, false, (t, r) =>
            0.55f * (float)(r.NextDouble() * 2 - 1) * Mathf.Exp(-t * 34f), 102),

        SfxId.MeleeSwing => Make(0.22f, false, (t, r) =>
        {
            float whoosh = (float)(r.NextDouble() * 2 - 1) * 0.45f * Mathf.Exp(-t * 9f);
            float sweep = Mathf.Sin(t * Mathf.Tau * (220f + 700f * t)) * 0.2f * Mathf.Exp(-t * 12f);
            return whoosh + sweep;
        }, 103),

        SfxId.Footstep => Make(0.16f, false, (t, r) =>
            Mathf.Sin(t * Mathf.Tau * 62f) * Mathf.Exp(-t * 32f) * 0.55f
            + (float)(r.NextDouble() * 2 - 1) * 0.10f * Mathf.Exp(-t * 45f), 104),

        SfxId.FootstepSprint => Make(0.18f, false, (t, r) =>
            Mathf.Sin(t * Mathf.Tau * 74f) * Mathf.Exp(-t * 26f) * 0.85f
            + (float)(r.NextDouble() * 2 - 1) * 0.18f * Mathf.Exp(-t * 38f), 105),

        SfxId.ZombieGroan => Make(0.95f, false, (t, r) =>
        {
            float env = Mathf.Sin(Mathf.Pi * Mathf.Clamp(t / 0.95f, 0f, 1f));
            float wobble = Mathf.Sin(t * Mathf.Tau * (66f + 6f * Mathf.Sin(t * Mathf.Tau * 3f)));
            return env * (0.45f * wobble + 0.10f * (float)(r.NextDouble() * 2 - 1));
        }, 106),

        SfxId.ZombieScream => Make(0.90f, false, (t, r) =>
        {
            float env = Mathf.Sin(Mathf.Pi * Mathf.Clamp(t / 0.90f, 0f, 1f));
            float tremolo = 0.7f + 0.3f * Mathf.Sin(t * Mathf.Tau * 17f);
            return env * tremolo * (0.35f * Mathf.Sin(t * Mathf.Tau * 179f)
                                    + 0.30f * Mathf.Sin(t * Mathf.Tau * 227f)
                                    + 0.15f * (float)(r.NextDouble() * 2 - 1));
        }, 107),

        SfxId.HumanVoice => Make(0.24f, false, (t, r) =>
        {
            float env = Mathf.Exp(-t * 12f);
            return env * (0.4f * Mathf.Sin(t * Mathf.Tau * 128f) + 0.15f * (float)(r.NextDouble() * 2 - 1));
        }, 108),

        SfxId.Explosion => Make(0.85f, false, (t, r) =>
            (float)(r.NextDouble() * 2 - 1) * Mathf.Exp(-t * 5f)
            + 0.8f * Mathf.Sin(t * Mathf.Tau * 45f) * Mathf.Exp(-t * 3.5f), 109),

        SfxId.ElectricZap => Make(0.35f, false, (t, r) =>
        {
            bool spike = (t % 0.05f) < 0.012f;
            return (spike ? 1f : 0f) * (float)(r.NextDouble() * 2 - 1) * Mathf.Exp(-t * 2.5f) * 0.7f;
        }, 110),

        SfxId.Blip => Make(0.20f, false, (t, r) =>
        {
            float freq = t < 0.09f ? 880f : 1320f;
            return 0.35f * Mathf.Sin(t * Mathf.Tau * freq) * Mathf.Exp(-t * 9f);
        }, 111),

        SfxId.GeneratorHum => Make(1.00f, true, (t, r) =>
            0.30f * Mathf.Sin(t * Mathf.Tau * 55f)
            + 0.14f * Mathf.Sin(t * Mathf.Tau * 110f)
            + 0.05f * (float)(r.NextDouble() * 2 - 1), 112),

        SfxId.AmbientWind => Make(3.00f, true, (t, r) =>
        {
            float gust = 0.55f + 0.45f * Mathf.Sin(t * Mathf.Tau * 0.22f);
            return (float)(r.NextDouble() * 2 - 1) * 0.18f * gust;
        }, 113),

        _ => Make(0.10f, false, (t, r) => 0f, 100),
    };
}
