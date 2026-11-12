using System;
using System.Collections.Generic;
using UnityEngine;

namespace Mario64Remake
{
    public enum JollyRogerElementType
    {
        TreasureChestPuzzleMember,
        UnagiTheEel,
        SnapClamShell,
        MetalCapGreenBlock,
        JetStreamGeyser,
        FallingCavernPillar,
        FloatingGalleonShip
    }
}

/// <summary>
/// Interactive mechanics for Jolly Roger Bay (Course 3 Remake - Super Mario 64):
/// -----------------------------------------------------------------------------
/// 1. TreasureChestPuzzleMember (Used in Act 1 "Plunder in the Sunken Ship" & Act 3 "Treasure of the Ocean Cave"):
///    - 4 chests arranged with puzzleGroupId ("SunkenShip" = 1, "OceanCave" = 2) and chestOrderIndex (1..4).
///    - Sunken Ship order: 1. Back/North (Isolated) -> 2. West (Left) -> 3. East (Right) -> 4. South (Center).
///    - Ocean Cave order:  1. North (Back) -> 2. West (Left) -> 3. East (Right) -> 4. South (Front Center).
///    - Touching/Attacking the next correct chest opens its lid with an air bubble & chime!
///    - Touching the wrong order chest triggers an electric shock knockback and resets the group's chests!
///    - Opening all 4 in order spawns the group's rewardStar!
/// 2. UnagiTheEel (Act 2 "Can the Eel Come Out to Play?"):
///    - Giant maroon-and-yellow moray eel lurking in the deep rocky niche; when Mario approaches in Act 2,
///      Unagi swims out along a multi-segment sinusoidal path with Star 2 attached to its glowing tail!
/// 3. SnapClamShell (Act 4 "Red Coins on the Ship Afloat"):
///    - 4 Giant Pink/Pearl Clam Shells on the ocean floor that rhythmically open for 3.5s and snap shut for 2.0s,
///      revealing the 4 underwater Red Coins inside!
/// 4. SkyCannonLauncher (via WhompFortressElement -> Act 5 "Blast to the Stone Pillar"):
///    - Fires Mario from the West Beach Dock to the needle-like Stone Pillar Spire so he can hop to the High Cliff Ledge for Star 5!
/// 5. MetalCapGreenBlock & JetStreamGeyser (Act 6 "Through the Jet Stream"):
///    - Hitting the Green Metal Cap Block coats Mario in heavy liquid-chrome Metal for 22 seconds!
///    - While Metal Mario is active, the central Trench Jet Stream cannot push Mario upward, letting him walk straight into Star 6!
/// 6. FallingCavernPillar:
///    - Tall stone stalactite pillars inside the Hidden Ocean Cavern that shake and drop when Mario approaches!
/// 7. FloatingGalleonShip:
///    - Gentle ocean wave bobbing & roll for the Raised Pirate Galleon in Acts 2..6!
/// </summary>
public class JollyRogerBayController : MonoBehaviour
{
    public Mario64Remake.JollyRogerElementType elementType = Mario64Remake.JollyRogerElementType.TreasureChestPuzzleMember;

    [Header("Treasure Chest Puzzle Settings")]
    public int puzzleGroupId = 1; // 1 = Sunken Ship (Act 1), 2 = Ocean Cave (Act 3)
    public int chestOrderIndex = 1; // 1, 2, 3, or 4
    public int totalChestsInGroup = 4;
    public Transform lidPivot;
    public bool isChestOpen;

    [Header("Shared References & Motion")]
    public StarCoinCollectible rewardStar;
    public Transform visualSubTarget;
    public Transform[] eelBodySegments;
    public Vector3 moveOffset = new(0f, -4.5f, 0f);
    public float cycleSpeed = 1.0f;
    public float phaseOffset;

    // Static per-group progress tracker (Group 1 = Sunken Ship, Group 2 = Ocean Cave)
    private static readonly Dictionary<int, int> GroupProgress = new();

    // Metal Cap Global State (for Act 6 "Through the Jet Stream")
    private static float MetalCapRemainingTimer { get; set; }
    public static bool IsMetalMarioActive => MetalCapRemainingTimer > 0f;

    private Vector3 _originPos;
    private Quaternion _originRot;
    private Quaternion _lidClosedRot;
    private float _clock;
    private float _cooldownTimer;
    private bool _completed;
    private bool _eelSwimmingOut;
    private int _pillarState; // 0 = top idle, 1 = warning shake, 2 = falling, 3 = fallen rest, 4 = rising reset
    private float _pillarTimer;

    private void Awake()
    {
        _originPos = transform.position;
        _originRot = transform.rotation;
        if (lidPivot != null) _lidClosedRot = lidPivot.localRotation;
    }

