using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Full Super Mario 64 Acrobatics Procedural Character Motion:
/// - Gait bob, limb swing, acceleration pitch lean, turn bank, idle breathe & sway
/// - Full SM64 Move Set Visual Poses:
///   - Single / Double / Triple Front-Flip (360° forward somersault)
///   - Backflip (reverse 360° backward somersault!)
///   - Side Sommersault (180° twist + aerial flip!)
///   - Long Jump ("Yahoo!" low-arc forward stretch pose)
///   - Crouch & Crawl (compressed torso & bent knees)
///   - Belly Slide / Dive Slide (horizontal stomach slide with arms & legs arched)
///   - Ground Pound Stall Flip (rapid mid-air tuck spin before slamming down!)
///   - 3-Hit Punch-Punch-Kick Combo (left punch -> right punch -> leaping roundhouse kick!)
///   - Jump Kick (mid-air forward karate kick)
///   - Wall Slide (bracing against wall with friction)
/// </summary>
public class ProceduralCharacterMotion : MonoBehaviour
{
    [FormerlySerializedAs("VisualRoot")] [Header("Hierarchy Parts")]
    public Transform visualRoot;
    [FormerlySerializedAs("BodyPivot")] public Transform bodyPivot;
    [FormerlySerializedAs("HeadPivot")] public Transform headPivot;
    [FormerlySerializedAs("CapVisor")] public Transform capVisor;
    [FormerlySerializedAs("LeftArm")] public Transform leftArm;
    [FormerlySerializedAs("RightArm")] public Transform rightArm;
    [FormerlySerializedAs("LeftLeg")] public Transform leftLeg;
    [FormerlySerializedAs("RightLeg")] public Transform rightLeg;
    [FormerlySerializedAs("Backpack")] public Transform backpack;

    public Transform VisualRoot { get => visualRoot; set => visualRoot = value; }
    public Transform BodyPivot { get => bodyPivot; set => bodyPivot = value; }
    public Transform HeadPivot { get => headPivot; set => headPivot = value; }
    public Transform CapVisor { get => capVisor; set => capVisor = value; }
    public Transform LeftArm { get => leftArm; set => leftArm = value; }
    public Transform RightArm { get => rightArm; set => rightArm = value; }
    public Transform LeftLeg { get => leftLeg; set => leftLeg = value; }
    public Transform RightLeg { get => rightLeg; set => rightLeg = value; }
    public Transform Backpack { get => backpack; set => backpack = value; }

    [Header("Tunables")]
    public float stepFreq = 2.65f;
    public float bobAmp = 0.088f;
    public float limbSwingDeg = 42f;
    public float leanPitchDeg = 10.5f;
    public float leanBankDeg = 8.0f;
    public float idleBreatheFreq = 0.65f;
    public float idleBreatheAmp = 0.026f;
    public float idleSwayFreq = 0.32f;
    public float idleSwayDeg = 6.5f;

    private float _gaitBlend;
    private float _stepClock;
    private float _idleClock;
    private float _airFlipAngle;
    private Vector3 _baseBodyLocalPos;
    private Quaternion _baseBodyLocalRot;
    private Quaternion _baseHeadLocalRot;
    private Quaternion _baseVisorLocalRot;
    private Quaternion _baseBackpackLocalRot;
    private Vector3 _baseVisualScale = Vector3.one;
    private Vector3 _squashScale = Vector3.one;

    private Vector3 _prevPlanarVel;
    private float _currentPitchLean;
    private float _currentBankLean;
    private float _secondaryLagAngle;
    private float _footstepTimer;
    private Coroutine _squashRoutine;

    public ProceduralCharacterMotion(Transform nearestPointOfInterest)
    {
        NearestPointOfInterest = nearestPointOfInterest;
    }

    public float NormalizedSpeed { get; set; }
    public Vector3 PlanarVelocity { get; set; }
    public bool IsGrounded { get; set; } = true;
    public int JumpStage { get; set; }
    public bool IsDiving { get; set; }
    public bool IsCrouching { get; set; }
    public bool IsSliding { get; set; }
    public bool IsWallSliding { get; set; }
    public bool IsGroundPounding { get; set; }
    public float GroundPoundStallNormalized { get; set; }
    public bool IsBackflipping { get; set; }
    public bool IsSideFlipping { get; set; }
    public bool IsLongJumping { get; set; }
    public bool IsSwimming { get; set; }
    public float SwimPitchAngle { get; set; }
    public int AttackStage { get; set; } // 0 = none, 1 = Punch L, 2 = Punch R, 3 = Kick, 4 = Air Kick
    public float AttackProgress { get; set; }
    private Transform NearestPointOfInterest { get; }

