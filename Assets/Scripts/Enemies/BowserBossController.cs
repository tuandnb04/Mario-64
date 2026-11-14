using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Authentic Super Mario 64 Boss Fight — BOWSER IN THE DARK WORLD:
/// -------------------------------------------------------------------
/// - Drops onto the circular Star Arena when Mario enters via the Warp Pipe.
/// - Attacks with heavy stalking footsteps (slow turn rate so agile Mario can get behind him!),
///   homing streams of Fireballs, and leaping Shockwave Stomps!
/// - Mario runs behind Bowser and presses Attack / Grab [Left Click / F / E] near his Tail!
/// - Mario latches onto Bowser's Tail_04 and spins him rapidly with A/D or Mouse Sweep!
/// - Releasing / clicking hurls Bowser sliding across the Arena:
///     * Impacting any Perimeter Spiked Bomb triggers an enormous explosion -> Bowser defeated -> drops GOLDEN BIG KEY / POWER STAR!
///     * Missing all bombs knocks Bowser over the arena edge briefly, then he leaps heroically back to Arena Center!
/// </summary>
public class BowserBossController : MonoBehaviour
{
    private enum BossState
    {
        Dormant,
        EntranceDrop,
        Stalking,
        FireBreathWindup,
        FireBreathing,
        LeapShockwave,
        Recovering,
        GrabbedAndSpun,
        HurledFlight,
        MissingEdgeRecover,
        Defeated
    }

    [Header("Arena Configuration")]
    public Vector3 arenaCenter = new(0f, 34f, 118f);
    public float arenaRadius = 16.5f;
    public int hitsToDefeat = 1; // Classic BitDW = 1 Spiked Bomb impact!
    public int currentBombHitsRemaining = 1;

    [Header("Movement & Combat Tunables")]
    public float stalkSpeed = 3.2f;
    public float turnSpeedDegPerSec = 78f;
    public float tailGrabRadius = 3.65f;

    [Header("References")]
    public Animator bowserAnimator;
    public Transform tailGrabPoint;
    public Transform mouthOrigin;
    public GameObject[] spikedBombs;
    public StarCoinCollectible rewardBigKeyStar;
    public GameObject fireballTemplate;

    private BossState _state = BossState.Dormant;
    private float _stateTimer;
    private Vector3 _originPos;
    private Quaternion _originRot;
    private Vector3 _velocity;
    private int _fireballsRemaining;
    private float _nextFireballTimer;
    private float _actionCycleTimer;
    private bool _leapAirborne;
    private float _spinAngleDeg;
    private float _spinAngularSpeedDeg;
    private float _spinHoldSeconds;
    private MarioRemakeController _grabbingHero;
    private Material _fireMat;
    private Material _shockMat;
    private GameObject _activeAimRing;
    private float _hudPromptCooldown;
    private ParticleSystem _fireFlamePs;
    private ParticleSystem _fireSmokePs;
    private Light _fireLight;
    private float _fireConeDamageCooldown;
    private Texture2D _softParticleTex;
    private readonly List<ActiveFireball> _activeFireballs = new();

    private class ActiveFireball
    {
        public GameObject Go;
        public Vector3 Vel;
        public float Life;
    }

    public bool CanBeTailGrabbed =>
        _state is BossState.Stalking or BossState.FireBreathWindup or BossState.FireBreathing or BossState.Recovering;

    public bool IsBeingSpun => _state == BossState.GrabbedAndSpun;

    private void Awake()
    {
        _originPos = transform.position;
        _originRot = transform.rotation;
        if (Vector3.Distance(_originPos, arenaCenter) > 35f)
        {
            arenaCenter = _originPos;
        }
        currentBombHitsRemaining = hitsToDefeat;
        ResolveBoneAnchors();
        CreateRuntimeVfxMaterials();
        BuildFireBreathRig();
    }

    private void ResolveBoneAnchors()
    {
        if (bowserAnimator == null) bowserAnimator = GetComponentInChildren<Animator>(true);
        if (tailGrabPoint == null) tailGrabPoint = FindChildDeep(transform, "Tail_04") ?? FindChildDeep(transform, "Tail_03");
        if (mouthOrigin == null) mouthOrigin = FindChildDeep(transform, "Jaw") ?? FindChildDeep(transform, "Head");
    }

    private static Transform FindChildDeep(Transform root, string targetName)
    {
        if (root == null) return null;
        for (var i = 0; i < root.childCount; i++)
        {
            var c = root.GetChild(i);
            if (c.name == targetName) return c;
            var hit = FindChildDeep(c, targetName);
            if (hit != null) return hit;
        }
        return null;
    }

