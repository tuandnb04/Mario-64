using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

public enum Sm64CourseTheme
{
    FortressMarch,
    OceanBayWaltz,
    DarkWorldOrgan,
    CoolMountainBell,
    PeachCastleWaltz
}

/// <summary>
/// Super Mario 64 / 64 DS Inspired Synthesized PCM Audio Engine.
/// Synthesizes authentic multi-voice chiptune/brass/marimba course themes and iconic SFX
/// (2-tone B5->E6 coin chime, SM64 Power Star fanfare, 3-stage jump progression,
/// Ground Pound slam, Piranha chomp snap, Damage hurt oof, Cannon blast, and Pause menu chime).
/// </summary>
public class GameAudio : MonoBehaviour
{
    private static GameAudio instance;
    public static GameAudio Instance
    {
        get
        {
            if (instance == null) instance = FindAnyObjectByType<GameAudio>();
            if (instance != null) instance.EnsureInitialized();
            return instance;
        }
    }

    [FormerlySerializedAs("MusicVol")]
    [Header("Exposed Volume Parameters (0..1 linear gain)")]
    [Range(0f, 1f)] public float musicVol = 0.75f;
    [FormerlySerializedAs("SFXVol")] [Range(0f, 1f)] public float sfxVol = 0.90f;

    [Header("Optional AudioMixer Routing")]
    [SerializeField] private AudioMixerGroup musicMixerGroup;
    [SerializeField] private AudioMixerGroup sfxMixerGroup;
    [SerializeField] private AudioMixerGroup stingMixerGroup;

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
    private AudioClip CourseMarchClip { get; set; }
    private AudioClip OceanBayClip { get; set; }
    private AudioClip DarkWorldOrganClip { get; set; }
    private AudioClip CoolMountainBellClip { get; set; }
    private AudioClip PeachCastleWaltzClip { get; set; }
    private AudioClip PaintingWarpClip { get; set; }
    private AudioClip PowerUpCapClip { get; set; }
    private AudioClip UIClickClip { get; set; }
    private AudioClip PauseChimeClip { get; set; }
    private AudioClip JumpStage1Clip { get; set; }
    private AudioClip JumpStage2Clip { get; set; }
    private AudioClip JumpStage3Clip { get; set; }
    private AudioClip GroundPoundThudClip { get; set; }
    private AudioClip HurtOofClip { get; set; }
    private AudioClip PiranhaChompClip { get; set; }
    private AudioClip EnemyPopClip { get; set; }
    private AudioClip CannonBlastClip { get; set; }
    private AudioClip BowserRoarClip { get; set; }
    private AudioClip BowserBombClip { get; set; }
    private AudioClip ActionWhooshClip { get; set; }
    private AudioClip SwitchTickClip { get; set; }

    private AudioSource AmbientSource { get; set; }
    private AudioSource SfxSource { get; set; }
    private AudioSource StingSource { get; set; }

