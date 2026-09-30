using Godot;
using System.Collections.Generic;
using ZombieApocalypse.Core.Audio;
using ZombieApocalypse.Core.Data;

namespace ZombieApocalypse.Core.Autoloads;

/// <summary>
/// Spatial audio playback service (blueprint autoload #5).
///
/// Turns the simulation's acoustic events into actual sound: it listens on
/// <see cref="EventBus.OnSoundEmitted"/> and plays a pooled
/// <see cref="AudioStreamPlayer3D"/> at the reported position, with volume and
/// audible distance derived from the event's noise radius. Clips come from
/// <see cref="ProceduralSfx"/> until real assets are imported.
/// </summary>
public partial class AudioManager : Node
{
    public static AudioManager? Instance { get; private set; }

    [ExportGroup("Pooling")]
    [Export] public int VoicePoolSize = 24;

    [ExportGroup("Mix")]
    [Export] public float AmbientVolume = 0.5f;
    [Export] public float UiVolume = 0.6f;

    /// <summary>Total sounds started since launch (self-test hook).</summary>
    public int SoundsPlayed { get; private set; }
    /// <summary>Sound events received from the EventBus (self-test hook).</summary>
    public int EventsReceived { get; private set; }

    private readonly List<AudioStreamPlayer3D> _voices = new();
    private AudioStreamPlayer? _ambientPlayer;
    private int _voiceCursor;
    private bool _audioOutputAvailable = true;

    /// <summary>False when running without an audio server (CI / --headless).</summary>
    public bool AudioOutputAvailable => _audioOutputAvailable;

    public override void _Ready()
    {
        Instance = this;

        // The headless dummy mixer never reaps playback objects, which shows up as
        // leaked resources at exit. Skip real playback there but keep the pool,
        // the mix and the bookkeeping identical so behaviour stays testable.
        _audioOutputAvailable = DisplayServer.GetName() != "headless";

        for (int i = 0; i < Mathf.Max(4, VoicePoolSize); i++)
        {
            var voice = new AudioStreamPlayer3D
            {
                Name = $"Voice{i}",
                UnitSize = 12f,
                MaxDistance = 70f,
            };
            AddChild(voice);
            _voices.Add(voice);
        }

        _ambientPlayer = new AudioStreamPlayer
        {
            Name = "AmbientLoop",
            Stream = ProceduralSfx.Get(SfxId.AmbientWind),
        };
        AddChild(_ambientPlayer);

        ApplyVolumes();
        if (_audioOutputAvailable)
            _ambientPlayer.Play();

        if (EventBus.Instance != null)
            EventBus.Instance.OnSoundEmitted += OnSoundEmitted;

        GD.Print($"[AudioManager] Online with {_voices.Count} voices " +
                 $"(procedural sound bank{(_audioOutputAvailable ? "" : ", output disabled: headless")}).");
    }

    public override void _ExitTree()
    {
        if (EventBus.Instance != null)
            EventBus.Instance.OnSoundEmitted -= OnSoundEmitted;

        // Drop stream references before releasing the shared clip cache, so no
        // native audio resource is left dangling at shutdown.
        if (_ambientPlayer != null)
        {
            _ambientPlayer.Stop();
            _ambientPlayer.Stream = null;
        }
        foreach (var voice in _voices)
        {
            voice.Stop();
            voice.Stream = null;
        }
        ProceduralSfx.ClearCache();

        if (Instance == this) Instance = null;
    }

    /// <summary>Re-apply mixer levels from <see cref="ConfigManager"/>.</summary>
    public void ApplyVolumes()
    {
        float master = ConfigManager.Instance?.MasterVolume ?? 0.8f;
        float ambient = AmbientVolume * (ConfigManager.Instance?.AmbientVolume ?? 0.5f) * master;

        if (_ambientPlayer != null)
            _ambientPlayer.VolumeDb = Mathf.LinearToDb(Mathf.Max(0.0001f, ambient));
    }

    // ── Event → sound mapping ────────────────────────────────────────