    private void CreateRuntimeVfxMaterials()
    {
        var sh = Shader.Find("Mario64Remake/ToonLit") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        _fireMat = new Material(sh) { color = new Color(1.0f, 0.42f, 0.08f) };
        if (_fireMat.HasProperty(EmissionColor)) _fireMat.SetColor(EmissionColor, new Color(2.4f, 0.72f, 0.08f));
        _shockMat = new Material(sh) { color = new Color(0.85f, 0.25f, 1.0f) };
        if (_shockMat.HasProperty(EmissionColor)) _shockMat.SetColor(EmissionColor, new Color(1.2f, 0.35f, 1.8f));
    }

    public void ResetBoss()
    {
        if (_originPos.sqrMagnitude < 0.0001f) _originPos = transform.position;
        transform.SetPositionAndRotation(_originPos, _originRot);
        transform.localScale = Vector3.one;
        currentBombHitsRemaining = hitsToDefeat;
        _state = BossState.Dormant;
        _stateTimer = 0f;
        _velocity = Vector3.zero;
        _grabbingHero = null;
        StopFireBreathVfx();
        ClearActiveFireballs();
        if (rewardBigKeyStar != null) rewardBigKeyStar.gameObject.SetActive(false);
        if (spikedBombs != null)
        {
            foreach (var b in spikedBombs)
                if (b != null) b.SetActive(true);
        }
        gameObject.SetActive(true);
        PlayBowserAnim("Idle");
    }

    public void TriggerBossEntrance()
    {
        if (_state != BossState.Dormant) return;
        _state = BossState.EntranceDrop;
        _stateTimer = 0f;
        transform.position = arenaCenter + new Vector3(0f, 10.5f, 6.5f);
        transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
        PlayBowserAnim("TailGrabbedFlail");
        if (GameAudio.Instance != null) GameAudio.Instance.PlayBowserRoarSound();
    }

    private void Update()
    {
        var dt = Time.deltaTime > 0.0001f ? Time.deltaTime : (Time.timeScale > 0f ? 0.016f * Time.timeScale : 0f);
        if (dt <= 0.00001f || _state == BossState.Defeated) return;

        _stateTimer += dt;
        UpdateFireballs(dt);

        var hero = MarioRemakeController.Instance;
        if (_state == BossState.Dormant)
        {
            if (hero != null && Vector3.Distance(hero.transform.position, arenaCenter) < arenaRadius + 2.5f)
            {
                TriggerBossEntrance();
            }
            return;
        }

        if (_hudPromptCooldown > 0f) _hudPromptCooldown -= dt;

        switch (_state)
        {
            case BossState.EntranceDrop:
                UpdateEntranceDrop(dt);
                break;
            case BossState.Stalking:
                UpdateStalking(hero, dt);
                break;
            case BossState.FireBreathWindup:
                UpdateFireWindup(hero, dt);
                break;
            case BossState.FireBreathing:
                UpdateFireBreathing(hero, dt);
                break;
            case BossState.LeapShockwave:
                UpdateLeapShockwave(dt);
                break;
            case BossState.Recovering:
                UpdateRecovering();
                break;
            case BossState.GrabbedAndSpun:
                UpdateGrabbedSpin(dt);
                break;
            case BossState.HurledFlight:
                UpdateHurledFlight(dt);
                break;
            case BossState.MissingEdgeRecover:
                UpdateMissingEdgeRecover(dt);
                break;
        }
    }

    private void UpdateEntranceDrop(float dt)
    {
        var targetY = arenaCenter.y;
        var pos = transform.position;
        pos.y = Mathf.MoveTowards(pos.y, targetY, 22f * dt);
        transform.position = pos;

        if (pos.y <= targetY + 0.02f)
        {
            transform.position = new Vector3(pos.x, targetY, pos.z);
            SpawnShockwaveRing(transform.position, 7.5f, damageHero: false);
            if (GameAudio.Instance != null) GameAudio.Instance.PlayGroundPoundSound();
            if (_grabbingHero == null && MarioRemakeController.Instance != null && MarioRemakeController.Instance.impulseSource != null)
                MarioRemakeController.Instance.impulseSource.GenerateImpulse(0.45f);
            EnterState(BossState.Stalking, "WalkStomp");
        }
    }

