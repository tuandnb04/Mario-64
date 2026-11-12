using System;
using System.Collections;
using Mario64Remake;
using UnityEngine;

namespace Mario64Remake
{
    public enum BattlefieldElementType
    {
        KingBobOmbBoss,
        ChainChompStake,
        KoopaTheQuickRacer,
        WingCapRedBlock,
        SkyCoinRingCenter,
        RollingIronBall,
        SeesawBridge
    }
}

/// <summary>
/// Interactive mechanics for Bob-omb Battlefield (Course 1 Remake):
/// 1. KingBobOmbBoss ("Big Bob-omb on the Summit" - Star 1):
///    - Giant regal black Bob-omb with golden crown, white royal mustache, and spinning windup key on the Summit!
///    - Takes 3 hits (Ground Pound / Dive / Jump-Stomp on his back/crown) -> flashes, hops, and spawns Star 1!
/// 2. KoopaTheQuickRacer ("Footrace with Koopa the Quick" - Star 2):
///    - Green-shelled Koopa standing near the start bridge; when Mario approaches, starts sprinting up the mountain path to the Summit Flagpole!
///    - If Mario reaches the Summit Flagpole before or right alongside Koopa, spawns Star 2!
/// 3. SkyCannonLauncher (via WhompFortressElement) -> shoots Mario to Floating Sky Island ("Shoot to the Island in the Sky" - Star 3).
/// 4. 8 Red Coins ("Find the 8 Red Coins" - Star 4) -> reveals Secret Star at the 4-post stilt area!
/// 5. WingCapRedBlock & SkyCoinRingCenter ("Mario Wings to the Sky" - Star 5):
///    - Hitting the Red Cap Block on the Floating Sky Island launches Mario through the 5 Sky Coin Rings and reveals Star 5!
/// 6. ChainChompStake ("Behind Chain Chomp's Gate" - Star 6):
///    - Wooden stake holding a lunging giant black Chain Chomp; 3 Ground Pounds drive the stake into the ground, freeing Chain Chomp to smash open the barred gate and reveal Star 6!
/// 7. RollingIronBall & SeesawBridge:
///    - Rolling black iron bowling balls along the mountain spiral & a tilting seesaw bridge over the trench!
/// </summary>
public class BobOmbBattlefieldController : MonoBehaviour
{
    public Mario64Remake.BattlefieldElementType elementType = Mario64Remake.BattlefieldElementType.KingBobOmbBoss;
    public int maxHits = 3;
    public int currentHits;
    public Transform visualSubTarget;
    public Transform secondaryVisualTarget;
    public GameObject gateToDestroy;
    public StarCoinCollectible rewardStar;
    public Vector3 patrolOrPathOffset = new(2.5f, 0f, 0f);
    public Vector3[] raceWaypoints;
    public float moveSpeed = 1.0f;

    private Vector3 _originPos;
    private Quaternion _originRot;
    private Vector3 _subTargetOriginPos;
    private float _clock;
    private float _invulnTimer;
    private bool _completed;
    private bool _activeTriggered;
    private int _waypointIndex;

    private void Awake()
    {
        _originPos = transform.position;
        _originRot = transform.rotation;
        if (visualSubTarget != null) _subTargetOriginPos = visualSubTarget.localPosition;
        currentHits = 0;
    }

    public void ResetBattlefieldElement()
    {
        if (_originPos.sqrMagnitude > 0.0001f)
        {
            transform.SetPositionAndRotation(_originPos, _originRot);
        }
        if (visualSubTarget != null && _subTargetOriginPos.sqrMagnitude > 0.0001f)
        {
            visualSubTarget.localPosition = _subTargetOriginPos;
            visualSubTarget.gameObject.SetActive(true);
        }
        if (gateToDestroy != null)
        {
            gateToDestroy.SetActive(true);
        }
        if (rewardStar != null && (elementType == Mario64Remake.BattlefieldElementType.KingBobOmbBoss ||
                                   elementType == Mario64Remake.BattlefieldElementType.ChainChompStake ||
                                   elementType == Mario64Remake.BattlefieldElementType.KoopaTheQuickRacer ||
                                   elementType == Mario64Remake.BattlefieldElementType.WingCapRedBlock))
        {
            rewardStar.gameObject.SetActive(false);
        }
        currentHits = 0;
        _completed = false;
        _activeTriggered = false;
        _waypointIndex = 0;
        _invulnTimer = 0f;
        gameObject.SetActive(true);
    }

