using System;
using UnityEngine;

namespace Mario64Remake
{
    public enum CcmElementType
    {
        AreaWarpNode,
        SlideFinishGoal,
        BabyPenguinCarry,
        MotherPenguinNpc,
        BigPenguinRacer,
        SnowmanHeadGoal,
        SnowmanRollingBody,
        SpindriftEnemy,
        MrBlizzardEnemy,
        CcmRopewayLift,
        SnowWeatherFX
    }
}

/// <summary>
/// Runtime gameplay controller for Super Mario 64 Course 4: Cool, Cool Mountain.
/// Handles:
/// - Cottage Chimney Warp (Area 1 Summit <-> Area 2 Ice Slide) & Finish Door Exit
/// - Broken Bridge Teleporter Pair (WARP_NODE_1F <-> WARP_NODE_20)
/// - Star 1 ("Slip Slidin' Away"): Reaching Area 2 Finish carpet / finish room
/// - Star 2 ("Li'l Penguin Lost"): Carrying Tuxie from Summit to Mother Penguin at Bottom Pond
/// - Star 3 ("Big Penguin Race"): Racing Big Penguin down Area 2 Slide (with shortcut check!)
/// - Star 5 ("Snowman's Lost His Head"): Guiding Snowman Rolling Body down the mountain to rejoin Head
/// - Spindrift propeller bounce-up enemies, Mr. Blizzard hopping snowball enemies, CcmRopewayLift gondola, and gentle Snow particles.
/// </summary>
public class CoolCoolMountainController : MonoBehaviour
{
    public Mario64Remake.CcmElementType elementType = Mario64Remake.CcmElementType.AreaWarpNode;

    [Header("Rewards & References")]
    public StarCoinCollectible rewardStar;
    public CoolCoolMountainController linkedPartner;
    public CarriableObject babyPenguinCarriable;
    public CarriableObject imposterPenguinCarriable;
    public Transform visualSubTarget;

    [Header("Warp & Teleport")]
    public Vector3 warpDestination;
    public float warpFacingYaw;
    public float triggerRadius = 2.0f;
    public string warpToastText = "WARPING!";

    [Header("Paths & Trajectories")]
    public Vector3[] waypoints;
    public Vector3 shortcutCheckPos;
    public Vector3 finishLinePos;
    public float moveSpeed = 6.5f;

    public float aggroRadius = 14.0f;
    public GameObject snowballPrefab;
    public Material snowballMaterial;

    private Vector3 _originPos;
    private Quaternion _originRot;
    private Vector3 _originScale;
    private float _clock;
    private float _cooldown;
    private float _attackTimer;
    private float _backBehindTimer;
    private int _waypointIndex;
    private bool _active;
    private bool _completed;
    private bool _shortcutFlagged;
    private bool _passedMidCourse;
    private float _slideTimer;
    private bool _ropewayReverse;
    private ParticleSystem _snowPs;

    private void Awake()
    {
        _originPos = transform.position;
        _originRot = transform.rotation;
        _originScale = transform.localScale;
        if (elementType == Mario64Remake.CcmElementType.SnowWeatherFX)
        {
            SetupSnowParticleSystem();
        }
    }

    public void ResetCcmElement()
    {
        if (_originPos.sqrMagnitude > 0.0001f)
            transform.SetPositionAndRotation(_originPos, _originRot);
        if (_originScale.sqrMagnitude > 0.0001f)
            transform.localScale = _originScale;

        _clock = 0f;
        _cooldown = 0f;
        _attackTimer = 0f;
        _backBehindTimer = 0f;
        _waypointIndex = 0;
        _active = false;
        _completed = false;
        _shortcutFlagged = false;
        _passedMidCourse = false;
        _slideTimer = 0f;
        _ropewayReverse = false;
        gameObject.SetActive(true);

        if (babyPenguinCarriable != null) babyPenguinCarriable.ResetCarriable();
        if (imposterPenguinCarriable != null) imposterPenguinCarriable.ResetCarriable();
    }