    private void UpdateStalking(MarioRemakeController hero, float dt)
    {
        if (hero == null) return;
        var toHero = hero.transform.position - transform.position;
        toHero.y = 0f;
        var dist = toHero.magnitude;

        if (toHero.sqrMagnitude > 0.01f)
        {
            var desiredRot = Quaternion.LookRotation(toHero.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, desiredRot, turnSpeedDegPerSec * dt);
        }

        // Advance along facing direction (keeps Mario able to flank behind Bowser!)
        if (dist > 3.2f)
        {
            var nextPos = transform.position + transform.forward * (stalkSpeed * dt);
            nextPos = ClampInsideArena(nextPos, arenaRadius - 2.6f);
            nextPos.y = arenaCenter.y;
            transform.position = nextPos;
            PlayBowserAnim("WalkStomp");
        }
        else
        {
            PlayBowserAnim("Idle");
        }

        // Body slam damage ONLY if Mario bumps squarely into Bowser's spiky front/torso while not behind tail & not attacking tail
        if (dist < 1.85f && Mathf.Abs(hero.transform.position.y - transform.position.y) < 2.2f)
        {
            var dotFwd = Vector3.Dot(transform.forward, toHero.normalized);
            if (dotFwd > 0.15f)
            {
                hero.TakeDamage(2, transform.position);
            }
        }

        _actionCycleTimer += dt;
        if (_actionCycleTimer >= 2.8f)
        {
            _actionCycleTimer = 0f;
            if (dist < 11.5f && Random.value > 0.42f)
            {
                EnterState(BossState.FireBreathWindup, "FireBreath");
            }
            else
            {
                _leapAirborne = false;
                _velocity = Vector3.up * 13.5f;
                EnterState(BossState.LeapShockwave, "TailGrabbedFlail");
            }
        }
    }