    private void Update()
    {
        if (_originPos.sqrMagnitude < 0.0001f && transform.position.sqrMagnitude > 0.0001f)
        {
            _originPos = transform.position;
            _originRot = transform.rotation;
        }

        var dt = Time.deltaTime > 0.0001f ? Time.deltaTime : (Time.timeScale > 0f ? 0.016f * Time.timeScale : 0f);
        if (dt <= 0.00001f) return;

        if (_invulnTimer > 0f) _invulnTimer -= dt;
        _clock += dt * moveSpeed;

        switch (elementType)
        {
            case Mario64Remake.BattlefieldElementType.KingBobOmbBoss:
                UpdateKingBobOmb(dt);
                break;
            case Mario64Remake.BattlefieldElementType.ChainChompStake:
                UpdateChainChompAndStake();
                break;
            case Mario64Remake.BattlefieldElementType.KoopaTheQuickRacer:
                UpdateKoopaRace(dt);
                break;
            case Mario64Remake.BattlefieldElementType.WingCapRedBlock:
                UpdateWingCapBlock(dt);
                break;
            case Mario64Remake.BattlefieldElementType.RollingIronBall:
                UpdateRollingBall(dt);
                break;
            case Mario64Remake.BattlefieldElementType.SeesawBridge:
                UpdateSeesaw();
                break;
        }
    }

    private void UpdateKingBobOmb(float dt)
    {
        if (_completed) return;

        // Regal waddle on the summit
        var s = Mathf.Sin(_clock * 1.4f);
        var pos = _originPos + patrolOrPathOffset * s;
        pos.y = _originPos.y + Mathf.Abs(Mathf.Sin(_clock * 7f)) * 0.14f;
        transform.position = pos;

        var yaw = Mathf.Sin(_clock * 1.4f) * 35f;
        var waddleRoll = Mathf.Sin(_clock * 7f) * 6f;
        transform.rotation = _originRot * Quaternion.Euler(0f, yaw, waddleRoll);

        if (secondaryVisualTarget != null)
        {
            secondaryVisualTarget.Rotate(0f, 0f, 220f * dt, Space.Self);
        }

        // Check if Mario attacks / Ground Pounds / jumps onto King Bob-omb!
        if (_invulnTimer > 0f) return;
        var hero = MarioRemakeController.Instance;
        if (hero == null) return;

        var dist = Vector3.Distance(hero.transform.position, transform.position);
        if (dist < 3.4f && (hero.transform.position.y > transform.position.y + 0.4f || hero.IsAttackingOrSliding))
        {
            RegisterHitOnElement();
        }
    }