    private void ReceiveWarpCooldown(float seconds = 1.6f)
    {
        _cooldown = Mathf.Max(_cooldown, seconds);
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
        _clock += dt;

        switch (elementType)
        {
            case Mario64Remake.CcmElementType.AreaWarpNode:
                UpdateAreaWarpNode();
                break;
            case Mario64Remake.CcmElementType.SlideFinishGoal:
                UpdateSlideFinishGoal();
                break;
            case Mario64Remake.CcmElementType.BabyPenguinCarry:
                UpdateBabyPenguinCarry();
                break;
            case Mario64Remake.CcmElementType.MotherPenguinNpc:
                UpdateMotherPenguin(dt);
                break;
            case Mario64Remake.CcmElementType.BigPenguinRacer:
                UpdateBigPenguinRacer(dt);
                break;
            case Mario64Remake.CcmElementType.SnowmanHeadGoal:
                UpdateSnowmanHeadGoal(dt);
                break;
            case Mario64Remake.CcmElementType.SnowmanRollingBody:
                UpdateSnowmanRollingBody(dt);
                break;
            case Mario64Remake.CcmElementType.SpindriftEnemy:
                UpdateSpindriftEnemy(dt);
                break;
            case Mario64Remake.CcmElementType.MrBlizzardEnemy:
                UpdateMrBlizzardEnemy(dt);
                break;
            case Mario64Remake.CcmElementType.CcmRopewayLift:
                UpdateRopewayLift(dt);
                break;
            case Mario64Remake.CcmElementType.SnowWeatherFX:
                UpdateSnowWeatherFX();
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void UpdateAreaWarpNode()
    {
        var hero = MarioRemakeController.Instance;
        if (hero == null || _cooldown > 0f) return;

        var gm = GameManager.Instance;
        if (gm != null && gm.currentState != GameState.Playing) return;

        var d = Vector3.Distance(hero.transform.position + Vector3.up * 0.5f, transform.position);
        if (d > triggerRadius) return;

        _cooldown = 1.8f;
        if (linkedPartner != null) linkedPartner.ReceiveWarpCooldown(1.8f);

        if (GameAudio.Instance != null) GameAudio.Instance.PlayCannonBlastSound();
        if (VFXPool.Instance != null) VFXPool.Instance.SpawnPickupBurst(transform.position, isStar: false);

        hero.TeleportAndFace(warpDestination, warpFacingYaw);

        if (gm != null && !string.IsNullOrEmpty(warpToastText))
            gm.SpawnFloatingWorldText(warpDestination + Vector3.up * 1.8f, warpToastText, Palette.AccentStarGold);

        if (VFXPool.Instance != null) VFXPool.Instance.SpawnPickupBurst(warpDestination, isStar: false);
    }

    private void UpdateSlideFinishGoal()
    {
        if (_completed) return;
        var hero = MarioRemakeController.Instance;
        if (hero == null) return;

        var gm = GameManager.Instance;
        if (gm != null && gm.currentState != GameState.Playing) return;

        if (!(Vector3.Distance(hero.transform.position, transform.position) <= triggerRadius)) return;
        _completed = true;
        if (rewardStar == null || rewardStar.IsCollected) return;
        rewardStar.gameObject.SetActive(true);
        if (GameAudio.Instance != null) GameAudio.Instance.PlayMilestoneSting();
        if (VFXPool.Instance != null) VFXPool.Instance.SpawnPickupBurst(rewardStar.transform.position, isStar: true);
        if (gm != null)
            gm.SpawnFloatingWorldText(rewardStar.transform.position + Vector3.up * 1.4f, "SLIP SLIDIN' AWAY!", Palette.AccentStarGold);
    }

    private void UpdateBabyPenguinCarry()
    {
        var vis = visualSubTarget != null ? visualSubTarget : transform;
        if (babyPenguinCarriable == null || babyPenguinCarriable.State == CarriableObject.CarryState.Idle)
        {
            var waddle = Mathf.Sin(_clock * 6.5f) * 8f;
            vis.localRotation = Quaternion.Euler(0f, 0f, waddle);
        }
        else if (babyPenguinCarriable.State == CarriableObject.CarryState.Carried)
        {
            var wiggle = Mathf.Sin(_clock * 14f) * 10f;
            vis.localRotation = Quaternion.Euler(wiggle * 0.5f, 0f, wiggle);
        }
    }

    private void UpdateMotherPenguin(float dt)
    {
        var hero = MarioRemakeController.Instance;
        if (hero != null)
        {
            var toHero = hero.transform.position - transform.position;
            toHero.y = 0f;
            if (toHero.sqrMagnitude > 0.25f)
            {
                var targetRot = Quaternion.LookRotation(toHero.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, dt * 4.5f);
            }
        }

        // Subtle idle bob / joyful jump when Star 2 is completed
        var hop = _completed ? Mathf.Abs(Mathf.Sin(_clock * 7.5f)) * 0.42f : Mathf.Sin(_clock * 2.4f) * 0.05f;
        transform.position = _originPos + Vector3.up * hop;

        if (_completed) return;

        // Reject imposter penguin if Mario brings the wrong baby penguin!
        if (imposterPenguinCarriable != null && _cooldown <= 0f)
        {
            var impDist = Vector3.Distance(imposterPenguinCarriable.transform.position, transform.position);
            if (impDist <= 4.0f)
            {
                _cooldown = 3.0f;
                if (GameManager.Instance != null)
                    GameManager.Instance.SpawnFloatingWorldText(transform.position + Vector3.up * 2.3f, "NOT MY TUXIE!", Palette.CharacterPrimary);
                if (GameAudio.Instance != null) GameAudio.Instance.PlayUIClick();
            }
        }

        if (babyPenguinCarriable == null) return;

        var tuxDist = Vector3.Distance(babyPenguinCarriable.transform.position, transform.position);
        var heroNearWithTuxie = hero != null && hero.IsCarryingObject && Vector3.Distance(hero.transform.position, transform.position) <= 4.3f && tuxDist <= 5.2f;
        if (!(tuxDist <= 4.0f) && !heroNearWithTuxie) return;
        _completed = true;
        if (babyPenguinCarriable.State == CarriableObject.CarryState.Carried)
        {
            babyPenguinCarriable.DropAtFeet(transform.position + transform.forward * 1.45f, transform.rotation);
        }

        if (rewardStar == null || rewardStar.IsCollected) return;
        rewardStar.gameObject.SetActive(true);
        if (GameAudio.Instance != null) GameAudio.Instance.PlayMilestoneSting();
        if (VFXPool.Instance != null) VFXPool.Instance.SpawnPickupBurst(rewardStar.transform.position, isStar: true);
        if (GameManager.Instance != null)
            GameManager.Instance.SpawnFloatingWorldText(rewardStar.transform.position + Vector3.up * 1.6f, "LI'L PENGUIN LOST!", Palette.AccentStarGold);
    }

    private void UpdateBigPenguinRacer(float dt)
    {
        if (waypoints == null || waypoints.Length < 2) return;
        var hero = MarioRemakeController.Instance;
        var gm = GameManager.Instance;
        if (gm != null && gm.currentState != GameState.Playing) return;

        if (!_active && !_completed)
        {
            transform.position = _originPos + Vector3.up * (Mathf.Sin(_clock * 3f) * 0.05f);
            if (hero == null) return;
            var d = Vector3.Distance(hero.transform.position, transform.position);
            if (!(d <= 5.5f)) return;
            _active = true;
            _waypointIndex = 0;
            _shortcutFlagged = false;
            _passedMidCourse = false;
            _slideTimer = 0f;
            if (gm != null)
                gm.SpawnFloatingWorldText(transform.position + Vector3.up * 2.2f, "RACE START!", Palette.AccentStarGold);
            return;
        }

        if (_completed)
        {
            var bob = Mathf.Abs(Mathf.Sin(_clock * 5f)) * 0.16f;
            var visDone = visualSubTarget != null ? visualSubTarget : transform;
            visDone.localPosition = new Vector3(0f, bob, 0f);
            return;
        }

        _slideTimer += dt;

        // Monitor anti-shortcut zone on Area 2 slide (or track Mario midway)
        if (hero != null)
        {
            if (waypoints.Length > 20 && Vector3.Distance(hero.transform.position, waypoints[waypoints.Length / 2]) < 16f)
                _passedMidCourse = true;

            if (shortcutCheckPos.sqrMagnitude > 1f)
            {
                Vector2 hxz = new(hero.transform.position.x, hero.transform.position.z);
                Vector2 sxz = new(shortcutCheckPos.x, shortcutCheckPos.z);
                if (Vector2.Distance(hxz, sxz) < 7.5f && !_passedMidCourse && _slideTimer < 7.5f)
                    _shortcutFlagged = true;
            }
        }

        // Advance Big Penguin smoothly along slide trajectory
        var penguinFinished = _waypointIndex >= waypoints.Length ? 1f : 0f;
        if (_waypointIndex < waypoints.Length)
        {
            var target = waypoints[_waypointIndex];
            var toTarget = target - transform.position;
            var dist = toTarget.magnitude;
            if (dist < 0.85f)
            {
                _waypointIndex++;
            }
            else
            {
                var dir = toTarget / dist;
                var step = moveSpeed * dt;
                transform.position += dir * Mathf.Min(step, dist);
                Vector3 flatDir = new(dir.x, 0f, dir.z);
                if (flatDir.sqrMagnitude > 0.001f)
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(flatDir.normalized, Vector3.up), dt * 9f);
            }
            if (_waypointIndex >= waypoints.Length) penguinFinished = 1f;
        }

        // Check finish line!
        if (hero == null || !(finishLinePos.sqrMagnitude > 1f)) return;
        var heroFinishDist = Vector3.Distance(hero.transform.position, finishLinePos);
        if (heroFinishDist <= 6.2f)
        {
            if (_shortcutFlagged)
            {
                if (!(_cooldown <= 0f)) return;
                _cooldown = 3.0f;
                if (gm != null)
                    gm.SpawnFloatingWorldText(hero.transform.position + Vector3.up * 2f, "NO SHORTCUTS ALLOWED!", Palette.CharacterPrimary);
                return;
            }

            // Did Mario beat (or virtually tie) the Big Penguin?
            if (penguinFinished < 0.5f || _waypointIndex >= waypoints.Length - 2)
            {
                _completed = true;
                _active = false;
                if (rewardStar == null || rewardStar.IsCollected) return;
                rewardStar.gameObject.SetActive(true);
                if (GameAudio.Instance != null) GameAudio.Instance.PlayMilestoneSting();
                if (VFXPool.Instance != null) VFXPool.Instance.SpawnPickupBurst(rewardStar.transform.position, isStar: true);
                if (gm != null)
                    gm.SpawnFloatingWorldText(rewardStar.transform.position + Vector3.up * 1.6f, "YOU BEAT BIG PENGUIN!", Palette.AccentStarGold);
            }
            else
            {
                if (!(_cooldown <= 0f)) return;
                _cooldown = 3.0f;
                if (gm != null)
                    gm.SpawnFloatingWorldText(transform.position + Vector3.up * 2.2f, "PENGUIN WON! TRY AGAIN!", Palette.CharacterPrimary);
            }
        }
        else if (hero != null && Vector3.Distance(hero.transform.position, _originPos) < 4.5f && _waypointIndex > 15)
        {
            // Mario climbed/warped back to start -> reset race!
            ResetCcmElement();
        }
    }

    private void UpdateSnowmanHeadGoal(float dt)
    {
        var vis = visualSubTarget != null ? visualSubTarget : transform;
        var bob = Mathf.Sin(_clock * (_completed ? 6f : 2.5f)) * 0.08f;
        vis.localPosition = new Vector3(0f, bob, 0f);
        var hero = MarioRemakeController.Instance;
        if (hero == null) return;
        var look = hero.transform.position - transform.position;
        look.y = 0f;
        if (look.sqrMagnitude > 0.2f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look.normalized, Vector3.up), dt * 3.5f);
    }