    private void UpdateFireWindup(MarioRemakeController hero, float dt)
    {
        if (hero != null)
        {
            var toHero = hero.transform.position - transform.position;
            toHero.y = 0f;
            if (toHero.sqrMagnitude > 0.01f)
            {
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(toHero.normalized, Vector3.up), 110f * dt);
            }
        }
        if (_stateTimer >= 0.58f)
        {
            _fireballsRemaining = 3;
            _nextFireballTimer = 0f;
            _fireConeDamageCooldown = 0f;
            StartFireBreathVfx();
            EnterState(BossState.FireBreathing, "FireBreath");
        }
    }

    private void UpdateFireBreathing(MarioRemakeController hero, float dt)
    {
        UpdateFireBreathVfxTransform(dt);

        if (_fireConeDamageCooldown > 0f) _fireConeDamageCooldown -= dt;
        if (hero != null && _fireConeDamageCooldown <= 0f)
        {
            var origin = mouthOrigin != null ? mouthOrigin.position : (transform.position + transform.forward * 1.6f + Vector3.up * 2.0f);
            var toHero = (hero.transform.position + Vector3.up * 0.7f) - origin;
            var dist = toHero.magnitude;
            if (dist is > 0.4f and < 7.8f && Vector3.Angle(transform.forward, toHero.normalized) < 28f)
            {
                _fireConeDamageCooldown = 0.65f;
                hero.TakeDamage(1, origin);
            }
        }

        _nextFireballTimer -= dt;
        if (_fireballsRemaining > 0 && _nextFireballTimer <= 0f)
        {
            _fireballsRemaining--;
            _nextFireballTimer = 0.34f;
            SpawnBowserFireball(hero);
        }

        if (_fireballsRemaining <= 0 && _stateTimer >= 1.45f)
        {
            StopFireBreathVfx();
            EnterState(BossState.Recovering, "Idle");
        }
    }

    private void UpdateLeapShockwave(float dt)
    {
        _velocity.y -= 28f * dt;
        var pos = transform.position + _velocity * dt;
        pos.y = Mathf.Max(arenaCenter.y, pos.y);
        transform.position = pos;

        if (!_leapAirborne && _velocity.y > 0f) _leapAirborne = true;

        if (!_leapAirborne || !(pos.y <= arenaCenter.y + 0.02f)) return;
        transform.position = new Vector3(pos.x, arenaCenter.y, pos.z);
        SpawnShockwaveRing(transform.position, 11.5f, damageHero: true);
        if (GameAudio.Instance != null) GameAudio.Instance.PlayGroundPoundSound();
        if (MarioRemakeController.Instance != null && MarioRemakeController.Instance.impulseSource != null)
            MarioRemakeController.Instance.impulseSource.GenerateImpulse(0.42f);
        EnterState(BossState.Recovering, "Idle");
    }

    private void UpdateRecovering()
    {
        if (_stateTimer >= 0.95f)
        {
            EnterState(BossState.Stalking, "WalkStomp");
        }
    }

    // Called from MarioRemakeController when Mario presses Attack/Grab near or behind Bowser!
    public bool TryGrabTail(MarioRemakeController hero)
    {
        if (!CanBeTailGrabbed || hero == null) return false;

        var tailWorld = tailGrabPoint != null
            ? tailGrabPoint.position
            : (transform.position - transform.forward * 2.1f + Vector3.up * 0.6f);

        var heroCenter = hero.transform.position + Vector3.up * 0.7f;
        var distToTail = Vector3.Distance(heroCenter, tailWorld);
        var toHeroDir = (hero.transform.position - transform.position);
        toHeroDir.y = 0f;
        var distToCenter = toHeroDir.magnitude;
        var rearDot = distToCenter > 0.05f ? Vector3.Dot(transform.forward, toHeroDir.normalized) : 1f;

        if (distToTail > tailGrabRadius && !(distToCenter < tailGrabRadius + 1.1f && rearDot < -0.15f))
            return false;

        _grabbingHero = hero;
        _spinAngleDeg = Mathf.Atan2(transform.position.x - hero.transform.position.x, transform.position.z - hero.transform.position.z) * Mathf.Rad2Deg;
        _spinAngularSpeedDeg = 165f;
        _spinHoldSeconds = 0f;
        StopFireBreathVfx();
        _state = BossState.GrabbedAndSpun;
        _stateTimer = 0f;
        PlayBowserAnim("TailGrabbedFlail");
        if (GameAudio.Instance != null) GameAudio.Instance.PlayBowserRoarSound();
        if (GameAudio.Instance != null) GameAudio.Instance.PlayTailGrabSound();
        return true;
    }

    public void FeedSpinInput(float angularDeltaDeg, bool throwTriggered)
    {
        if (_state != BossState.GrabbedAndSpun) return;

        // Smooth acceleration up to high speed with directional A/D or Mouse Sweep!
        if (Mathf.Abs(angularDeltaDeg) > 0.01f)
        {
            var dirSign = Mathf.Sign(angularDeltaDeg);
            if (Mathf.Abs(_spinAngularSpeedDeg) < 120f)
            {
            }

            _spinAngularSpeedDeg += dirSign * Mathf.Abs(angularDeltaDeg) * 1.65f;
            _spinAngularSpeedDeg = Mathf.Clamp(_spinAngularSpeedDeg, -560f, 560f);
        }

        if (throwTriggered && _spinHoldSeconds >= 0.14f)
        {
            ReleaseAndHurlBowser();
        }
    }

    private void UpdateGrabbedSpin(float dt)
    {
        if (_grabbingHero == null)
        {
            EnterState(BossState.Recovering, "Idle");
            return;
        }

        _spinHoldSeconds += dt;
        // Maintain slight natural rotation torque so player never feels stuck if holding a key
        if (Mathf.Abs(_spinAngularSpeedDeg) < 140f)
        {
            _spinAngularSpeedDeg = Mathf.MoveTowards(_spinAngularSpeedDeg, Mathf.Sign(_spinAngularSpeedDeg == 0f ? 1f : _spinAngularSpeedDeg) * 155f, 300f * dt);
        }

        _spinAngleDeg += _spinAngularSpeedDeg * dt;

        // Keep Mario anchored inside safe arena center radius while swinging Bowser around him!
        var heroPos = _grabbingHero.transform.position;
        heroPos = ClampInsideArena(heroPos, arenaRadius - 4.2f);
        heroPos.y = arenaCenter.y;
        _grabbingHero.transform.position = heroPos;

        var rad = _spinAngleDeg * Mathf.Deg2Rad;
        var outDir = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
        var speedRatio = Mathf.Clamp01((Mathf.Abs(_spinAngularSpeedDeg) - 120f) / 420f);
        var liftY = Mathf.Lerp(0.32f, 1.35f, speedRatio);
        var swingRadius = Mathf.Lerp(2.45f, 2.85f, speedRatio);
        var tiltPitch = Mathf.Lerp(14f, 34f, speedRatio);
        var bankRoll = -Mathf.Sign(_spinAngularSpeedDeg) * Mathf.Lerp(8f, 24f, speedRatio);

        // Bowser lifts and leans outward with centrifugal force while Mario grips Tail_04!
        transform.position = heroPos + outDir * swingRadius + Vector3.up * liftY;
        transform.rotation = Quaternion.LookRotation(outDir, Vector3.up) * Quaternion.Euler(tiltPitch, 0f, bankRoll);
        _grabbingHero.transform.rotation = Quaternion.LookRotation(outDir, Vector3.up);
    }

    public void ReleaseAndHurlBowser()
    {
        if (_state != BossState.GrabbedAndSpun) return;

        var rad = _spinAngleDeg * Mathf.Deg2Rad;
        var radialDir = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)).normalized;
        var tangentDir = Vector3.Cross(Vector3.up, radialDir) * Mathf.Sign(_spinAngularSpeedDeg);
        var throwDir = (radialDir * 0.68f + tangentDir * 0.55f).normalized;
        var speedRatio = Mathf.Clamp01((Mathf.Abs(_spinAngularSpeedDeg) - 120f) / 420f);
        var launchSpeed = Mathf.Lerp(17.5f, 29.5f, speedRatio);

        _velocity = throwDir * launchSpeed + Vector3.up * 3.8f;
        if (_grabbingHero != null)
        {
            _grabbingHero.OnBowserThrown();
            _grabbingHero = null;
        }

        if (GameAudio.Instance != null) GameAudio.Instance.PlayCannonBlastSound();
        if (VFXPool.Instance != null) VFXPool.Instance.SpawnPickupBurst(transform.position + Vector3.up * 0.9f, isStar: false);
        EnterState(BossState.HurledFlight, "TailGrabbedFlail");
    }

    private void UpdateHurledFlight(float dt)
    {
        _velocity.y -= 20f * dt;
        var nextPos = transform.position + _velocity * dt;
        if (nextPos.y < arenaCenter.y)
        {
            nextPos.y = arenaCenter.y;
            _velocity.y = 0f;
            _velocity.x *= Mathf.Exp(-1.8f * dt);
            _velocity.z *= Mathf.Exp(-1.8f * dt);
            if (Mathf.Abs(Mathf.Sin(Time.time * 24f)) > 0.7f && VFXPool.Instance != null)
            {
                VFXPool.Instance.SpawnFootstepPuff(nextPos + Vector3.up * 0.2f);
            }
        }
        transform.position = nextPos;
        transform.Rotate(0f, 380f * dt, 0f, Space.World);

        // 1. Check collision against ALL perimeter Spiked Bombs!
        if (spikedBombs != null)
        {
            foreach (var bomb in spikedBombs)
            {
                if (bomb == null || !bomb.activeInHierarchy) continue;
                var d = Vector2.Distance(
                    new Vector2(nextPos.x, nextPos.z),
                    new Vector2(bomb.transform.position.x, bomb.transform.position.z));
                if (!(d < 3.15f)) continue;
                DetonateSpikedBomb(bomb);
                return;
            }
        }

        // 2. Check if Bowser went over the Arena rim without hitting a bomb!
        var radialDist = Vector2.Distance(
            new Vector2(nextPos.x, nextPos.z),
            new Vector2(arenaCenter.x, arenaCenter.z));

        if (radialDist > arenaRadius + 1.4f)
        {
            EnterState(BossState.MissingEdgeRecover, "TailGrabbedFlail");
            return;
        }

        // 3. Stopped sliding inside arena safely
        if (_stateTimer > 0.35f && new Vector2(_velocity.x, _velocity.z).magnitude < 2.1f)
        {
            EnterState(BossState.Stalking, "WalkStomp");
        }
    }

    private void UpdateMissingEdgeRecover(float dt)
    {
        if (_stateTimer < 0.55f)
        {
            transform.position += Vector3.down * (3.5f * dt);
            return;
        }
        // Leap smoothly back to Arena Center!
        var t = Mathf.Clamp01((_stateTimer - 0.55f) / 0.85f);
        var startOut = new Vector3(transform.position.x, arenaCenter.y - 1.5f, transform.position.z);
        var arc = Vector3.Lerp(startOut, arenaCenter, Ease.OutCubic(t));
        arc.y += Mathf.Sin(t * Mathf.PI) * 7.5f;
        transform.position = arc;

        if (!(t >= 1f)) return;
        transform.position = arenaCenter;
        SpawnShockwaveRing(arenaCenter, 8.0f, damageHero: false);
        if (GameAudio.Instance != null) GameAudio.Instance.PlayGroundPoundSound();
        EnterState(BossState.Stalking, "WalkStomp");
    }

    private void DetonateSpikedBomb(GameObject bomb)
    {
        var bombPos = bomb.transform.position;
        bomb.SetActive(false);

        if (GameAudio.Instance != null)
        {
            GameAudio.Instance.PlayBowserBombExplosionSound();
            GameAudio.Instance.PlayBowserRoarSound();
        }
        if (GameManager.Instance != null) GameManager.Instance.TriggerHitStop(0.16f);
        if (MarioRemakeController.Instance != null && MarioRemakeController.Instance.impulseSource != null)
            MarioRemakeController.Instance.impulseSource.GenerateImpulse(0.95f);

        if (VFXPool.Instance != null)
        {
            for (var k = 0; k < 5; k++)
                VFXPool.Instance.SpawnPickupBurst(bombPos + Vector3.up * 1.1f + Random.insideUnitSphere * 1.4f, isStar: true);
        }

        currentBombHitsRemaining--;
        if (GameManager.Instance != null)
            GameManager.Instance.SpawnFloatingWorldText(bombPos + Vector3.up * 2.2f, "BOWSER BLASTED!", Palette.CharacterPrimary);
        if (MarioRemakeController.Instance != null && MarioRemakeController.Instance.impulseSource != null)
            MarioRemakeController.Instance.impulseSource.GenerateImpulse(0.65f);
        if (currentBombHitsRemaining <= 0)
        {
            _state = BossState.Defeated;
            StartCoroutine(BowserDefeatRoutine(bombPos));
        }
        else
        {
            transform.position = arenaCenter + Vector3.up * 4.5f;
            EnterState(BossState.EntranceDrop, "TailGrabbedFlail");
        }
    }

    private IEnumerator BowserDefeatRoutine(Vector3 impactPos)
    {
        ClearActiveFireballs();
        var startPos = impactPos + Vector3.up * 1.2f;
        for (var i = 0; i < 32; i++)
        {
            var u = i / 31f;
            transform.position = startPos + new Vector3(Mathf.Sin(i * 1.7f) * 0.28f, u * 3.2f, Mathf.Cos(i * 1.7f) * 0.28f);
            transform.Rotate(0f, 35f, 12f, Space.Self);
            transform.localScale = Vector3.one * Mathf.Lerp(1.0f, 0.15f, u * u);
            yield return new WaitForSeconds(0.032f);
        }

        if (VFXPool.Instance != null)
        {
            for (var k = 0; k < 4; k++)
                VFXPool.Instance.SpawnPickupBurst(arenaCenter + Vector3.up * 2.2f + Random.insideUnitSphere * 1.5f, isStar: true);
        }
        if (GameAudio.Instance != null) GameAudio.Instance.PlayMilestoneSting();

        if (rewardBigKeyStar != null)
        {
            rewardBigKeyStar.transform.position = arenaCenter + Vector3.up * 2.2f;
            rewardBigKeyStar.gameObject.SetActive(true);
        }

        gameObject.SetActive(false);
    }

    private void SpawnBowserFireball(MarioRemakeController hero)
    {
        var origin = mouthOrigin != null ? mouthOrigin.position : (transform.position + transform.forward * 1.5f + Vector3.up * 2.1f);
        var dir = transform.forward;
        if (hero != null)
        {
            var toHero = hero.transform.position - origin;
            toHero.y = 0f;
            if (toHero.sqrMagnitude > 0.05f) dir = Vector3.RotateTowards(transform.forward, toHero.normalized, 0.45f, 1f).normalized;
        }

        var go = fireballTemplate != null ? Instantiate(fireballTemplate, origin, Quaternion.identity) : GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "BowserFireball";
        go.transform.position = new Vector3(origin.x, arenaCenter.y + 0.72f, origin.z);
        go.transform.localScale = Vector3.one * 1.05f;
        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);
        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null && _fireMat != null) mr.sharedMaterial = _fireMat;

        _activeFireballs.Add(new ActiveFireball
        {
            Go = go,
            Vel = dir * 10.2f,
            Life = 3.2f
        });

        if (GameAudio.Instance != null) GameAudio.Instance.PlayCannonBlastSound();
    }

    private void UpdateFireballs(float dt)
    {
        var hero = MarioRemakeController.Instance;
        for (var i = _activeFireballs.Count - 1; i >= 0; i--)
        {
            var fb = _activeFireballs[i];
            if (fb.Go == null)
            {
                _activeFireballs.RemoveAt(i);
                continue;
            }
            fb.Life -= dt;
            fb.Go.transform.position += fb.Vel * dt;
            var flicker = 0.95f + Mathf.Sin(Time.time * 28f + i) * 0.16f;
            fb.Go.transform.localScale = Vector3.one * (1.05f * flicker);

            if (hero != null && Vector3.Distance(hero.transform.position + Vector3.up * 0.6f, fb.Go.transform.position) < 1.15f)
            {
                hero.TakeDamage(1, fb.Go.transform.position);
                Destroy(fb.Go);
                _activeFireballs.RemoveAt(i);
                continue;
            }

            if (!(fb.Life <= 0f)) continue;
            Destroy(fb.Go);
            _activeFireballs.RemoveAt(i);
        }
    }

    private void ClearActiveFireballs()
    {
        foreach (var fb in _activeFireballs.Where(fb => fb.Go != null)) Destroy(fb.Go);
        _activeFireballs.Clear();
    }

    private void BuildFireBreathRig()
    {
        var anchor = mouthOrigin != null ? mouthOrigin : transform;
        var rigGo = new GameObject("BowserFireBreathVFX");
        rigGo.transform.SetParent(anchor, false);
        rigGo.transform.localPosition = mouthOrigin != null ? new Vector3(0f, 0.05f, 0.45f) : new Vector3(0f, 2.0f, 1.6f);

        _softParticleTex = CreateSoftParticleTexture();
        var particleMat = new Material(Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default"));
        particleMat.mainTexture = _softParticleTex;

        // 1. Roaring flame cone (bright yellow-white core -> deep orange-red)
        var flameGo = new GameObject("FlameStream");
        flameGo.transform.SetParent(rigGo.transform, false);
        _fireFlamePs = flameGo.AddComponent<ParticleSystem>();
        var fm = _fireFlamePs.main;
        fm.playOnAwake = false;
        fm.loop = true;
        fm.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.72f);
        fm.startSpeed = new ParticleSystem.MinMaxCurve(9.5f, 13.5f);
        fm.startSize = new ParticleSystem.MinMaxCurve(0.65f, 1.35f);
        fm.simulationSpace = ParticleSystemSimulationSpace.World;
        fm.maxParticles = 140;

        var fEmit = _fireFlamePs.emission;
        fEmit.rateOverTime = 75f;

        var fShape = _fireFlamePs.shape;
        fShape.shapeType = ParticleSystemShapeType.Cone;
        fShape.angle = 16f;
        fShape.radius = 0.22f;

        var fCol = _fireFlamePs.colorOverLifetime;
        fCol.enabled = true;
        var fGrad = new Gradient();
        fGrad.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 0.98f, 0.75f), 0f),
                new GradientColorKey(new Color(1f, 0.55f, 0.08f), 0.35f),
                new GradientColorKey(new Color(0.92f, 0.14f, 0.04f), 0.85f)
            },
            new[]
            {
                new GradientAlphaKey(0.95f, 0f),
                new GradientAlphaKey(0.85f, 0.55f),
                new GradientAlphaKey(0f, 1f)
            });
        fCol.color = fGrad;

        var fSize = _fireFlamePs.sizeOverLifetime;
        fSize.enabled = true;
        fSize.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.45f, 1f, 2.2f));

        var fRen = flameGo.GetComponent<ParticleSystemRenderer>();
        fRen.sharedMaterial = particleMat;

        // 2. Rising dark smoke billows at the outer tip of the flame
        var smokeGo = new GameObject("SmokeBillows");
        smokeGo.transform.SetParent(rigGo.transform, false);
        _fireSmokePs = smokeGo.AddComponent<ParticleSystem>();
        var sm = _fireSmokePs.main;
        sm.playOnAwake = false;
        sm.loop = true;
        sm.startLifetime = new ParticleSystem.MinMaxCurve(0.85f, 1.35f);
        sm.startSpeed = new ParticleSystem.MinMaxCurve(6.5f, 9.5f);
        sm.startSize = new ParticleSystem.MinMaxCurve(1.1f, 2.1f);
        sm.gravityModifier = -0.35f;
        sm.simulationSpace = ParticleSystemSimulationSpace.World;
        sm.maxParticles = 90;

        var sEmit = _fireSmokePs.emission;
        sEmit.rateOverTime = 32f;

        var sShape = _fireSmokePs.shape;
        sShape.shapeType = ParticleSystemShapeType.Cone;
        sShape.angle = 22f;
        sShape.radius = 0.3f;

        var sCol = _fireSmokePs.colorOverLifetime;
        sCol.enabled = true;
        var sGrad = new Gradient();
        sGrad.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.35f, 0.22f, 0.16f), 0f),
                new GradientColorKey(new Color(0.14f, 0.14f, 0.15f), 0.45f),
                new GradientColorKey(new Color(0.08f, 0.08f, 0.09f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.65f, 0.3f),
                new GradientAlphaKey(0f, 1f)
            });
        sCol.color = sGrad;

        var sRen = smokeGo.GetComponent<ParticleSystemRenderer>();
        sRen.sharedMaterial = particleMat;

        var lightGo = new GameObject("FireLight");
        lightGo.transform.SetParent(rigGo.transform, false);
        _fireLight = lightGo.AddComponent<Light>();
        _fireLight.type = LightType.Point;
        _fireLight.color = new Color(1f, 0.48f, 0.12f);
        _fireLight.range = 10f;
        _fireLight.intensity = 0f;
        _fireLight.enabled = false;
    }

    private void StartFireBreathVfx()
    {
        if (_fireFlamePs != null) _fireFlamePs.Play();
        if (_fireSmokePs != null) _fireSmokePs.Play();
        if (_fireLight != null) _fireLight.enabled = true;
        if (GameAudio.Instance != null) GameAudio.Instance.PlayBowserRoarSound();
    }

    private void UpdateFireBreathVfxTransform(float dt)
    {
        var aimRot = Quaternion.LookRotation((transform.forward - Vector3.up * 0.08f).normalized, Vector3.up);
        if (_fireFlamePs != null) _fireFlamePs.transform.parent.rotation = aimRot;
        if (_fireLight != null && _fireLight.enabled)
        {
            _fireLight.intensity = 4.2f + Mathf.Sin(Time.time * 38f) * 1.4f;
        }
    }

    private void StopFireBreathVfx()
    {
        if (_fireFlamePs != null && _fireFlamePs.isPlaying) _fireFlamePs.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        if (_fireSmokePs != null && _fireSmokePs.isPlaying) _fireSmokePs.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        if (_fireLight != null) _fireLight.enabled = false;
    }

    private static Texture2D CreateSoftParticleTexture()
    {
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        const float center = (size - 1) * 0.5f;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var d = Vector2.Distance(new Vector2(x, y), new Vector2(center, center)) / center;
                var a = Mathf.Clamp01(1f - d);
                a = a * a;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();
        return tex;
    }

    private void SpawnShockwaveRing(Vector3 center, float maxRadius, bool damageHero)
    {
        StartCoroutine(ShockwaveExpandRoutine(center, maxRadius, damageHero));
    }

    private static Mesh shockRingMesh;
    private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

    private static Mesh GetShockwaveRingMesh()
    {
        if (shockRingMesh != null) return shockRingMesh;
        const int segs = 36;
        const float innerR = 0.88f;
        const float outerR = 1.00f;
        const float h = 0.08f;
        var verts = new Vector3[segs * 4];
        var normals = new Vector3[segs * 4];
        var tris = new int[segs * 6];
        for (var i = 0; i < segs; i++)
        {
            var a0 = (i / (float)segs) * Mathf.PI * 2f;
            var a1 = ((i + 1) / (float)segs) * Mathf.PI * 2f;
            var v = i * 4;
            verts[v + 0] = new Vector3(Mathf.Cos(a0) * innerR, h, Mathf.Sin(a0) * innerR);
            verts[v + 1] = new Vector3(Mathf.Cos(a0) * outerR, h, Mathf.Sin(a0) * outerR);
            verts[v + 2] = new Vector3(Mathf.Cos(a1) * outerR, h, Mathf.Sin(a1) * outerR);
            verts[v + 3] = new Vector3(Mathf.Cos(a1) * innerR, h, Mathf.Sin(a1) * innerR);
            normals[v + 0] = normals[v + 1] = normals[v + 2] = normals[v + 3] = Vector3.up;
            var t = i * 6;
            tris[t + 0] = v + 0; tris[t + 1] = v + 2; tris[t + 2] = v + 1;
            tris[t + 3] = v + 0; tris[t + 4] = v + 3; tris[t + 5] = v + 2;
        }
        shockRingMesh = new Mesh { name = "BowserShockwaveHollowRingMesh" };
        shockRingMesh.SetVertices(verts);
        shockRingMesh.SetNormals(normals);
        shockRingMesh.SetTriangles(tris, 0);
        shockRingMesh.RecalculateBounds();
        return shockRingMesh;
    }

    private IEnumerator ShockwaveExpandRoutine(Vector3 center, float maxRadius, bool damageHero)
    {
        var ring = new GameObject("BowserShockwaveRing");
        var mf = ring.AddComponent<MeshFilter>();
        mf.sharedMesh = GetShockwaveRingMesh();
        var mr = ring.AddComponent<MeshRenderer>();
        if (_shockMat != null) mr.sharedMaterial = _shockMat;

        var damaged = false;
        const float dur = 0.85f;
        var elapsed = 0f;
        while (elapsed < dur)
        {
            elapsed += Time.deltaTime;
            var u = Mathf.Clamp01(elapsed / dur);
            var r = Mathf.Lerp(1.2f, maxRadius, Ease.OutCubic(u));
            ring.transform.position = new Vector3(center.x, arenaCenter.y + 0.06f, center.z);
            ring.transform.localScale = new Vector3(r, 1f, r);

            if (damageHero && !damaged && MarioRemakeController.Instance != null)
            {
                var h = MarioRemakeController.Instance;
                var hd = Vector2.Distance(new Vector2(h.transform.position.x, h.transform.position.z), new Vector2(center.x, center.z));
                // Jump over the wave! Only hits if Mario is within 0.95m of ground and near ring radius
                if (Mathf.Abs(hd - r) < 1.15f && h.transform.position.y < arenaCenter.y + 0.95f)
                {
                    damaged = true;
                    h.TakeDamage(1, center);
                }
            }
            yield return null;
        }
        Destroy(ring);
    }

    private Vector3 ClampInsideArena(Vector3 p, float maxR)
    {
        var dx = p.x - arenaCenter.x;
        var dz = p.z - arenaCenter.z;
        var dist = Mathf.Sqrt(dx * dx + dz * dz);
        if (!(dist > maxR) || !(dist > 0.001f)) return p;
        var scale = maxR / dist;
        p.x = arenaCenter.x + dx * scale;
        p.z = arenaCenter.z + dz * scale;
        return p;
    }

    private void EnterState(BossState next, string animClip)
    {
        _state = next;
        _stateTimer = 0f;
        PlayBowserAnim(animClip);
    }

    private void PlayBowserAnim(string clipName)
    {
        if (bowserAnimator == null) return;
        if (bowserAnimator.HasState(0, Animator.StringToHash(clipName)))
        {
            bowserAnimator.CrossFadeInFixedTime(clipName, 0.12f, 0);
        }
    }
}