    public void RegisterHitOnElement()
    {
        if (_completed || _invulnTimer > 0f) return;
        switch (elementType)
        {
            case BattlefieldElementType.KingBobOmbBoss:
            {
                _invulnTimer = 0.85f;
                currentHits++;
                if (GameAudio.Instance != null) GameAudio.Instance.PlayGroundPoundSound();
                if (VFXPool.Instance != null) VFXPool.Instance.SpawnPickupBurst(transform.position + Vector3.up * 1.4f, isStar: true);

                if (currentHits >= maxHits)
                {
                    _completed = true;
                    if (rewardStar != null) rewardStar.gameObject.SetActive(true);
                    if (GameManager.Instance != null) GameManager.Instance.TriggerHitStop(0.08f);
                    gameObject.SetActive(false);
                }
                else
                {
                    if (GameManager.Instance != null) GameManager.Instance.TriggerHitStop(0.05f);
                }

                break;
            }
            case BattlefieldElementType.ChainChompStake:
            {
                _invulnTimer = 0.55f;
                currentHits++;
                transform.position -= new Vector3(0f, 0.35f, 0f);
                if (GameAudio.Instance != null) GameAudio.Instance.PlayGroundPoundSound();
                if (VFXPool.Instance != null) VFXPool.Instance.SpawnFootstepPuff(transform.position);

                if (currentHits >= maxHits)
                {
                    _completed = true;
                    StartCoroutine(ReleaseChainChompRoutine());
                }

                break;
            }
            case BattlefieldElementType.KoopaTheQuickRacer:
            case BattlefieldElementType.WingCapRedBlock:
            case BattlefieldElementType.SkyCoinRingCenter:
            case BattlefieldElementType.RollingIronBall:
            case BattlefieldElementType.SeesawBridge:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void UpdateChainChompAndStake()
    {
        if (_completed) return;

        // Animate Chain Chomp lunging & barking on its chain!
        if (visualSubTarget != null)
        {
            var lunge = Mathf.Sin(_clock * 3.8f);
            var hop = Mathf.Abs(Mathf.Sin(_clock * 7.6f)) * 0.65f;
            visualSubTarget.localPosition = _subTargetOriginPos + new Vector3(lunge * 1.35f, hop, Mathf.Cos(_clock * 2.5f) * 0.9f);
            visualSubTarget.localRotation = Quaternion.Euler(Mathf.Sin(_clock * 7.6f) * -14f, _clock * 45f, 0f);
        }

        // Check if Mario Ground Pounds or stomps the Wooden Stake!
        if (_invulnTimer > 0f) return;
        var hero = MarioRemakeController.Instance;
        if (hero == null) return;

        var dist = Vector3.Distance(hero.transform.position, transform.position);
        if (!(dist < 2.2f) || !(hero.transform.position.y > transform.position.y + 0.15f)) return;
        _invulnTimer = 0.65f;
        currentHits++;
        // Drive stake deeper into the ground by 0.35 units per pound!
        transform.position -= new Vector3(0f, 0.35f, 0f);
        if (GameAudio.Instance != null) GameAudio.Instance.PlayGroundPoundSound();
        if (VFXPool.Instance != null) VFXPool.Instance.SpawnFootstepPuff(transform.position);

        if (currentHits < maxHits) return;
        _completed = true;
        StartCoroutine(ReleaseChainChompRoutine());
    }

    private IEnumerator ReleaseChainChompRoutine()
    {
        if (GameManager.Instance != null) GameManager.Instance.TriggerHitStop(0.07f);
        if (visualSubTarget != null && gateToDestroy != null)
        {
            var start = visualSubTarget.position;
            var end = gateToDestroy.transform.position;
            yield return Ease.Tween(0.45f, t =>
            {
                if (visualSubTarget == null) return;
                var p = Vector3.Lerp(start, end, t);
                p.y += Mathf.Sin(t * Mathf.PI) * 2.8f;
                visualSubTarget.position = p;
            }, Ease.OutCubic, unscaled: false);
        }

        if (gateToDestroy != null)
        {
            if (VFXPool.Instance != null) VFXPool.Instance.SpawnPickupBurst(gateToDestroy.transform.position, isStar: true);
            gateToDestroy.SetActive(false);
        }

        if (visualSubTarget != null) visualSubTarget.gameObject.SetActive(false);
        if (rewardStar != null && (GameManager.Instance == null || GameManager.Instance.currentActNumber == 6))
            rewardStar.gameObject.SetActive(true);
    }

    private void UpdateKoopaRace(float dt)
    {
        if (_completed) return;
        var hero = MarioRemakeController.Instance;
        if (hero == null) return;

        if (!_activeTriggered)
        {
            // Idle bounce at start line until Mario approaches
            transform.position = _originPos + Vector3.up * (Mathf.Abs(Mathf.Sin(_clock * 5f)) * 0.15f);
            if (!(Vector3.Distance(hero.transform.position, transform.position) < 4.5f)) return;
            _activeTriggered = true;
            _waypointIndex = 0;
            if (GameAudio.Instance != null) GameAudio.Instance.PlayPickupBlip(0.2f);
            return;
        }

        // Move along race waypoints up the mountain!
        if (raceWaypoints != null && _waypointIndex < raceWaypoints.Length)
        {
            var target = raceWaypoints[_waypointIndex];
            transform.position = Vector3.MoveTowards(transform.position, target, moveSpeed * 4.2f * dt);
            var dir = target - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f)
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir.normalized), 10f * dt);
            }
            if (Vector3.Distance(transform.position, target) < 0.35f)
            {
                _waypointIndex++;
            }
        }

        // Check if Mario reaches the Summit Flagpole (final waypoint)!
        if (raceWaypoints is not { Length: > 0 }) return;
        var finishPos = raceWaypoints[^1];
        if (!(Vector3.Distance(hero.transform.position, finishPos) < 4.2f)) return;
        _completed = true;
        if (rewardStar != null) rewardStar.gameObject.SetActive(true);
        if (VFXPool.Instance != null) VFXPool.Instance.SpawnPickupBurst(finishPos + Vector3.up * 1.2f, isStar: true);
    }

    private void UpdateWingCapBlock(float dt)
    {
        transform.position = _originPos + Vector3.up * (Mathf.Sin(_clock * 3.5f) * 0.18f);
        transform.Rotate(0f, 45f * dt, 0f, Space.World);

        if (_activeTriggered) return;
        var hero = MarioRemakeController.Instance;
        if (hero == null) return;

        if (Vector3.Distance(hero.transform.position, transform.position) < 2.2f)
        {
            StartCoroutine(WingCapFlightSequenceRoutine(hero));
        }
    }

    private IEnumerator WingCapFlightSequenceRoutine(MarioRemakeController hero)
    {
        _activeTriggered = true;
        var cc = hero.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        if (GameAudio.Instance != null) GameAudio.Instance.PlayMilestoneSting();
        if (VFXPool.Instance != null) VFXPool.Instance.SpawnPickupBurst(transform.position, isStar: true);

        // Fly Mario gracefully through the 5 Sky Coin Rings!
        if (raceWaypoints is { Length: > 0 })
        {
            for (var i = 0; i < raceWaypoints.Length; i++)
            {
                var p0 = hero.transform.position;
                var p1 = raceWaypoints[i];
                yield return Ease.Tween(0.42f, t =>
                {
                    if (hero == null) return;
                    var p = Vector3.Lerp(p0, p1, t);
                    p.y += Mathf.Sin(t * Mathf.PI) * 1.2f;
                    hero.transform.position = p;
                    var dir = (p1 - p0);
                    if (dir.sqrMagnitude > 0.01f) hero.transform.rotation = Quaternion.LookRotation(dir.normalized);
                }, Ease.OutCubic, unscaled: false);

                if (GameManager.Instance != null) GameManager.Instance.coinsCollected += 1;
                if (GameAudio.Instance != null) GameAudio.Instance.PlayPickupBlip(i * 0.08f);
                if (VFXPool.Instance != null && hero != null) VFXPool.Instance.SpawnPickupBurst(hero.transform.position, isStar: false);
            }
        }

        if (rewardStar != null)
        {
            rewardStar.gameObject.SetActive(true);
        }

        if (hero != null && cc != null) cc.enabled = true;
    }

    private void UpdateRollingBall(float dt)
    {
        var t = Mathf.Repeat(_clock * 0.28f, 1f);
        var pos = Vector3.Lerp(_originPos, _originPos + patrolOrPathOffset, t);
        pos.y = _originPos.y + Mathf.Lerp(0f, patrolOrPathOffset.y, t) + Mathf.Abs(Mathf.Sin(t * Mathf.PI * 6f)) * 0.15f;
        transform.position = pos;
        transform.Rotate(260f * dt, 120f * dt, 0f, Space.Self);
    }

    private void UpdateSeesaw()
    {
        var angle = Mathf.Sin(_clock * 2.2f) * 15f;
        transform.rotation = _originRot * Quaternion.Euler(angle, 0f, 0f);
    }
}