    private void UpdateSnowmanRollingBody(float dt)
    {
        if (waypoints == null || waypoints.Length < 2) return;
        var hero = MarioRemakeController.Instance;
        var gm = GameManager.Instance;
        if (gm != null && gm.currentState != GameState.Playing) return;

        if (_completed) return;

        if (!_active)
        {
            transform.position = _originPos + Vector3.up * (Mathf.Sin(_clock * 2.8f) * 0.04f);
            if (hero == null || !(Vector3.Distance(hero.transform.position, transform.position) <= 5.5f)) return;
            _active = true;
            _waypointIndex = 0;
            if (gm != null)
                gm.SpawnFloatingWorldText(transform.position + Vector3.up * 2.2f, "LEAD SNOWMAN TO HEAD!", Palette.AccentStarGold);
            return;
        }

        // Wait if Mario falls behind far so he can race down alongside the rolling body (classic SM64!)
        var heroFollowingClose = hero == null || Vector3.Distance(hero.transform.position, transform.position) <= 24f;
        if (!heroFollowingClose) return;

        if (_waypointIndex < waypoints.Length)
        {
            var target = waypoints[_waypointIndex];
            var toTarget = target - transform.position;
            var dist = toTarget.magnitude;
            if (dist < 0.85f)
            {
                _waypointIndex++;
            }
            else
            {
                var dir = toTarget / dist;
                var step = moveSpeed * dt;
                transform.position += dir * Mathf.Min(step, dist);
                var spinAxis = Vector3.Cross(Vector3.up, dir);
                if (spinAxis.sqrMagnitude > 0.001f)
                    transform.Rotate(spinAxis.normalized, step * 110f, Space.World);
            }
        }

        if (_waypointIndex >= waypoints.Length || (linkedPartner != null && Vector3.Distance(transform.position, linkedPartner.transform.position) <= 4.2f))
        {
            CompleteSnowmanMerge();
        }
    }

