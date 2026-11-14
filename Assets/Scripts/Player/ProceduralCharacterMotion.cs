using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Super Mario Odyssey-Polish Character Animator & Secondary Motion Driver.
/// - Primary motion drives the rigged Blender SM64_MarioHero Animator (23 skeletal clips)
///   with snappy crossfade transitions and locomotion speed synchronization.
/// - Layering: responsive landing squash, jump stretch, sharp-turn banking, and somersault juice
///   on visualScaleRoot (never fighting skeletal bone transforms!).
/// - Gracefully falls back to hinge transforms if characterAnimator is absent.
/// </summary>
public class ProceduralCharacterMotion : MonoBehaviour
{
    [Header("Rigged Blender Animator & Secondary Scale Root")]
    public Animator characterAnimator;
    public Transform visualScaleRoot;

    [Header("Legacy Fallback Limb Transforms")]
    public Transform bodyPivot;
    public Transform headPivot;
    public Transform capVisor;
    public Transform leftArm;
    public Transform rightArm;
    public Transform leftLeg;
    public Transform rightLeg;
    public Transform backpack;

    [FormerlySerializedAs("NormalizedSpeed")]
    [Header("Locomotion State Inputs (Driven by MarioRemakeController)")]
    [Range(0f, 1.5f)] public float normalizedSpeed;
    [FormerlySerializedAs("PlanarVelocity")] public Vector3 planarVelocity;
    [FormerlySerializedAs("IsGrounded")] public bool isGrounded = true;
    [FormerlySerializedAs("JumpStage")] public int jumpStage;
    [FormerlySerializedAs("IsDiving")] public bool isDiving;
    [FormerlySerializedAs("IsCrouching")] public bool isCrouching;
    [FormerlySerializedAs("IsSliding")] public bool isSliding;
    [FormerlySerializedAs("IsWallSliding")] public bool isWallSliding;
    [FormerlySerializedAs("IsGroundPounding")] public bool isGroundPounding;
    [FormerlySerializedAs("GroundPoundStallNormalized")] public float groundPoundStallNormalized;
    [FormerlySerializedAs("IsBackflipping")] public bool isBackflipping;
    [FormerlySerializedAs("IsSideFlipping")] public bool isSideFlipping;
    [FormerlySerializedAs("IsLongJumping")] public bool isLongJumping;
    [FormerlySerializedAs("AttackStage")] public int attackStage; // 0 = none, 1 = PunchL, 2 = PunchR, 3 = Kick, 4 = AirKick, 5 = SweepKick, 6 = SlideKick, 7 = SpinAttack, 8 = CarryThrow
    [FormerlySerializedAs("AttackProgress")] public float attackProgress;

    [FormerlySerializedAs("IsSwimming")] [Header("Extended Odyssey / SM64 Feel Flags")]
    public bool isSwimming;
    [FormerlySerializedAs("IsLedgeHanging")] public bool isLedgeHanging;
    [FormerlySerializedAs("IsSpinningBowser")] public bool isSpinningBowser;
    [FormerlySerializedAs("IsSkidding")] public bool isSkidding;
    [FormerlySerializedAs("IsRolling")] public bool isRolling;
    [FormerlySerializedAs("IsSpinAttacking")] public bool isSpinAttacking;
    [FormerlySerializedAs("IsSlopeSliding")] public bool isSlopeSliding;
    [FormerlySerializedAs("IsClimbingPole")] public bool isClimbingPole;
    [FormerlySerializedAs("IsPoleHandstand")] public bool isPoleHandstand;
    [FormerlySerializedAs("PoleClimbSpeed")] public float poleClimbSpeed;
    [FormerlySerializedAs("IsCarryingObject")] public bool isCarryingObject;
    [FormerlySerializedAs("TurnBankRate")] public float turnBankRate;
    public bool isWingGliding;

    private string _currentAnimState = "";
    private Vector3 _scaleVelocity = Vector3.one;
    private float _squashAmount;
    private float _stretchAmount;
    private float _bankAngleDeg;
    private float _forwardLeanDeg;

    private void Awake()
    {
        if (characterAnimator == null) characterAnimator = GetComponentInChildren<Animator>(true);
        if (visualScaleRoot == null)
        {
            visualScaleRoot = characterAnimator != null ? characterAnimator.transform : transform;
        }
    }

    public void TriggerJumpStretch(int stage = 1)
    {
        _stretchAmount = stage >= 3 ? 0.24f : (stage == 2 ? 0.19f : 0.15f);
        _squashAmount = 0f;
    }

    public void TriggerLandSquash(bool heavy)
    {
        TriggerLandSquash(heavy ? 1.45f : 1.0f);
    }

