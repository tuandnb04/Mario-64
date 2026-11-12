using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Synthesized audio floor & static gain layer (MusicVol / SFXVol handshake).
/// Generates intentional PCM AudioClips at runtime with 5ms fade-in/out envelopes so zero external assets are needed.
/// Every AudioSource routes through MusicVol / SFXVol gain multipliers.
/// </summary>
public class GameAudio : MonoBehaviour
{
    private static GameAudio _instance;
    public static GameAudio Instance
    {
        get
        {
            if (_instance == null) _instance = FindAnyObjectByType<GameAudio>();
            if (_instance != null) _instance.EnsureInitialized();
            return _instance;
        }
    }

    [FormerlySerializedAs("MusicVol")]
    [Header("Exposed Volume Parameters (0..1 linear gain)")]
    [Range(0f, 1f)] public float musicVol = 0.75f;
    [FormerlySerializedAs("SFXVol")] [Range(0f, 1f)] public float sfxVol = 0.90f;

    public float MusicVol
    {
        get => musicVol;
        set => SetMusicVolume(value);
    }

    public float SfxVol => sfxVol;

    private bool _isDucked;
    private float _duckMultiplier = 1f;

    private AudioClip CoinBlipClip { get; set; }
    private AudioClip StarMilestoneClip { get; set; }
    private AudioClip AmbientCourtyardClip { get; set; }
    private AudioClip UIClickClip { get; set; }
    private AudioClip JumpWhooshClip { get; set; }
    private AudioClip GroundPoundThudClip { get; set; }

    private AudioSource AmbientSource { get; set; }
    private AudioSource SfxSource { get; set; }
    private AudioSource StingSource { get; set; }