    private void CompleteSnowmanMerge()
    {
        if (_completed) return;
        _completed = true;
        _active = false;
        if (linkedPartner != null)
        {
            transform.position = linkedPartner.transform.position;
            transform.rotation = linkedPartner.transform.rotation;
            linkedPartner.transform.position += Vector3.up * 2.15f;
            linkedPartner._completed = true;
        }

        if (rewardStar == null || rewardStar.IsCollected) return;
        rewardStar.gameObject.SetActive(true);
        if (GameAudio.Instance != null) GameAudio.Instance.PlayMilestoneSting();
        if (VFXPool.Instance != null) VFXPool.Instance.SpawnPickupBurst(rewardStar.transform.position, isStar: true);
        if (GameManager.Instance != null)
            GameManager.Instance.SpawnFloatingWorldText(rewardStar.transform.position + Vector3.up * 1.7f, "SNOWMAN'S GOT HIS HEAD!", Palette.AccentStarGold);
    }

    private void UpdateSpindriftEnemy(float dt)
    {
        var vis = visualSubTarget != null ? visualSubTarget : transform;
        vis.Rotate(0f, 340f * dt, 0f, Space.Self);
        var hover = Mathf.Sin(_clock * 3.8f) * 0.22f;
        transform.position = _originPos + new Vector3(Mathf.Sin(_clock * 1.1f) * 0.9f, hover, Mathf.Cos(_clock * 1.1f) * 0.9f);

        var hero = MarioRemakeController.Instance;
        if (hero == null || _cooldown > 0f) return;

        var dist = Vector3.Distance(hero.transform.position + Vector3.up * 0.6f, transform.position + Vector3.up * 0.5f);
        if (dist > 1.35f) return;

        var yDiff = hero.transform.position.y - transform.position.y;
        var stompOrTop = yDiff >= 0.32f || hero.IsStompingDown || hero.IsGroundPoundingActive;
        if (stompOrTop || hero.IsAttackingOrSliding)
        {
            _cooldown = 0.65f;
            hero.BounceUpFromStomp(4.85f);
            if (GameAudio.Instance != null) GameAudio.Instance.PlayJumpSound(3);
            if (VFXPool.Instance != null) VFXPool.Instance.SpawnPickupBurst(transform.position + Vector3.up * 0.8f, isStar: false);
            if (GameManager.Instance != null)
                GameManager.Instance.SpawnFloatingWorldText(transform.position + Vector3.up * 1.6f, "SPIN-FLOAT BOUNCE!", Palette.AccentStarGold);
        }
        else
        {
            _cooldown = 1.0f;
            hero.TakeDamage(1, transform.position);
        }
    }