    public void TriggerLandSquash(float intensity = 1f)
    {
        _squashAmount = Mathf.Clamp(0.22f * intensity, 0.12f, 0.30f);
        _stretchAmount = 0f;
    }

    private void LateUpdate()
    {
        var dt = Time.deltaTime > 0.0001f ? Time.deltaTime : 0.016f;

        if (characterAnimator == null)
        {
            characterAnimator = GetComponentInChildren<Animator>(true);
            if (characterAnimator != null && visualScaleRoot == null) visualScaleRoot = characterAnimator.transform;
        }

        UpdateAnimatorState();
        UpdateSecondaryJuice(dt);
        if (characterAnimator == null)
        {
            UpdateLegacyHingeFallback(dt);
        }
    }

    private void UpdateAnimatorState()
    {
        if (characterAnimator == null || characterAnimator.runtimeAnimatorController == null) return;

        string desiredState;
        float desiredSpeed;
        float blendDuration;

        if (isSpinningBowser)
        {
            desiredState = "SpinThrow";
            desiredSpeed = 1.4f;
            blendDuration = 0.06f;
        }
        else if (isClimbingPole)
        {
            if (isPoleHandstand)
            {
                desiredState = "PoleHandstand";
                desiredSpeed = 1f;
            }
            else if (Mathf.Abs(poleClimbSpeed) > 0.12f)
            {
                desiredState = "PoleClimb";
                desiredSpeed = Mathf.Lerp(0.85f, 1.55f, Mathf.Abs(poleClimbSpeed) / 4.5f);
            }
            else
            {
                desiredState = "PoleIdle";
                desiredSpeed = 1f;
            }
            blendDuration = 0.06f;
        }
        else if (isLedgeHanging)
        {
            desiredState = "LedgeHang";
            desiredSpeed = 1f;
            blendDuration = 0.06f;
        }
        else if (isSwimming)
        {
            desiredState = "Swim";
            desiredSpeed = Mathf.Lerp(0.75f, 1.5f, normalizedSpeed);
            blendDuration = 0.10f;
        }
        else if (isGroundPounding)
        {
            desiredState = groundPoundStallNormalized > 0.01f ? "GroundPoundStall" : "GroundPoundDrop";
            desiredSpeed = groundPoundStallNormalized > 0.01f ? 1.8f : 1.0f;
            blendDuration = 0.05f;
        }
        else if (isSpinAttacking)
        {
            desiredState = "SpinAttack";
            desiredSpeed = 1.35f;
            blendDuration = 0.04f;
        }
        else if (isRolling)
        {
            desiredState = "Roll";
            desiredSpeed = Mathf.Lerp(1.0f, 1.85f, Mathf.Clamp01(normalizedSpeed));
            blendDuration = 0.04f;
        }
        else if (attackStage > 0)
        {
            desiredState = attackStage switch
            {
                1 => "PunchL",
                2 => "PunchR",
                3 => "Kick",
                4 => "AirKick",
                5 => "SweepKick",
                6 => "SlideKick",
                7 => "SpinAttack",
                8 => "CarryThrow",
                _ => "PunchL"
            };
            desiredSpeed = attackStage == 5 ? 1.35f : 1.25f;
            blendDuration = 0.04f;
        }
        else if (isSlopeSliding)
        {
            desiredState = "SlopeSlide";
            desiredSpeed = 1.1f;
            blendDuration = 0.06f;
        }
        else if (isSliding)
        {
            desiredState = "BellySlide";
            desiredSpeed = 1.15f;
            blendDuration = 0.06f;
        }
        else if (isDiving)
        {
            desiredState = "Dive";
            desiredSpeed = 1.2f;
            blendDuration = 0.05f;
        }
        else if (isWallSliding)
        {
            desiredState = "WallSlide";
            desiredSpeed = 1.0f;
            blendDuration = 0.06f;
        }
        else if (isCarryingObject)
        {
            desiredState = normalizedSpeed > 0.08f ? "CarryWalk" : "CarryIdle";
            desiredSpeed = normalizedSpeed > 0.08f ? Mathf.Lerp(0.75f, 1.35f, normalizedSpeed) : 1f;
            blendDuration = 0.07f;
        }
        else if (!isGrounded)
        {
            if (isLongJumping) desiredState = "LongJump";
            else if (isBackflipping) desiredState = "Backflip";
            else if (isSideFlipping) desiredState = "SideFlip";
            else
                desiredState = jumpStage switch
                {
                    >= 3 => "TripleFlip",
                    2 => "JumpDouble",
                    _ => "JumpSingle"
                };
            desiredSpeed = jumpStage >= 3 || isBackflipping || isSideFlipping ? 1.25f : 1.0f;
            blendDuration = 0.06f;
        }
        else if (isCrouching)
        {
            desiredState = normalizedSpeed > 0.08f ? "Crawl" : "Crouch";
            desiredSpeed = normalizedSpeed > 0.08f ? Mathf.Lerp(0.8f, 1.5f, normalizedSpeed) : 1f;
            blendDuration = 0.07f;
        }
        else if (normalizedSpeed > 0.55f)
        {
            desiredState = "Run";
            desiredSpeed = Mathf.Lerp(0.88f, 1.48f, (normalizedSpeed - 0.55f) / 0.65f);
            blendDuration = 0.08f;
        }
        else if (normalizedSpeed > 0.07f)
        {
            desiredState = "Walk";
            desiredSpeed = Mathf.Lerp(0.65f, 1.35f, normalizedSpeed / 0.55f);
            blendDuration = 0.09f;
        }
        else
        {
            desiredState = "Idle";
            desiredSpeed = 1f;
            blendDuration = 0.12f;
        }

        characterAnimator.speed = desiredSpeed;
        if (desiredState == _currentAnimState) return;
        _currentAnimState = desiredState;
        var hash = Animator.StringToHash(desiredState);
        if (!characterAnimator.HasState(0, hash))
        {
            var fallback = desiredState switch
            {
                "AirKick" => "Kick",
                "SlideKick" or "SlopeSlide" => "BellySlide",
                "Roll" => "TripleFlip",
                "SpinAttack" => "SpinThrow",
                "PoleClimb" or "PoleIdle" or "PoleHandstand" => "LedgeHang",
                "CarryIdle" => "Idle",
                "CarryWalk" => "Walk",
                "CarryThrow" => "PunchR",
                _ => "Idle"
            };
            hash = Animator.StringToHash(fallback);
        }
        if (characterAnimator.HasState(0, hash))
        {
            characterAnimator.CrossFadeInFixedTime(hash, blendDuration, 0);
        }
    }

