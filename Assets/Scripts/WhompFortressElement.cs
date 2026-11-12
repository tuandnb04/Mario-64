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
        SleepingPiranha
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
public class WhompFortressElement : MonoBehaviour
{
    public Mario64Remake.FortressHazardType hazardType = Mario64Remake.FortressHazardType.ShoveBlock;
    public Vector3 moveOffset = new(0f, 0f, 3.2f);
    public float cycleSpeed = 1.2f;
    public float phaseOffset;
    public float rotationSpeed = 42f;
    public float tiltMaxAngle = 16f;
    public Vector3 primaryLaunchTarget = new(-13.5f, 16.5f, 5.5f);
    public Vector3 secondaryLaunchTarget = new(14.5f, 10.2f, -1.5f);
    public StarCoinCollectible revealedStar;

    private Vector3 _startPos;
    private Quaternion _startRot;
    private Vector3 _startScale;
    private float _clock;
    private bool _boardKnockedDown;
    private bool _boardFalling;
    private bool _cannonLaunching;
    private bool _useSecondaryTarget;
    private float _cannonCooldown;
    private float _thwompTimer;
    private int _thwompState; // 0 = top wait, 1 = slamming down, 2 = bottom wait, 3 = rising up

    private void Awake()
    {
        _startPos = transform.position;
        _startRot = transform.rotation;
        _startScale = transform.localScale;
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
        _thwompTimer = 0f;
        _thwompState = 0;
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
                transform.rotation = _startRot * Quaternion.Euler(0f, 0f, angle);
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
                UpdateSleepingPiranha();
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
            if (GameManager.Instance != null && GameManager.Instance.currentActNumber == 6)
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

        if (GameAudio.Instance != null) GameAudio.Instance.PlayGroundPoundSound();
        if (VFXPool.Instance != null) VFXPool.Instance.SpawnPickupBurst(transform.position + Vector3.up * 0.8f, isStar: true);

        var p0 = transform.position + Vector3.up * 1.1f;
        yield return Ease.Tween(0.85f, t =>
        {
            if (hero == null) return;
            var pos = Vector3.Lerp(p0, destination, t);
            pos.y += Mathf.Sin(t * Mathf.PI) * 9.5f;
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
            if (GameAudio.Instance != null) GameAudio.Instance.PlayGroundPoundSound();
            if (VFXPool.Instance != null) VFXPool.Instance.SpawnPickupBurst(transform.position, isStar: true);
            if (GameManager.Instance != null) GameManager.Instance.TriggerHitStop(0.06f);
            if (revealedStar != null && (GameManager.Instance == null || GameManager.Instance.currentActNumber == 6))
                revealedStar.gameObject.SetActive(true);
            gameObject.SetActive(false);
        }
    }

    private void UpdateSleepingPiranha()
    {
        if (_startScale.sqrMagnitude < 0.0001f) _startScale = Vector3.one;
        var breathe = 1f + Mathf.Sin(_clock * 2.2f) * 0.06f;
        transform.localScale = new Vector3(_startScale.x * breathe, _startScale.y / breathe, _startScale.z * breathe);

        var hero = MarioRemakeController.Instance;
        if (hero == null) return;
        var dist = Vector3.Distance(hero.transform.position, transform.position);
        if (dist < 1.9f && hero.transform.position.y > transform.position.y + 0.5f)
        {
            if (GameAudio.Instance != null) GameAudio.Instance.PlayPickupBlip(0.25f);
            if (VFXPool.Instance != null) VFXPool.Instance.SpawnPickupBurst(transform.position + Vector3.up * 0.6f, isStar: false);
            if (GameManager.Instance != null)
            {
                GameManager.Instance.coinsCollected += 5; // Blue Coin equivalent (5 Coins!)
            }
            gameObject.SetActive(false);
        }
    }

    private void UpdateThwomp(float dt)
    {
        _thwompTimer += dt;
        var topPos = _startPos + new Vector3(0f, Mathf.Abs(moveOffset.y), 0f);
        var bottomPos = _startPos;

        switch (_thwompState)
        {
            case 0: // Top wait with slight angry tremble
            {
                var shake = (_thwompTimer > 1.0f) ? Mathf.Sin(_thwompTimer * 55f) * 0.06f : 0f;
                transform.position = topPos + new Vector3(shake, 0f, 0f);
                if (_thwompTimer >= 1.45f + phaseOffset)
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
                        if (pDist < 1.45f && hero.transform.position.y < bottomPos.y + 1.15f)
                        {
                            if (hero.motion != null) hero.motion.TriggerLandSquash(heavy: true);
                            hero.ResetToSpawn();
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

        var dist = Vector3.Distance(hero.transform.position, transform.position);
        if (dist < 2.25f)
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
