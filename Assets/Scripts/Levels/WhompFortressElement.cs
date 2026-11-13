using System;
using System.Collections;
using UnityEngine;

namespace Mario64Remake
{
    public enum FortressHazardType
    {
        ShoveBlock,
        ThwompCrusher,
        RotatingBridge,
        TiltingPlank,
        KickableBoard,
        FloatingIsle,
        SkyCannonLauncher,
        BreakawayBrickWall,
        SleepingPiranha,
        ElevatorLift,
        HootOwlCarry
    }
}

/// <summary>
/// Interactive mechanics for Whomp's Fortress (Course 2 Remake):
/// - ShoveBlock: pushes in and out of the fortress cliff wall along localOffset
/// - ThwompCrusher: hovers at top, slams down with a heavy thud & camera impulse, pauses, rises back
/// - RotatingBridge: rotates continuously around Y axis so Mario must time his crossing
/// - TiltingPlank: tilts back and forth on Z/X axis over a gap
/// - KickableBoard: tall wooden plank standing upright; when Mario touches/dives/punches it, falls flat 90 deg to form a bridge!
/// - FloatingIsle: gently bobs up and down in the sky for the "Blast Away the Wall" / Sky Isles stars
/// - SkyCannonLauncher: launches Mario along a parabolic arc to the West Floating Isle (Star 3) or East Breakaway Wall (Star 6)!
/// - BreakawayBrickWall: shatters when Mario blasts or dives into its corner, revealing Star 6 ("Blast Away the Wall")!
/// - SleepingPiranha: gently breathes in and out while sleeping; if Mario attacks/Ground Pounds it, bursts into coins!
/// </summary>
public class WhompFortressElement : MonoBehaviour, IImpactReceiver
{
    public void OnImpact(Vector3 impactOrigin, float distanceSq)
    {
        if (hazardType == FortressHazardType.BreakawayBrickWall)
            ShatterBreakawayWall();
    }
    public Mario64Remake.FortressHazardType hazardType = Mario64Remake.FortressHazardType.ShoveBlock;
    public Vector3 moveOffset = new(0f, 0f, 3.2f);
    public float cycleSpeed = 1.2f;
    public float phaseOffset;
    public float rotationSpeed = 42f;
    public float tiltMaxAngle = 16f;
    public Vector3 tiltAxis = Vector3.forward;
    public Vector3 primaryLaunchTarget = new(-13.5f, 16.5f, 5.5f);
    public Vector3 secondaryLaunchTarget = new(14.5f, 10.2f, -1.5f);
    public StarCoinCollectible revealedStar;
    public StarCoinCollectible primaryTargetStar;
    public StarCoinCollectible secondaryTargetStar;

    private Vector3 _startPos;
    private Quaternion _startRot;
    private Vector3 _startScale;
    private float _clock;
    private bool _boardKnockedDown;
    private bool _boardFalling;
    private bool _cannonLaunching;
    private bool _useSecondaryTarget;
    private float _cannonCooldown;
    private float _hootCooldown;
    private float _thwompTimer;
    private int _thwompState; // 0 = top wait, 1 = slamming down, 2 = bottom wait, 3 = rising up

    // Piranha Plant Animated State & Hierarchy Pivots
    private int _piranhaState; // 0 = Sleeping, 1 = WakingUp, 2 = Chomping/Lunging, 3 = ReturningToSleep
    private float _piranhaTimer;
    private float _piranhaBiteCooldown;
    private float _piranhaFarTimer;
    private bool _piranhaPivotsResolved;
    private Transform _piranhaStem;
    private Transform _piranhaHead;
    private Transform _piranhaJawUpper;
    private Transform _piranhaJawLower;
    private Transform _piranhaLeafL;
    private Transform _piranhaLeafR;

    private void Awake()
    {
        _startPos = transform.position;
        _startRot = transform.rotation;
        _startScale = transform.localScale;
        ResolvePiranhaPivots();
    }

