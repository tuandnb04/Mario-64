using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

/// <summary>
/// Full Super Mario 64 Remake Acrobatics & Lakitu 360° Camera Controller:
/// ---------------------------------------------------------------------
/// 1. COMPLETE SM64 ACROBATICS ARSENAL:
///    - Single Jump -> Double Jump -> Triple Front-Flip Somersault! (3-stage combo within landing window)
///    - Backflip! (Crouch while stationary/slow + Jump -> high vertical reverse somersault!)
///    - Side Somersault! (Snap stick 180° opposite to running velocity + Jump -> high twisting aerial flip!)
///    - Long Jump! (Hold Crouch/Z while running + Jump -> low-arc fast horizontal leap across huge chasms!)
///    - 3-Hit Punch-Punch-Kick Combo! (Left Click / J / F on ground: Left Punch -> Right Punch -> Leaping Kick!)
///    - Air Jump Kick! (Attack in mid-air while moving slowly -> forward karate kick with upward hover pop!)
///    - Air Dive & Belly Slide! (E / Right Click or Attack while running fast -> forward dive onto stomach -> slides along ground -> press Jump/Attack to Dive-Rollout flip!)
///    - Ground Pound! (C / Ctrl / Shift in mid-air -> mid-air flip stall -> heavy downward slam + shockwave + camera impulse + Pound-Jump if Jump pressed on impact!)
///    - Wall Slide & Wall Kick! (Slide down walls with friction & dust puffs -> press Jump to kick off at 45°!)
///    - Moving Platform Inheritance! (Automatically rides Shove-Blocks, Rotating Bridges, Seesaws & Floating Isles without slipping!)
///
/// 2. TRUE 360° LAKITU ORBIT CAMERA + C-BUTTONS:
///    - Free Mouse Look (when Playing, cursor locks for smooth 360° Yaw & Pitch orbit; Esc unlocks for Pause)
///    - Q / E or Left / Right Arrow or Gamepad Right Stick for classic Lakitu C-Left / C-Right orbit
///    - R / Tab or Middle Mouse Click to snap camera behind Mario's back
///    - Scroll Wheel or Z/X zoom distance (Close / Medium / Wide Lakitu View)
///    - Intelligent gentle auto-follow yaw when running sideways without fighting manual mouse input!
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class MarioRemakeController : MonoBehaviour
{
    private static MarioRemakeController _instance;
    public static MarioRemakeController Instance
    {
        get
        {
            if (_instance == null) _instance = FindAnyObjectByType<MarioRemakeController>();
            return _instance;
        }
        private set => _instance = value;
    }

    [Header("Movement Tunables")]
    public float maxRunSpeed = 9.2f;
    public float crawlSpeed = 3.2f;
    public float acceleration = 32f;
    public float deceleration = 26f;
    public float airControl = 16f;
    public float turnSmoothSpeed = 16f;
    public float gravity = -26f;

    [Header("SM64 Jump & Acrobatics Heights")]
    public float singleJumpHeight = 2.7f;
    public float doubleJumpHeight = 3.7f;
    public float tripleJumpHeight = 5.4f;
    public float backflipHeight = 4.9f;
    public float sideFlipHeight = 4.5f;
    public float longJumpHeight = 2.2f;
    public float longJumpSpeedMultiplier = 1.58f;
    public float comboWindowSeconds = 0.36f;
    public float coyoteTimeSeconds = 0.15f;
    public float jumpBufferSeconds = 0.15f;

    [Header("Lakitu 360° Camera Tunables")]
    public float mouseSensitivity = 0.18f;
    public float keyboardOrbitSpeed = 125f;
    public float autoFollowYawStrength = 1.65f;
    public float minCameraPitch = -12f;
    public float maxCameraPitch = 58f;
    public float cameraDistance = 6.4f;

    [Header("SM64 3D Swimming & Oxygen / Power Meter")]
    public bool hasWaterInCourse;
    public float waterSurfaceY = -0.25f;
    public Vector2 waterBoundsX = new(-36f, 36f);
    public Vector2 waterBoundsZ = new(-14f, 30f);
    public float maxOxygenSeconds = 32f; // 8 SM64 Power Wedges (4s per wedge)
    public float currentOxygenSeconds = 32f;
    public float swimStrokeSpeed = 7.8f;
    public float swimFlutterSpeed = 9.2f;
    public bool IsSwimming { get; private set; }
    public int OxygenWedges => Mathf.Clamp(Mathf.CeilToInt((currentOxygenSeconds / Mathf.Max(1f, maxOxygenSeconds)) * 8f), 0, 8);
    public float OxygenNormalized => Mathf.Clamp01(currentOxygenSeconds / Mathf.Max(1f, maxOxygenSeconds));
    private float _swimPitch;

    [FormerlySerializedAs("CameraYawProxy")] [Header("References")]
    public Transform cameraYawProxy;
    [FormerlySerializedAs("Motion")] public ProceduralCharacterMotion motion;
    [FormerlySerializedAs("ImpulseSource")] public CinemachineImpulseSource impulseSource;
    [FormerlySerializedAs("PopProxy")] public TransformScalePopRoot popProxy;

    [FormerlySerializedAs("DebugMoveInput")] [Header("Debug / CLI Verification Hooks")]
    public Vector2 debugMoveInput;
    [FormerlySerializedAs("DebugJumpTrigger")] public bool debugJumpTrigger;
    [FormerlySerializedAs("DebugDiveTrigger")] public bool debugDiveTrigger;
    [FormerlySerializedAs("DebugGroundPoundTrigger")] public bool debugGroundPoundTrigger;

    private CharacterController _cc;
    private Vector3 _planarVelocity;
    private float _verticalVelocity;
    private bool _wasGrounded = true;
    private float _lastGroundedTime;
    private float _lastJumpPressedTime = -10f;
    private float _landedTimer = 10f;

    // SM64 Acrobatics State Flags
    private bool _isCrouching;
    private bool _isDiving;
    private bool _isBellySliding;
    private bool _isGroundPounding;
    private bool _isBackflipping;
    private bool _isSideFlipping;
    private bool _isLongJumping;
    private bool _isWallSliding;
    private float _groundPoundStallTimer;
    private float _groundPoundImpactTimer;

    // Reversal / Side-Somersault Detection
    private Vector3 _lastRunDirection = Vector3.forward;
    private float _skidReversalTimer;

    // 3-Hit Punch-Punch-Kick & Air Kick State
    private int _attackComboStage; // 0 = none, 1 = Punch L, 2 = Punch R, 3 = Kick, 4 = Air Kick
    private float _attackTimer;
    private float _attackDuration = 0.26f;
    private float _attackChainWindowTimer;
    private bool _attackHitRegistered;

    // Moving Platform Tracking
    private Transform _activePlatform;
    private Vector3 _lastPlatformWorldPos;
    private Quaternion _lastPlatformWorldRot;

    // Lakitu 360° Camera Orbit State
    private float _cameraYaw;
    private float _cameraPitch = 16f;
    private float _manualOrbitCooldown;
    private CinemachineThirdPersonFollow _cmFollow;

    private Vector3 _spawnPosition;
    private Quaternion _spawnRotation;
    private Vector3 _lastWallNormal;
    private float _lastWallTouchTime = -10f;

    public bool IsGroundPoundingActive => _isGroundPounding || _groundPoundImpactTimer > 0f;
    public bool IsAttackingOrSliding => _attackComboStage > 0 || _isDiving || _isBellySliding || IsGroundPoundingActive;
    private int JumpComboStage { get; set; }

    private void Awake()
    {
        Instance = this;
        _cc = GetComponent<CharacterController>();
        if (motion == null) motion = GetComponent<ProceduralCharacterMotion>();
        if (impulseSource == null) impulseSource = GetComponent<CinemachineImpulseSource>();
        if (popProxy == null) popProxy = GetComponentInChildren<TransformScalePopRoot>();

        _spawnPosition = transform.position;
        _spawnRotation = transform.rotation;
        _cameraYaw = transform.eulerAngles.y;
        _lastRunDirection = transform.forward;
        currentOxygenSeconds = maxOxygenSeconds;

        var waterObj = GameObject.Find("TranslucentBayWaterSurface");
        if (waterObj != null)
        {
            hasWaterInCourse = true;
            waterSurfaceY = waterObj.transform.position.y;
        }
    }

    public void RefillOxygen(float secondsToAdd)
    {
        currentOxygenSeconds = Mathf.Clamp(currentOxygenSeconds + secondsToAdd, 0f, maxOxygenSeconds);
    }

    public void ResetToSpawn()
    {
        if (_cc != null) _cc.enabled = false;
        transform.SetPositionAndRotation(_spawnPosition, _spawnRotation);
        _planarVelocity = Vector3.zero;
        _verticalVelocity = -2f;
        JumpComboStage = 0;
        _isCrouching = false;
        _isDiving = false;
        _isBellySliding = false;
        _isGroundPounding = false;
        _isBackflipping = false;
        _isSideFlipping = false;
        _isLongJumping = false;
        _isWallSliding = false;
        IsSwimming = false;
        currentOxygenSeconds = maxOxygenSeconds;
        _attackComboStage = 0;
        _activePlatform = null;
        debugMoveInput = Vector2.zero;
        _cameraYaw = _spawnRotation.eulerAngles.y;
        _cameraPitch = 16f;
        if (_cc != null) _cc.enabled = true;
        if (cameraYawProxy != null)
        {
            cameraYawProxy.SetPositionAndRotation(transform.position, Quaternion.Euler(_cameraPitch, _cameraYaw, 0f));
        }
    }

    private void Update()
    {
        var unscaledDt = Time.unscaledDeltaTime > 0.0001f ? Time.unscaledDeltaTime : 0.016f;

        // 1. Update Lakitu 360° Camera Proxy
        UpdateLakituCamera(unscaledDt);

        if (GameManager.Instance != null && GameManager.Instance.currentState != GameState.Playing)
        {
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            if (motion == null) return;
            motion.NormalizedSpeed = 0f;
            motion.PlanarVelocity = Vector3.zero;
            motion.IsGrounded = true;
            return;
        }

        var dt = Time.deltaTime;
        if (dt <= 0.0001f || _cc == null || !_cc.enabled) return;

        // 2. Apply Moving Platform Delta (Shove-Blocks, Rotating Bridges, Seesaws, Floating Isles)
        ApplyMovingPlatformDelta();

        // 3. Hazard floor recovery (water moat or deep abyss)
        if (transform.position.y < -12.5f)
        {
            ResetToSpawn();
            return;
        }

        if (_skidReversalTimer > 0f) _skidReversalTimer -= dt;
        if (_groundPoundImpactTimer > 0f) _groundPoundImpactTimer -= dt;
        if (_attackChainWindowTimer > 0f) _attackChainWindowTimer -= dt;

        var moveInput = ReadMoveInput();
        var jumpPressed = ReadJumpPressed();
        var attackPressed = ReadAttackPressed();
        var divePressed = ReadDivePressed();
        var crouchHeld = ReadCrouchHeld();
        var groundPoundPressed = ReadGroundPoundPressed();

        if (jumpPressed) _lastJumpPressedTime = Time.time;

        var isGrounded = _cc.isGrounded;
        if (isGrounded)
        {
            _lastGroundedTime = Time.time;
            _landedTimer += dt;
            _isWallSliding = false;

            if (!_wasGrounded)
            {
                OnLanded();
            }
        }
        else
        {
            _landedTimer = 0f;
            _activePlatform = null;
        }

        // Update Crouch state on ground
        _isCrouching = isGrounded && crouchHeld && !_isBellySliding;

        // 4. Update Active Punch/Kick Combo Timer
        UpdateAttackCombo(dt);

        // 4B. Check SM64 3D Swimming & Oxygen / Power Meter State!
        UpdateWaterAndOxygenState(moveInput, jumpPressed, attackPressed, divePressed, crouchHeld, dt);

        if (IsSwimming)
        {
            // Move CharacterController in 3D water space
            var swimDisp = (_planarVelocity + Vector3.up * _verticalVelocity) * dt;
            _cc.Move(swimDisp);
            _wasGrounded = _cc.isGrounded;

            if (motion == null) return;
            motion.NormalizedSpeed = Mathf.Clamp01(_planarVelocity.magnitude / maxRunSpeed);
            motion.PlanarVelocity = _planarVelocity;
            motion.IsGrounded = false;
            motion.IsSwimming = true;
            motion.SwimPitchAngle = _swimPitch;
            motion.IsDiving = false;
            motion.IsCrouching = false;
            motion.IsSliding = false;
            motion.IsWallSliding = false;
            motion.IsGroundPounding = false;
            motion.AttackStage = _attackComboStage;
            return;
        }
        else if (motion != null)
        {
            motion.IsSwimming = false;
            motion.SwimPitchAngle = 0f;
        }

        // 5. Handle Ground Pound Stall & Slam
        if (_isGroundPounding)
        {
            _planarVelocity = Vector3.zero;
            if (_groundPoundStallTimer > 0f)
            {
                _groundPoundStallTimer -= dt;
                _verticalVelocity = 0f;
            }
            else
            {
                _verticalVelocity = -36f;
            }
        }
        else
        {
            UpdatePlanarMovement(moveInput, isGrounded, dt);
            HandleJumpAndAcrobatics(isGrounded, attackPressed, divePressed, groundPoundPressed);
        }

        // 6. Gravity & Wall-Slide Friction
        if (!_isGroundPounding)
        {
            var touchingWallInAir = !isGrounded && (Time.time - _lastWallTouchTime) <= 0.14f && _verticalVelocity < 0f;
            _isWallSliding = touchingWallInAir;

            if (isGrounded && _verticalVelocity < 0f)
            {
                _verticalVelocity = -3.5f;
            }
            else if (_isWallSliding)
            {
                // Wall friction slows descent like SM64!
                _verticalVelocity = Mathf.Max(_verticalVelocity + gravity * 0.35f * dt, -5.5f);
            }
            else
            {
                _verticalVelocity += gravity * dt;
                if (_verticalVelocity < -30f) _verticalVelocity = -30f;
            }
        }

        // 7. Move CharacterController
        var finalDisp = (_planarVelocity + Vector3.up * _verticalVelocity) * dt;
        var flags = _cc.Move(finalDisp);

        if ((flags & CollisionFlags.Sides) != 0 && !isGrounded)
        {
            _lastWallTouchTime = Time.time;
        }

        _wasGrounded = isGrounded;

        // 8. Drive ProceduralCharacterMotion Poses
        if (motion == null) return;
        motion.NormalizedSpeed = Mathf.Clamp01(_planarVelocity.magnitude / maxRunSpeed);
        motion.PlanarVelocity = _planarVelocity;
        motion.IsGrounded = isGrounded;
        motion.JumpStage = JumpComboStage;
        motion.IsDiving = _isDiving;
        motion.IsCrouching = _isCrouching;
        motion.IsSliding = _isBellySliding;
        motion.IsWallSliding = _isWallSliding;
        motion.IsGroundPounding = _isGroundPounding;
        motion.GroundPoundStallNormalized = Mathf.Clamp01(_groundPoundStallTimer / 0.18f);
        motion.IsBackflipping = _isBackflipping;
        motion.IsSideFlipping = _isSideFlipping;
        motion.IsLongJumping = _isLongJumping;
        motion.AttackStage = _attackComboStage;
        motion.AttackProgress = _attackDuration > 0.001f ? Mathf.Clamp01(1f - (_attackTimer / _attackDuration)) : 0f;
    }

    private void UpdateLakituCamera(float unscaledDt)
    {
        if (cameraYawProxy == null) return;

        var isPlaying = GameManager.Instance == null || GameManager.Instance.currentState == GameState.Playing;
        if (!isPlaying)
        {
            cameraYawProxy.position = Vector3.Lerp(cameraYawProxy.position, transform.position, 1f - Mathf.Exp(-14f * unscaledDt));
            return;
        }

        // Mouse Look + Keyboard C-Buttons (Q/E or Left/Right Arrow) + Gamepad Right Stick
        var yawInput = 0f;
        var pitchInput = 0f;

        var mouse = Mouse.current;
        if (mouse != null)
        {
            // Lock cursor when left/right clicking inside Game View during Playing state
            if (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            if (Cursor.lockState == CursorLockMode.Locked || mouse.rightButton.isPressed)
            {
                var delta = mouse.delta.ReadValue();
                yawInput += delta.x * mouseSensitivity;
                pitchInput -= delta.y * mouseSensitivity * 0.75f;
            }

            // Scroll wheel zooms Lakitu camera distance!
            var scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                cameraDistance = Mathf.Clamp(cameraDistance - Mathf.Sign(scroll) * 0.65f, 3.8f, 11.5f);
            }

            if (mouse.middleButton.wasPressedThisFrame)
            {
                SnapCameraBehindMario();
            }
        }

        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.qKey.isPressed || kb.leftArrowKey.isPressed) yawInput -= keyboardOrbitSpeed * unscaledDt;
            if (kb.eKey.isPressed || kb.rightArrowKey.isPressed) yawInput += keyboardOrbitSpeed * unscaledDt;
            if (kb.rKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame)
            {
                SnapCameraBehindMario();
            }
            if (kb.zKey.wasPressedThisFrame) cameraDistance = 4.5f; // Close Mario Cam
            if (kb.xKey.wasPressedThisFrame) cameraDistance = 8.8f; // Wide Lakitu Cam
        }

        var gp = Gamepad.current;
        if (gp != null)
        {
            var rStick = gp.rightStick.ReadValue();
            if (rStick.sqrMagnitude > 0.02f)
            {
                yawInput += rStick.x * keyboardOrbitSpeed * 1.15f * unscaledDt;
                pitchInput -= rStick.y * keyboardOrbitSpeed * 0.65f * unscaledDt;
            }
            if (gp.rightStickButton.wasPressedThisFrame)
            {
                SnapCameraBehindMario();
            }
        }

        if (Mathf.Abs(yawInput) > 0.001f || Mathf.Abs(pitchInput) > 0.001f)
        {
            _cameraYaw += yawInput;
            _cameraPitch = Mathf.Clamp(_cameraPitch + pitchInput, minCameraPitch, maxCameraPitch);
            _manualOrbitCooldown = 1.25f;
        }
        else
        {
            if (_manualOrbitCooldown > 0f) _manualOrbitCooldown -= unscaledDt;

            // Gentle Lakitu auto-follow yaw when Mario runs sideways and player isn't actively moving mouse
            if (_manualOrbitCooldown <= 0f && _planarVelocity.sqrMagnitude > 2.5f)
            {
                var velYaw = Mathf.Atan2(_planarVelocity.x, _planarVelocity.z) * Mathf.Rad2Deg;
                var angleDiff = Mathf.DeltaAngle(_cameraYaw, velYaw);
                // Only gently nudge when running perpendicular, never whip when running toward camera!
                if (Mathf.Abs(angleDiff) is > 20f and < 135f)
                {
                    _cameraYaw = Mathf.MoveTowardsAngle(_cameraYaw, velYaw, Mathf.Abs(angleDiff) * autoFollowYawStrength * unscaledDt);
                }
            }
        }

        cameraYawProxy.position = Vector3.Lerp(cameraYawProxy.position, transform.position, 1f - Mathf.Exp(-18f * unscaledDt));
        var targetRot = Quaternion.Euler(_cameraPitch, _cameraYaw, 0f);
        cameraYawProxy.rotation = Quaternion.Slerp(cameraYawProxy.rotation, targetRot, 1f - Mathf.Exp(-16f * unscaledDt));

        if (_cmFollow == null && GameManager.Instance != null && GameManager.Instance.gameplayVCamObject != null)
        {
            _cmFollow = GameManager.Instance.gameplayVCamObject.GetComponent<CinemachineThirdPersonFollow>();
        }
        if (_cmFollow != null)
        {
            _cmFollow.CameraDistance = Mathf.Lerp(_cmFollow.CameraDistance, cameraDistance, 1f - Mathf.Exp(-10f * unscaledDt));
        }
    }

    private void SnapCameraBehindMario()
    {
        _cameraYaw = transform.eulerAngles.y;
        _cameraPitch = 16f;
        _manualOrbitCooldown = 0.6f;
        if (GameAudio.Instance != null) GameAudio.Instance.PlayUIClick();
    }

    private void ApplyMovingPlatformDelta()
    {
        var plat = _activePlatform;
        if (plat == null || !plat.gameObject.activeInHierarchy)
        {
            _activePlatform = null;
            return;
        }

        var worldDelta = plat.position - _lastPlatformWorldPos;
        var rotDelta = plat.rotation * Quaternion.Inverse(_lastPlatformWorldRot);

        if (rotDelta != Quaternion.identity)
        {
            var offset = transform.position - plat.position;
            worldDelta += (rotDelta * offset) - offset;
            var yawDelta = rotDelta.eulerAngles.y;
            if (Mathf.Abs(yawDelta) > 0.001f)
            {
                transform.Rotate(0f, yawDelta, 0f, Space.World);
            }
        }

        if (worldDelta.sqrMagnitude is > 0.000001f and < 4f)
        {
            _cc.Move(worldDelta);
        }

        if (_activePlatform != null)
        {
            _lastPlatformWorldPos = _activePlatform.position;
            _lastPlatformWorldRot = _activePlatform.rotation;
        }
    }

    private void UpdatePlanarMovement(Vector2 moveInput, bool isGrounded, float dt)
    {
        // Use Lakitu Camera Yaw for crisp camera-relative movement
        var camYawRot = Quaternion.Euler(0f, _cameraYaw, 0f);
        if (Camera.main != null)
        {
            var fwd = Camera.main.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude > 0.01f)
            {
                camYawRot = Quaternion.LookRotation(fwd.normalized, Vector3.up);
            }
        }

        var desiredDir = (camYawRot * new Vector3(moveInput.x, 0f, moveInput.y));
        if (desiredDir.sqrMagnitude > 1f) desiredDir.Normalize();

        // Detect 180° sudden stick reversal for Side Somersault!
        if (isGrounded && _planarVelocity.magnitude > maxRunSpeed * 0.48f && desiredDir.sqrMagnitude > 0.25f)
        {
            var dot = Vector3.Dot(desiredDir.normalized, _lastRunDirection);
            if (dot < -0.55f)
            {
                _skidReversalTimer = 0.28f;
            }
        }

        if (_planarVelocity.magnitude > maxRunSpeed * 0.45f)
        {
            _lastRunDirection = _planarVelocity.normalized;
        }

        // Belly Slide friction on ground
        if (_isBellySliding)
        {
            _planarVelocity = Vector3.MoveTowards(_planarVelocity, desiredDir * 2.5f, 11f * dt);
            if (_planarVelocity.magnitude < 1.8f)
            {
                _isBellySliding = false;
            }
            return;
        }

        // Attack lunge deceleration
        if (_attackComboStage is >= 1 and <= 3 && isGrounded)
        {
            _planarVelocity = Vector3.MoveTowards(_planarVelocity, Vector3.zero, deceleration * 1.4f * dt);
            return;
        }

        var targetMaxSpeed = _isCrouching ? crawlSpeed : maxRunSpeed;
        var targetVel = desiredDir * targetMaxSpeed;
        var rate = isGrounded
            ? (desiredDir.sqrMagnitude > 0.01f ? acceleration : deceleration)
            : airControl;

        if (!_isDiving && !_isLongJumping)
        {
            _planarVelocity = Vector3.MoveTowards(_planarVelocity, targetVel, rate * dt);
        }
        else if (_isLongJumping && !isGrounded)
        {
            // Slight air steering during Long Jump
            _planarVelocity = Vector3.MoveTowards(_planarVelocity, _planarVelocity.normalized * (maxRunSpeed * longJumpSpeedMultiplier) + desiredDir * 2.2f, airControl * 0.45f * dt);
        }

        if (!(_planarVelocity.sqrMagnitude > 0.15f)) return;
        var targetRot = Quaternion.LookRotation(_planarVelocity.normalized, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 1f - Mathf.Exp(-turnSmoothSpeed * dt));
    }

    private void UpdateWaterAndOxygenState(
        Vector2 moveInput,
        bool jumpPressed,
        bool attackPressed,
        bool divePressed,
        bool crouchHeld,
        float dt)
    {
        if (!hasWaterInCourse)
        {
            IsSwimming = false;
            currentOxygenSeconds = maxOxygenSeconds;
            return;
        }

        var pos = transform.position;
        var inHorizontalLagoon = pos.x >= waterBoundsX.x && pos.x <= waterBoundsX.y &&
                                 pos.z >= waterBoundsZ.x && pos.z <= waterBoundsZ.y;
        var belowWaterSurface = inHorizontalLagoon && pos.y < (waterSurfaceY - 0.35f);

        // Metal Mario (Act 6) walks heavy on the ocean floor & does not lose oxygen!
        if (JollyRogerBayController.IsMetalMarioActive)
        {
            IsSwimming = false;
            currentOxygenSeconds = Mathf.MoveTowards(currentOxygenSeconds, maxOxygenSeconds, dt * 8f);
            return;
        }

        if (belowWaterSurface)
        {
            // Deplete Oxygen while submerged (1 wedge per 4s -> 32s total air)
            if (pos.y < waterSurfaceY - 0.85f)
            {
                currentOxygenSeconds -= dt;
                if (currentOxygenSeconds <= 0f)
                {
                    currentOxygenSeconds = 0f;
                    if (GameAudio.Instance != null) GameAudio.Instance.PlayGroundPoundSound();
                    ResetToSpawn();
                    return;
                }
            }
            else
            {
                // At water surface: rapidly gasp & refill Oxygen!
                currentOxygenSeconds = Mathf.MoveTowards(currentOxygenSeconds, maxOxygenSeconds, dt * 10f);
            }

            if (!IsSwimming)
            {
                IsSwimming = true;
                _isDiving = false;
                _isBellySliding = false;
                _isGroundPounding = false;
                _isBackflipping = false;
                _isSideFlipping = false;
                _isLongJumping = false;
                _verticalVelocity = Mathf.Max(_verticalVelocity * 0.35f, -3.5f);
                if (VFXPool.Instance != null) VFXPool.Instance.SpawnFootstepPuff(transform.position);
            }

            // 3D Underwater Swimming Controls (SM64 style):
            // - WASD / Left Stick steers Mario relative to Lakitu Camera Yaw
            // - Space (Jump) = Breaststroke Kick + Ascend toward surface!
            // - C / Ctrl / Shift (Crouch) = Dive deeper toward ocean floor!
            // - Left Click / J / E = Fast Flutter Kick forward thrust!
            var camYawRot = Quaternion.Euler(0f, _cameraYaw, 0f);
            var desiredPlanarDir = camYawRot * new Vector3(moveInput.x, 0f, moveInput.y);
            if (desiredPlanarDir.sqrMagnitude > 1f) desiredPlanarDir.Normalize();

            var targetSwimSpeed = (attackPressed || divePressed) ? swimFlutterSpeed : swimStrokeSpeed;
            if (jumpPressed)
            {
                targetSwimSpeed = swimFlutterSpeed;
                _verticalVelocity = 5.4f; // Breaststroke upward surge!
                if (GameAudio.Instance != null) GameAudio.Instance.PlayJumpSound(1);
                if (VFXPool.Instance != null) VFXPool.Instance.SpawnFootstepPuff(transform.position);

                // Dolphin Jump out of the water if near surface!
                if (pos.y >= waterSurfaceY - 1.15f)
                {
                    IsSwimming = false;
                    _verticalVelocity = Mathf.Sqrt(2f * Mathf.Abs(gravity) * doubleJumpHeight);
                    JumpComboStage = 2;
                    if (motion != null) motion.TriggerJumpStretch(2);
                    return;
                }
            }
            else if (crouchHeld)
            {
                // Dive deeper toward the trench floor!
                _verticalVelocity = Mathf.MoveTowards(_verticalVelocity, -6.2f, 18f * dt);
            }
            else if (Keyboard.current != null && Keyboard.current.spaceKey.isPressed)
            {
                // Hold Space to glide smoothly upward
                _verticalVelocity = Mathf.MoveTowards(_verticalVelocity, 4.5f, 14f * dt);
            }
            else
            {
                // Gentle neutral buoyancy drag
                _verticalVelocity = Mathf.MoveTowards(_verticalVelocity, -0.65f, 6.5f * dt);
            }

            if (attackPressed || divePressed)
            {
                _planarVelocity = transform.forward * swimFlutterSpeed;
                if (VFXPool.Instance != null) VFXPool.Instance.SpawnFootstepPuff(transform.position);
            }
            else
            {
                _planarVelocity = Vector3.MoveTowards(_planarVelocity, desiredPlanarDir * targetSwimSpeed, 14f * dt);
            }

            _swimPitch = Mathf.Clamp(-_verticalVelocity * 5.5f, -35f, 35f);

            if (desiredPlanarDir.sqrMagnitude > 0.05f)
            {
                var swimLook = Quaternion.LookRotation(desiredPlanarDir.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, swimLook, 1f - Mathf.Exp(-10f * dt));
            }
        }
        else
        {
            IsSwimming = false;
            currentOxygenSeconds = Mathf.MoveTowards(currentOxygenSeconds, maxOxygenSeconds, dt * 12f);
        }
    }

    private void HandleJumpAndAcrobatics(
        bool isGrounded,
        bool attackPressed,
        bool divePressed,
        bool groundPoundPressed)
    {
        var canCoyoteJump = (Time.time - _lastGroundedTime) <= coyoteTimeSeconds;
        var hasBufferedJump = (Time.time - _lastJumpPressedTime) <= jumpBufferSeconds;

        // 0. Dive Rollout! (Press Jump or Attack while Belly Sliding on ground -> front flip recovery!)
        if (isGrounded && _isBellySliding && (hasBufferedJump || attackPressed))
        {
            _lastJumpPressedTime = -10f;
            _isBellySliding = false;
            _isDiving = false;
            JumpComboStage = 2;
            _verticalVelocity = Mathf.Sqrt(2f * Mathf.Abs(gravity) * (singleJumpHeight * 0.9f));
            _planarVelocity = transform.forward * Mathf.Max(_planarVelocity.magnitude, maxRunSpeed * 0.95f);
            if (motion != null) motion.TriggerJumpStretch(2);
            if (GameAudio.Instance != null) GameAudio.Instance.PlayJumpSound(2);
            if (VFXPool.Instance != null) VFXPool.Instance.SpawnFootstepPuff(transform.position);
            return;
        }

        // 1. Ground / Coyote Jump Arsenal:
        //    - Long Jump (Crouch + moving + Jump)
        //    - Backflip (Crouch + stationary/slow + Jump)
        //    - Side Somersault (180° Skid Reversal + Jump)
        //    - Ground-Pound Boost Jump (Jump immediately after Ground Pound impact)
        //    - 3-Stage Combo Jump (Single -> Double -> Triple Somersault!)
        if (canCoyoteJump && hasBufferedJump && _verticalVelocity <= 0.5f)
        {
            _lastJumpPressedTime = -10f;
            _lastGroundedTime = -10f;
            _activePlatform = null;
            _isDiving = false;
            _isBellySliding = false;
            _isBackflipping = false;
            _isSideFlipping = false;
            _isLongJumping = false;

            var speed = _planarVelocity.magnitude;

            if (_isCrouching && speed > 2.2f)
            {
                // LONG JUMP! ("Yahoo!")
                _isLongJumping = true;
                _isCrouching = false;
                JumpComboStage = 2;
                _verticalVelocity = Mathf.Sqrt(2f * Mathf.Abs(gravity) * longJumpHeight);
                _planarVelocity = transform.forward * (maxRunSpeed * longJumpSpeedMultiplier);
            }
            else if (_isCrouching && speed <= 2.2f)
            {
                // BACKFLIP! (High vertical reverse somersault!)
                _isBackflipping = true;
                _isCrouching = false;
                JumpComboStage = 3;
                _verticalVelocity = Mathf.Sqrt(2f * Mathf.Abs(gravity) * backflipHeight);
                _planarVelocity = -transform.forward * (maxRunSpeed * 0.42f);
            }
            else if (_skidReversalTimer > 0f)
            {
                // SIDE SOMERSAULT! (180° reversal twist + high flip!)
                _isSideFlipping = true;
                _skidReversalTimer = 0f;
                JumpComboStage = 3;
                _verticalVelocity = Mathf.Sqrt(2f * Mathf.Abs(gravity) * sideFlipHeight);
                var revDir = -_lastRunDirection;
                revDir.y = 0f;
                if (revDir.sqrMagnitude > 0.01f)
                {
                    revDir.Normalize();
                    transform.rotation = Quaternion.LookRotation(revDir, Vector3.up);
                    _planarVelocity = revDir * (maxRunSpeed * 0.65f);
                }
            }
            else if (_groundPoundImpactTimer > 0f)
            {
                // GROUND POUND BOOST JUMP!
                _groundPoundImpactTimer = 0f;
                _isBackflipping = true;
                JumpComboStage = 3;
                _verticalVelocity = Mathf.Sqrt(2f * Mathf.Abs(gravity) * doubleJumpHeight * 1.15f);
            }
            else
            {
                // 3-STAGE COMBO JUMP (Single -> Double -> Triple Front-Flip!)
                if (_landedTimer <= comboWindowSeconds && JumpComboStage == 1)
                {
                    JumpComboStage = 2;
                }
                else if (_landedTimer <= comboWindowSeconds && JumpComboStage == 2 && speed > maxRunSpeed * 0.42f)
                {
                    JumpComboStage = 3;
                }
                else
                {
                    JumpComboStage = 1;
                }

                var h = JumpComboStage == 3 ? tripleJumpHeight
                    : (JumpComboStage == 2 ? doubleJumpHeight : singleJumpHeight);

                _verticalVelocity = Mathf.Sqrt(2f * Mathf.Abs(gravity) * h);
                if (JumpComboStage == 3)
                {
                    _planarVelocity = transform.forward * Mathf.Max(speed, maxRunSpeed * 1.18f);
                }
            }

            if (motion != null) motion.TriggerJumpStretch(JumpComboStage);
            if (GameAudio.Instance != null) GameAudio.Instance.PlayJumpSound(JumpComboStage);
            if (VFXPool.Instance != null) VFXPool.Instance.SpawnFootstepPuff(transform.position);
            return;
        }

        switch (isGrounded)
        {
            // 2. Ground Attack: 3-Hit Punch-Punch-Kick OR High-Speed Dive!
            case true when !_isBellySliding && (attackPressed || divePressed):
            {
                if (divePressed || (_planarVelocity.magnitude > maxRunSpeed * 0.72f && _attackComboStage == 0))
                {
                    // Launch into a forward Dive!
                    TriggerForwardDive();
                    return;
                }

                TriggerGroundPunchKickCombo();
                return;
            }
            // 3. Mid-Air Actions: Wall Kick, Air Dive, Air Jump Kick, Ground Pound
            // 3A. Wall Kick!
            case false when hasBufferedJump && (Time.time - _lastWallTouchTime) <= 0.25f && _lastWallNormal.sqrMagnitude > 0.1f:
            {
                _lastJumpPressedTime = -10f;
                _lastWallTouchTime = -10f;
                _isDiving = false;
                _isWallSliding = false;
                _isBackflipping = false;
                _isSideFlipping = false;
                _isLongJumping = false;
                JumpComboStage = 2;

                var kickDir = (_lastWallNormal + Vector3.up * 0.15f).normalized;
                kickDir.y = 0f;
                if (kickDir.sqrMagnitude > 0.01f)
                {
                    kickDir.Normalize();
                    transform.rotation = Quaternion.LookRotation(kickDir, Vector3.up);
                    _planarVelocity = kickDir * (maxRunSpeed * 1.05f);
                }
                _verticalVelocity = Mathf.Sqrt(2f * Mathf.Abs(gravity) * doubleJumpHeight);
                if (motion != null) motion.TriggerJumpStretch(2);
                if (GameAudio.Instance != null) GameAudio.Instance.PlayJumpSound(2);
                if (VFXPool.Instance != null) VFXPool.Instance.SpawnFootstepPuff(transform.position);
                return;
            }
            // 3B. Air Dive vs Air Jump Kick!
            case false when !_isDiving && !_isGroundPounding && (divePressed || attackPressed):
            {
                if (divePressed || _planarVelocity.magnitude > maxRunSpeed * 0.62f)
                {
                    TriggerForwardDive();
                }
                else
                {
                    // SM64 Air Jump Kick! ("Hoo!" with slight upward hover pop!)
                    _attackComboStage = 4;
                    _attackDuration = 0.34f;
                    _attackTimer = _attackDuration;
                    _attackHitRegistered = false;
                    _verticalVelocity = Mathf.Max(_verticalVelocity, 6.2f);
                    _planarVelocity += transform.forward * 2.5f;
                    if (GameAudio.Instance != null) GameAudio.Instance.PlayJumpSound(1);
                }
                return;
            }
            // 3C. Ground Pound in mid-air!
            case false:
            {
                if (!_isGroundPounding && groundPoundPressed && _verticalVelocity < 5.5f)
                {
                    _isGroundPounding = true;
                    _isDiving = false;
                    _isBackflipping = false;
                    _isSideFlipping = false;
                    _isLongJumping = false;
                    _groundPoundStallTimer = 0.18f;
                    _planarVelocity = Vector3.zero;
                    _verticalVelocity = 0f;
                    if (GameAudio.Instance != null) GameAudio.Instance.PlayJumpSound(2);
                }

                break;
            }
        }
    }

    private void TriggerForwardDive()
    {
        _isDiving = true;
        _isCrouching = false;
        _isBackflipping = false;
        _isSideFlipping = false;
        _isLongJumping = false;
        _verticalVelocity = Mathf.Sqrt(2f * Mathf.Abs(gravity) * 1.35f);
        _planarVelocity = transform.forward * (maxRunSpeed * 1.42f);
        if (motion != null) motion.TriggerJumpStretch(2);
        if (GameAudio.Instance != null) GameAudio.Instance.PlayJumpSound(2);
        if (VFXPool.Instance != null) VFXPool.Instance.SpawnFootstepPuff(transform.position);
    }

    private void TriggerGroundPunchKickCombo()
    {
        if (_attackComboStage == 0 || _attackChainWindowTimer <= 0f)
        {
            _attackComboStage = 1; // 1. Left Punch ("Yah!")
            _attackDuration = 0.22f;
        }
        else switch (_attackComboStage)
        {
            case 1 when _attackTimer <= 0.12f:
                _attackComboStage = 2; // 2. Right Punch ("Wah!")
                _attackDuration = 0.24f;
                break;
            case 2 when _attackTimer <= 0.14f:
                _attackComboStage = 3; // 3. Heavy Roundhouse Kick! ("Hoo!")
                _attackDuration = 0.34f;
                _verticalVelocity = 6.5f; // Leaping kick pop!
                break;
            default:
                return;
        }

        _attackTimer = _attackDuration;
        _attackChainWindowTimer = 0.48f;
        _attackHitRegistered = false;
        _planarVelocity = transform.forward * (_attackComboStage == 3 ? 6.5f : 3.2f);
        if (motion != null) motion.TriggerJumpStretch(_attackComboStage);
        if (GameAudio.Instance != null) GameAudio.Instance.PlayJumpSound(_attackComboStage);
    }

    private void UpdateAttackCombo(float dt)
    {
        if (_attackComboStage == 0) return;

        _attackTimer -= dt;
        if (!_attackHitRegistered && _attackTimer <= _attackDuration * 0.65f)
        {
            _attackHitRegistered = true;
            CheckMeleeHitbox(transform.position + transform.forward * 1.15f + Vector3.up * 0.7f, 1.85f);
        }

        if (!(_attackTimer <= 0f)) return;
        if (_attackChainWindowTimer <= 0f || _attackComboStage >= 3)
        {
            _attackComboStage = 0;
        }
    }

    private void CheckMeleeHitbox(Vector3 center, float radius)
    {
        var cols = Physics.OverlapSphere(center, radius);
        foreach (var col in cols)
        {
            if (col == null || col.gameObject == gameObject) continue;

            var bob = col.GetComponentInParent<BobOmbPatrol>();
            if (bob != null)
            {
                bob.OnMarioAttackHit();
            }

            var bfElem = col.GetComponentInParent<BobOmbBattlefieldController>();
            if (bfElem != null)
            {
                bfElem.RegisterHitOnElement();
            }

            var whomp = col.GetComponentInParent<WhompBossController>();
            if (whomp != null)
            {
                whomp.ForceGroundPoundHitFromMario();
            }

            var wfElem = col.GetComponentInParent<WhompFortressElement>();
            if (wfElem != null && wfElem.hazardType == Mario64Remake.FortressHazardType.KickableBoard)
            {
                wfElem.KnockDownImmediate();
            }

            var jrbElem = col.GetComponentInParent<JollyRogerBayController>();
            if (jrbElem != null && jrbElem.elementType == Mario64Remake.JollyRogerElementType.TreasureChestPuzzleMember)
            {
                jrbElem.InteractWithChest(this);
            }
        }
    }

    private void OnLanded()
    {
        var wasHeavy = _isGroundPounding || JumpComboStage == 3 || _isBackflipping || _isSideFlipping;
        _isBackflipping = false;
        _isSideFlipping = false;
        _isLongJumping = false;
        if (_attackComboStage == 4) _attackComboStage = 0;

        if (_isDiving)
        {
            // Transition smoothly from Air Dive into Belly Slide on ground!
            _isDiving = false;
            _isBellySliding = true;
            _planarVelocity = transform.forward * Mathf.Max(_planarVelocity.magnitude, maxRunSpeed * 1.25f);
            if (VFXPool.Instance != null) VFXPool.Instance.SpawnFootstepPuff(transform.position);
        }
        else if (_isGroundPounding)
        {
            _isGroundPounding = false;
            _groundPoundImpactTimer = 0.28f;
            if (GameAudio.Instance != null) GameAudio.Instance.PlayGroundPoundSound();
            if (VFXPool.Instance != null) VFXPool.Instance.SpawnPickupBurst(transform.position);
            if (impulseSource != null) impulseSource.GenerateImpulse(0.45f);
            if (GameManager.Instance != null) GameManager.Instance.TriggerHitStop();
            CheckMeleeHitbox(transform.position, 2.8f);
        }
        else if (VFXPool.Instance != null)
        {
            VFXPool.Instance.SpawnFootstepPuff(transform.position);
        }

        if (motion != null) motion.TriggerLandSquash(wasHeavy);
    }

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        // Track moving platform under Mario's feet
        if (hit.normal.y > 0.65f && hit.collider != null)
        {
            var wf = hit.collider.GetComponentInParent<WhompFortressElement>();
            var bf = hit.collider.GetComponentInParent<BobOmbBattlefieldController>();
            var jrb = hit.collider.GetComponentInParent<JollyRogerBayController>();
            if (wf != null || bf != null || jrb != null)
            {
                var platTransform = hit.collider.transform;
                if (_activePlatform != platTransform)
                {
                    _activePlatform = platTransform;
                    _lastPlatformWorldPos = _activePlatform.position;
                    _lastPlatformWorldRot = _activePlatform.rotation;
                }
            }
            else
            {
                _activePlatform = null;
            }
        }

        // Track wall normal for Wall-Slide & Wall-Kick
        if (Mathf.Abs(hit.normal.y) < 0.35f)
        {
            _lastWallNormal = hit.normal;
            _lastWallTouchTime = Time.time;
        }
    }

    private Vector2 ReadMoveInput()
    {
        if (debugMoveInput.sqrMagnitude > 0.001f)
            return Vector2.ClampMagnitude(debugMoveInput, 1f);

        var move = Vector2.zero;
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) move.y += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) move.y -= 1f;
            if (kb.dKey.isPressed) move.x += 1f;
            if (kb.aKey.isPressed) move.x -= 1f;
        }

        var gp = Gamepad.current;
        if (gp != null && gp.leftStick.ReadValue().sqrMagnitude > move.sqrMagnitude)
        {
            move = gp.leftStick.ReadValue();
        }

        return Vector2.ClampMagnitude(move, 1f);
    }

    private bool ReadJumpPressed()
    {
        if (debugJumpTrigger)
        {
            debugJumpTrigger = false;
            return true;
        }
        var kb = Keyboard.current;
        if (kb != null && kb.spaceKey.wasPressedThisFrame) return true;
        var gp = Gamepad.current;
        return gp != null && gp.buttonSouth.wasPressedThisFrame;
    }

    private bool ReadAttackPressed()
    {
        var kb = Keyboard.current;
        if (kb != null && (kb.jKey.wasPressedThisFrame || kb.fKey.wasPressedThisFrame)) return true;
        var mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame && GameManager.Instance != null && GameManager.Instance.currentState == GameState.Playing)
            return true;
        var gp = Gamepad.current;
        return gp != null && gp.buttonWest.wasPressedThisFrame;
    }

    private bool ReadDivePressed()
    {
        if (debugDiveTrigger)
        {
            debugDiveTrigger = false;
            return true;
        }
        var kb = Keyboard.current;
        if (kb != null && kb.kKey.wasPressedThisFrame) return true;
        var gp = Gamepad.current;
        return gp != null && gp.buttonNorth.wasPressedThisFrame;
    }

    private bool ReadCrouchHeld()
    {
        if (debugGroundPoundTrigger) return true;
        var kb = Keyboard.current;
        if (kb != null && (kb.leftShiftKey.isPressed || kb.cKey.isPressed || kb.leftCtrlKey.isPressed)) return true;
        var gp = Gamepad.current;
        return gp != null && (gp.leftTrigger.isPressed || gp.rightTrigger.isPressed);
    }

    private bool ReadGroundPoundPressed()
    {
        if (debugGroundPoundTrigger)
        {
            debugGroundPoundTrigger = false;
            return true;
        }
        var kb = Keyboard.current;
        if (kb != null && (kb.cKey.wasPressedThisFrame || kb.leftCtrlKey.wasPressedThisFrame || kb.leftShiftKey.wasPressedThisFrame)) return true;
        var gp = Gamepad.current;
        return gp != null && (gp.leftTrigger.wasPressedThisFrame || gp.rightTrigger.wasPressedThisFrame);
    }
}
