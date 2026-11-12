using System.Collections;
using UnityEngine;

/// <summary>
/// Whomp King Boss (Super Mario 64 - Course 2: Whomp's Fortress):
/// -------------------------------------------------------------
/// Official SM64 Boss Mechanics & Lore:
/// 1. Pre-Battle Monologue:
///    "IT MAKES ME SO MAD! We build your houses, your castles. We pave your roads, and still you walk all over us!
///     Do you ever say thank you? No! Well, you're not going to wipe your feet on me! I think I'll crush you just for fun!
///     Just try to pound me, wimp! Ha!"
/// 2. Tracking & Stomping:
///    - Whomp King turns toward Mario and stomps across the summit arena with heavy ground-shaking thuds.
/// 3. Body Slam Attack ("URUAGH!"):
///    - Rears back and slams face-down onto the floor with immense force.
///    - Massive impact shockwave, dust cloud & camera shake. Mario takes damage if caught underneath!
/// 4. Vulnerable Window on Ground:
///    - Lies flat for ~3.2 seconds. Punches/kicks bounce off his stone body.
///    - Mario must jump onto his back and GROUND POUND the X-Bandage Plaster!
/// 5. Defeat & Monologue:
///    - Takes 3 Ground Pounds.
///    - Upon defeat: "No! Crushed again! I'm just a stepping stone, after all... Here, you win. Take this with you!"
///    - Crumbles into stone fragments and reveals "1. CHIP OFF WHOMP'S BLOCK" Power Star!
/// </summary>
public class WhompBossController : MonoBehaviour
{
    public bool isKingBoss = true;
    public int maxHealth = 3;
    public int currentHealth = 3;
    public float pursueSpeed = 2.4f;
    public float turnSpeed = 3.5f;
    public Transform leftLeg;
    public Transform rightLeg;
    public Transform leftArm;
    public Transform rightArm;
    public StarCoinCollectible rewardStar;

    private Vector3 _origin;
    private Quaternion _originRot;
    private float _clock;
    private float _stateTimer;
    private int _state; // 0 = Pursuing Mario, 1 = Slam Windup & Leap, 2 = Face-Down Pinned (Vulnerable!), 3 = Standing Back Up, 4 = Defeated Monologue & Explode
    private float _invulnTimer;
    private bool _introDialogueShown;
    private float _footstepTimer;
    private MeshRenderer[] _allRenderers;

    private void Awake()
    {
        _origin = transform.position;
        _originRot = transform.rotation;
        currentHealth = maxHealth;
        _allRenderers = GetComponentsInChildren<MeshRenderer>(true);
    }

    public void ResetBoss()
    {
        if (_origin.sqrMagnitude < 0.0001f) _origin = transform.position;
        transform.SetPositionAndRotation(_origin, _originRot);
        currentHealth = maxHealth;
        _state = 0;
        _stateTimer = 0f;
        _introDialogueShown = false;
        _invulnTimer = 0f;

        if (rewardStar != null) rewardStar.gameObject.SetActive(false);
        gameObject.SetActive(true);
    }

    private void Update()
    {
        if (_state == 4) return;
        if (_origin.sqrMagnitude < 0.0001f && transform.position.sqrMagnitude > 0.0001f)
        {
            _origin = transform.position;
            _originRot = transform.rotation;
        }

        var dt = Time.deltaTime > 0.0001f ? Time.deltaTime : (Time.timeScale > 0f ? 0.016f * Time.timeScale : 0f);
        if (dt <= 0.00001f) return;

        if (_invulnTimer > 0f)
        {
            _invulnTimer -= dt;
            // Flashing damaged state
            var flash = Mathf.Sin(Time.time * 35f) > 0f;
            foreach (var r in _allRenderers)
            {
                if (r != null) r.enabled = flash;
            }
        }
        else
        {
            foreach (var r in _allRenderers)
            {
                if (r != null && !r.enabled) r.enabled = true;
            }
        }

        _stateTimer += dt;
        var hero = MarioRemakeController.Instance;

        // Intro Monologue Banner when Mario approaches the Summit Arena in Act 1
        if (isKingBoss && !_introDialogueShown && hero != null)
        {
            var distToHero = Vector3.Distance(hero.transform.position, transform.position);
            if (distToHero < 14.5f)
            {
                _introDialogueShown = true;
                if (GameManager.Instance != null)
                {
                    GameManager.Instance.TriggerHitStop(0.12f);
                }
                if (GameAudio.Instance != null) GameAudio.Instance.PlayMilestoneSting();
            }
        }

        switch (_state)
        {
            case 0: // Pursuing & Stomping toward Mario
                UpdatePursuitState(hero, dt);
                break;
            case 1: // Rearing back and Body Slamming Face-Down
                UpdateSlamWindupState(hero, dt);
                break;
            case 2: // Face-down on stone! Vulnerable to Ground Pound on the X-Bandage!
                UpdateVulnerableState(hero, dt);
                break;
            case 3: // Recovering and standing back up
                UpdateStandUpState(dt);
                break;
        }
    }