    private void ResolvePiranhaPivots()
    {
        if (_piranhaPivotsResolved) return;
        _piranhaPivotsResolved = true;
        if (hazardType != Mario64Remake.FortressHazardType.SleepingPiranha) return;
        FindChildDeep(transform, "VisualModel");
        _piranhaStem = FindChildDeep(transform, "StemPivot");
        _piranhaHead = FindChildDeep(transform, "HeadPivot");
        _piranhaJawUpper = FindChildDeep(transform, "JawUpperPivot");
        _piranhaJawLower = FindChildDeep(transform, "JawLowerPivot");
        _piranhaLeafL = FindChildDeep(transform, "LeafPivot_L");
        _piranhaLeafR = FindChildDeep(transform, "LeafPivot_R");
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

    public void ResetElement()
    {
        if (_startPos.sqrMagnitude > 0.0001f)
        {
            transform.SetPositionAndRotation(_startPos, _startRot);
        }
        if (_startScale.sqrMagnitude > 0.0001f)
        {
            transform.localScale = _startScale;
        }
        _boardKnockedDown = false;
        _boardFalling = false;
        _cannonLaunching = false;
        _cannonCooldown = 0f;
        _hootCooldown = 0f;
        _thwompTimer = 0f;
        _thwompState = 0;
        _piranhaState = 0;
        _piranhaTimer = 0f;
        _piranhaBiteCooldown = 0f;
        _piranhaFarTimer = 0f;
        ApplyPiranhaPose(0f, 0f, 0f, 0f);
        gameObject.SetActive(true);
    }

    private void Update()
    {
        if (_startPos.sqrMagnitude < 0.0001f && transform.position.sqrMagnitude > 0.0001f)
        {
            _startPos = transform.position;
            _startRot = transform.rotation;
        }

        var dt = Time.deltaTime > 0.0001f ? Time.deltaTime : (Time.timeScale > 0f ? 0.016f * Time.timeScale : 0f);
        if (dt <= 0.00001f) return;

        _clock += dt * cycleSpeed;

        switch (hazardType)
        {
            case Mario64Remake.FortressHazardType.ShoveBlock:
            {
                // Sharp push out, hold, smooth retract
                var wave = Mathf.Sin((_clock + phaseOffset) * Mathf.PI);
                var t = Mathf.Clamp01((wave + 0.5f));
                transform.position = _startPos + moveOffset * Ease.OutCubic(t);
                break;
            }
            case Mario64Remake.FortressHazardType.ThwompCrusher:
            {
                UpdateThwomp(dt);
                break;
            }
            case Mario64Remake.FortressHazardType.RotatingBridge:
            {
                transform.Rotate(0f, rotationSpeed * dt, 0f, Space.World);
                break;
            }
            case Mario64Remake.FortressHazardType.TiltingPlank:
            {
                var angle = Mathf.Sin((_clock + phaseOffset) * Mathf.PI) * tiltMaxAngle;
                var axis = tiltAxis.sqrMagnitude > 0.01f ? tiltAxis.normalized : Vector3.forward;
                transform.rotation = _startRot * Quaternion.AngleAxis(angle, axis);
                break;
            }
            case Mario64Remake.FortressHazardType.KickableBoard:
            {
                CheckKickableBoardProximity();
                break;
            }
            case Mario64Remake.FortressHazardType.FloatingIsle:
            {
                var y = Mathf.Sin((_clock + phaseOffset) * Mathf.PI) * 0.45f;
                transform.position = _startPos + new Vector3(0f, y, 0f);
                break;
            }
            case Mario64Remake.FortressHazardType.SkyCannonLauncher:
            {
                UpdateCannonLauncher(dt);
                break;
            }
            case Mario64Remake.FortressHazardType.BreakawayBrickWall:
            {
                CheckBreakawayWallImpact();
                break;
            }
            case Mario64Remake.FortressHazardType.SleepingPiranha:
            {
                UpdateSleepingPiranha(dt);
                break;
            }
            case Mario64Remake.FortressHazardType.ElevatorLift:
            {
                // Smooth dwell at bottom & top so Mario steps on and off comfortably
                var ping = Mathf.PingPong(_clock + phaseOffset, 1.4f);
                var t = Mathf.Clamp01((ping - 0.2f) / 1.0f);
                transform.position = _startPos + moveOffset * Mathf.SmoothStep(0f, 1f, t);
                break;
            }
            case Mario64Remake.FortressHazardType.HootOwlCarry:
            {
                UpdateHootOwl(dt);
                break;
            }
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void UpdateCannonLauncher(float dt)
    {
        if (_cannonCooldown > 0f) _cannonCooldown -= dt;
        if (_cannonLaunching || _cannonCooldown > 0f) return;

        var hero = MarioRemakeController.Instance;
        if (hero == null) return;

        var dist = Vector3.Distance(hero.transform.position, transform.position);
        if (dist < 1.85f)
        {
            Vector3 dest;
            var primaryPending = primaryTargetStar != null && !primaryTargetStar.IsCollected;
            var secondaryPending = secondaryTargetStar != null && !secondaryTargetStar.IsCollected;

            if (primaryTargetStar != null || secondaryTargetStar != null)
            {
                if (primaryPending && !secondaryPending) dest = primaryLaunchTarget;
                else if (secondaryPending && !primaryPending) dest = secondaryLaunchTarget;
                else
                {
                    dest = _useSecondaryTarget ? secondaryLaunchTarget : primaryLaunchTarget;
                    _useSecondaryTarget = !_useSecondaryTarget;
                }
            }
            else if (GameManager.Instance != null && GameManager.Instance.currentActNumber == 6)
            {
                dest = secondaryLaunchTarget;
            }
            else if (GameManager.Instance != null && (GameManager.Instance.currentActNumber == 3 || GameManager.Instance.currentActNumber == 5))
            {
                dest = primaryLaunchTarget;
            }
            else
            {
                dest = _useSecondaryTarget ? secondaryLaunchTarget : primaryLaunchTarget;
                _useSecondaryTarget = !_useSecondaryTarget;
            }
            StartCoroutine(CannonArcLaunchRoutine(hero, dest));
        }
    }

    private IEnumerator CannonArcLaunchRoutine(MarioRemakeController hero, Vector3 destination)
    {
        _cannonLaunching = true;
        var cc = hero.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        if (GameAudio.Instance != null) GameAudio.Instance.PlayCannonBlastSound();
        if (VFXPool.Instance != null) VFXPool.Instance.SpawnPickupBurst(transform.position + Vector3.up * 0.8f, isStar: true);

        var p0 = transform.position + Vector3.up * 1.25f;
        var arcPeak = Mathf.Max(9.5f, Vector3.Distance(p0, destination) * 0.36f);
        yield return Ease.Tween(1.05f, t =>
        {
            if (hero == null) return;
            var pos = Vector3.Lerp(p0, destination, t);
            pos.y += Mathf.Sin(t * Mathf.PI) * arcPeak;
            hero.transform.position = pos;
            hero.transform.Rotate(540f * Time.unscaledDeltaTime, 0f, 0f, Space.Self);
        }, Ease.OutCubic, unscaled: false);

        if (hero != null)
        {
            hero.transform.position = destination;
            hero.transform.rotation = Quaternion.identity;
            if (cc != null) cc.enabled = true;
        }

        _cannonCooldown = 2.2f;
        _cannonLaunching = false;
    }

    private void CheckBreakawayWallImpact()
    {
        var hero = MarioRemakeController.Instance;
        if (hero == null) return;

        var dist = Vector3.Distance(hero.transform.position, transform.position);
        if (dist < 2.8f)
        {
            ShatterBreakawayWall();
        }
    }

    public void ShatterBreakawayWall()
    {
        if (!gameObject.activeSelf) return;
        if (GameAudio.Instance != null) GameAudio.Instance.PlayGroundPoundSound();
        if (VFXPool.Instance != null) VFXPool.Instance.SpawnPickupBurst(transform.position, isStar: true);
        if (GameManager.Instance != null) GameManager.Instance.TriggerHitStop(0.06f);
        if (revealedStar != null && !revealedStar.IsCollected)
            revealedStar.gameObject.SetActive(true);
        gameObject.SetActive(false);
    }

    private void UpdateSleepingPiranha(float dt)
    {
        if (!_piranhaPivotsResolved) ResolvePiranhaPivots();
        if (_startScale.sqrMagnitude < 0.0001f) _startScale = Vector3.one;
        if (_piranhaBiteCooldown > 0f) _piranhaBiteCooldown -= dt;

        var hero = MarioRemakeController.Instance;
        var flatDist = hero != null
            ? Vector2.Distance(new Vector2(hero.transform.position.x, hero.transform.position.z), new Vector2(transform.position.x, transform.position.z))
            : 999f;
        var yDiff = hero != null ? hero.transform.position.y - transform.position.y : 99f;

        // Check if Mario attacks / Ground Pounds / Stomps the Piranha Plant!
        if (hero != null && flatDist < 2.45f && Mathf.Abs(yDiff) < 3.0f)
        {
            var stompHit = hero.IsStompingDown && yDiff > 0.75f && flatDist < 1.9f;
            if (hero.IsAttackingOrSliding || stompHit)
            {
                if (stompHit) hero.BounceUpFromStomp(1.85f);
                DefeatSleepingPiranha();
                return;
            }
        }

        _piranhaTimer += dt;

        switch (_piranhaState)
        {
            case 0: // Sleeping & Snoring crouched low over soil tuft
            {
                var snore = Mathf.Sin(_clock * 2.2f);
                var breathe = 1f + snore * 0.06f;
                transform.localScale = new Vector3(_startScale.x * breathe, _startScale.y / breathe, _startScale.z * breathe);
                ApplyPiranhaPose(erect01: 0f, jawOpen01: 0.05f, lungePitchDeg: 0f, leafFlapDeg: snore * 4f);

                // Wake up when Mario approaches aggressively (or steps right beside it)!
                if (hero != null && Mathf.Abs(yDiff) < 3.2f)
                {
                    var runningLoud = !hero.IsCrouching && hero.CurrentPlanarSpeed > 3.2f;
                    if (flatDist < 2.8f || (flatDist < 5.6f && runningLoud))
                    {
                        _piranhaState = 1;
                        _piranhaTimer = 0f;
                        _piranhaFarTimer = 0f;
                        transform.localScale = _startScale;
                        if (GameAudio.Instance != null) GameAudio.Instance.PlayPiranhaChompSound();
                    }
                }
                break;
            }
            case 1: // Waking up & unfurling erect toward Mario!
            {
                var u = Mathf.Clamp01(_piranhaTimer / 0.34f);
                var eased = Ease.OutBack(u);
                FaceHeroSmoothly(hero, dt * 10f);
                ApplyPiranhaPose(erect01: eased, jawOpen01: Mathf.Sin(u * Mathf.PI) * 0.85f, lungePitchDeg: -10f * u, leafFlapDeg: Mathf.Sin(u * 22f) * 14f);
                if (u >= 1f)
                {
                    _piranhaState = 2;
                    _piranhaTimer = 0f;
                }
                break;
            }
            case 2: // Awake, snapping, lunging & biting Mario!
            {
                transform.localScale = _startScale;
                FaceHeroSmoothly(hero, dt * 6.5f);

                // Rhythmic jaw open -> fast snap lunge bite cycle (~1.15s per snap)
                var snapPhase = Mathf.Repeat(_piranhaTimer * 1.2f, 1f);
                float jawOpen;
                float lungeDeg;
                if (snapPhase < 0.45f)
                {
                    var w = snapPhase / 0.45f;
                    jawOpen = Mathf.Lerp(0.15f, 1.0f, w);
                    lungeDeg = Mathf.Lerp(0f, -14f, w); // rear head back opening wide
                }
                else if (snapPhase < 0.68f)
                {
                    var s = (snapPhase - 0.45f) / 0.23f;
                    jawOpen = Mathf.Lerp(1.0f, 0.0f, s * s);
                    lungeDeg = Mathf.Lerp(-14f, 32f, s * s); // lunge jaws forward & snap!
                    if (s is > 0.72f and < 0.92f && flatDist < 4.5f && _piranhaBiteCooldown <= 0f)
                    {
                        if (GameAudio.Instance != null) GameAudio.Instance.PlayPiranhaChompSound();
                    }
                }
                else
                {
                    var r = (snapPhase - 0.68f) / 0.32f;
                    jawOpen = Mathf.Lerp(0f, 0.25f, r);
                    lungeDeg = Mathf.Lerp(32f, 0f, Ease.OutCubic(r));
                }

                ApplyPiranhaPose(erect01: 1f, jawOpen01: jawOpen, lungePitchDeg: lungeDeg, leafFlapDeg: Mathf.Sin(_piranhaTimer * 12f) * 10f);

                // Bite hitbox check during active lunge / close contact!
                if (hero != null && _piranhaBiteCooldown <= 0f && flatDist < 2.25f && Mathf.Abs(yDiff) < 2.4f && snapPhase is >= 0.48f and <= 0.76f)
                {
                    _piranhaBiteCooldown = 1.15f;
                    hero.TakeDamage(2, transform.position);
                    if (GameAudio.Instance != null) GameAudio.Instance.PlayPiranhaChompSound();
                }

                if (hero == null || flatDist > 8.6f || Mathf.Abs(yDiff) > 4.2f)
                {
                    _piranhaFarTimer += dt;
                    if (_piranhaFarTimer > 1.35f)
                    {
                        _piranhaState = 3;
                        _piranhaTimer = 0f;
                    }
                }
                else
                {
                    _piranhaFarTimer = 0f;
                }
                break;
            }
            case 3: // Returning back to curled sleep pose
            {
                var u = Mathf.Clamp01(_piranhaTimer / 0.55f);
                ApplyPiranhaPose(erect01: 1f - Ease.OutCubic(u), jawOpen01: 0.05f, lungePitchDeg: 0f, leafFlapDeg: 0f);
                if (u >= 1f)
                {
                    _piranhaState = 0;
                    _piranhaTimer = 0f;
                }
                break;
            }
        }
    }

    private void FaceHeroSmoothly(MarioRemakeController hero, float speed)
    {
        if (hero == null) return;
        var dir = hero.transform.position - transform.position;
        dir.y = 0f;
        if (!(dir.sqrMagnitude > 0.01f)) return;
        var look = Quaternion.LookRotation(dir.normalized, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, look, Mathf.Clamp01(speed));
    }

    private void ApplyPiranhaPose(float erect01, float jawOpen01, float lungePitchDeg, float leafFlapDeg)
    {
        if (_piranhaStem != null)
        {
            var stemY = Mathf.Lerp(0.46f, 1.05f, erect01);
            var stemW = Mathf.Lerp(1.12f, 1.0f, erect01);
            _piranhaStem.localScale = new Vector3(stemW, stemY, stemW);
            var sleepCurlX = Mathf.Lerp(34f, 0f, erect01);
            _piranhaStem.localRotation = Quaternion.Euler(sleepCurlX + lungePitchDeg * 0.45f, 0f, 0f);
        }
        if (_piranhaHead != null)
        {
            var headPitch = Mathf.Lerp(26f, -6f, erect01) + lungePitchDeg * 0.55f;
            _piranhaHead.localRotation = Quaternion.Euler(headPitch, 0f, 0f);
        }
        if (_piranhaJawUpper != null)
        {
            _piranhaJawUpper.localRotation = Quaternion.Euler(-38f * jawOpen01, 0f, 0f);
        }
        if (_piranhaJawLower != null)
        {
            _piranhaJawLower.localRotation = Quaternion.Euler(32f * jawOpen01, 0f, 0f);
        }
        if (_piranhaLeafL != null)
        {
            _piranhaLeafL.localRotation = Quaternion.Euler(0f, 0f, 14f + leafFlapDeg);
        }
        if (_piranhaLeafR != null)
        {
            _piranhaLeafR.localRotation = Quaternion.Euler(0f, 0f, -14f - leafFlapDeg);
        }
    }

    public void DefeatSleepingPiranha()
    {
        if (!gameObject.activeSelf) return;
        if (GameAudio.Instance != null) GameAudio.Instance.PlayEnemyDefeatSound();
        if (VFXPool.Instance != null) VFXPool.Instance.SpawnPickupBurst(transform.position + Vector3.up * 0.9f, isStar: false);
        if (GameManager.Instance != null)
        {
            GameManager.Instance.coinsCollected += 5; // Blue Coin equivalent (5 Coins!)
            GameManager.Instance.TriggerHitStop(0.05f);
        }
        if (MarioRemakeController.Instance != null) MarioRemakeController.Instance.HealHealth(2);
        gameObject.SetActive(false);
    }

    private void UpdateHootOwl(float dt)
    {
        if (_hootCooldown > 0f) _hootCooldown -= dt;
        if (_cannonLaunching) return;

        var bobY = Mathf.Sin(_clock * 2.4f) * 0.14f;
        transform.position = _startPos + new Vector3(0f, bobY, 0f);

        if (_hootCooldown > 0f) return;

        var hero = MarioRemakeController.Instance;
        if (hero == null) return;

        var flatDist = Vector2.Distance(
            new Vector2(hero.transform.position.x, hero.transform.position.z),
            new Vector2(_startPos.x, _startPos.z));
        if (flatDist < 2.2f && hero.transform.position.y >= _startPos.y - 2.4f && hero.transform.position.y <= _startPos.y + 2.5f)
        {
            StartCoroutine(HootOwlFlightRoutine(hero));
        }
    }

    private IEnumerator HootOwlFlightRoutine(MarioRemakeController hero)
    {
        _cannonLaunching = true;
        var cc = hero.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        if (GameAudio.Instance != null) GameAudio.Instance.PlayJumpSound(2);
        if (VFXPool.Instance != null) VFXPool.Instance.SpawnFootstepPuff(_startPos);

        var p0 = _startPos + Vector3.up * 1.2f;
        var mid = primaryLaunchTarget;
        var dropOverCage = secondaryLaunchTarget;

        // Glide across fortress toward Sky Islands, then hold right above Caged Island's top opening
        yield return Ease.Tween(1.1f, t =>
        {
            if (hero == null) return;
            var pos = Vector3.Lerp(p0, mid, Mathf.SmoothStep(0f, 1f, t));
            pos.y += Mathf.Sin(t * Mathf.PI) * 3.5f;
            transform.position = pos + Vector3.up * 1.15f;
            transform.LookAt(new Vector3(dropOverCage.x, transform.position.y, dropOverCage.z));
            hero.transform.position = pos;
        }, Ease.OutCubic, unscaled: false);

        yield return Ease.Tween(1.15f, t =>
        {
            if (hero == null) return;
            var pos = Vector3.Lerp(mid, dropOverCage, Mathf.SmoothStep(0f, 1f, t));
            pos.y += Mathf.Sin(t * Mathf.PI) * 1.4f;
            transform.position = pos + Vector3.up * 1.15f;
            hero.transform.position = pos;
        }, Ease.OutCubic, unscaled: false);

        if (hero != null)
        {
            hero.transform.position = dropOverCage;
            if (cc != null) cc.enabled = true;
        }
        if (VFXPool.Instance != null) VFXPool.Instance.SpawnFootstepPuff(dropOverCage);

        transform.SetPositionAndRotation(_startPos, _startRot);
        _hootCooldown = 2.8f;
        _cannonLaunching = false;
    }

    private void UpdateThwomp(float dt)
    {
        _thwompTimer += dt;
        var topPos = _startPos + new Vector3(0f, Mathf.Abs(moveOffset.y), 0f);
        var bottomPos = _startPos;

        switch (_thwompState)
        {
            case 0: // Top wait — react swiftly when Mario enters underneath or after max cycle timeout!
            {
                var hero = MarioRemakeController.Instance;
                var heroBeneath = hero != null &&
                    Vector2.Distance(new Vector2(hero.transform.position.x, hero.transform.position.z), new Vector2(_startPos.x, _startPos.z)) < 4.5f &&
                    hero.transform.position.y <= _startPos.y + 3.5f &&
                    hero.transform.position.y >= _startPos.y - 3.5f;

                var preTrigger = heroBeneath && _thwompTimer >= 0.18f;
                var maxCycle = _thwompTimer >= 4.2f + phaseOffset;
                var shake = (preTrigger || _thwompTimer > 3.6f) ? Mathf.Sin(_thwompTimer * 58f) * 0.08f : 0f;
                transform.position = topPos + new Vector3(shake, 0f, 0f);
                if ((heroBeneath && _thwompTimer >= 0.38f) || maxCycle)
                {
                    _thwompTimer = 0f;
                    _thwompState = 1;
                }
                break;
            }
            case 1: // Slam down fast!
            {
                var t = Mathf.Clamp01(_thwompTimer / 0.24f);
                transform.position = Vector3.Lerp(topPos, bottomPos, t * t);
                if (t >= 1f)
                {
                    _thwompTimer = 0f;
                    _thwompState = 2;
                    if (VFXPool.Instance != null) VFXPool.Instance.SpawnFootstepPuff(bottomPos);
                    var hero = MarioRemakeController.Instance;
                    if (hero != null && Vector3.Distance(hero.transform.position, bottomPos) < 14f)
                    {
                        if (GameAudio.Instance != null) GameAudio.Instance.PlayGroundPoundSound();
                        if (hero.impulseSource != null) hero.impulseSource.GenerateImpulse(0.32f);

                        // Crush check: if Mario is caught directly underneath when Thwomp slams down!
                        var pDist = Vector2.Distance(
                            new Vector2(hero.transform.position.x, hero.transform.position.z),
                            new Vector2(bottomPos.x, bottomPos.z));
                        if (pDist < 1.95f && hero.transform.position.y < bottomPos.y + 1.35f)
                        {
                            if (hero.motion != null) hero.motion.TriggerLandSquash(heavy: true);
                            hero.TakeDamage(3, bottomPos);
                        }
                    }
                }
                break;
            }
            case 2: // Rest at bottom
            {
                transform.position = bottomPos;
                if (_thwompTimer >= 0.85f)
                {
                    _thwompTimer = 0f;
                    _thwompState = 3;
                }
                break;
            }
            case 3: // Rise back to top
            {
                var t = Mathf.Clamp01(_thwompTimer / 1.35f);
                transform.position = Vector3.Lerp(bottomPos, topPos, Ease.OutCubic(t));
                if (t >= 1f)
                {
                    _thwompTimer = 0f;
                    _thwompState = 0;
                }
                break;
            }
        }
    }

    private void CheckKickableBoardProximity()
    {
        if (_boardKnockedDown || _boardFalling) return;
        var hero = MarioRemakeController.Instance;
        if (hero == null) return;

        var flatDist = Vector2.Distance(
            new Vector2(hero.transform.position.x, hero.transform.position.z),
            new Vector2(transform.position.x, transform.position.z));
        if (flatDist < 2.25f && Mathf.Abs(hero.transform.position.y - transform.position.y) < 3.5f)
        {
            StartCoroutine(FallBridgeRoutine());
        }
    }

    public void KnockDownImmediate()
    {
        _boardKnockedDown = true;
        _boardFalling = false;
        transform.rotation = _startRot * Quaternion.Euler(90f, 0f, 0f);
    }

    private IEnumerator FallBridgeRoutine()
    {
        _boardFalling = true;
        if (GameAudio.Instance != null) GameAudio.Instance.PlayGroundPoundSound();
        var endRot = _startRot * Quaternion.Euler(90f, 0f, 0f);
        yield return Ease.Tween(0.45f, t =>
        {
            transform.rotation = Quaternion.SlerpUnclamped(_startRot, endRot, Ease.OutBounceCustom(t));
        }, Ease.OutCubic, unscaled: false);
        transform.rotation = endRot;
        _boardKnockedDown = true;
        _boardFalling = false;
        if (VFXPool.Instance != null) VFXPool.Instance.SpawnFootstepPuff(transform.position);
    }
}
