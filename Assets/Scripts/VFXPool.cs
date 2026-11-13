using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.Serialization;

/// <summary>
/// VFX Manager & Object Pool (vfx.md floor):
/// - Palette-only colors (never default white/gray)
/// - Short-lived event bursts (0.3-0.8s) with size-over-lifetime shrinking to zero
/// - Pooled with UnityEngine.Pool.ObjectPool<ParticleSystem> (zero runtime Instantiate leaks)
/// - Ambient motes drifting in the courtyard key light
/// </summary>
public class VFXPool : MonoBehaviour
{
    private static VFXPool instance;
    public static VFXPool Instance
    {
        get
        {
            if (instance == null) instance = FindAnyObjectByType<VFXPool>();
            if (instance != null) instance.EnsureInitialized();
            return instance;
        }
    }

    [FormerlySerializedAs("_particleMaterial")] [SerializeField] private Material particleMaterial;

    private ObjectPool<ParticleSystem> _pickupBurstPool;
    private ObjectPool<ParticleSystem> _footstepPuffPool;
    private bool _initialized;

    private int CreatedBurstInstances { get; set; }
    private ParticleSystem AmbientMotes { get; set; }
    private ParticleSystem MilestoneConfetti { get; set; }

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
        if (_initialized && _pickupBurstPool != null) return;
        _initialized = true;
        EnsureParticleMaterial();
        InitializePools();
        if (MilestoneConfetti == null) CreateMilestoneConfettiSystem();
        if (AmbientMotes == null) CreateAmbientMotesSystem();
    }

    private void EnsureParticleMaterial()
    {
        if (particleMaterial != null) return;
        var sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        particleMaterial = new Material(sh)
        {
            name = "PaletteParticleMat",
            color = Palette.AccentStarGold
        };
    }

    private void InitializePools()
    {
        _pickupBurstPool = new ObjectPool<ParticleSystem>(
            createFunc: CreatePickupBurstInstance,
            actionOnGet: ps =>
            {
                ps.gameObject.SetActive(true);
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            },
            actionOnRelease: ps =>
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.gameObject.SetActive(false);
            },
            actionOnDestroy: ps =>
            {
                if (ps == null) return;
                if (Application.isPlaying) Destroy(ps.gameObject);
                else DestroyImmediate(ps.gameObject);
            },
            collectionCheck: false,
            defaultCapacity: 6,
            maxSize: 16
        );

        _footstepPuffPool = new ObjectPool<ParticleSystem>(
            createFunc: CreateFootstepPuffInstance,
            actionOnGet: ps =>
            {
                ps.gameObject.SetActive(true);
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            },
            actionOnRelease: ps =>
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.gameObject.SetActive(false);
            },
            actionOnDestroy: ps =>
            {
                if (ps == null) return;
                if (Application.isPlaying) Destroy(ps.gameObject);
                else DestroyImmediate(ps.gameObject);
            },
            collectionCheck: false,
            defaultCapacity: 6,
            maxSize: 16
        );

        // Pre-warm 4 instances so firing 10 times reuses pooled instances with 0 new allocations
        var prewarm = new ParticleSystem[4];
        for (var i = 0; i < prewarm.Length; i++) prewarm[i] = _pickupBurstPool.Get();
        foreach (var t in prewarm)
            _pickupBurstPool.Release(t);
    }

    private ParticleSystem CreatePickupBurstInstance()
    {
        CreatedBurstInstances++;
        var go = new GameObject($"PooledPickupBurst_{CreatedBurstInstances}");
        go.transform.SetParent(transform, false);

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 0.6f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.42f, 0.68f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2.4f, 4.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.22f, 0.36f);
        main.gravityModifier = 0.35f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startColor = new ParticleSystem.MinMaxGradient(Palette.AccentStarGold, Palette.CharacterPrimary);

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 12) });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.25f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[]
            {
                new GradientColorKey(Palette.AccentStarGold, 0f),
                new GradientColorKey(Palette.PropsTerracotta, 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0.85f, 0.6f),
                new GradientAlphaKey(0f, 1f)
            }
        );
        col.color = new ParticleSystem.MinMaxGradient(grad);

        var sz = ps.sizeOverLifetime;
        sz.enabled = true;
        var sizeCurve = new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(0.5f, 0.85f),
            new Keyframe(1f, 0f)
        );
        sz.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = particleMaterial;

        return ps;
    }

    private ParticleSystem CreateFootstepPuffInstance()
    {
        var go = new GameObject("PooledFootstepPuff");
        go.transform.SetParent(transform, false);

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 0.35f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.38f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.16f, 0.26f);
        main.gravityModifier = -0.1f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startColor = new ParticleSystem.MinMaxGradient(Palette.WallsStone, Palette.FloorGrass);

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 5) });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = 0.2f;

        var sz = ps.sizeOverLifetime;
        sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = particleMaterial;

        return ps;
    }

    private void CreateMilestoneConfettiSystem()
    {
        var go = new GameObject("MilestoneConfettiSystem");
        go.transform.SetParent(transform, false);

        MilestoneConfetti = go.AddComponent<ParticleSystem>();
        MilestoneConfetti.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = MilestoneConfetti.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 1.25f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.35f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(4.5f, 8.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.24f, 0.42f);
        main.gravityModifier = 0.85f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startColor = new ParticleSystem.MinMaxGradient(Palette.AccentStarGold, Palette.CharacterPrimary);

        var emission = MilestoneConfetti.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 42) });

        var shape = MilestoneConfetti.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 32f;
        shape.radius = 0.35f;
        go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

        var sz = MilestoneConfetti.sizeOverLifetime;
        sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = particleMaterial;
    }

    private void CreateAmbientMotesSystem()
    {
        var go = new GameObject("AmbientSunlitMotes");
        go.transform.SetParent(transform, false);
        go.transform.position = new Vector3(0f, 5.5f, 6f);

        AmbientMotes = go.AddComponent<ParticleSystem>();
        AmbientMotes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = AmbientMotes.main;
        main.playOnAwake = true;
        main.loop = true;
        main.prewarm = true;
        main.duration = 8f;
        main.maxParticles = 24;
        main.startLifetime = new ParticleSystem.MinMaxCurve(4.5f, 7.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.38f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.30f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startColor = new ParticleSystem.MinMaxGradient(
            Palette.WithAlpha(Palette.AccentStarGold, 0.65f),
            Palette.WithAlpha(Palette.KeyLightWarm, 0.55f)
        );

        var emission = AmbientMotes.emission;
        emission.enabled = true;
        emission.rateOverTime = 4.5f;

        var shape = AmbientMotes.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(26f, 8f, 26f);

        var sz = AmbientMotes.sizeOverLifetime;
        sz.enabled = true;
        var pulse = new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.25f, 1f),
            new Keyframe(0.75f, 1f),
            new Keyframe(1f, 0f)
        );
        sz.size = new ParticleSystem.MinMaxCurve(1f, pulse);

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = particleMaterial;

        AmbientMotes.Play();
    }

    public void SpawnPickupBurst(Vector3 position, bool isStar = false)
    {
        EnsureInitialized();
        var ps = _pickupBurstPool.Get();
        ps.transform.position = position;
        var main = ps.main;
        main.startColor = isStar
            ? new ParticleSystem.MinMaxGradient(Palette.AccentStarGold, Palette.KeyLightWarm)
            : new ParticleSystem.MinMaxGradient(Palette.AccentStarGold, Palette.CharacterPrimary);
        ps.Play();
        StartCoroutine(ReleaseAfterDelay(_pickupBurstPool, ps, 0.72f));
    }

    public void SpawnFootstepPuff(Vector3 position)
    {
        EnsureInitialized();
        var ps = _footstepPuffPool.Get();
        ps.transform.position = position;
        ps.Play();
        StartCoroutine(ReleaseAfterDelay(_footstepPuffPool, ps, 0.42f));
    }

    public void SpawnMilestoneConfetti(Vector3 position)
    {
        EnsureInitialized();
        if (MilestoneConfetti == null) return;
        MilestoneConfetti.transform.position = position + Vector3.up * 0.5f;
        MilestoneConfetti.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        MilestoneConfetti.Play();
    }

    private static System.Collections.IEnumerator ReleaseAfterDelay(ObjectPool<ParticleSystem> pool, ParticleSystem ps, float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        if (ps != null && ps.gameObject.activeSelf)
        {
            pool.Release(ps);
        }
    }
}