    private Sm64CourseTheme _activeTheme = Sm64CourseTheme.FortressMarch;
    private bool _initialized;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        EnsureInitialized();
    }

    private void EnsureInitialized()
    {
        if (_initialized && CoinBlipClip != null && AmbientSource != null) return;
        _initialized = true;
        if (Application.isPlaying && SaveManager.Instance != null)
        {
            musicVol = SaveManager.Instance.MusicVolume;
            sfxVol = SaveManager.Instance.SfxVolume;
        }
        DetectCourseThemeFromScene();
        SynthesizeAllClips();
        SetupSources();
    }

    private void DetectCourseThemeFromScene()
    {
        var sceneName = SceneManager.GetActiveScene().name;
        _activeTheme = string.IsNullOrEmpty(sceneName) switch
        {
            false when (sceneName.Contains("Castle") || sceneName.Contains("Hub")) => Sm64CourseTheme.PeachCastleWaltz,
            false when (sceneName.Contains("Bowser") || sceneName.Contains("DarkWorld")) => Sm64CourseTheme
                .DarkWorldOrgan,
            false when sceneName.Contains("Cool") => Sm64CourseTheme.CoolMountainBell,
            false when sceneName.Contains("JollyRoger") => Sm64CourseTheme.OceanBayWaltz,
            _ => Sm64CourseTheme.FortressMarch
        };
    }

    private void SynthesizeAllClips()
    {
        const int sr = 44100;
        CoinBlipClip = CreateSm64CoinClip(sr);
        StarMilestoneClip = CreateSm64StarFanfare(sr);
        CourseMarchClip = CreateCourseMarchLoop(sr);
        OceanBayClip = CreateOceanBayLoop(sr);
        DarkWorldOrganClip = CreateDarkWorldOrganLoop(sr);
        CoolMountainBellClip = CreateCoolMountainLoop(sr);
        PeachCastleWaltzClip = CreatePeachCastleLoop(sr);
        PaintingWarpClip = CreatePaintingWarpClip(sr);
        PowerUpCapClip = CreatePowerUpCapClip(sr);
        UIClickClip = CreateMenuBlip(sr, 680f, 1020f, 0.045f);
        PauseChimeClip = CreateMenuBlip(sr, 523.25f, 1046.5f, 0.14f);
        JumpStage1Clip = CreateMarioJumpClip(sr, 330f, 590f, 0.14f);
        JumpStage2Clip = CreateMarioJumpClip(sr, 410f, 760f, 0.16f);
        JumpStage3Clip = CreateMarioJumpClip(sr, 480f, 980f, 0.24f);
        GroundPoundThudClip = CreateGroundPoundClip(sr);
        HurtOofClip = CreateHurtClip(sr);
        PiranhaChompClip = CreatePiranhaChompClip(sr);
        EnemyPopClip = CreateEnemyDefeatClip(sr);
        CannonBlastClip = CreateCannonBlastClip(sr);
        BowserRoarClip = CreateBowserRoarClip(sr);
        BowserBombClip = CreateBowserBombExplosionClip(sr);
        ActionWhooshClip = CreateMenuBlip(sr, 280f, 540f, 0.09f);
        SwitchTickClip = CreateSwitchTickClip(sr);
    }

    private void SetupSources()
    {
        AmbientSource = CreateChildSource("AmbientBedSource", spatial: false, loop: true, musicMixerGroup);
        AmbientSource.clip = _activeTheme switch
        {
            Sm64CourseTheme.PeachCastleWaltz => PeachCastleWaltzClip,
            Sm64CourseTheme.OceanBayWaltz => OceanBayClip,
            Sm64CourseTheme.DarkWorldOrgan => DarkWorldOrganClip,
            Sm64CourseTheme.CoolMountainBell => CoolMountainBellClip,
            _ => CourseMarchClip
        };
        AmbientSource.volume = CalculateMusicEffectiveVolume();
        AmbientSource.Play();

        SfxSource = CreateChildSource("SFX2DSource", spatial: false, loop: false, sfxMixerGroup);
        var resolvedStingGroup = stingMixerGroup != null ? stingMixerGroup : sfxMixerGroup;
        StingSource = CreateChildSource("MilestoneStingSource", spatial: false, loop: false, resolvedStingGroup);
    }

    private AudioSource CreateChildSource(string sourceName, bool spatial, bool loop, AudioMixerGroup outputGroup = null)
    {
        var existing = transform.Find(sourceName);
        if (existing != null)
        {
            var es = existing.GetComponent<AudioSource>();
            if (es != null)
            {
                if (outputGroup != null && es.outputAudioMixerGroup == null)
                    es.outputAudioMixerGroup = outputGroup;
                return es;
            }
        }
        var go = new GameObject(sourceName);
        go.transform.SetParent(transform, false);
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = loop;
        src.spatialBlend = spatial ? 1f : 0f;
        src.minDistance = 2.5f;
        src.maxDistance = 28f;
        if (outputGroup != null)
            src.outputAudioMixerGroup = outputGroup;
        return src;
    }

    private void Update()
    {
        var targetDuck = _isDucked ? 0.42f : 1.0f;
        _duckMultiplier = Mathf.MoveTowards(_duckMultiplier, targetDuck, Time.unscaledDeltaTime * 5f);
        ApplyGainToAllSources();
    }

    public void SetPausedDuck(bool paused)
    {
        if (paused != _isDucked && paused && PauseChimeClip != null && SfxSource != null)
        {
            SfxSource.pitch = 1f;
            SfxSource.volume = CalculateSfxEffectiveVolume(0.75f);
            SfxSource.PlayOneShot(PauseChimeClip);
        }
        _isDucked = paused;
        ApplyGainToAllSources();
    }

    public void SetCourseTheme(Sm64CourseTheme theme)
    {
        if (_activeTheme == theme && AmbientSource != null && AmbientSource.clip != null) return;
        _activeTheme = theme;
        if (AmbientSource == null) return;
        var nextClip = theme switch
        {
            Sm64CourseTheme.PeachCastleWaltz => PeachCastleWaltzClip,
            Sm64CourseTheme.OceanBayWaltz => OceanBayClip,
            Sm64CourseTheme.DarkWorldOrgan => DarkWorldOrganClip,
            Sm64CourseTheme.CoolMountainBell => CoolMountainBellClip,
            _ => CourseMarchClip
        };
        if (nextClip == null || AmbientSource.clip == nextClip) return;
        AmbientSource.clip = nextClip;
        AmbientSource.Play();
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

    private float CalculateMusicEffectiveVolume(float baseGain = 0.34f)
    {
        return baseGain * musicVol * _duckMultiplier;
    }

    private float CalculateSfxEffectiveVolume(float baseGain = 0.88f)
    {
        return baseGain * sfxVol;
    }

    private void ApplyGainToAllSources()
    {
        if (AmbientSource != null) AmbientSource.volume = CalculateMusicEffectiveVolume();
        if (SfxSource != null) SfxSource.volume = CalculateSfxEffectiveVolume();
        if (StingSource != null) StingSource.volume = CalculateSfxEffectiveVolume(0.95f);
    }

    public void PlayPickupBlip(float pitchOffset = 0f)
    {
        if (SfxSource == null || CoinBlipClip == null) return;
        SfxSource.pitch = Mathf.Clamp(1.0f + pitchOffset, 0.7f, 1.85f);
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
        SfxSource.pitch = Random.Range(0.97f, 1.03f);
        SfxSource.volume = CalculateSfxEffectiveVolume(0.7f);
        SfxSource.PlayOneShot(UIClickClip);
    }

    public void PlaySwitchTick(bool urgent = false)
    {
        if (SfxSource == null || SwitchTickClip == null) return;
        SfxSource.pitch = urgent ? 1.26f : 1.0f;
        SfxSource.volume = CalculateSfxEffectiveVolume(urgent ? 0.85f : 0.72f);
        SfxSource.PlayOneShot(SwitchTickClip);
    }

    public void PlayJumpSound(int jumpStage)
    {
        if (SfxSource == null) return;
        var clip = jumpStage >= 3 ? JumpStage3Clip : (jumpStage == 2 ? JumpStage2Clip : JumpStage1Clip);
        if (clip == null) return;
        SfxSource.pitch = Random.Range(0.98f, 1.04f);
        SfxSource.volume = CalculateSfxEffectiveVolume(0.78f);
        SfxSource.PlayOneShot(clip);
    }

    public void PlayGroundPoundSound()
    {
        if (SfxSource == null || GroundPoundThudClip == null) return;
        SfxSource.pitch = Random.Range(0.96f, 1.04f);
        SfxSource.volume = CalculateSfxEffectiveVolume(0.95f);
        SfxSource.PlayOneShot(GroundPoundThudClip);
    }

    public void PlayHurtSound()
    {
        if (SfxSource == null || HurtOofClip == null) return;
        SfxSource.pitch = Random.Range(0.95f, 1.05f);
        SfxSource.volume = CalculateSfxEffectiveVolume(0.95f);
        SfxSource.PlayOneShot(HurtOofClip);
    }

    public void PlayPiranhaChompSound()
    {
        if (SfxSource == null || PiranhaChompClip == null) return;
        SfxSource.pitch = Random.Range(0.94f, 1.08f);
        SfxSource.volume = CalculateSfxEffectiveVolume(0.9f);
        SfxSource.PlayOneShot(PiranhaChompClip);
    }

    public void PlayEnemyDefeatSound()
    {
        if (SfxSource == null || EnemyPopClip == null) return;
        SfxSource.pitch = Random.Range(0.96f, 1.06f);
        SfxSource.volume = CalculateSfxEffectiveVolume(0.9f);
        SfxSource.PlayOneShot(EnemyPopClip);
    }

    public void PlayCannonBlastSound()
    {
        if (SfxSource == null || CannonBlastClip == null) return;
        SfxSource.pitch = 1.0f;
        SfxSource.volume = CalculateSfxEffectiveVolume(0.98f);
        SfxSource.PlayOneShot(CannonBlastClip);
    }

    public void PlayBowserRoarSound()
    {
        if (SfxSource == null || BowserRoarClip == null) return;
        SfxSource.pitch = Random.Range(0.92f, 1.04f);
        SfxSource.volume = CalculateSfxEffectiveVolume(1.0f);
        SfxSource.PlayOneShot(BowserRoarClip);
    }

    public void PlayBowserBombExplosionSound()
    {
        if (SfxSource == null || BowserBombClip == null) return;
        SfxSource.pitch = 1.0f;
        SfxSource.volume = CalculateSfxEffectiveVolume(1.0f);
        SfxSource.PlayOneShot(BowserBombClip);
    }

    public void PlayTailGrabSound()
    {
        if (SfxSource == null || ActionWhooshClip == null) return;
        SfxSource.pitch = 1.25f;
        SfxSource.volume = CalculateSfxEffectiveVolume(0.9f);
        SfxSource.PlayOneShot(ActionWhooshClip);
    }

    public void PlayWallKickSound()
    {
        if (SfxSource == null || ActionWhooshClip == null) return;
        SfxSource.pitch = 1.48f;
        SfxSource.volume = CalculateSfxEffectiveVolume(0.8f);
        SfxSource.PlayOneShot(ActionWhooshClip);
    }

    public void PlayLedgeClimbSound()
    {
        if (SfxSource == null || ActionWhooshClip == null) return;
        SfxSource.pitch = 1.15f;
        SfxSource.volume = CalculateSfxEffectiveVolume(0.75f);
        SfxSource.PlayOneShot(ActionWhooshClip);
    }

    public void PlaySkidSound()
    {
        if (SfxSource == null || PiranhaChompClip == null) return;
        SfxSource.pitch = 1.65f;
        SfxSource.volume = CalculateSfxEffectiveVolume(0.45f);
        SfxSource.PlayOneShot(PiranhaChompClip);
    }

    public void PlayPaintingWarp()
    {
        if (StingSource != null && PaintingWarpClip != null)
        {
            StingSource.pitch = 1.0f;
            StingSource.volume = CalculateSfxEffectiveVolume(0.95f);
            StingSource.PlayOneShot(PaintingWarpClip);
            return;
        }
        if (SfxSource != null && PaintingWarpClip != null)
        {
            SfxSource.pitch = 1.0f;
            SfxSource.volume = CalculateSfxEffectiveVolume(0.95f);
            SfxSource.PlayOneShot(PaintingWarpClip);
        }
    }

    public void PlayPowerUpCap()
    {
        if (StingSource != null && PowerUpCapClip != null)
        {
            StingSource.pitch = 1.0f;
            StingSource.volume = CalculateSfxEffectiveVolume(0.95f);
            StingSource.PlayOneShot(PowerUpCapClip);
            return;
        }
        if (SfxSource != null && PowerUpCapClip != null)
        {
            SfxSource.pitch = 1.0f;
            SfxSource.volume = CalculateSfxEffectiveVolume(0.95f);
            SfxSource.PlayOneShot(PowerUpCapClip);
        }
    }

    // =========================================================================
    // SM64 / 64 DS PROCEDURAL PCM SYNTHESIZERS
    // =========================================================================

    private static AudioClip CreateSm64CoinClip(int sr)
    {
        // Classic Nintendo Coin: B5 (987.77 Hz) for 68ms -> E6 (1318.51 Hz) sustain & bell ring
        const float dur = 0.34f;
        var samples = Mathf.RoundToInt(sr * dur);
        var data = new float[samples];
        var fade = Mathf.RoundToInt(sr * 0.004f);
        float p1 = 0f, p2 = 0f;

        for (var i = 0; i < samples; i++)
        {
            var t = (float)i / sr;
            var freq = t < 0.068f ? 987.77f : 1318.51f;
            p1 += (2f * Mathf.PI * freq) / sr;
            p2 += (2f * Mathf.PI * freq * 2f) / sr;

            var localT = t < 0.068f ? t / 0.068f : (t - 0.068f) / (dur - 0.068f);
            var env = t < 0.068f ? 0.9f : Mathf.Pow(1f - Mathf.Clamp01(localT), 1.35f);
            var wave = Mathf.Sin(p1) * 0.65f + (Mathf.Sin(p1) > 0f ? 0.14f : -0.14f) + Mathf.Sin(p2) * 0.22f;
            data[i] = wave * env * 0.48f * Envelope5MS(i, samples, fade);
        }

        var clip = AudioClip.Create("SM64_Coin", samples, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static AudioClip CreateSm64StarFanfare(int sr)
    {
        // Classic SM64 Star Fanfare cadence: G4 - C5 - E5 - G5 - C6 - E6! (bright brass + bell sustain)
        (float start, float dur, float freq)[] notes =
        {
            (0.00f, 0.13f, 392.00f),
            (0.11f, 0.13f, 523.25f),
            (0.22f, 0.13f, 659.25f),
            (0.33f, 0.15f, 783.99f),
            (0.46f, 0.18f, 659.25f),
            (0.60f, 0.56f, 783.99f),
            (0.60f, 0.56f, 1046.50f),
            (0.60f, 0.56f, 1318.51f)
        };
        const float totalDur = 1.22f;
        var samples = Mathf.RoundToInt(sr * totalDur);
        var data = new float[samples];
        var fade = Mathf.RoundToInt(sr * 0.005f);

        foreach (var (start, dur, freq) in notes)
        {
            var s0 = Mathf.RoundToInt(start * sr);
            var len = Mathf.RoundToInt(dur * sr);
            var ph = 0f;
            for (var i = 0; i < len && s0 + i < samples; i++)
            {
                var u = (float)i / len;
                ph += (2f * Mathf.PI * freq) / sr;
                var attack = Mathf.Clamp01(u * 14f);
                var decay = Mathf.Pow(1f - u, 0.72f);
                var brass = Mathf.Sin(ph) * 0.58f + Mathf.Sin(ph * 2f) * 0.25f + Mathf.Sin(ph * 3f) * 0.12f;
                data[s0 + i] = Mathf.Clamp(data[s0 + i] + brass * attack * decay * 0.34f, -0.95f, 0.95f);
            }
        }

        for (var i = 0; i < samples; i++) data[i] *= Envelope5MS(i, samples, fade);
        var clip = AudioClip.Create("SM64_PowerStarFanfare", samples, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static AudioClip CreateMarioJumpClip(int sr, float fStart, float fEnd, float dur)
    {
        var samples = Mathf.RoundToInt(sr * dur);
        var data = new float[samples];
        var fade = Mathf.RoundToInt(sr * 0.004f);
        var ph = 0f;
        var fPh = 0f;
        for (var i = 0; i < samples; i++)
        {
            var u = (float)i / samples;
            var curve = 1f - Mathf.Pow(1f - u, 1.8f);
            var freq = Mathf.Lerp(fStart, fEnd, curve);
            ph += (2f * Mathf.PI * freq) / sr;
            fPh += (2f * Mathf.PI * freq * 2.25f) / sr;
            var env = Mathf.Clamp01(u * 12f) * Mathf.Pow(1f - u, 0.65f);
            var tone = Mathf.Sin(ph) * 0.65f + Mathf.Sin(fPh) * 0.25f + Mathf.Sin(ph * 3f) * 0.08f;
            data[i] = tone * env * 0.52f * Envelope5MS(i, samples, fade);
        }
        var clip = AudioClip.Create("SM64_Jump", samples, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static AudioClip CreateGroundPoundClip(int sr)
    {
        const float dur = 0.28f;
        var samples = Mathf.RoundToInt(sr * dur);
        var data = new float[samples];
        var fade = Mathf.RoundToInt(sr * 0.004f);
        var ph = 0f;
        var rng = new System.Random(42);
        for (var i = 0; i < samples; i++)
        {
            var u = (float)i / samples;
            var freq = Mathf.Lerp(210f, 44f, Mathf.Pow(u, 0.45f));
            ph += (2f * Mathf.PI * freq) / sr;
            var thump = Mathf.Sin(ph) * Mathf.Pow(1f - u, 0.8f);
            var crunch = (float)(rng.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-u * 18f) * 0.35f;
            data[i] = Mathf.Clamp((thump * 0.78f + crunch) * Envelope5MS(i, samples, fade), -0.95f, 0.95f);
        }
        var clip = AudioClip.Create("SM64_GroundPound", samples, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static AudioClip CreateHurtClip(int sr)
    {
        const float dur = 0.26f;
        var samples = Mathf.RoundToInt(sr * dur);
        var data = new float[samples];
        var fade = Mathf.RoundToInt(sr * 0.005f);
        var ph = 0f;
        for (var i = 0; i < samples; i++)
        {
            var u = (float)i / samples;
            var freq = Mathf.Lerp(340f, 118f, u);
            ph += (2f * Mathf.PI * freq) / sr;
            var buzz = Mathf.Clamp(Mathf.Sin(ph) * 1.4f, -0.8f, 0.8f) + Mathf.Sin(ph * 1.5f) * 0.25f;
            var env = Mathf.Pow(1f - u, 0.6f);
            data[i] = buzz * env * 0.55f * Envelope5MS(i, samples, fade);
        }
        var clip = AudioClip.Create("SM64_Hurt", samples, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static AudioClip CreatePiranhaChompClip(int sr)
    {
        const float dur = 0.16f;
        var samples = Mathf.RoundToInt(sr * dur);
        var data = new float[samples];
        var fade = Mathf.RoundToInt(sr * 0.003f);
        var ph = 0f;
        var rng = new System.Random(77);
        for (var i = 0; i < samples; i++)
        {
            var u = (float)i / samples;
            var freq = Mathf.Lerp(620f, 165f, Mathf.Pow(u, 0.4f));
            ph += (2f * Mathf.PI * freq) / sr;
            var snap = (float)(rng.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-u * 26f) * 0.5f;
            var body = Mathf.Sin(ph) * Mathf.Exp(-u * 12f) * 0.6f;
            data[i] = Mathf.Clamp((snap + body) * Envelope5MS(i, samples, fade), -0.95f, 0.95f);
        }
        var clip = AudioClip.Create("SM64_PiranhaChomp", samples, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static AudioClip CreateSwitchTickClip(int sr)
    {
        const float dur = 0.042f;
        var samples = Mathf.RoundToInt(sr * dur);
        var data = new float[samples];
        var fade = Mathf.RoundToInt(sr * 0.002f);
        var ph = 0f;
        for (var i = 0; i < samples; i++)
        {
            var u = (float)i / samples;
            var freq = Mathf.Lerp(1450f, 880f, u);
            ph += (2f * Mathf.PI * freq) / sr;
            var env = Mathf.Exp(-u * 10f);
            data[i] = Mathf.Sin(ph) * env * 0.7f * Envelope5MS(i, samples, fade);
        }
        var clip = AudioClip.Create("SM64_SwitchTick", samples, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static AudioClip CreateEnemyDefeatClip(int sr)
    {
        const float dur = 0.24f;
        var samples = Mathf.RoundToInt(sr * dur);
        var data = new float[samples];
        var fade = Mathf.RoundToInt(sr * 0.004f);
        var ph = 0f;
        for (var i = 0; i < samples; i++)
        {
            var u = (float)i / samples;
            var freq = Mathf.Lerp(280f, 920f, u * u);
            ph += (2f * Mathf.PI * freq) / sr;
            data[i] = Mathf.Sin(ph) * Mathf.Pow(1f - u, 0.7f) * 0.55f * Envelope5MS(i, samples, fade);
        }
        var clip = AudioClip.Create("SM64_EnemyDefeat", samples, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static AudioClip CreateCannonBlastClip(int sr)
    {
        const float dur = 0.48f;
        var samples = Mathf.RoundToInt(sr * dur);
        var data = new float[samples];
        var fade = Mathf.RoundToInt(sr * 0.005f);
        var boomPh = 0f;
        var whistlePh = 0f;
        var rng = new System.Random(196);
        for (var i = 0; i < samples; i++)
        {
            var u = (float)i / samples;
            boomPh += (2f * Mathf.PI * Mathf.Lerp(145f, 38f, Mathf.Pow(u, 0.35f))) / sr;
            whistlePh += (2f * Mathf.PI * Mathf.Lerp(320f, 980f, u)) / sr;
            var noise = (float)(rng.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-u * 9f) * 0.48f;
            var boom = Mathf.Sin(boomPh) * Mathf.Exp(-u * 6f) * 0.65f;
            var whistle = Mathf.Sin(whistlePh) * Mathf.Clamp01(u * 4f) * Mathf.Pow(1f - u, 0.9f) * 0.22f;
            data[i] = Mathf.Clamp((noise + boom + whistle) * Envelope5MS(i, samples, fade), -0.95f, 0.95f);
        }
        var clip = AudioClip.Create("SM64_CannonBlast", samples, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static AudioClip CreateMenuBlip(int sr, float f1, float f2, float dur)
    {
        var samples = Mathf.RoundToInt(sr * dur);
        var data = new float[samples];
        var fade = Mathf.RoundToInt(sr * 0.004f);
        var ph = 0f;
        for (var i = 0; i < samples; i++)
        {
            var u = (float)i / samples;
            var freq = u < 0.45f ? f1 : f2;
            ph += (2f * Mathf.PI * freq) / sr;
            data[i] = (Mathf.Sin(ph) * 0.7f + Mathf.Sin(ph * 2f) * 0.25f) * (1f - u * 0.6f) * 0.45f * Envelope5MS(i, samples, fade);
        }
        var clip = AudioClip.Create("SM64_MenuBlip", samples, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static AudioClip CreateCourseMarchLoop(int sr)
    {
        // Upbeat 8-bar (6.4s @ 150 BPM eighth steps) bouncy adventure march with walking bass & brass lead
        const float stepDur = 0.20f;
        const int steps = 32;
        const float totalDur = stepDur * steps; // 6.4s exact loop
        var samples = Mathf.RoundToInt(sr * totalDur);
        var data = new float[samples];
        var rng = new System.Random(100);

        float[] bassHz = {
            130.81f, 0f, 196.00f, 0f, 164.81f, 0f, 196.00f, 0f,
            174.61f, 0f, 220.00f, 0f, 196.00f, 0f, 146.83f, 0f,
            130.81f, 0f, 196.00f, 0f, 220.00f, 0f, 164.81f, 0f,
            174.61f, 0f, 196.00f, 0f, 130.81f, 0f, 196.00f, 0f
        };
        float[] leadHz = {
            523.25f, 659.25f, 783.99f, 0f, 659.25f, 783.99f, 880.00f, 0f,
            698.46f, 880.00f, 783.99f, 659.25f, 587.33f, 0f, 659.25f, 587.33f,
            523.25f, 587.33f, 659.25f, 783.99f, 880.00f, 783.99f, 659.25f, 0f,
            698.46f, 659.25f, 587.33f, 783.99f, 523.25f, 0f, 783.99f, 523.25f
        };

        for (var s = 0; s < steps; s++)
        {
            var startIdx = Mathf.RoundToInt(s * stepDur * sr);
            var len = Mathf.RoundToInt(stepDur * sr);
            var bFreq = bassHz[s];
            var lFreq = leadHz[s];
            float bPh = 0f, lPh = 0f;

            for (var i = 0; i < len && startIdx + i < samples; i++)
            {
                var u = (float)i / len;
                var val = 0f;

                if (bFreq > 1f)
                {
                    bPh += (2f * Mathf.PI * bFreq) / sr;
                    var bEnv = Mathf.Exp(-u * 4.2f);
                    val += (Mathf.Sin(bPh) * 0.65f + Mathf.Sin(bPh * 2f) * 0.2f) * bEnv * 0.24f;
                }

                if (lFreq > 1f)
                {
                    lPh += (2f * Mathf.PI * lFreq) / sr;
                    var lEnv = Mathf.Clamp01(u * 14f) * Mathf.Exp(-u * 3.1f);
                    var brass = Mathf.Sin(lPh) * 0.55f + (Mathf.Sin(lPh) > 0f ? 0.14f : -0.14f) + Mathf.Sin(lPh * 2f) * 0.16f;
                    val += brass * lEnv * 0.19f;
                }

                // Crisp light shaker hi-hat on offbeats
                if ((s % 2 == 1) && u < 0.35f)
                {
                    var hat = (float)(rng.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-u * 24f) * 0.06f;
                    val += hat;
                }

                data[startIdx + i] = Mathf.Clamp(data[startIdx + i] + val, -0.9f, 0.9f);
            }
        }

        var fade = Mathf.RoundToInt(sr * 0.025f);
        for (var i = 0; i < samples; i++) data[i] *= Envelope5MS(i, samples, fade);

        var clip = AudioClip.Create("SM64_CourseMarch", samples, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static AudioClip CreateOceanBayLoop(int sr)
    {
        // Dreamy 8s Dire Dire Docks / Jolly Roger Bay style electric piano & warm pad arpeggio loop
        const float stepDur = 0.25f;
        const int steps = 32;
        const float totalDur = stepDur * steps; // 8.0s loop
        var samples = Mathf.RoundToInt(sr * totalDur);
        var data = new float[samples];

        float[] arpHz = {
            261.63f, 329.63f, 392.00f, 493.88f, 392.00f, 329.63f, 493.88f, 392.00f,
            293.66f, 349.23f, 440.00f, 523.25f, 440.00f, 349.23f, 523.25f, 440.00f,
            246.94f, 329.63f, 392.00f, 493.88f, 392.00f, 329.63f, 440.00f, 392.00f,
            220.00f, 261.63f, 329.63f, 392.00f, 440.00f, 392.00f, 329.63f, 293.66f
        };
        float[] padRootHz = { 130.81f, 146.83f, 123.47f, 110.00f };

        for (var s = 0; s < steps; s++)
        {
            var startIdx = Mathf.RoundToInt(s * stepDur * sr);
            var len = Mathf.RoundToInt(stepDur * 1.4f * sr);
            var freq = arpHz[s];
            var pRoot = padRootHz[s / 8];
            float ph = 0f, phBell = 0f, rPh = 0f;

            for (var i = 0; i < len && startIdx + i < samples; i++)
            {
                var u = (float)i / len;
                ph += (2f * Mathf.PI * freq) / sr;
                phBell += (2f * Mathf.PI * freq * 3f) / sr;
                rPh += (2f * Mathf.PI * pRoot) / sr;

                var epEnv = Mathf.Clamp01(u * 20f) * Mathf.Pow(1f - u, 1.4f);
                var ep = (Mathf.Sin(ph) * 0.72f + Mathf.Sin(phBell) * Mathf.Exp(-u * 8f) * 0.24f) * epEnv * 0.22f;
                var pad = (Mathf.Sin(rPh) * 0.55f + Mathf.Sin(rPh * 1.5f) * 0.25f) * 0.14f;

                data[startIdx + i] = Mathf.Clamp(data[startIdx + i] + ep + pad, -0.9f, 0.9f);
            }
        }

        var fade = Mathf.RoundToInt(sr * 0.03f);
        for (var i = 0; i < samples; i++) data[i] *= Envelope5MS(i, samples, fade);

        var clip = AudioClip.Create("SM64_OceanBayWaltz", samples, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static AudioClip CreateDarkWorldOrganLoop(int sr)
    {
        // Dramatic SM64 Bowser in the Dark World: pipe-organ gothic triad + driving rock bass ostinato (6.4s loop)
        const float stepDur = 0.20f;
        const int steps = 32;
        const float totalDur = stepDur * steps;
        var samples = Mathf.RoundToInt(sr * totalDur);
        var data = new float[samples];
        var rng = new System.Random(664);

        float[] organBass = {
            98.00f, 98.00f, 116.54f, 130.81f, 98.00f, 146.83f, 138.59f, 130.81f,
            98.00f, 98.00f, 116.54f, 130.81f, 155.56f, 146.83f, 130.81f, 116.54f,
            87.31f, 87.31f, 103.83f, 116.54f, 146.83f, 130.81f, 116.54f, 103.83f,
            98.00f, 116.54f, 130.81f, 146.83f, 155.56f, 146.83f, 130.81f, 116.54f
        };
        float[] organLead = {
            392.00f, 0f, 466.16f, 523.25f, 587.33f, 0f, 554.37f, 523.25f,
            392.00f, 0f, 466.16f, 587.33f, 622.25f, 587.33f, 523.25f, 466.16f,
            349.23f, 0f, 415.30f, 466.16f, 587.33f, 523.25f, 466.16f, 415.30f,
            392.00f, 466.16f, 523.25f, 587.33f, 622.25f, 0f, 587.33f, 466.16f
        };

        for (var s = 0; s < steps; s++)
        {
            var startIdx = Mathf.RoundToInt(s * stepDur * sr);
            var len = Mathf.RoundToInt(stepDur * 1.1f * sr);
            var bHz = organBass[s];
            var lHz = organLead[s];
            float bPh = 0f, lPh = 0f;

            for (var i = 0; i < len && startIdx + i < samples; i++)
            {
                var u = (float)i / len;
                var val = 0f;
                bPh += (2f * Mathf.PI * bHz) / sr;
                var bassWave = (Mathf.Sin(bPh) * 0.55f + Mathf.Sin(bPh * 2f) * 0.3f + (Mathf.Sin(bPh) > 0f ? 0.18f : -0.18f));
                val += bassWave * Mathf.Exp(-u * 2.2f) * 0.22f;

                if (lHz > 1f)
                {
                    lPh += (2f * Mathf.PI * lHz) / sr;
                    var org = Mathf.Sin(lPh) * 0.45f + Mathf.Sin(lPh * 2f) * 0.28f + Mathf.Sin(lPh * 3f) * 0.16f + Mathf.Sin(lPh * 4f) * 0.09f;
                    var env = Mathf.Clamp01(u * 18f) * Mathf.Exp(-u * 1.6f);
                    val += org * env * 0.18f;
                }

                if (s % 2 == 0 && u < 0.28f)
                {
                    val += (float)(rng.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-u * 22f) * 0.08f;
                }
                data[startIdx + i] = Mathf.Clamp(data[startIdx + i] + val, -0.92f, 0.92f);
            }
        }
        var fade = Mathf.RoundToInt(sr * 0.03f);
        for (var i = 0; i < samples; i++) data[i] *= Envelope5MS(i, samples, fade);
        var clip = AudioClip.Create("SM64_DarkWorldOrgan", samples, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static AudioClip CreateCoolMountainLoop(int sr)
    {
        const float stepDur = 0.21f;
        const int steps = 32;
        var samples = Mathf.RoundToInt(stepDur * steps * sr);
        var data = new float[samples];
        var rng = new System.Random(404);
        float[] bass = {
            196.00f, 0f, 293.66f, 246.94f, 196.00f, 0f, 293.66f, 246.94f,
            174.61f, 0f, 261.63f, 220.00f, 196.00f, 0f, 293.66f, 246.94f,
            196.00f, 0f, 293.66f, 246.94f, 164.81f, 0f, 246.94f, 196.00f,
            146.83f, 0f, 220.00f, 196.00f, 196.00f, 246.94f, 293.66f, 0f
        };
        float[] lead = {
            783.99f, 987.77f, 1174.66f, 987.77f, 783.99f, 659.25f, 783.99f, 880.00f,
            698.46f, 880.00f, 1046.50f, 880.00f, 783.99f, 0f, 659.25f, 587.33f,
            783.99f, 987.77f, 1174.66f, 1318.51f, 1174.66f, 987.77f, 783.99f, 659.25f,
            587.33f, 698.46f, 880.00f, 783.99f, 659.25f, 587.33f, 523.25f, 0f
        };

        for (var s = 0; s < steps; s++)
        {
            var startIdx = Mathf.RoundToInt(s * stepDur * sr);
            var len = Mathf.RoundToInt(stepDur * 1.15f * sr);
            float bPh = 0f, lPh = 0f;
            for (var i = 0; i < len && startIdx + i < samples; i++)
            {
                var u = (float)i / len;
                var val = 0f;
                if (bass[s] > 1f)
                {
                    bPh += (2f * Mathf.PI * bass[s]) / sr;
                    val += (Mathf.Sin(bPh) * 0.7f + Mathf.Sin(bPh * 2f) * 0.25f) * Mathf.Exp(-u * 4.5f) * 0.17f;
                }
                if (lead[s] > 1f)
                {
                    lPh += (2f * Mathf.PI * lead[s]) / sr;
                    var bell = Mathf.Sin(lPh) * 0.65f + Mathf.Sin(lPh * 2.0f) * 0.25f + Mathf.Sin(lPh * 3.0f) * 0.1f;
                    val += bell * Mathf.Exp(-u * 3.6f) * 0.16f;
                }
                if (u < 0.18f)
                {
                    val += (float)(rng.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-u * 32f) * 0.045f; // crisp sleigh bell tick
                }
                data[startIdx + i] = Mathf.Clamp(data[startIdx + i] + val, -0.92f, 0.92f);
            }
        }
        var fade = Mathf.RoundToInt(sr * 0.03f);
        for (var i = 0; i < samples; i++) data[i] *= Envelope5MS(i, samples, fade);
        var clip = AudioClip.Create("SM64_CoolMountainBell", samples, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static AudioClip CreateBowserRoarClip(int sr)
    {
        const float dur = 0.65f;
        var samples = Mathf.RoundToInt(sr * dur);
        var data = new float[samples];
        var fade = Mathf.RoundToInt(sr * 0.008f);
        var ph = 0f;
        var modPh = 0f;
        var rng = new System.Random(808);
        for (var i = 0; i < samples; i++)
        {
            var u = (float)i / samples;
            var freq = Mathf.Lerp(155f, 68f, Mathf.Pow(u, 0.6f));
            modPh += (2f * Mathf.PI * 38f) / sr;
            ph += (2f * Mathf.PI * (freq + Mathf.Sin(modPh) * 18f)) / sr;
            var growl = Mathf.Clamp(Mathf.Sin(ph) * 1.6f, -0.85f, 0.85f) + (float)(rng.NextDouble() * 2.0 - 1.0) * 0.28f;
            var env = Mathf.Clamp01(u * 10f) * Mathf.Pow(1f - u, 0.55f);
            data[i] = Mathf.Clamp(growl * env * 0.58f * Envelope5MS(i, samples, fade), -0.95f, 0.95f);
        }
        var clip = AudioClip.Create("SM64_BowserRoar", samples, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static AudioClip CreateBowserBombExplosionClip(int sr)
    {
        const float dur = 0.85f;
        var samples = Mathf.RoundToInt(sr * dur);
        var data = new float[samples];
        var fade = Mathf.RoundToInt(sr * 0.008f);
        var boomPh = 0f;
        var rng = new System.Random(909);
        for (var i = 0; i < samples; i++)
        {
            var u = (float)i / samples;
            boomPh += (2f * Mathf.PI * Mathf.Lerp(130f, 32f, Mathf.Pow(u, 0.35f))) / sr;
            var sub = Mathf.Sin(boomPh) * Mathf.Exp(-u * 4.2f) * 0.72f;
            var blast = (float)(rng.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-u * 5.8f) * 0.62f;
            data[i] = Mathf.Clamp((sub + blast) * Envelope5MS(i, samples, fade), -0.98f, 0.98f);
        }
        var clip = AudioClip.Create("SM64_BowserBomb", samples, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static AudioClip CreatePeachCastleLoop(int sr)
    {
        // Gentle music-box / string waltz inspired by Peach's Castle Courtyard & Foyer (3/4 feel)
        const float stepDur = 0.24f;
        const int steps = 36; // 12 bars of 3 beats
        var samples = Mathf.RoundToInt(stepDur * steps * sr);
        var data = new float[samples];
        float[] bass = {
            174.61f, 261.63f, 349.23f,  174.61f, 261.63f, 349.23f,
            196.00f, 293.66f, 392.00f,  130.81f, 261.63f, 329.63f,
            174.61f, 261.63f, 349.23f,  146.83f, 220.00f, 293.66f,
            196.00f, 293.66f, 392.00f,  130.81f, 261.63f, 392.00f,
            174.61f, 220.00f, 261.63f,  174.61f, 261.63f, 349.23f,
            196.00f, 261.63f, 130.81f,  174.61f, 261.63f, 0f
        };
        float[] lead = {
            698.46f, 0f, 880.00f,       1046.50f, 880.00f, 698.46f,
            783.99f, 659.25f, 587.33f,  659.25f, 523.25f, 0f,
            698.46f, 783.99f, 880.00f,  783.99f, 698.46f, 587.33f,
            659.25f, 783.99f, 880.00f,  783.99f, 0f, 523.25f,
            698.46f, 880.00f, 1046.50f, 1174.66f, 1046.50f, 880.00f,
            783.99f, 659.25f, 587.33f,  698.46f, 0f, 0f
        };

        for (var s = 0; s < steps; s++)
        {
            var startIdx = Mathf.RoundToInt(s * stepDur * sr);
            var len = Mathf.RoundToInt(stepDur * 1.45f * sr);
            float bPh = 0f, lPh = 0f;
            for (var i = 0; i < len && startIdx + i < samples; i++)
            {
                var u = (float)i / len;
                var val = 0f;
                if (bass[s] > 1f)
                {
                    bPh += (2f * Mathf.PI * bass[s]) / sr;
                    var harp = Mathf.Sin(bPh) * 0.72f + Mathf.Sin(bPh * 2f) * 0.18f;
                    val += harp * Mathf.Clamp01(u * 14f) * Mathf.Exp(-u * 3.4f) * 0.14f;
                }
                if (lead[s] > 1f)
                {
                    lPh += (2f * Mathf.PI * lead[s]) / sr;
                    var flute = Mathf.Sin(lPh) * 0.68f + Mathf.Sin(lPh * 2f) * 0.22f + Mathf.Sin(lPh * 3f) * 0.08f;
                    val += flute * Mathf.Clamp01(u * 10f) * Mathf.Exp(-u * 2.4f) * 0.17f;
                }
                data[startIdx + i] = Mathf.Clamp(data[startIdx + i] + val, -0.90f, 0.90f);
            }
        }
        var fade = Mathf.RoundToInt(sr * 0.03f);
        for (var i = 0; i < samples; i++) data[i] *= Envelope5MS(i, samples, fade);
        var clip = AudioClip.Create("SM64_PeachCastleWaltz", samples, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static AudioClip CreatePaintingWarpClip(int sr)
    {
        // Classic watery painting-portal warble ("Ba-ba-ba-ba-ba!" shimmer)
        const float dur = 0.65f;
        var samples = Mathf.RoundToInt(sr * dur);
        var data = new float[samples];
        var fade = Mathf.RoundToInt(sr * 0.006f);
        var ph = 0f;
        var wPh = 0f;
        for (var i = 0; i < samples; i++)
        {
            var u = (float)i / samples;
            wPh += (2f * Mathf.PI * 16f) / sr;
            var baseFreq = Mathf.Lerp(360f, 1120f, Mathf.Pow(u, 0.75f)) + Mathf.Sin(wPh) * 75f;
            ph += (2f * Mathf.PI * baseFreq) / sr;
            var wave = Mathf.Sin(ph) * 0.66f + Mathf.Sin(ph * 2.0f) * 0.24f;
            var pulse = 0.72f + 0.28f * Mathf.Sin(wPh);
            var env = Mathf.Clamp01(u * 14f) * Mathf.Pow(1f - u, 0.45f);
            data[i] = Mathf.Clamp(wave * pulse * env * 0.62f * Envelope5MS(i, samples, fade), -0.95f, 0.95f);
        }
        var clip = AudioClip.Create("SM64_PaintingWarp", samples, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static AudioClip CreatePowerUpCapClip(int sr)
    {
        // Energetic ascending triad chord arpeggio for Wing / Metal Cap
        float[] notes = { 523.25f, 659.25f, 783.99f, 1046.50f, 1318.51f };
        const float noteDur = 0.095f;
        var totalDur = notes.Length * noteDur + 0.16f;
        var samples = Mathf.RoundToInt(totalDur * sr);
        var data = new float[samples];
        for (var n = 0; n < notes.Length; n++)
        {
            var startIdx = Mathf.RoundToInt(n * noteDur * sr);
            var len = Mathf.RoundToInt((n == notes.Length - 1 ? 0.25f : 0.14f) * sr);
            var ph = 0f;
            for (var i = 0; i < len && startIdx + i < samples; i++)
            {
                var u = (float)i / len;
                ph += (2f * Mathf.PI * notes[n]) / sr;
                var brass = Mathf.Sin(ph) * 0.62f + Mathf.Sin(ph * 2f) * 0.28f + Mathf.Sin(ph * 3f) * 0.12f;
                var env = Mathf.Clamp01(u * 16f) * Mathf.Exp(-u * 2.8f);
                data[startIdx + i] = Mathf.Clamp(data[startIdx + i] + brass * env * 0.42f, -0.95f, 0.95f);
            }
        }
        var fade = Mathf.RoundToInt(sr * 0.005f);
        for (var i = 0; i < samples; i++) data[i] *= Envelope5MS(i, samples, fade);
        var clip = AudioClip.Create("SM64_PowerUpCap", samples, 1, sr, false);
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
