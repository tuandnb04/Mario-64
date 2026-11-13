using System;
using System.Collections;
using Mario64Remake;
using UnityEngine;

namespace Mario64Remake
{
    public enum DarkWorldHazardType
    {
        PurpleSwitchCrystal,
        SwitchTimedStairStep,
        TiltingSeesawBridge,
        MovingDiamondLift,
        SlidingPusherBlock,
        FlameJetNozzle,
        OrbitingAmp,
        BowserWarpPipe,
        FerrisWheelRotator
    }
}

/// <summary>
/// Interactive hazards & platforming mechanics for Course "Bowser in the Dark World":
/// - PurpleSwitchCrystal: stepping/ground-pounding lights up & elevates linked timed crystal staircase steps for 14s
/// - TiltingSeesawBridge: seesaw walkway rocking cleanly across void chasms (Mario rides cleanly!)
/// - MovingDiamondLift: floating diamond platform translating along moveOffset
/// - FlameJetNozzle: rhythmic fire-breathing hazard nozzle along platform edge
/// - OrbitingAmp: electrified spiky ball orbiting its pivot & shocking Mario on contact
/// - BowserWarpPipe: warps Mario from end of obstacle course onto the high Bowser Battle Arena!
/// </summary>
public class BowserDarkWorldElement : MonoBehaviour
{
    private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
    public DarkWorldHazardType hazardType = DarkWorldHazardType.TiltingSeesawBridge;

    [Header("Motion / Timing")]
    public Vector3 moveOffset = new(0f, 0f, 6f);
    public float cycleSpeed = 0.85f;
    public float phaseOffset;
    public float tiltMaxAngle = 14f;
    public Vector3 tiltAxis = Vector3.forward;
    public float orbitRadius = 2.6f;

    [Header("Switch Staircase Links")]
    public BowserDarkWorldElement[] linkedSwitchSteps;
    public float switchDuration = 22f;
    public int stepOrderIndex;
    public Renderer ghostWireframeVisual;

    [Header("Auto 1x1 Step Generator (Purple Switch)")]
    [Min(0)] public int autoStepCount = 5;
    public GameObject stepPrefab;
    public Vector3 stepSize = Vector3.one;
    public Vector3 firstStepLocalOffset = new(0f, 0.5f, 2.5f);
    public Vector3 stepSpacing = new(0f, 0.5f, 1.5f);
    [Min(0f)] public float stepSequenceInterval = 0.12f;
    [HideInInspector] public Transform generatedStepsContainer;

    [Header("Ferris Wheel Children")]
    public Transform[] ferrisPlatforms;

    [Header("Flame Jet References")]
    public Transform flameColumnVisual;
    public Renderer nozzleGlowRenderer;

    [Header("Warp Pipe Arena Transfer")]
    public Vector3 warpDestination = new(0f, 34.8f, 105.5f);
    public float warpFacingYaw;
    public BowserBossController arenaBoss;

    private Vector3 _originPos;
    private Quaternion _originRot;
    private Vector3 _originScale;
    private float _clock;
    private float _switchRemainingTimer;
    private float _switchTickTimer;
    private bool _stepActive;
    private float _stepActivationTimer;
    private float _cooldown;
    private bool _flameCurrentlyHot;
    private Transform[] _switchButtonParts;
    private Vector3[] _switchButtonOriginLocalPos;