    private void UpdatePursuitState(MarioRemakeController hero, float dt)
    {
        if (hero == null) return;

        var toHero = hero.transform.position - transform.position;
        toHero.y = 0f;
        var dist = toHero.magnitude;

        // Turn to face Mario
        if (toHero.sqrMagnitude > 0.01f)
        {
            var targetRot = Quaternion.LookRotation(toHero.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, turnSpeed * dt);
        }

        // Stomp forward
        var speedMod = 1f + (maxHealth - currentHealth) * 0.35f;
        var currentSpeed = pursueSpeed * speedMod;
        transform.position += transform.forward * (currentSpeed * dt);

        // Footstep clomping & screen shake
        _footstepTimer += dt * (3.4f * speedMod);
        var stepCycle = _footstepTimer * Mathf.PI * 2f;
        var swing = Mathf.Sin(stepCycle) * 32f;
        var roll = Mathf.Sin(stepCycle) * 3.5f;

        transform.rotation *= Quaternion.Euler(0f, 0f, roll);

        if (leftLeg != null) leftLeg.localRotation = Quaternion.Euler(swing, 0f, 0f);
        if (rightLeg != null) rightLeg.localRotation = Quaternion.Euler(-swing, 0f, 0f);
        if (leftArm != null) leftArm.localRotation = Quaternion.Euler(-swing * 0.7f, 0f, 18f);
        if (rightArm != null) rightArm.localRotation = Quaternion.Euler(swing * 0.7f, 0f, -18f);

        // Footstep impact sound & shake on each step
        if (Mathf.Abs(Mathf.Sin(stepCycle)) > 0.95f)
        {
            if (VFXPool.Instance != null) VFXPool.Instance.SpawnFootstepPuff(transform.position);
            if (hero.impulseSource != null && dist < 12f)
            {
                hero.impulseSource.GenerateImpulse(0.08f);
            }
        }

        // If close enough (dist < 5.8m) or after chasing for a bit, slam face-down!
        if ((dist < 5.8f && _stateTimer > 1.2f) || _stateTimer > 4.5f)
        {
            _state = 1;
            _stateTimer = 0f;
            if (GameAudio.Instance != null) GameAudio.Instance.PlayJumpSound(2);
        }
    }

    private void UpdateSlamWindupState(MarioRemakeController hero, float dt)
    {
        // 0.0s..0.35s: Rear back slightly with angry grunt
        // 0.35s..0.70s: Slam forward flat 90 degrees onto the ground!
        var t = _stateTimer / 0.68f;

        if (t < 0.45f)
        {
            var rearT = t / 0.45f;
            var tiltBack = Mathf.Lerp(0f, -18f, rearT);
            transform.rotation = Quaternion.Euler(tiltBack, transform.eulerAngles.y, 0f);
            transform.position += Vector3.up * (2.8f * dt);
        }
        else
        {
            var slamT = Mathf.Clamp01((t - 0.45f) / 0.55f);
            var pitch = Mathf.Lerp(-18f, 90f, slamT * slamT);
            transform.rotation = Quaternion.Euler(pitch, transform.eulerAngles.y, 0f);

            if (slamT >= 1f)
            {
                // CRUNCH! Slammed face-down onto the stone arena!
                _state = 2;
                _stateTimer = 0f;
                transform.rotation = Quaternion.Euler(90f, transform.eulerAngles.y, 0f);

                if (GameAudio.Instance != null) GameAudio.Instance.PlayGroundPoundSound();
                if (VFXPool.Instance != null)
                {
                    VFXPool.Instance.SpawnFootstepPuff(transform.position + transform.up * 2f);
                    VFXPool.Instance.SpawnPickupBurst(transform.position, isStar: false);
                }

                if (hero != null && hero.impulseSource != null)
                {
                    hero.impulseSource.GenerateImpulse(0.48f); // Heavy impact camera shake!
                }

                // Check if Mario was directly underneath or in front of the slamming slab (takes damage/knockback!)
                if (hero != null)
                {
                    var slamHitCenter = transform.position + transform.up * (isKingBoss ? 1.8f : 1.0f);
                    var distToHero = Vector3.Distance(hero.transform.position, slamHitCenter);
                    var crushRadius = isKingBoss ? 3.2f : 1.9f;
                    if (distToHero < crushRadius && hero.transform.position.y < transform.position.y + 1.2f)
                    {
                        var knockDir = (hero.transform.position - transform.position).normalized + Vector3.up * 0.65f;
                        hero.ResetToSpawn();
                    }
                }
            }
        }
    }

    private void UpdateVulnerableState(MarioRemakeController hero, float dt)
    {
        // Check if Mario jumps on his back and executes a Ground Pound!
        CheckMarioGroundPoundHit();

        // After lying face-down for 3.2 seconds, Whomp King pushes himself back up!
        if (_stateTimer >= 3.2f)
        {
            _state = 3;
            _stateTimer = 0f;
        }
    }