    public static void ResetAllChestGroups()
    {
        GroupProgress.Clear();
        MetalCapRemainingTimer = 0f;
    }

    public void ResetBayElement()
    {
        if (_originPos.sqrMagnitude > 0.0001f)
        {
            transform.SetPositionAndRotation(_originPos, _originRot);
        }
        if (lidPivot != null)
        {
            lidPivot.localRotation = _lidClosedRot;
        }
        isChestOpen = false;
        _completed = false;
        _eelSwimmingOut = false;
        _cooldownTimer = 0f;
        _pillarState = 0;
        _pillarTimer = 0f;

        if (rewardStar != null && (elementType == Mario64Remake.JollyRogerElementType.TreasureChestPuzzleMember ||
                                   elementType == Mario64Remake.JollyRogerElementType.UnagiTheEel ||
                                   elementType == Mario64Remake.JollyRogerElementType.JetStreamGeyser))
        {
            rewardStar.gameObject.SetActive(false);
        }

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

        if (_cooldownTimer > 0f) _cooldownTimer -= dt;
        _clock += dt * cycleSpeed;

        switch (elementType)
        {
            case Mario64Remake.JollyRogerElementType.TreasureChestPuzzleMember:
                UpdateTreasureChest(dt);
                break;
            case Mario64Remake.JollyRogerElementType.UnagiTheEel:
                UpdateUnagiEel(dt);
                break;
            case Mario64Remake.JollyRogerElementType.SnapClamShell:
                UpdateSnapClam();
                break;
            case Mario64Remake.JollyRogerElementType.MetalCapGreenBlock:
                UpdateMetalCapBlock(dt);
                break;
            case Mario64Remake.JollyRogerElementType.JetStreamGeyser:
                UpdateJetStreamGeyser(dt);
                break;
            case Mario64Remake.JollyRogerElementType.FallingCavernPillar:
                UpdateFallingPillar(dt);
                break;
            case Mario64Remake.JollyRogerElementType.FloatingGalleonShip:
                UpdateFloatingGalleon();
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void UpdateTreasureChest(float dt)
    {
        // Smoothly animate lid rotation (open = -75 deg X, closed = 0 deg)
        if (lidPivot != null)
        {
            var targetRot = isChestOpen ? _lidClosedRot * Quaternion.Euler(-78f, 0f, 0f) : _lidClosedRot;
            lidPivot.localRotation = Quaternion.Slerp(lidPivot.localRotation, targetRot, 10f * dt);
        }

        if (isChestOpen || _completed || _cooldownTimer > 0f) return;

        var hero = MarioRemakeController.Instance;
        if (hero == null) return;

        var dist = Vector3.Distance(hero.transform.position, transform.position);
        if (dist < 2.25f)
        {
            InteractWithChest(hero);
        }
    }

    public void InteractWithChest(MarioRemakeController hero)
    {
        if (isChestOpen || _completed || _cooldownTimer > 0f) return;
        _cooldownTimer = 0.75f;

        var currentStep = GroupProgress.GetValueOrDefault(puzzleGroupId, 0);
        var expectedIndex = currentStep + 1;

        if (chestOrderIndex == expectedIndex)
        {
            // Correct chest in sequence! Release air bubble that refills Mario's Oxygen / Power Meter!
            isChestOpen = true;
            GroupProgress[puzzleGroupId] = expectedIndex;
            if (hero != null) hero.RefillOxygen(10f);

            if (GameAudio.Instance != null) GameAudio.Instance.PlayPickupBlip(expectedIndex * 0.12f);
            if (VFXPool.Instance != null) VFXPool.Instance.SpawnPickupBurst(transform.position + Vector3.up * 0.85f, isStar: false);

            if (expectedIndex >= totalChestsInGroup)
            {
                // All 4 chests opened in the right order! Reveal the Power Star!
                _completed = true;
                RevealGroupRewardStar();
            }
        }
        else
        {
            // Wrong chest order! Shock Mario (costs 1 Power Wedge / 4s oxygen) & reset all chests in this puzzle group!
            if (hero != null) hero.RefillOxygen(-4.5f);
            if (GameAudio.Instance != null) GameAudio.Instance.PlayGroundPoundSound();
            if (VFXPool.Instance != null) VFXPool.Instance.SpawnFootstepPuff(transform.position + Vector3.up * 0.6f);
            if (hero != null && hero.impulseSource != null) hero.impulseSource.GenerateImpulse(0.35f);

            GroupProgress[puzzleGroupId] = 0;
            foreach (var chest in FindObjectsByType<JollyRogerBayController>(FindObjectsInactive.Include))
            {
                if (chest == null ||
                    chest.elementType != Mario64Remake.JollyRogerElementType.TreasureChestPuzzleMember ||
                    chest.puzzleGroupId != puzzleGroupId) continue;
                chest.isChestOpen = false;
                chest._cooldownTimer = 0.85f;
            }
        }
    }

    private void RevealGroupRewardStar()
    {
        var starToReveal = rewardStar;
        if (starToReveal == null)
        {
            foreach (var chest in FindObjectsByType<JollyRogerBayController>(FindObjectsInactive.Include))
            {
                if (chest == null ||
                    chest.elementType != Mario64Remake.JollyRogerElementType.TreasureChestPuzzleMember ||
                    chest.puzzleGroupId != puzzleGroupId ||
                    chest.rewardStar == null) continue;
                starToReveal = chest.rewardStar;
                break;
            }
        }

        if (starToReveal == null) return;
        starToReveal.gameObject.SetActive(true);
        if (GameAudio.Instance != null) GameAudio.Instance.PlayMilestoneSting();
        if (VFXPool.Instance != null) VFXPool.Instance.SpawnPickupBurst(starToReveal.transform.position, isStar: true);
    }

    private void UpdateUnagiEel(float dt)
    {
        var hero = MarioRemakeController.Instance;
        var isAct2 = GameManager.Instance == null || GameManager.Instance.currentActNumber == 2;

        if (!_eelSwimmingOut)
        {
            // Peek head out of deep rock niche and sway menacingly
            var sway = Mathf.Sin(_clock * 2.2f) * 18f;
            transform.position = _originPos + new Vector3(Mathf.Sin(_clock * 1.5f) * 0.45f, Mathf.Cos(_clock * 2.0f) * 0.25f, 0f);
            transform.rotation = _originRot * Quaternion.Euler(0f, sway, 0f);
            AnimateEelSegments();

            // When Mario swims near Unagi's niche (or in Act 2), Unagi emerges to swim around the bay!
            if (hero == null || !(Vector3.Distance(hero.transform.position, transform.position) < 14f)) return;
            _eelSwimmingOut = true;
            if (rewardStar != null && isAct2 && !rewardStar.IsCollected)
            {
                rewardStar.gameObject.SetActive(true);
            }
            return;
        }

        // Swim in a grand figure-8 / elliptical circuit around the Deep Bay Trench!
        var angle = _clock * 0.55f;
        const float radiusX = 13.5f;
        const float radiusZ = 10.5f;
        var center = _originPos + new Vector3(0f, 1.5f, 8.5f);

        var nextPos = center + new Vector3(Mathf.Sin(angle) * radiusX, Mathf.Sin(angle * 2f) * 1.6f, Mathf.Cos(angle) * radiusZ);
        var moveDir = nextPos - transform.position;
        if (moveDir.sqrMagnitude > 0.0001f)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(moveDir.normalized, Vector3.up), 6f * dt);
        }
        transform.position = nextPos;

        AnimateEelSegments();

        // Keep Star 2 attached to the tip of Unagi's tail until collected!
        if (rewardStar == null || rewardStar.IsCollected || !isAct2) return;
        if (!rewardStar.gameObject.activeSelf) rewardStar.gameObject.SetActive(true);
        var tailTransform = eelBodySegments is { Length: > 0 }
            ? eelBodySegments[^1]
            : transform;
        if (tailTransform != null)
        {
            rewardStar.transform.position = tailTransform.position - tailTransform.forward * 1.2f + Vector3.up * 0.35f;
        }
    }

