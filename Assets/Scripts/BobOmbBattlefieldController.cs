using System;
using System.Collections;
using Mario64Remake;
using UnityEngine;
using UnityEngine.InputSystem;

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
    public BattlefieldElementType elementType = BattlefieldElementType.KingBobOmbBoss;
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
    private float _kingGrabTimer;
    private float _kingFrontBumpCooldown;
    private float _chompCycleTimer;
    private Vector3 _chompRestLocalPos;
    private Transform[] _chompChainLinks;

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
        if (rewardStar != null && (elementType == BattlefieldElementType.KingBobOmbBoss ||
                                   elementType == BattlefieldElementType.ChainChompStake ||
                                   elementType == BattlefieldElementType.KoopaTheQuickRacer ||
                                   elementType == BattlefieldElementType.WingCapRedBlock))
        {
            rewardStar.gameObject.SetActive(false);
        }
        currentHits = 0;
        _completed = false;
        _activeTriggered = false;
        _waypointIndex = 0;
        _invulnTimer = 0f;
        _kingGrabTimer = 0f;
        _kingFrontBumpCooldown = 0f;
        _chompCycleTimer = 0f;
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
            case BattlefieldElementType.KingBobOmbBoss:
                UpdateKingBobOmb(dt);
                break;
            case BattlefieldElementType.ChainChompStake:
                UpdateChainChompAndStake(dt);
                break;
            case BattlefieldElementType.KoopaTheQuickRacer:
                UpdateKoopaRace(dt);
                break;
            case BattlefieldElementType.WingCapRedBlock:
                UpdateWingCapBlock(dt);
                break;
            case BattlefieldElementType.RollingIronBall:
                UpdateRollingBall(dt);
                break;
            case BattlefieldElementType.SeesawBridge:
                UpdateSeesaw();
                break;
            case BattlefieldElementType.SkyCoinRingCenter:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void UpdateKingBobOmb(float dt)
    {
        if (_completed) return;
        if (_kingFrontBumpCooldown > 0f) _kingFrontBumpCooldown -= dt;

        var hero = MarioRemakeController.Instance;
        if (secondaryVisualTarget != null)
        {
            secondaryVisualTarget.Rotate(0f, 0f, (_kingGrabTimer > 0f ? 520f : 220f) * dt, Space.Self);
        }

        // Behind-back Grab & Slam Throw animation if triggered
        if (_kingGrabTimer > 0f)
        {
            _kingGrabTimer -= dt;
            if (hero != null && _kingGrabTimer > 0.25f)
            {
                transform.position = Vector3.Lerp(transform.position, hero.transform.position + Vector3.up * 2.2f, 12f * dt);
                transform.Rotate(0f, 480f * dt, 0f, Space.Self);
            }
            else if (_kingGrabTimer <= 0.01f)
            {
                transform.position = new Vector3(transform.position.x, _originPos.y, transform.position.z);
                RegisterHitOnElement();
            }
            return;
        }

        var flatDistToHero = 999f;
        if (hero != null)
        {
            var toHero = hero.transform.position - transform.position;
            toHero.y = 0f;
            flatDistToHero = toHero.magnitude;

            // Pursue hero when Mario is up on the Summit Arena
            if (Mathf.Abs(hero.transform.position.y - _originPos.y) < 5.5f && flatDistToHero < 16f)
            {
                if (toHero.sqrMagnitude > 0.01f)
                {
                    var desiredRot = Quaternion.LookRotation(toHero.normalized, Vector3.up);
                    // Slow turning allows Mario to dash behind King Bob-omb!
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, desiredRot, 92f * dt);
                }

                if (flatDistToHero > 2.35f)
                {
                    var nextPos = transform.position + transform.forward * (3.15f * dt);
                    var offset = new Vector2(nextPos.x - _originPos.x, nextPos.z - _originPos.z);
                    if (offset.magnitude > 7.6f)
                    {
                        offset = offset.normalized * 7.6f;
                        nextPos.x = _originPos.x + offset.x;
                        nextPos.z = _originPos.z + offset.y;
                    }
                    var bounce = Mathf.Abs(Mathf.Sin(_clock * 8.5f)) * 0.16f;
                    transform.position = new Vector3(nextPos.x, _originPos.y + bounce, nextPos.z);
                }
            }
            else
            {
                // Pacing calmly on Summit waiting for challenger
                var wanderRot = _originRot * Quaternion.Euler(0f, Mathf.Sin(_clock * 0.7f) * 35f, 0f);
                transform.rotation = Quaternion.Slerp(transform.rotation, wanderRot, 3f * dt);
                var bounce = Mathf.Abs(Mathf.Sin(_clock * 5.2f)) * 0.08f;
                transform.position = new Vector3(transform.position.x, _originPos.y + bounce, transform.position.z);
            }
        }

        if (visualSubTarget != null)
        {
            var swayZ = Mathf.Sin(_clock * 8.5f) * 5.5f;
            visualSubTarget.localRotation = Quaternion.Euler(0f, 0f, swayZ);
        }

        // Combat Checks: Behind-back Grab (F) or Jump-Stomp / Ground-Pound / Attack
        if (_invulnTimer > 0f || hero == null) return;

        var dist3D = Vector3.Distance(hero.transform.position, transform.position);
        var heroFromKing = hero.transform.position - transform.position;
        heroFromKing.y = 0f;
        var dotFront = heroFromKing.sqrMagnitude > 0.01f
            ? Vector3.Dot(transform.forward, heroFromKing.normalized)
            : 1f;

        var grabPressed = Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame;
        switch (dist3D)
        {
            case < 3.4f when dotFront < 0.2f && grabPressed:
            {
                _kingGrabTimer = 0.55f;
                if (GameAudio.Instance != null) GameAudio.Instance.PlayLedgeClimbSound();
                return;
            }
            case < 3.4f when (hero.transform.position.y > transform.position.y + 0.55f || hero.IsAttackingOrSliding):
            {
                RegisterHitOnElement();
                if (hero.IsStompingDown) hero.BounceUpFromStomp(1.85f);
                return;
            }
        }

        if (!(flatDistToHero < 2.15f) || !(dotFront > 0.55f) ||
            !(Mathf.Abs(hero.transform.position.y - transform.position.y) < 2.2f) ||
            !(_kingFrontBumpCooldown <= 0f)) return;
        _kingFrontBumpCooldown = 1.35f;
        hero.TakeDamage(1, transform.position);
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

    private void UpdateChainChompAndStake(float dt)
    {
        if (_completed) return;

        if (_chompChainLinks == null)
        {
            var links = new System.Collections.Generic.List<Transform>();
            for (var c = 1; c <= 6; c++)
            {
                var tr = transform.Find($"ChainLink_{c}");
                if (tr != null) links.Add(tr);
            }
            _chompChainLinks = links.ToArray();
            if (visualSubTarget != null) _chompRestLocalPos = _subTargetOriginPos;
        }

        var hero = MarioRemakeController.Instance;
        // SM64 AI: Aim & bark-lunge straight toward Mario clamped by chain leash, then yank back!
        if (visualSubTarget != null)
        {
            if (_chompRestLocalPos.sqrMagnitude < 0.01f) _chompRestLocalPos = _subTargetOriginPos;
            const float chainMaxRadius = 6.2f;
            var restWorld = transform.TransformPoint(_chompRestLocalPos);

            Vector3 targetWorld;
            var pitch = Mathf.Sin(_clock * 5f) * -8f;
            Quaternion yawLook;

            var flatDistFromStake = hero != null
                ? Vector2.Distance(new Vector2(hero.transform.position.x, hero.transform.position.z), new Vector2(transform.position.x, transform.position.z))
                : 999f;

            if (hero != null && flatDistFromStake < 12.0f && Mathf.Abs(hero.transform.position.y - transform.position.y) < 5.5f)
            {
                _chompCycleTimer += dt;
                var phase = Mathf.Repeat(_chompCycleTimer, 2.25f);
                var toHero = hero.transform.position - transform.position;
                toHero.y = 0f;
                var dirToHero = toHero.sqrMagnitude > 0.01f ? toHero.normalized : transform.forward;
                yawLook = Quaternion.LookRotation(dirToHero, Vector3.up);

                var lungeEndWorld = transform.position + dirToHero * chainMaxRadius + Vector3.up * Mathf.Max(1.35f, _chompRestLocalPos.y);
                var restTargetWorld = transform.position + dirToHero * 3.1f + Vector3.up * Mathf.Max(1.25f, _chompRestLocalPos.y);

                switch (phase)
                {
                    case < 0.45f:
                    {
                        // Telegraph hop + bark
                        var u = phase / 0.45f;
                        targetWorld = Vector3.Lerp(visualSubTarget.position, restTargetWorld + Vector3.up * (Mathf.Sin(u * Mathf.PI) * 0.7f), 10f * dt);
                        pitch = -18f * Mathf.Sin(u * Mathf.PI);
                        break;
                    }
                    case < 0.85f:
                    {
                        // Aggressive straight-line lunge at Mario until chain pulls taut!
                        var u = (phase - 0.45f) / 0.40f;
                        targetWorld = Vector3.Lerp(restTargetWorld, lungeEndWorld, Ease.OutCubic(u));
                        targetWorld.y += Mathf.Sin(u * Mathf.PI) * 0.95f;
                        pitch = 14f;
                        break;
                    }
                    case < 1.35f:
                        // Chain snaps taut & Chomp chomps
                        targetWorld = lungeEndWorld;
                        pitch = Mathf.Sin(phase * 32f) * 12f;
                        break;
                    default:
                    {
                        // Yank back along chain toward stake
                        var u = (phase - 1.35f) / 0.90f;
                        targetWorld = Vector3.Lerp(lungeEndWorld, restTargetWorld, Mathf.SmoothStep(0f, 1f, u));
                        break;
                    }
                }
            }
            else
            {
                _chompCycleTimer = 0f;
                var hop = Mathf.Abs(Mathf.Sin(_clock * 5.2f)) * 0.45f;
                targetWorld = restWorld + new Vector3(Mathf.Sin(_clock * 2.2f) * 0.6f, hop, Mathf.Cos(_clock * 1.9f) * 0.6f);
                yawLook = Quaternion.Euler(0f, _clock * 38f, 0f);
            }

            visualSubTarget.position = Vector3.Lerp(visualSubTarget.position, targetWorld, 1f - Mathf.Exp(-14f * dt));
            visualSubTarget.rotation = Quaternion.Slerp(visualSubTarget.rotation, yawLook * Quaternion.Euler(pitch, 0f, 0f), 1f - Mathf.Exp(-12f * dt));

            // Keep chain links taut between stake top and Chomp body
            if (_chompChainLinks.Length > 0)
            {
                var stakeTop = transform.position + Vector3.up * 0.65f;
                var chompBack = visualSubTarget.position - visualSubTarget.forward * 0.65f;
                for (var c = 0; c < _chompChainLinks.Length; c++)
                {
                    var link = _chompChainLinks[c];
                    if (link == null) continue;
                    var frac = (c + 1f) / (_chompChainLinks.Length + 1f);
                    var sag = Vector3.down * (Mathf.Sin(frac * Mathf.PI) * 0.22f);
                    link.position = Vector3.Lerp(stakeTop, chompBack, frac) + sag;
                }
            }
        }

        // Check if Mario Ground Pounds or stomps the Wooden Stake (or gets bitten by Chain Chomp!)
        if (_invulnTimer > 0f) return;
        if (hero == null) return;

        if (visualSubTarget != null && Vector3.Distance(hero.transform.position, visualSubTarget.position) < 2.15f && !hero.IsAttackingOrSliding)
        {
            hero.TakeDamage(3, visualSubTarget.position);
        }

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
        if (rewardStar != null && !rewardStar.IsCollected)
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

        var hero = MarioRemakeController.Instance;
        if (hero != null && Vector3.Distance(hero.transform.position, pos) < 1.85f)
        {
            hero.TakeDamage(2, pos);
        }
    }

    private void UpdateSeesaw()
    {
        var angle = Mathf.Sin(_clock * 2.2f) * 15f;
        transform.rotation = _originRot * Quaternion.Euler(angle, 0f, 0f);
    }
}