    private void UpdateMrBlizzardEnemy(float dt)
    {
        var hero = MarioRemakeController.Instance;
        if (hero == null) return;

        var dist = Vector3.Distance(hero.transform.position, transform.position);
        var toHero = hero.transform.position - transform.position;
        toHero.y = 0f;

        if (dist <= aggroRadius && toHero.sqrMagnitude > 0.25f)
        {
            var lookRot = Quaternion.LookRotation(toHero.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, lookRot, dt * 3.6f);

            // Hopping bob on snow (jumping variant!)
            var hopPhase = Mathf.Repeat(_clock * 1.4f, 1f);
            var hopY = Mathf.Sin(hopPhase * Mathf.PI) * 0.52f;
            transform.position = _originPos + Vector3.up * hopY;

            // Circle-behind defeat or attack defeat
            var behindDot = Vector3.Dot(transform.forward, toHero.normalized);
            if (dist < 2.8f && behindDot < -0.25f) _backBehindTimer += dt;
            else _backBehindTimer = Mathf.Max(0f, _backBehindTimer - dt * 2f);

            if ((dist < 1.95f && hero.IsAttackingOrSliding) || _backBehindTimer >= 0.55f)
            {
                DefeatMrBlizzard();
                return;
            }

            // Throw snowball periodically
            _attackTimer += dt;
            if (!(_attackTimer >= 2.6f) || !(dist > 2.6f)) return;
            _attackTimer = 0f;
            SpawnSnowball(transform.position + Vector3.up * 1.25f + transform.forward * 0.8f, (hero.transform.position + Vector3.up * 0.8f - (transform.position + Vector3.up * 1.25f)).normalized);
        }
        else
        {
            transform.position = _originPos;
        }
    }