    private void Start()
    {
        InitializeBaselines();
    }

    private void InitializeBaselines()
    {
        if (visualRoot != null) _baseVisualScale = visualRoot.localScale;
        if (bodyPivot != null)
        {
            _baseBodyLocalPos = bodyPivot.localPosition;
            _baseBodyLocalRot = bodyPivot.localRotation;
        }
        if (headPivot != null) _baseHeadLocalRot = headPivot.localRotation;
        if (capVisor != null) _baseVisorLocalRot = capVisor.localRotation;
        if (backpack != null) _baseBackpackLocalRot = backpack.localRotation;
    }

    private void Update()
    {
        if (bodyPivot == null) return;

        var dt = Time.deltaTime;
        if (dt <= 0.00001f) return;

        var targetBlend = Mathf.Clamp01(NormalizedSpeed);
        var blendRate = targetBlend > _gaitBlend ? 9f : (1f / 0.25f);
        _gaitBlend = Mathf.MoveTowards(_gaitBlend, targetBlend, dt * blendRate);

        _stepClock += dt * stepFreq * Mathf.Lerp(0.65f, 1.25f, _gaitBlend);
        _idleClock += dt;

        if (IsGrounded)
        {
            _airFlipAngle = 0f;
        }

        // 1. Gait bob + Crouch offset + Slide offset
        var gaitPhase = _stepClock * Mathf.PI * 2f;
        var gaitY = Mathf.Abs(Mathf.Sin(gaitPhase)) * bobAmp * _gaitBlend;
        var gaitRoll = Mathf.Sin(gaitPhase * 0.5f) * 3.0f * _gaitBlend;

        var idleBreatheY = Mathf.Sin(_idleClock * idleBreatheFreq * Mathf.PI * 2f) * idleBreatheAmp * (1f - _gaitBlend * 0.7f);
        var idleSway = Mathf.Sin(_idleClock * idleSwayFreq * Mathf.PI * 2f) * idleSwayDeg * (1f - _gaitBlend * 0.6f);

        var verticalOffset = gaitY + idleBreatheY;
        if (IsSwimming)
        {
            verticalOffset = -0.15f + Mathf.Sin(_idleClock * 2.8f) * 0.08f;
        }
        else if (IsSliding || IsDiving)
        {
            verticalOffset = -0.32f;
        }
        else if (IsCrouching)
        {
            verticalOffset = -0.22f + Mathf.Abs(Mathf.Sin(gaitPhase)) * 0.03f * _gaitBlend;
        }

        bodyPivot.localPosition = _baseBodyLocalPos + new Vector3(0f, verticalOffset, 0f);

        // 2. Acceleration pitch lean, turn bank, & Acrobatics Somersaults!
        var accel = (PlanarVelocity - _prevPlanarVel) / Mathf.Max(dt, 0.001f);
        _prevPlanarVel = PlanarVelocity;

        var targetPitch = NormalizedSpeed * leanPitchDeg + Mathf.Clamp(accel.magnitude * 0.15f, -4f, 7f) * _gaitBlend;
        if (IsSwimming)
        {
            targetPitch = 72f + SwimPitchAngle; // Horizontal swimming body posture!
        }
        else if (IsDiving || IsSliding)
        {
            targetPitch = 78f; // Belly-down slide!
        }
        else if (IsCrouching)
        {
            targetPitch = 24f;
        }
        else if (IsLongJumping && !IsGrounded)
        {
            targetPitch = -18f; // Feet-forward Long Jump pose!
        }
        else if (IsWallSliding)
        {
            targetPitch = -12f;
        }

        var localAccel = transform.InverseTransformDirection(accel);
        var targetBank = Mathf.Clamp(-localAccel.x * 0.42f, -leanBankDeg, leanBankDeg) * _gaitBlend;

        _currentPitchLean = Mathf.Lerp(_currentPitchLean, targetPitch, 1f - Mathf.Exp(-12f * dt));
        _currentBankLean = Mathf.Lerp(_currentBankLean, targetBank, 1f - Mathf.Exp(-12f * dt));

        // Aerial Flip Angles (Triple Jump, Backflip, Side Somersault, Ground Pound Stall)
        var extraPitchFlip = 0f;
        var extraYawTwist = 0f;
        if (!IsGrounded)
        {
            if (IsGroundPounding && GroundPoundStallNormalized > 0f)
            {
                extraPitchFlip = (1f - GroundPoundStallNormalized) * 360f;
            }
            else if (IsBackflipping)
            {
                _airFlipAngle -= dt * 560f;
                extraPitchFlip = _airFlipAngle;
            }
            else if (IsSideFlipping)
            {
                _airFlipAngle += dt * 520f;
                extraPitchFlip = _airFlipAngle;
                extraYawTwist = Mathf.Clamp01( Mathf.Abs(_airFlipAngle) / 180f ) * 180f;
            }
            else if (JumpStage == 3 && !IsDiving)
            {
                _airFlipAngle += dt * 620f;
                extraPitchFlip = _airFlipAngle;
            }
        }

        bodyPivot.localRotation = _baseBodyLocalRot * Quaternion.Euler(
            _currentPitchLean + extraPitchFlip,
            idleSway * 0.35f + extraYawTwist,
            _currentBankLean + gaitRoll);

        // 3. Limb Swing + Punch / Kick / Slide / Crouch Poses
        var swing = Mathf.Sin(gaitPhase) * limbSwingDeg * _gaitBlend;
        var airPose = IsGrounded ? 0f : 32f;

        var lLegPitch = swing - airPose;
        var rLegPitch = -swing + airPose * 0.6f;
        var lArmPitch = -swing * 0.9f - airPose * 1.25f;
        var rArmPitch = swing * 0.9f - airPose * 1.25f;
        var lArmRoll = 14f;
        var rArmRoll = -14f;

        if (IsSwimming)
        {
            // Authentic SM64 Breaststroke arms + Flutter Kick legs!
            var swimCycle = _idleClock * 5.2f;
            var stroke = Mathf.Sin(swimCycle);
            lArmPitch = -115f + stroke * 55f;
            rArmPitch = -115f + stroke * 55f;
            lArmRoll = 42f + Mathf.Cos(swimCycle) * 28f;
            rArmRoll = -42f - Mathf.Cos(swimCycle) * 28f;
            lLegPitch = Mathf.Sin(swimCycle * 2.2f) * 32f;
            rLegPitch = -Mathf.Sin(swimCycle * 2.2f) * 32f;
        }
        else if (IsSliding || IsDiving)
        {
            // Superman / Belly-slide pose!
            lArmPitch = -135f;
            rArmPitch = -135f;
            lArmRoll = 28f;
            rArmRoll = -28f;
            lLegPitch = 35f;
            rLegPitch = 35f;
        }
        else if (IsCrouching)
        {
            lLegPitch = -55f + swing * 0.4f;
            rLegPitch = -55f - swing * 0.4f;
            lArmPitch = 25f;
            rArmPitch = 25f;
        }
        else if (IsLongJumping && !IsGrounded)
        {
            lLegPitch = -65f;
            rLegPitch = -45f;
            lArmPitch = -110f;
            rArmPitch = -110f;
        }

        // Punch-Punch-Kick & Air Kick Overrides!
        if (AttackStage > 0)
        {
            var punchArc = Mathf.Sin(AttackProgress * Mathf.PI);
            switch (AttackStage)
            {
                case 1: // Left Punch ("Yah!")
                    lArmPitch = Mathf.Lerp(lArmPitch, -95f, punchArc);
                    break;
                case 2: // Right Punch ("Wah!")
                    rArmPitch = Mathf.Lerp(rArmPitch, -95f, punchArc);
                    break;
                case 3: // Roundhouse / Heavy Kick ("Hoo!")
                case 4: // Jump Kick!
                    rLegPitch = Mathf.Lerp(rLegPitch, -88f, punchArc);
                    lArmPitch = -45f;
                    rArmPitch = 45f;
                    break;
            }
        }

        if (leftLeg != null)
            leftLeg.localRotation = Quaternion.Euler(lLegPitch, 0f, -3f);
        if (rightLeg != null)
            rightLeg.localRotation = Quaternion.Euler(rLegPitch, 0f, 3f);
        if (leftArm != null)
            leftArm.localRotation = Quaternion.Euler(lArmPitch, 0f, lArmRoll);
        if (rightArm != null)
            rightArm.localRotation = Quaternion.Euler(rArmPitch, 0f, rArmRoll);

        // 4. Secondary motion (exponential damp lag on cap visor & backpack)
        var targetLag = -gaitY * 140f + (_currentPitchLean * 0.45f) + idleSway;
        _secondaryLagAngle = Mathf.Lerp(_secondaryLagAngle, targetLag, 1f - Mathf.Exp(-12f * dt));

        if (capVisor != null)
            capVisor.localRotation = _baseVisorLocalRot * Quaternion.Euler(_secondaryLagAngle * 0.45f, 0f, 0f);
        if (backpack != null)
            backpack.localRotation = _baseBackpackLocalRot * Quaternion.Euler(-_secondaryLagAngle * 0.6f, idleSway, 0f);

        // 5. Head look
        if (headPivot != null)
        {
            var headYaw = idleSway * 0.5f;
            var headPitch = (IsSliding || IsDiving) ? -48f : (-4f + Mathf.Sin(_idleClock * idleBreatheFreq * Mathf.PI * 2f) * 2.5f);

            if (NearestPointOfInterest != null && !IsSliding && !IsDiving)
            {
                var toPoi = NearestPointOfInterest.position - transform.position;
                toPoi.y = 0f;
                if (toPoi.sqrMagnitude is > 0.5f and < 225f)
                {
                    var signedYaw = Vector3.SignedAngle(transform.forward, toPoi.normalized, Vector3.up);
                    headYaw = Mathf.Clamp(signedYaw * 0.45f, -52f, 52f);
                }
            }

            var targetHead = _baseHeadLocalRot * Quaternion.Euler(headPitch, headYaw, 0f);
            headPivot.localRotation = Quaternion.Slerp(headPivot.localRotation, targetHead, 1f - Mathf.Exp(-8f * dt));
        }

        // 6. Apply squash & stretch onto VisualRoot
        if (visualRoot != null)
        {
            var crouchScale = IsCrouching && !IsSliding ? new Vector3(1.12f, 0.78f, 1.12f) : Vector3.one;
            visualRoot.localScale = Vector3.Scale(_baseVisualScale, Vector3.Scale(_squashScale, crouchScale));
        }

        // 7. Footstep & Slide dust puffs
        if (!IsGrounded || (!(_gaitBlend > 0.35f) && !IsSliding && !IsWallSliding)) return;
        var puffRate = (IsSliding || IsWallSliding) ? 9f : (stepFreq * 2f);
        _footstepTimer += dt * puffRate;
        if (!(_footstepTimer >= 1f)) return;
        _footstepTimer -= 1f;
        if (VFXPool.Instance != null)
            VFXPool.Instance.SpawnFootstepPuff(transform.position + Vector3.up * 0.05f);
    }

    public void TriggerJumpStretch(int stage)
    {
        if (_squashRoutine != null) StopCoroutine(_squashRoutine);
        var stretchY = 1.14f + (stage * 0.045f);
        var squashXZ = 1f / Mathf.Sqrt(stretchY);
        _squashRoutine = StartCoroutine(AnimateSquash(new Vector3(squashXZ, stretchY, squashXZ), 0.16f));
    }

    public void TriggerLandSquash(bool heavy = false)
    {
        if (_squashRoutine != null) StopCoroutine(_squashRoutine);
        var impact = heavy ? new Vector3(1.26f, 0.72f, 1.26f) : new Vector3(1.14f, 0.84f, 1.14f);
        _squashRoutine = StartCoroutine(AnimateSquash(impact, heavy ? 0.19f : 0.12f));
    }

    private System.Collections.IEnumerator AnimateSquash(Vector3 peakScale, float duration)
    {
        _squashScale = peakScale;
        yield return Ease.Tween(duration, t =>
        {
            _squashScale = Vector3.LerpUnclamped(peakScale, Vector3.one, Ease.OutBack(t));
        }, Ease.OutCubic, unscaled: false);
        _squashScale = Vector3.one;
    }
}