    private void UpdateStandUpState(float dt)
    {
        var t = Mathf.Clamp01(_stateTimer / 0.65f);
        var pitch = Mathf.Lerp(90f, 0f, Ease.OutCubic(t));
        transform.rotation = Quaternion.Euler(pitch, transform.eulerAngles.y, 0f);

        if (t >= 1f)
        {
            _state = 0;
            _stateTimer = 0f;
        }
    }

    private void CheckMarioGroundPoundHit()
    {
        if (_invulnTimer > 0f || _state != 2) return;
        var hero = MarioRemakeController.Instance;
        if (hero == null) return;

        // Bandage target position on Whomp's back (when lying face-down, his back faces +Y / up!)
        var backOffset = isKingBoss ? 1.8f : 1.0f;
        var upOffset = isKingBoss ? 1.35f : 0.85f;
        var bandageWorldPos = transform.position + Vector3.up * upOffset + transform.up * backOffset;
        var distToBandage = Vector3.Distance(hero.transform.position, bandageWorldPos);

        // Mario must Ground Pound on or directly above his back bandage!
        var hitRadius = isKingBoss ? 3.8f : 2.2f;
        if (distToBandage < hitRadius && (hero.IsGroundPoundingActive || hero.transform.position.y > transform.position.y + 0.35f))
        {
            TakeGroundPoundDamage();
        }
    }

    public void ForceGroundPoundHitFromMario()
    {
        if (_state == 2)
        {
            TakeGroundPoundDamage();
        }
        else
        {
            // Mario hits Whomp when upright - bounce off!
            if (GameAudio.Instance != null) GameAudio.Instance.PlayJumpSound(1);
        }
    }

    private void TakeGroundPoundDamage()
    {
        if (_invulnTimer > 0f || _state == 4) return;
        _invulnTimer = 0.95f;
        currentHealth--;

        if (VFXPool.Instance != null)
        {
            VFXPool.Instance.SpawnPickupBurst(transform.position + Vector3.up * (isKingBoss ? 1.5f : 0.9f), isStar: isKingBoss);
        }
        if (GameAudio.Instance != null) GameAudio.Instance.PlayGroundPoundSound();

        if (currentHealth <= 0)
        {
            _state = 4;
            StartCoroutine(DefeatAndCrumbleRoutine());
        }
        else
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.TriggerHitStop(0.09f);
            }
            // Enraged recovery! Stand up quickly
            _stateTimer = 0f;
            _state = 3;
        }
    }

    private IEnumerator DefeatAndCrumbleRoutine()
    {
        if (GameManager.Instance != null) GameManager.Instance.TriggerHitStop(isKingBoss ? 0.15f : 0.08f);
        if (GameAudio.Instance != null) GameAudio.Instance.PlayMilestoneSting();

        // Tremble violently before crumbling
        var startPos = transform.position;
        var shakes = isKingBoss ? 28 : 14;
        for (var i = 0; i < shakes; i++)
        {
            transform.position = startPos + Random.insideUnitSphere * (isKingBoss ? 0.22f : 0.12f);
            if (VFXPool.Instance != null && i % 4 == 0)
            {
                VFXPool.Instance.SpawnFootstepPuff(transform.position + Random.insideUnitSphere * (isKingBoss ? 1.5f : 0.8f));
            }
            yield return new WaitForSeconds(0.035f);
        }

        // Explode into stone dust & debris!
        if (VFXPool.Instance != null)
        {
            var burstCount = isKingBoss ? 4 : 2;
            for (var k = 0; k < burstCount; k++)
            {
                VFXPool.Instance.SpawnPickupBurst(transform.position + Random.insideUnitSphere * (isKingBoss ? 1.5f : 0.8f), isStar: isKingBoss);
            }
        }

        // Spawn "1. CHIP OFF WHOMP'S BLOCK" Power Star for King Boss, or 5 Yellow Coins for normal Whomp!
        if (rewardStar != null)
        {
            rewardStar.transform.position = transform.position + Vector3.up * 2.2f;
            rewardStar.gameObject.SetActive(true);
        }
        else if (!isKingBoss)
        {
            // Fountain of 5 SM64 Yellow Coins!
            for (var c = 0; c < 5; c++)
            {
                var ang = c * (Mathf.PI * 2f / 5f);
                var coinPos = transform.position + new Vector3(Mathf.Cos(ang) * 1.3f, 1.0f, Mathf.Sin(ang) * 1.3f);
                var coin = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                coin.name = $"WhompDropCoin_{c + 1}";
                coin.transform.position = coinPos;
                coin.transform.localScale = new Vector3(0.65f, 0.07f, 0.65f);
                coin.transform.rotation = Quaternion.Euler(0f, 0f, 90f);

                var matGold = Resources.Load<Material>("M_JRB_StarGold");
                if (matGold != null) coin.GetComponent<MeshRenderer>().sharedMaterial = matGold;

                var collect = coin.AddComponent<StarCoinCollectible>();
                collect.kind = CollectibleKind.YellowCoin;
                collect.spinSpeed = 160f;
            }
        }

        gameObject.SetActive(false);
    }
}