    private void OnSoundEmitted(Vector3 position, float radius, AudioSourceType type)
    {
        EventsReceived++;

        var (sfx, maxDistance) = MapSource(type, radius);
        PlayAt(sfx, position, LoudnessFromRadius(radius), maxDistance);
    }

    /// <summary>
    /// Map a simulation sound type to a clip. A very loud zombie voice is
    /// treated as a scream, which is also what the zombie AI's call-for-help
    /// logic reacts to.
    /// </summary>
    public static (SfxId sfx, float maxDistance) MapSource(AudioSourceType type, float radius) => type switch
    {
        AudioSourceType.Gunshot => (SfxId.Gunshot, Mathf.Max(40f, radius * 1.3f)),
        AudioSourceType.GunshotSuppressed => (SfxId.SuppressedShot, 18f),
        AudioSourceType.MeleeImpact => (SfxId.MeleeSwing, 12f),
        AudioSourceType.Footstep => (SfxId.Footstep, 10f),
        AudioSourceType.FootstepSprint => (SfxId.FootstepSprint, 18f),
        AudioSourceType.VoiceZombie => radius > 30f ? (SfxId.ZombieScream, 60f) : (SfxId.ZombieGroan, 25f),
        AudioSourceType.VoiceHuman => (SfxId.HumanVoice, 20f),
        AudioSourceType.Explosion => (SfxId.Explosion, 80f),
        AudioSourceType.Generator => (SfxId.GeneratorHum, 55f),
        AudioSourceType.Alarm => (SfxId.Blip, 30f),
        AudioSourceType.DoorBreak => (SfxId.MeleeSwing, 25f),
        AudioSourceType.Construction => (SfxId.Blip, 18f),
        AudioSourceType.Environmental => (SfxId.ElectricZap, 25f),
        _ => (SfxId.Blip, 12f),
    };

    /// <summary>Convert a noise radius into a normalised volume factor.</summary>
    public static float LoudnessFromRadius(float radius) => Mathf.Clamp(0.45f + radius / 90f, 0.45f, 1f);

    // ── Playback ─────────────────────────────────────────────────────

    /// <summary>Play a clip at a world position using the voice pool.</summary>
    public AudioStreamPlayer3D PlayAt(SfxId id, Vector3 position, float loudness = 1f, float maxDistance = 40f)
    {
        float volume = loudness
                       * (ConfigManager.Instance?.SfxVolume ?? 0.9f)
                       * (ConfigManager.Instance?.MasterVolume ?? 0.8f);
        return PlayInternal(ProceduralSfx.Get(id), position, volume, maxDistance);
    }

    /// <summary>Play a non-positional UI/feedback blip.</summary>
    public void PlayUi(SfxId id)
    {
        float volume = UiVolume * (ConfigManager.Instance?.MasterVolume ?? 0.8f);
        PlayInternal(ProceduralSfx.Get(id), Vector3.Zero, volume, 0f);
    }

    private AudioStreamPlayer3D PlayInternal(AudioStreamWav stream, Vector3 position, float volumeLinear, float maxDistance)
    {
        AudioStreamPlayer3D voice = AcquireVoice();
        voice.Stream = stream;
        voice.GlobalPosition = position;
        if (maxDistance > 0f) voice.MaxDistance = maxDistance;
        voice.VolumeDb = Mathf.LinearToDb(Mathf.Max(0.0001f, volumeLinear));
        if (_audioOutputAvailable)
            voice.Play();

        SoundsPlayed++;
        return voice;
    }

    /// <summary>Prefer a free voice; otherwise round-robin steal the oldest slot.</summary>
    private AudioStreamPlayer3D AcquireVoice()
    {
        for (int i = 0; i < _voices.Count; i++)
        {
            int index = (_voiceCursor + i) % _voices.Count;
            if (!_voices[index].Playing)
            {
                _voiceCursor = (index + 1) % _voices.Count;
                return _voices[index];
            }
        }

        AudioStreamPlayer3D stolen = _voices[_voiceCursor];
        _voiceCursor = (_voiceCursor + 1) % _voices.Count;
        return stolen;
    }
}