    private void AnimateEelSegments()
    {
        if (eelBodySegments == null) return;
        for (var i = 0; i < eelBodySegments.Length; i++)
        {
            var seg = eelBodySegments[i];
            if (seg == null) continue;
            var wave = Mathf.Sin(_clock * 5.2f - i * 0.85f) * 18f;
            seg.localRotation = Quaternion.Euler(0f, wave, 0f);
        }
    }

    private void UpdateSnapClam()
    {
        // Rhythmic open/close cycle on the seabed: open wide for 3.6s, snap shut for 1.8s
        var cycle = Mathf.Repeat(_clock + phaseOffset, 5.4f);
        var openFactor = cycle switch
        {
            < 0.5f => Ease.OutCubic(cycle / 0.5f),
            < 3.6f => 1f,
            < 4.0f => 1f - Ease.OutBounceCustom((cycle - 3.6f) / 0.4f),
            _ => 0f
        };

        if (lidPivot != null)
        {
            lidPivot.localRotation = _lidClosedRot * Quaternion.Euler(-68f * openFactor, 0f, 0f);
        }
    }

    private void UpdateMetalCapBlock(float dt)
    {
        if (MetalCapRemainingTimer > 0f)
        {
            MetalCapRemainingTimer -= dt;
        }

        transform.position = _originPos + Vector3.up * (Mathf.Sin(_clock * 3.2f) * 0.16f);
        transform.Rotate(0f, 48f * dt, 0f, Space.World);

        var hero = MarioRemakeController.Instance;
        if (hero == null || _cooldownTimer > 0f) return;

        if (!(Vector3.Distance(hero.transform.position, transform.position) < 2.3f)) return;
        _cooldownTimer = 2.0f;
        MetalCapRemainingTimer = 24f; // 24 seconds of heavy Metal Mario!
        if (GameAudio.Instance != null) GameAudio.Instance.PlayMilestoneSting();
        if (VFXPool.Instance != null) VFXPool.Instance.SpawnPickupBurst(transform.position, isStar: true);
        if (hero.popProxy != null) hero.popProxy.TriggerScalePop(1.3f);
    }