    private bool _initialized;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        EnsureInitialized();
    }

    private void EnsureInitialized()
    {
        if (_initialized && CoinBlipClip != null && AmbientSource != null) return;
        _initialized = true;
        SynthesizeAllClips();
        SetupSources();
    }

    private void SynthesizeAllClips()
    {
        const int sampleRate = 44100;
        CoinBlipClip = CreatePickupBlip(sampleRate, 0.095f, 880f, 1320f);
        StarMilestoneClip = CreateMilestoneSting(sampleRate);
        AmbientCourtyardClip = CreateAmbientBed(sampleRate, 6.0f);
        UIClickClip = CreateUIClick(sampleRate, 0.022f);
        JumpWhooshClip = CreatePickupBlip(sampleRate, 0.14f, 260f, 620f);
        GroundPoundThudClip = CreatePickupBlip(sampleRate, 0.18f, 175f, 52f);
    }

    private void SetupSources()
    {
        AmbientSource = CreateChildSource("AmbientBedSource", spatial: false, loop: true);
        AmbientSource.clip = AmbientCourtyardClip;
        AmbientSource.volume = CalculateMusicEffectiveVolume();
        AmbientSource.Play();

        SfxSource = CreateChildSource("SFX2DSource", spatial: false, loop: false);
        StingSource = CreateChildSource("MilestoneStingSource", spatial: false, loop: false);
    }

    private AudioSource CreateChildSource(string sourceName, bool spatial, bool loop)
    {
        var go = new GameObject(sourceName);
        go.transform.SetParent(transform, false);
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = loop;
        src.spatialBlend = spatial ? 1f : 0f;
        src.minDistance = 2.5f;
        src.maxDistance = 28f;
        return src;
    }

    private void Update()
    {
        var targetDuck = _isDucked ? 0.5f : 1.0f;
        _duckMultiplier = Mathf.MoveTowards(_duckMultiplier, targetDuck, Time.unscaledDeltaTime * 4f);
        ApplyGainToAllSources();
    }

    public void SetPausedDuck(bool paused)
    {
        _isDucked = paused;
        ApplyGainToAllSources();
    }

    public void SetMusicVolume(float normalized)
    {
        musicVol = Mathf.Clamp01(normalized);
        ApplyGainToAllSources();
    }

    public void SetSfxVolume(float normalized)
    {
        sfxVol = Mathf.Clamp01(normalized);
        ApplyGainToAllSources();
    }

    private float CalculateMusicEffectiveVolume(float baseGain = 0.38f)
    {
        return baseGain * musicVol * _duckMultiplier;
    }

    private float CalculateSfxEffectiveVolume(float baseGain = 0.85f)
    {
        return baseGain * sfxVol;
    }

    private void ApplyGainToAllSources()
    {
        if (AmbientSource != null)
            AmbientSource.volume = CalculateMusicEffectiveVolume();
        if (SfxSource != null)
            SfxSource.volume = CalculateSfxEffectiveVolume();
        if (StingSource != null)
            StingSource.volume = CalculateSfxEffectiveVolume(0.95f);
    }

    public void PlayPickupBlip(float pitchOffset = 0f)
    {
        if (SfxSource == null || CoinBlipClip == null) return;
        SfxSource.pitch = 1.0f + pitchOffset + Random.Range(-0.06f, 0.06f);
        SfxSource.volume = CalculateSfxEffectiveVolume();
        SfxSource.PlayOneShot(CoinBlipClip);
    }

    public void PlayMilestoneSting()
    {
        if (StingSource == null || StarMilestoneClip == null) return;
        StingSource.pitch = 1.0f;
        StingSource.volume = CalculateSfxEffectiveVolume(0.95f);
        StingSource.PlayOneShot(StarMilestoneClip);
    }

    public void PlayUIClick()
    {
        if (SfxSource == null || UIClickClip == null) return;
        SfxSource.pitch = Random.Range(0.96f, 1.04f);
        SfxSource.volume = CalculateSfxEffectiveVolume(0.75f);
        SfxSource.PlayOneShot(UIClickClip);
    }

    public void PlayJumpSound(int jumpStage)
    {
        if (SfxSource == null || JumpWhooshClip == null) return;
        SfxSource.pitch = 0.92f + (jumpStage * 0.16f);
        SfxSource.volume = CalculateSfxEffectiveVolume(0.72f);
        SfxSource.PlayOneShot(JumpWhooshClip);
    }

    public void PlayGroundPoundSound()
    {
        if (SfxSource == null || GroundPoundThudClip == null) return;
        SfxSource.pitch = Random.Range(0.95f, 1.05f);
        SfxSource.volume = CalculateSfxEffectiveVolume(0.9f);
        SfxSource.PlayOneShot(GroundPoundThudClip);
    }

    private static AudioClip CreatePickupBlip(int sampleRate, float duration, float startFreq, float endFreq)
    {
        var samples = Mathf.Max(64, Mathf.RoundToInt(sampleRate * duration));
        var data = new float[samples];
        var phase = 0f;
        var fadeSamples = Mathf.Min(samples / 4, Mathf.RoundToInt(sampleRate * 0.005f));

        for (var i = 0; i < samples; i++)
        {
            var t = (float)i / samples;
            var freq = Mathf.Lerp(startFreq, endFreq, t);
            phase += (2f * Mathf.PI * freq) / sampleRate;
            var decay = Mathf.Pow(1f - t, 0.75f);
            var val = (Mathf.Sin(phase) * 0.8f + Mathf.Sin(phase * 2f) * 0.2f) * decay * 0.55f;
            val *= Envelope5MS(i, samples, fadeSamples);
            data[i] = val;
        }

        var clip = AudioClip.Create("SynthesizedBlip", samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static AudioClip CreateMilestoneSting(int sampleRate)
    {
        float[] notes = { 523.25f, 659.25f, 783.99f, 1046.50f };
        const float duration = 0.58f;
        var samples = Mathf.RoundToInt(sampleRate * duration);
        var data = new float[samples];
        var fadeSamples = Mathf.RoundToInt(sampleRate * 0.005f);

        for (var n = 0; n < notes.Length; n++)
        {
            var noteStart = n * 0.105f;
            var noteDur = (n == notes.Length - 1) ? 0.26f : 0.15f;
            var startIdx = Mathf.RoundToInt(noteStart * sampleRate);
            var noteSamples = Mathf.RoundToInt(noteDur * sampleRate);
            var phase = 0f;
            var freq = notes[n];

            for (var i = 0; i < noteSamples && (startIdx + i) < samples; i++)
            {
                var t = (float)i / noteSamples;
                phase += (2f * Mathf.PI * freq) / sampleRate;
                var env = Mathf.Sin(Mathf.Clamp01(t * 6f) * Mathf.PI * 0.5f) * Mathf.Pow(1f - t, 0.65f);
                var tone = (Mathf.Sin(phase) * 0.7f + Mathf.Sin(phase * 2f) * 0.2f + Mathf.Sin(phase * 3f) * 0.1f) * env * 0.45f;
                data[startIdx + i] = Mathf.Clamp(data[startIdx + i] + tone, -0.95f, 0.95f);
            }
        }

        for (var i = 0; i < samples; i++)
            data[i] *= Envelope5MS(i, samples, fadeSamples);

        var clip = AudioClip.Create("MilestoneSting", samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static AudioClip CreateAmbientBed(int sampleRate, float duration)
    {
        var samples = Mathf.RoundToInt(sampleRate * duration);
        var data = new float[samples];
        var fadeSamples = Mathf.RoundToInt(sampleRate * 0.02f);
        var brown = 0f;
        var rng = new System.Random(64);

        for (var i = 0; i < samples; i++)
        {
            var t = (float)i / sampleRate;
            var white = (float)(rng.NextDouble() * 2.0 - 1.0);
            brown = Mathf.Clamp((brown + 0.02f * white) / 1.02f, -1f, 1f);

            var swell = 0.5f + 0.5f * Mathf.Sin(t * (2f * Mathf.PI / duration));
            var pad =
                Mathf.Sin(2f * Mathf.PI * 130.81f * t) * 0.12f +
                Mathf.Sin(2f * Mathf.PI * 196.00f * t) * 0.10f +
                Mathf.Sin(2f * Mathf.PI * 329.63f * t) * 0.06f * swell;

            var val = (brown * 0.16f + pad) * 0.5f;
            val *= Envelope5MS(i, samples, fadeSamples);
            data[i] = val;
        }

        var clip = AudioClip.Create("AmbientCourtyardBed", samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static AudioClip CreateUIClick(int sampleRate, float duration)
    {
        var samples = Mathf.RoundToInt(sampleRate * duration);
        var data = new float[samples];
        var fadeSamples = Mathf.Min(samples / 3, Mathf.RoundToInt(sampleRate * 0.005f));
        var rng = new System.Random(1996);
        var prev = 0f;

        for (var i = 0; i < samples; i++)
        {
            var t = (float)i / samples;
            var white = (float)(rng.NextDouble() * 2.0 - 1.0);
            var filtered = Mathf.Lerp(prev, white, 0.35f);
            prev = filtered;
            var clickTone = Mathf.Sin(2f * Mathf.PI * 1100f * ((float)i / sampleRate));
            var env = Mathf.Pow(1f - t, 1.8f);
            data[i] = (filtered * 0.45f + clickTone * 0.55f) * env * 0.5f * Envelope5MS(i, samples, fadeSamples);
        }

        var clip = AudioClip.Create("UIClick", samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static float Envelope5MS(int index, int totalSamples, int fadeSamples)
    {
        if (fadeSamples <= 0) return 1f;
        if (index < fadeSamples) return (float)index / fadeSamples;
        if (index > totalSamples - fadeSamples) return (float)(totalSamples - index) / fadeSamples;
        return 1f;
    }
}