    private void UpdateSecondaryJuice(float dt)
    {
        if (visualScaleRoot == null) return;

        _squashAmount = Mathf.MoveTowards(_squashAmount, 0f, dt * 2.1f);
        _stretchAmount = Mathf.MoveTowards(_stretchAmount, 0f, dt * 1.9f);

        var sy = 1f - _squashAmount + _stretchAmount;
        var sxz = 1f + (_squashAmount * 0.65f) - (_stretchAmount * 0.42f);

        // Subtle skid lean, wing-cap glide tilt, or high-speed turn bank
        var targetBank = isWingGliding
            ? Mathf.Clamp(-turnBankRate * 0.10f, -24f, 24f)
            : (isGrounded && normalizedSpeed > 0.25f ? Mathf.Clamp(-turnBankRate * 0.06f, -15f, 15f) : 0f);
        var targetLean = isWingGliding ? 18f : (isSkidding ? -16f : (isGrounded && normalizedSpeed > 0.7f ? 7f : 0f));
        _bankAngleDeg = Mathf.Lerp(_bankAngleDeg, targetBank, 1f - Mathf.Exp(-14f * dt));
        _forwardLeanDeg = Mathf.Lerp(_forwardLeanDeg, targetLean, 1f - Mathf.Exp(-16f * dt));

        var targetScale = new Vector3(sxz, sy, sxz);
        _scaleVelocity = Vector3.Lerp(_scaleVelocity, targetScale, 1f - Mathf.Exp(-24f * dt));
        visualScaleRoot.localScale = _scaleVelocity;
        visualScaleRoot.localRotation = Quaternion.Euler(_forwardLeanDeg, 0f, _bankAngleDeg);
    }

    private void UpdateLegacyHingeFallback(float dt)
    {
        if (bodyPivot == null) return;
        var lean = isDiving ? 58f : (isCrouching ? 24f : normalizedSpeed * 12f);
        bodyPivot.localRotation = Quaternion.Slerp(bodyPivot.localRotation, Quaternion.Euler(lean, 0f, 0f), 14f * dt);
        var swing = Mathf.Sin(Time.time * Mathf.Lerp(5f, 13f, normalizedSpeed)) * 38f * normalizedSpeed;
        if (leftLeg != null) leftLeg.localRotation = Quaternion.Euler(swing, 0f, 0f);
        if (rightLeg != null) rightLeg.localRotation = Quaternion.Euler(-swing, 0f, 0f);
        if (leftArm != null) leftArm.localRotation = Quaternion.Euler(-swing * 0.8f, 0f, 0f);
        if (rightArm != null) rightArm.localRotation = Quaternion.Euler(swing * 0.8f, 0f, 0f);
    }
}
