using UnityEngine;

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

    [Header("Locomotion State Inputs (Driven by MarioRemakeController)")]
    [Range(0f, 1.5f)] public float NormalizedSpeed;
    public Vector3 PlanarVelocity;
    public bool IsGrounded = true;
    public int JumpStage;
    public bool IsDiving;
    public bool IsCrouching;
    public bool IsSliding;
    public bool IsWallSliding;
    public bool IsGroundPounding;
    public float GroundPoundStallNormalized;
    public bool IsBackflipping;
    public bool IsSideFlipping;
    public bool IsLongJumping;
    public int AttackStage; // 0 = none, 1 = PunchL, 2 = PunchR, 3 = Kick, 4 = AirKick, 5 = SweepKick, 6 = SlideKick, 7 = SpinAttack, 8 = CarryThrow
    public float AttackProgress;

    [Header("Extended Odyssey / SM64 Feel Flags")]
    public bool IsSwimming;
    public bool IsLedgeHanging;
    public bool IsSpinningBowser;
    public bool IsSkidding;
    public bool IsRolling;
    public bool IsSpinAttacking;
    public bool IsSlopeSliding;
    public bool IsClimbingPole;
    public bool IsPoleHandstand;
    public float PoleClimbSpeed;
    public bool IsCarryingObject;
    public float TurnBankRate;

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
        float desiredSpeed = 1f;
        float blendDuration = 0.08f;

        if (IsSpinningBowser)
        {
            desiredState = "SpinThrow";
            desiredSpeed = 1.4f;
            blendDuration = 0.06f;
        }
        else if (IsClimbingPole)
        {
            if (IsPoleHandstand)
            {
                desiredState = "PoleHandstand";
                desiredSpeed = 1f;
            }
            else if (Mathf.Abs(PoleClimbSpeed) > 0.12f)
            {
                desiredState = "PoleClimb";
                desiredSpeed = Mathf.Lerp(0.85f, 1.55f, Mathf.Abs(PoleClimbSpeed) / 4.5f);
            }
            else
            {
                desiredState = "PoleIdle";
                desiredSpeed = 1f;
            }
            blendDuration = 0.06f;
        }
        else if (IsLedgeHanging)
        {
            desiredState = "LedgeHang";
            desiredSpeed = 1f;
            blendDuration = 0.06f;
        }
        else if (IsSwimming)
        {
            desiredState = "Swim";
            desiredSpeed = Mathf.Lerp(0.75f, 1.5f, NormalizedSpeed);
            blendDuration = 0.10f;
        }
        else if (IsGroundPounding)
        {
            desiredState = GroundPoundStallNormalized > 0.01f ? "GroundPoundStall" : "GroundPoundDrop";
            desiredSpeed = GroundPoundStallNormalized > 0.01f ? 1.8f : 1.0f;
            blendDuration = 0.05f;
        }
        else if (IsSpinAttacking)
        {
            desiredState = "SpinAttack";
            desiredSpeed = 1.35f;
            blendDuration = 0.04f;
        }
        else if (IsRolling)
        {
            desiredState = "Roll";
            desiredSpeed = Mathf.Lerp(1.0f, 1.85f, Mathf.Clamp01(NormalizedSpeed));
            blendDuration = 0.04f;
        }
        else if (AttackStage > 0)
        {
            desiredState = AttackStage switch
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
            desiredSpeed = AttackStage == 5 ? 1.35f : 1.25f;
            blendDuration = 0.04f;
        }
        else if (IsSlopeSliding)
        {
            desiredState = "SlopeSlide";
            desiredSpeed = 1.1f;
            blendDuration = 0.06f;
        }
        else if (IsSliding)
        {
            desiredState = "BellySlide";
            desiredSpeed = 1.15f;
            blendDuration = 0.06f;
        }
        else if (IsDiving)
        {
            desiredState = "Dive";
            desiredSpeed = 1.2f;
            blendDuration = 0.05f;
        }
        else if (IsWallSliding)
        {
            desiredState = "WallSlide";
            desiredSpeed = 1.0f;
            blendDuration = 0.06f;
        }
        else if (IsCarryingObject)
        {
            desiredState = NormalizedSpeed > 0.08f ? "CarryWalk" : "CarryIdle";
            desiredSpeed = NormalizedSpeed > 0.08f ? Mathf.Lerp(0.75f, 1.35f, NormalizedSpeed) : 1f;
            blendDuration = 0.07f;
        }
        else if (!IsGrounded)
        {
            if (IsLongJumping) desiredState = "LongJump";
            else if (IsBackflipping) desiredState = "Backflip";
            else if (IsSideFlipping) desiredState = "SideFlip";
            else if (JumpStage >= 3) desiredState = "TripleFlip";
            else if (JumpStage == 2) desiredState = "JumpDouble";
            else desiredState = "JumpSingle";
            desiredSpeed = JumpStage >= 3 || IsBackflipping || IsSideFlipping ? 1.25f : 1.0f;
            blendDuration = 0.06f;
        }
        else if (IsCrouching)
        {
            desiredState = NormalizedSpeed > 0.08f ? "Crawl" : "Crouch";
            desiredSpeed = NormalizedSpeed > 0.08f ? Mathf.Lerp(0.8f, 1.5f, NormalizedSpeed) : 1f;
            blendDuration = 0.07f;
        }
        else if (NormalizedSpeed > 0.55f)
        {
            desiredState = "Run";
            desiredSpeed = Mathf.Lerp(0.88f, 1.48f, (NormalizedSpeed - 0.55f) / 0.65f);
            blendDuration = 0.08f;
        }
        else if (NormalizedSpeed > 0.07f)
        {
            desiredState = "Walk";
            desiredSpeed = Mathf.Lerp(0.65f, 1.35f, NormalizedSpeed / 0.55f);
            blendDuration = 0.09f;
        }
        else
        {
            desiredState = "Idle";
            desiredSpeed = 1f;
            blendDuration = 0.12f;
        }

        characterAnimator.speed = desiredSpeed;
        if (desiredState != _currentAnimState)
        {
            _currentAnimState = desiredState;
            var hash = Animator.StringToHash(desiredState);
            if (!characterAnimator.HasState(0, hash))
            {
                string fallback = desiredState switch
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
    }

    private void UpdateSecondaryJuice(float dt)
    {
        if (visualScaleRoot == null) return;

        _squashAmount = Mathf.MoveTowards(_squashAmount, 0f, dt * 2.1f);
        _stretchAmount = Mathf.MoveTowards(_stretchAmount, 0f, dt * 1.9f);

        float sy = 1f - _squashAmount + _stretchAmount;
        float sxz = 1f + (_squashAmount * 0.65f) - (_stretchAmount * 0.42f);

        // Subtle skid lean or high-speed turn bank
        float targetBank = IsGrounded && NormalizedSpeed > 0.25f ? Mathf.Clamp(-TurnBankRate * 0.06f, -15f, 15f) : 0f;
        float targetLean = IsSkidding ? -16f : (IsGrounded && NormalizedSpeed > 0.7f ? 7f : 0f);
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
        float lean = IsDiving ? 58f : (IsCrouching ? 24f : NormalizedSpeed * 12f);
        bodyPivot.localRotation = Quaternion.Slerp(bodyPivot.localRotation, Quaternion.Euler(lean, 0f, 0f), 14f * dt);
        float swing = Mathf.Sin(Time.time * Mathf.Lerp(5f, 13f, NormalizedSpeed)) * 38f * NormalizedSpeed;
        if (leftLeg != null) leftLeg.localRotation = Quaternion.Euler(swing, 0f, 0f);
        if (rightLeg != null) rightLeg.localRotation = Quaternion.Euler(-swing, 0f, 0f);
        if (leftArm != null) leftArm.localRotation = Quaternion.Euler(-swing * 0.8f, 0f, 0f);
        if (rightArm != null) rightArm.localRotation = Quaternion.Euler(swing * 0.8f, 0f, 0f);
    }
}