    private void UpdateJetStreamGeyser(float dt)
    {
        if (MetalCapRemainingTimer > 0f)
        {
            MetalCapRemainingTimer -= dt;
        }

        // Rotate rising bubble column visual
        if (visualSubTarget != null)
        {
            visualSubTarget.Rotate(0f, 140f * dt, 0f, Space.Self);
            var pulse = 1f + Mathf.Sin(_clock * 6f) * 0.08f;
            visualSubTarget.localScale = new Vector3(pulse, 1f, pulse);
        }

        var isAct6 = GameManager.Instance == null || GameManager.Instance.currentActNumber == 6;
        if (rewardStar != null && isAct6 && !rewardStar.IsCollected && !rewardStar.gameObject.activeSelf)
        {
            rewardStar.gameObject.SetActive(true);
        }

        var hero = MarioRemakeController.Instance;
        if (hero == null) return;

        var planarDist = Vector2.Distance(
            new Vector2(hero.transform.position.x, hero.transform.position.z),
            new Vector2(transform.position.x, transform.position.z));

        // If Mario enters the Jet Stream column without the Metal Cap, push him upward so he must grab the Green Metal Cap Block first!
        if (!(planarDist < 2.2f) || !(hero.transform.position.y >= transform.position.y - 0.5f) ||
            !(hero.transform.position.y < transform.position.y + 8.5f)) return;
        if (IsMetalMarioActive) return;
        var cc = hero.GetComponent<CharacterController>();
        if (cc != null && cc.enabled)
        {
            cc.Move(Vector3.up * (11.5f * dt));
        }
    }

    private void UpdateFallingPillar(float dt)
    {
        var hero = MarioRemakeController.Instance;
        var topPos = _originPos;
        var bottomPos = _originPos + moveOffset;

        switch (_pillarState)
        {
            case 0: // Waiting at cavern ceiling
            {
                transform.position = topPos;
                if (hero != null && Vector3.Distance(hero.transform.position, bottomPos) < 6.5f)
                {
                    _pillarState = 1;
                    _pillarTimer = 0f;
                }
                break;
            }
            case 1: // Warning rumble
            {
                _pillarTimer += dt;
                transform.position = topPos + new Vector3(Mathf.Sin(_pillarTimer * 60f) * 0.08f, 0f, 0f);
                if (_pillarTimer >= 0.45f)
                {
                    _pillarState = 2;
                    _pillarTimer = 0f;
                }
                break;
            }
            case 2: // Crashing down!
            {
                _pillarTimer += dt;
                var t = Mathf.Clamp01(_pillarTimer / 0.28f);
                transform.position = Vector3.Lerp(topPos, bottomPos, t * t);
                if (t >= 1f)
                {
                    _pillarState = 3;
                    _pillarTimer = 0f;
                    if (GameAudio.Instance != null) GameAudio.Instance.PlayGroundPoundSound();
                    if (VFXPool.Instance != null) VFXPool.Instance.SpawnFootstepPuff(bottomPos);
                }
                break;
            }
            case 3: // Rest on cavern floor, then reset
            {
                _pillarTimer += dt;
                if (_pillarTimer >= 2.2f)
                {
                    _pillarState = 4;
                    _pillarTimer = 0f;
                }
                break;
            }
            case 4: // Smoothly rise back up
            {
                _pillarTimer += dt;
                var t = Mathf.Clamp01(_pillarTimer / 1.4f);
                transform.position = Vector3.Lerp(bottomPos, topPos, Ease.OutCubic(t));
                if (t >= 1f)
                {
                    _pillarState = 0;
                    _pillarTimer = 0f;
                }
                break;
            }
        }
    }

    private void UpdateFloatingGalleon()
    {
        var bobY = Mathf.Sin(_clock * 1.6f) * 0.32f;
        var pitch = Mathf.Sin(_clock * 1.2f) * 2.4f;
        var roll = Mathf.Cos(_clock * 1.5f) * 3.1f;
        transform.position = _originPos + new Vector3(0f, bobY, 0f);
        transform.rotation = _originRot * Quaternion.Euler(pitch, 0f, roll);
    }
}