    private void DefeatMrBlizzard()
    {
        if (GameAudio.Instance != null) GameAudio.Instance.PlayEnemyDefeatSound();
        if (VFXPool.Instance != null) VFXPool.Instance.SpawnPickupBurst(transform.position + Vector3.up * 1f, isStar: false);
        if (GameManager.Instance != null)
        {
            GameManager.Instance.coinsCollected += 3;
            GameManager.Instance.SpawnFloatingWorldText(transform.position + Vector3.up * 1.8f, "MR. BLIZZARD +3 COINS", Palette.AccentStarGold);
        }
        gameObject.SetActive(false);
    }

    private void SpawnSnowball(Vector3 startPos, Vector3 dir)
    {
        GameObject sb;
        if (snowballPrefab != null)
        {
            sb = Instantiate(snowballPrefab, startPos, Quaternion.identity);
        }
        else
        {
            sb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sb.transform.position = startPos;
            sb.transform.localScale = Vector3.one * 0.46f;
            var sc = sb.GetComponent<SphereCollider>();
            if (sc != null) sc.isTrigger = true;
        }
        sb.name = "BlizzardSnowball";
        if (snowballMaterial != null)
        {
            var mr = sb.GetComponentInChildren<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = snowballMaterial;
        }
        var proj = sb.GetComponent<CcmSnowballProjectile>();
        if (proj == null) proj = sb.AddComponent<CcmSnowballProjectile>();
        proj.velocity = dir * 10.5f + Vector3.up * 2.0f;
    }

    private void UpdateRopewayLift(float dt)
    {
        if (waypoints == null || waypoints.Length < 2) return;

        var target = waypoints[_waypointIndex];
        var delta = target - transform.position;
        var dist = delta.magnitude;
        if (dist < 0.35f)
        {
            if (!_ropewayReverse)
            {
                if (_waypointIndex + 1 < waypoints.Length) _waypointIndex++;
                else { _ropewayReverse = true; _waypointIndex--; }
            }
            else
            {
                if (_waypointIndex - 1 >= 0) _waypointIndex--;
                else { _ropewayReverse = false; _waypointIndex++; }
            }
        }
        else
        {
            var move = (delta / dist) * Mathf.Min(moveSpeed * dt, dist);
            transform.position += move;

            // Carry Mario cleanly while riding the gondola floor
            var hero = MarioRemakeController.Instance;
            if (hero == null) return;
            var diff = hero.transform.position - transform.position;
            if (!(Mathf.Abs(diff.x) < 1.9f) || !(Mathf.Abs(diff.z) < 1.9f) || !(diff.y > -2.0f) ||
                !(diff.y < 1.6f)) return;
            var cc = hero.GetComponent<CharacterController>();
            if (cc != null && cc.enabled) cc.Move(move);
        }
    }

    private void SetupSnowParticleSystem()
    {
        _snowPs = gameObject.AddComponent<ParticleSystem>();
        var main = _snowPs.main;
        main.maxParticles = 140;
        main.startLifetime = 4.2f;
        main.startSpeed = 1.6f;
        main.startSize = 0.14f;
        main.startColor = new Color(0.95f, 0.98f, 1f, 0.85f);
        main.gravityModifier = 0.08f;
        var shape = _snowPs.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(44f, 4f, 44f);
        var pr = GetComponent<ParticleSystemRenderer>();
        if (pr == null) return;
        var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Sprites/Default");
        if (sh == null) return;
        var mat = new Material(sh)
        {
            color = Color.white
        };
        pr.sharedMaterial = mat;
    }

    private void UpdateSnowWeatherFX()
    {
        var hero = MarioRemakeController.Instance;
        if (hero != null)
        {
            transform.position = hero.transform.position + Vector3.up * 12f;
        }
    }
}