    private void Awake()
    {
        _originPos = transform.position;
        _originRot = transform.rotation;
        _originScale = transform.localScale;
        switch (hazardType)
        {
            case DarkWorldHazardType.SwitchTimedStairStep:
                ApplyStepVisibility(false);
                break;
            case DarkWorldHazardType.FlameJetNozzle when flameColumnVisual != null:
                flameColumnVisual.gameObject.SetActive(false);
                break;
            case DarkWorldHazardType.PurpleSwitchCrystal:
                CacheSwitchButtonParts();
                if ((linkedSwitchSteps == null || linkedSwitchSteps.Length == 0) && autoStepCount > 0)
                    GenerateSteps(runtimeSpawn: true);
                break;
            case DarkWorldHazardType.TiltingSeesawBridge:
            case DarkWorldHazardType.MovingDiamondLift:
            case DarkWorldHazardType.SlidingPusherBlock:
            case DarkWorldHazardType.OrbitingAmp:
            case DarkWorldHazardType.BowserWarpPipe:
            case DarkWorldHazardType.FerrisWheelRotator:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public void ResetDarkWorldElement()
    {
        if (_originPos.sqrMagnitude > 0.0001f) transform.SetPositionAndRotation(_originPos, _originRot);
        if (_originScale.sqrMagnitude > 0.0001f) transform.localScale = _originScale;
        _switchRemainingTimer = 0f;
        _stepActive = false;
        _stepActivationTimer = 0f;
        _cooldown = 0f;
        _flameCurrentlyHot = false;
        switch (hazardType)
        {
            case DarkWorldHazardType.SwitchTimedStairStep:
                ApplyStepVisibility(false);
                break;
            case DarkWorldHazardType.FlameJetNozzle when flameColumnVisual != null:
                flameColumnVisual.gameObject.SetActive(false);
                break;
            case DarkWorldHazardType.PurpleSwitchCrystal:
                CacheSwitchButtonParts();
                if ((linkedSwitchSteps == null || linkedSwitchSteps.Length == 0) && autoStepCount > 0)
                    GenerateSteps(runtimeSpawn: true);
                break;
            case DarkWorldHazardType.TiltingSeesawBridge:
            case DarkWorldHazardType.MovingDiamondLift:
            case DarkWorldHazardType.SlidingPusherBlock:
            case DarkWorldHazardType.OrbitingAmp:
            case DarkWorldHazardType.BowserWarpPipe:
            case DarkWorldHazardType.FerrisWheelRotator:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
        gameObject.SetActive(true);
    }

    private void Update()
    {
        if (_originPos.sqrMagnitude < 0.0001f && transform.position.sqrMagnitude > 0.0001f)
        {
            _originPos = transform.position;
            _originRot = transform.rotation;
            _originScale = transform.localScale;
        }

        var dt = Time.deltaTime > 0.0001f ? Time.deltaTime : (Time.timeScale > 0f ? 0.016f * Time.timeScale : 0f);
        if (dt <= 0.00001f) return;

        if (_cooldown > 0f) _cooldown -= dt;
        _clock += dt * cycleSpeed;

        switch (hazardType)
        {
            case DarkWorldHazardType.PurpleSwitchCrystal:
                UpdatePurpleSwitch(dt);
                break;
            case DarkWorldHazardType.SwitchTimedStairStep:
                UpdateTimedStairStep(dt);
                break;
            case DarkWorldHazardType.TiltingSeesawBridge:
                UpdateTiltingSeesaw();
                break;
            case DarkWorldHazardType.MovingDiamondLift:
                UpdateMovingLift();
                break;
            case DarkWorldHazardType.SlidingPusherBlock:
                UpdatePusherBlock();
                break;
            case DarkWorldHazardType.FlameJetNozzle:
                UpdateFlameJet(dt);
                break;
            case DarkWorldHazardType.OrbitingAmp:
                UpdateOrbitingAmp(dt);
                break;
            case DarkWorldHazardType.BowserWarpPipe:
                UpdateWarpPipe();
                break;
            case DarkWorldHazardType.FerrisWheelRotator:
                UpdateFerrisWheel(dt);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void UpdateFerrisWheel(float dt)
    {
        transform.Rotate(0f, 0f, cycleSpeed * 34f * dt, Space.Self);
        if (ferrisPlatforms == null) return;
        foreach (var fp in ferrisPlatforms)
        {
            if (fp != null) fp.rotation = Quaternion.identity;
        }
    }

    private void CacheSwitchButtonParts()
    {
        var count = transform.childCount;
        if (count == 0) return;

        var parts = new System.Collections.Generic.List<Transform>(count);
        for (var i = 0; i < count; i++)
        {
            var child = transform.GetChild(i);
            if (child == null) continue;
            // Keep the outer gold base/rim fixed on the ground.
            if (string.Equals(child.name, "Plinth", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(child.name, "BasePlate", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            parts.Add(child);
        }

        _switchButtonParts = parts.ToArray();
        _switchButtonOriginLocalPos = new Vector3[_switchButtonParts.Length];
        for (var i = 0; i < _switchButtonParts.Length; i++)
            _switchButtonOriginLocalPos[i] = _switchButtonParts[i].localPosition;
    }

    private void ApplySwitchPressVisual(bool pressed, float dt, bool instant = false)
    {
        float pressOffset = pressed ? -0.07f : 0f;
        if (_switchButtonParts != null && _switchButtonParts.Length > 0)
        {
            for (var i = 0; i < _switchButtonParts.Length; i++)
            {
                var part = _switchButtonParts[i];
                if (part == null) continue;
                var target = _switchButtonOriginLocalPos[i] + new Vector3(0f, pressOffset, 0f);
                part.localPosition = instant ? target : Vector3.Lerp(part.localPosition, target, 16f * dt);
            }
        }
        else
        {
            var target = _originPos + new Vector3(0f, pressOffset, 0f);
            transform.position = instant ? target : Vector3.Lerp(transform.position, target, 16f * dt);
        }
    }

    private void UpdatePurpleSwitch(float dt)
    {
        if (_switchRemainingTimer > 0f)
        {
            _switchRemainingTimer -= dt;
            if (_switchRemainingTimer <= 0f)
            {
                _switchRemainingTimer = 0f;
                ApplySwitchPressVisual(false, dt, instant: true);
                if (linkedSwitchSteps != null)
                {
                    foreach (var step in linkedSwitchSteps)
                    {
                        if (step != null) step.DeactivateStep();
                    }
                }
            }
            else
            {
                ApplySwitchPressVisual(true, dt);

                // Classic SM64 tick-tick-tick countdown & urgent blink warning before disappearing
                float blinkThreshold = Mathf.Min(4.0f, switchDuration * 0.35f);
                bool urgent = _switchRemainingTimer <= blinkThreshold;
                float tickInterval = urgent ? (_switchRemainingTimer <= 1.5f ? 0.16f : 0.28f) : 0.52f;

                _switchTickTimer -= dt;
                if (_switchTickTimer <= 0f)
                {
                    _switchTickTimer = tickInterval;
                    if (GameAudio.Instance != null) GameAudio.Instance.PlaySwitchTick(urgent);
                }

                if (urgent && linkedSwitchSteps != null)
                {
                    float blinkFreq = _switchRemainingTimer <= 1.5f ? 12f : 6f;
                    bool showVisual = ((int)(_switchRemainingTimer * blinkFreq) % 2) == 0;
                    foreach (var step in linkedSwitchSteps)
                    {
                        if (step != null) step.SetStepBlinkVisual(showVisual);
                    }
                }
            }
        }
        else
        {
            ApplySwitchPressVisual(false, dt);
        }

        var hero = MarioRemakeController.Instance;
        if (hero == null || _cooldown > 0f || _switchRemainingTimer > 0f) return;
        if (Vector3.Distance(hero.transform.position, transform.position) < 1.6f)
        {
            ActivateSwitch();
        }
    }

    public void ActivateSwitch()
    {
        _cooldown = 1.0f;
        _switchRemainingTimer = switchDuration;
        _switchTickTimer = 0.25f;
        if (GameAudio.Instance != null) GameAudio.Instance.PlayMilestoneSting();
        if (VFXPool.Instance != null) VFXPool.Instance.SpawnPickupBurst(transform.position + Vector3.up * 0.5f, isStar: false);

        if (linkedSwitchSteps == null) return;
        for (var i = 0; i < linkedSwitchSteps.Length; i++)
        {
            if (linkedSwitchSteps[i] != null)
            {
                linkedSwitchSteps[i].ActivateStep(i * stepSequenceInterval);
            }
        }
    }

    [ContextMenu("Generate 1x1 Steps In Order")]
    public void GenerateStepsFromMenu() => GenerateSteps(runtimeSpawn: false);

    [ContextMenu("Clear Generated Steps")]
    public void ClearGeneratedSteps()
    {
        if (generatedStepsContainer != null)
        {
            if (Application.isPlaying) Destroy(generatedStepsContainer.gameObject);
            else DestroyImmediate(generatedStepsContainer.gameObject);
            generatedStepsContainer = null;
        }
        linkedSwitchSteps = Array.Empty<BowserDarkWorldElement>();
    }

    public void GenerateSteps(bool runtimeSpawn = false)
    {
        if (hazardType != DarkWorldHazardType.PurpleSwitchCrystal) return;

        ClearGeneratedSteps();
        if (autoStepCount <= 0) return;

        var containerGo = new GameObject($"{gameObject.name}_Steps");
        if (transform.parent != null)
            containerGo.transform.SetParent(transform.parent, false);
        containerGo.transform.SetPositionAndRotation(transform.position, transform.rotation);
        generatedStepsContainer = containerGo.transform;

        var steps = new BowserDarkWorldElement[autoStepCount];
        Material fallbackMat = null;

        for (var i = 0; i < autoStepCount; i++)
        {
            var localPos = firstStepLocalOffset + stepSpacing * i;
            var worldPos = transform.TransformPoint(localPos);

            GameObject stepGo;
            if (stepPrefab != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying && !runtimeSpawn)
                    stepGo = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(stepPrefab, containerGo.transform);
                else
                    stepGo = Instantiate(stepPrefab, containerGo.transform);
#else
                stepGo = Instantiate(stepPrefab, containerGo.transform);
#endif
                stepGo.transform.SetPositionAndRotation(worldPos, transform.rotation);
            }
            else
            {
                stepGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                stepGo.transform.SetParent(containerGo.transform, false);
                stepGo.transform.SetPositionAndRotation(worldPos, transform.rotation);

                if (fallbackMat == null)
                {
                    var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                    fallbackMat = new Material(shader);
                    fallbackMat.SetColor("_BaseColor", new Color(0.55f, 0.25f, 0.85f, 1f));
                }
                var mr = stepGo.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = fallbackMat;
            }

            stepGo.name = $"Step_{i + 1:00}";
            stepGo.transform.localScale = stepSize;

            var elem = stepGo.GetComponent<BowserDarkWorldElement>();
            if (elem == null) elem = stepGo.AddComponent<BowserDarkWorldElement>();
            elem.hazardType = DarkWorldHazardType.SwitchTimedStairStep;
            elem.stepOrderIndex = i;
            if (Application.isPlaying) elem.ApplyStepVisibility(false);

            steps[i] = elem;
        }

        linkedSwitchSteps = steps;
    }

    private void OnDrawGizmos()
    {
        if (hazardType != DarkWorldHazardType.PurpleSwitchCrystal) return;

        Gizmos.color = new Color(0.65f, 0.3f, 1f, 0.85f);
        if (linkedSwitchSteps != null && linkedSwitchSteps.Length > 0)
        {
            Vector3 prev = transform.position;
            for (var i = 0; i < linkedSwitchSteps.Length; i++)
            {
                var step = linkedSwitchSteps[i];
                if (step == null) continue;
                Gizmos.DrawLine(prev, step.transform.position);
                Gizmos.DrawWireCube(step.transform.position, step.transform.lossyScale);
                prev = step.transform.position;
            }
            return;
        }

        if (autoStepCount <= 0) return;
        Vector3 prevPreview = transform.position;
        for (var i = 0; i < autoStepCount; i++)
        {
            var worldPos = transform.TransformPoint(firstStepLocalOffset + stepSpacing * i);
            Gizmos.DrawLine(prevPreview, worldPos);
            Gizmos.matrix = Matrix4x4.TRS(worldPos, transform.rotation, stepSize);
            Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
            Gizmos.matrix = Matrix4x4.identity;
            prevPreview = worldPos;
        }
    }

    private void ActivateStep(float delay)
    {
        _stepActive = true;
        if (delay <= 0f)
        {
            _stepActivationTimer = 0f;
            ApplyStepVisibility(true);
        }
        else
        {
            _stepActivationTimer = delay;
        }
    }

    private void DeactivateStep()
    {
        _stepActive = false;
        ApplyStepVisibility(false);
    }

    public void SetStepBlinkVisual(bool visible)
    {
        if (!_stepActive || _stepActivationTimer > 0f) return;
        foreach (var mr in GetComponentsInChildren<MeshRenderer>(true))
        {
            if (mr == null) continue;
            if (ghostWireframeVisual != null && mr == ghostWireframeVisual) continue;
            mr.enabled = visible;
        }
    }

    private void UpdateTimedStairStep(float dt)
    {
        if (!_stepActive) return;
        if (!(_stepActivationTimer > 0f)) return;
        _stepActivationTimer -= dt;
        if (_stepActivationTimer <= 0f)
        {
            ApplyStepVisibility(true);
        }
    }

    private void ApplyStepVisibility(bool visible)
    {
        foreach (var col in GetComponentsInChildren<Collider>(true))
            if (col != null) col.enabled = visible;
        foreach (var mr in GetComponentsInChildren<MeshRenderer>(true))
        {
            if (mr == null) continue;
            if (ghostWireframeVisual != null && mr == ghostWireframeVisual)
                mr.enabled = !visible;
            else
                mr.enabled = visible;
        }
    }

    private void UpdateTiltingSeesaw()
    {
        var angle = Mathf.Sin((_clock + phaseOffset) * Mathf.PI) * tiltMaxAngle;
        var axis = tiltAxis.sqrMagnitude > 0.01f ? tiltAxis.normalized : Vector3.forward;
        transform.rotation = _originRot * Quaternion.AngleAxis(angle, axis);
    }

    private void UpdateMovingLift()
    {
        var ping = Mathf.PingPong(_clock + phaseOffset, 1.4f);
        var u = Mathf.Clamp01((ping - 0.15f) / 1.1f);
        transform.position = _originPos + moveOffset * Mathf.SmoothStep(0f, 1f, u);
    }

    private void UpdatePusherBlock()
    {
        var wave = Mathf.Sin((_clock + phaseOffset) * Mathf.PI);
        var u = Mathf.Clamp01(wave * 0.65f + 0.5f);
        transform.position = _originPos + moveOffset * Ease.OutCubic(u);
    }

    private void UpdateFlameJet(float dt)
    {
        // 4.2s cycle: 0..2.0 cool, 2.0..2.6 warning ember, 2.6..4.2 blazing jet!
        var cycle = Mathf.Repeat(_clock + phaseOffset, 4.2f);
        var warning = cycle is >= 1.95f and < 2.55f;
        var firing = cycle >= 2.55f;

        _flameCurrentlyHot = firing;

        if (flameColumnVisual != null)
        {
            if (firing != flameColumnVisual.gameObject.activeSelf)
                flameColumnVisual.gameObject.SetActive(firing);

            if (firing)
            {
                var flicker = 1f + Mathf.Sin(Time.time * 32f + phaseOffset * 5f) * 0.14f;
                flameColumnVisual.localScale = new Vector3(0.85f * flicker, 1.0f * flicker, 1f);
                flameColumnVisual.Rotate(0f, 220f * dt, 0f, Space.Self);
            }
        }

        if (nozzleGlowRenderer != null && nozzleGlowRenderer.material != null)
        {
            var glow = firing ? new Color(1.0f, 0.35f, 0.05f) : (warning ? new Color(0.85f, 0.45f, 0.05f) : new Color(0.15f, 0.08f, 0.08f));
            if (nozzleGlowRenderer.material.HasProperty(EmissionColor))
                nozzleGlowRenderer.material.SetColor(EmissionColor, glow * (firing ? 2.4f : warning ? 1.2f : 0.15f));
        }

        if (!_flameCurrentlyHot || _cooldown > 0f) return;
        var hero = MarioRemakeController.Instance;
        if (hero == null) return;

        // Check cylinder along flame nozzle's local forward/up (flameColumnVisual points outward)
        var flameDir = flameColumnVisual != null ? (flameColumnVisual.position - transform.position).normalized : transform.up;
        if (flameDir.sqrMagnitude < 0.01f) flameDir = Vector3.up;
        var closest = transform.position + flameDir * Mathf.Clamp(Vector3.Dot(hero.transform.position - transform.position, flameDir), 0.3f, 3.6f);
        if (!(Vector3.Distance(hero.transform.position + Vector3.up * 0.7f, closest) < 1.15f)) return;
        _cooldown = 0.85f;
        hero.TakeDamage(1, transform.position);
    }

    private void UpdateOrbitingAmp(float dt)
    {
        var ang = (_clock + phaseOffset) * 2.1f;
        var orbitPos = _originPos + new Vector3(Mathf.Cos(ang) * orbitRadius, Mathf.Sin(ang * 2f) * 0.22f, Mathf.Sin(ang) * orbitRadius);
        transform.position = orbitPos;
        transform.Rotate(0f, 240f * dt, 90f * dt, Space.Self);

        if (_cooldown > 0f) return;
        var hero = MarioRemakeController.Instance;
        if (hero == null) return;
        if (Vector3.Distance(hero.transform.position + Vector3.up * 0.7f, transform.position) < 1.15f)
        {
            _cooldown = 0.9f;
            hero.TakeDamage(1, transform.position);
        }
    }

    private void UpdateWarpPipe()
    {
        var hero = MarioRemakeController.Instance;
        if (hero == null || _cooldown > 0f) return;

        var planar = Vector2.Distance(
            new Vector2(hero.transform.position.x, hero.transform.position.z),
            new Vector2(transform.position.x, transform.position.z));

        if (planar < 1.38f && Mathf.Abs(hero.transform.position.y - (transform.position.y + 1.95f)) < 1.35f)
        {
            _cooldown = 2.0f;
            StartCoroutine(WarpMarioToBossArenaRoutine(hero));
        }
    }

    private IEnumerator WarpMarioToBossArenaRoutine(MarioRemakeController hero)
    {
        if (GameAudio.Instance != null) GameAudio.Instance.PlayCannonBlastSound();
        if (VFXPool.Instance != null) VFXPool.Instance.SpawnPickupBurst(transform.position + Vector3.up * 1.9f, isStar: true);

        yield return new WaitForSeconds(0.18f);

        if (hero != null)
        {
            hero.TeleportAndFace(warpDestination, warpFacingYaw);
        }

        if (VFXPool.Instance != null)
        {
            VFXPool.Instance.SpawnPickupBurst(warpDestination + Vector3.up * 1.0f, isStar: true);
        }

        if (arenaBoss != null)
        {
            arenaBoss.TriggerBossEntrance();
        }
    }
}

