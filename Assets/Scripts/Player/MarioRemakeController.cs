using System;
using Mario64Remake;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

/// <summary>
/// Super Mario Odyssey-Grade Feel & Camera Controller (Strictly SM64 Moveset — Zero Cappy/Capture):
/// -----------------------------------------------------------------------------------------------
/// 1. ZERO-LAG ODYSSEY 360° FREE-ORBIT CAMERA (runs in LateUpdate after CharacterController.Move):
///    - Right-Click drag works immediately even without locking cursor; clicking Game View or pressing Tab/F9 toggles cursor mouselook!
///    - Wide vertical pitch [-28°..+74°], smooth height cushioning, dynamic FOV widen during flips/dives/long jumps,
///      scroll zoom [4.0m..11.5m], Q/E & Arrow Keys C-button orbit, R / Middle-Click snappy align behind Mario!
///    - Intelligent gentle auto-follow yields immediately whenever player manually orbits Camera!
/// 2. SNAPPY SM64 MOVESET WITH ODYSSEY RESPONSIVENESS:
///    - Variable Jump Height (tap vs hold Space) + Apex Float Gravity Reduction + Crisp Fast Fall + Coyote (0.16s) + Buffer (0.16s)
///    - Single -> Double -> Triple Front-Flip Somersault!
///    - Skid-to-Side Somersault (reverse stick >130° while sprinting -> pivot skid sparks -> Jump!)
///    - Crouch (C/Ctrl) & Crawl, Backflip (stationary Crouch + Jump), Long Jump (running Crouch + Jump!)
///    - 3-Hit Punch -> Punch -> Roundhouse Kick (Left Click / F / J) & Air Jump Kick!
///    - Sweep Kick (stationary Crouch + Attack) & Slide Kick (running Crouch + Attack)!
///    - Dedicated Air Dive (E / Right Click / X) -> Belly Slide -> Jump Rollout Hop!
///    - Ground Pound (Ctrl/C/Shift in mid-air) -> Stall -> Slam + Shockwave -> Pound Boost Jump,
///      AND classic pro trick: Ground-Pound -> Dive Cancel (press Dive during GP stall)!
///    - Forgiving Wall Slide & Crisp 45° Wall Kick (with brief outward steer grace)
///    - Ledge Grab & Vault Climb onto platform edges automatically while falling!
///    - Steep Slope Sliding + Universal Moving Platform Riding + 3D Swimming
///    - Bowser Boss Tail Grab -> 360° Spin -> Hurl toward Spiked Bombs!
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class MarioRemakeController : MonoBehaviour
{
    private static MarioRemakeController instance;
    public static MarioRemakeController Instance
    {
        get
        {
            if (instance == null) instance = FindAnyObjectByType<MarioRemakeController>();
            return instance;
        }
    }

    [Header("Movement Tunables (Odyssey-Snappy SM64)")]
    public float maxRunSpeed = 9.2f;
    public float crawlSpeed = 3.4f;
    public float acceleration = 46f;
    public float deceleration = 40f;
    public float airControl = 20f;
    public float turnSmoothSpeed = 20f;
    public float gravity = -27.5f;

    [Header("SM64 Jump & Acrobatics Heights")]
    public float singleJumpHeight = 2.35f;
    public float doubleJumpHeight = 3.35f;
    public float tripleJumpHeight = 4.95f;
    public float backflipHeight = 4.35f;
    public float sideFlipHeight = 4.10f;
    public float longJumpHeight = 1.98f;
    public float longJumpSpeedMultiplier = 1.48f;
    public float comboWindowSeconds = 0.40f;
    public float coyoteTimeSeconds = 0.16f;
    public float jumpBufferSeconds = 0.16f;

    [Header("Odyssey 360° Camera Tunables")]
    public float mouseSensitivity = 0.22f;
    public float keyboardOrbitSpeed = 148f;
    public float autoFollowYawStrength;
    public float minCameraPitch = -42f;
    public float maxCameraPitch = 82f;
    public float cameraDistance = 6.6f;
    public bool autoLockCursorOnPlay = true;

    [Header("SM64 8-Wedge Power Meter & Swimming")]
    public int maxHealth = 8;
    public int currentHealth = 8;
    public int lives = 4;
    public float hurtInvulnDuration = 1.45f;
    public bool hasWaterInCourse;
    public float abyssDeathY = -18.5f;
    public float waterSurfaceY = -0.25f;
    public Vector2 waterBoundsX = new(-36f, 36f);
    public Vector2 waterBoundsZ = new(-14f, 30f);
    public float maxOxygenSeconds = 32f;
    public float currentOxygenSeconds = 32f;
    public float swimStrokeSpeed = 8.2f;
    public float swimFlutterSpeed = 9.6f;
    public bool IsSwimming { get; private set; }
    public int HealthWedges => Mathf.Clamp(currentHealth, 0, maxHealth);
    public float HealthNormalized => Mathf.Clamp01((float)currentHealth / Mathf.Max(1, maxHealth));
    public int OxygenWedges => Mathf.Clamp(Mathf.CeilToInt((currentOxygenSeconds / Mathf.Max(1f, maxOxygenSeconds)) * 8f), 0, 8);
    public float OxygenNormalized => Mathf.Clamp01(currentOxygenSeconds / Mathf.Max(1f, maxOxygenSeconds));

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
    public bool debugGrabOrAttackTrigger;
    public Vector2 debugOrbitDelta;
    public bool debugRollTrigger;
    public bool debugSpinAttackTrigger;
    public bool debugToggleFirstPerson;

    private CharacterController _cc;
    private Vector3 _planarVelocity;
    private bool _wasGrounded = true;
    private float _lastGroundedTime;
    private float _lastJumpPressedTime = -10f;
    private float _landedTimer = 10f;
    private bool _jumpCutApplied;

    // SM64 Acrobatics State Flags
    private bool _isDiving;
    private bool _isBellySliding;
    private bool _isSlideKicking;
    private bool _isGroundPounding;
    private bool _isBackflipping;
    private bool _isSideFlipping;
    private bool _isLongJumping;
    private bool _isWallSliding;
    private bool _isLedgeHanging;
    private bool _isRolling;
    private float _rollTimer;
    private bool _isSpinAttacking;
    private float _spinTimer;
    private float _spinCooldownTimer;
    private bool _usedAirSpinHover;
    private bool _isSlopeSliding;
    private ClimbablePole _activePole;
    private float _poleHeight;
    private float _poleAngleDeg;
    private bool _isPoleHandstand;
    private float _poleCooldownTimer;
    private CarriableObject _carriedObject;
    private TestPlaygroundManager _playgroundMgr;
    private float _groundPoundStallTimer;
    private float _groundPoundImpactTimer;
    private float _wallKickSteerLockTimer;
    private Vector3 _wallKickSteerDir;
    private float _ledgeCooldownTimer;
    private Vector3 _ledgeTopPos;
    private Vector3 _ledgeForward;

    // Reversal / Side-Somersault Detection
    private float _skidReversalTimer;
    private float _prevFacingYaw;

    // Combat: 3-Hit Punch-Punch-Kick, Air Kick, Sweep Kick
    private int _attackComboStage; // 0 = none, 1 = Punch L, 2 = Punch R, 3 = Kick, 4 = Air Kick, 5 = Sweep Kick
    private float _attackTimer;
    private float _attackDuration = 0.25f;
    private float _attackChainWindowTimer;
    private bool _attackHitRegistered;
    private bool _usedAirKick;

    // Bowser Tail Grab & Spin State
    private BowserBossController _spinningBowser;
    private float _prevMouseXForSpin;

    // Moving Platform Tracking
    private Transform _activePlatform;
    private Vector3 _lastPlatformWorldPos;
    private Quaternion _lastPlatformWorldRot;

    // Odyssey 360° Camera Orbit State
    private float _cameraPitch = 17f;
    private float _manualOrbitCooldown;
    private float _snapTargetYaw;
    private bool _isSnappingCamera;
    private CinemachineThirdPersonFollow _cmFollow;
    private CinemachineCamera _cmCam;
    private float _baseCameraFov = 54f;
    private float _smoothedCamY;
    private bool _cursorLockedMode;
    private bool _rightDragHeld;

    private float _swimPitch;
    private float _hurtInvulnTimer;
    private Renderer[] _heroRenderers;

    private Vector3 _spawnPosition;
    private Quaternion _spawnRotation;
    private Vector3 _lastWallNormal;
    private float _lastWallTouchTime = -10f;

    public bool IsGroundPoundingActive => _isGroundPounding || _groundPoundImpactTimer > 0f;
    public bool IsAttackingOrSliding => _attackComboStage > 0 || _isDiving || _isBellySliding || _isSlideKicking || _isRolling || _isSpinAttacking || _isSlopeSliding || IsGroundPoundingActive;
    public bool IsStompingDown => CurrentVerticalSpeed < -0.5f && !_cc.isGrounded && !_isLedgeHanging;
    public float CurrentPlanarSpeed => _planarVelocity.magnitude;
    public float CurrentVerticalSpeed { get; private set; }

    public float CurrentGroundAngle { get; private set; }

    public float CurrentCameraYaw { get; private set; }

    public bool IsCrouching { get; private set; }

    public bool IsCarryingObject => _carriedObject != null;
    public bool IsFirstPersonLooking { get; private set; }

    private bool IsSpinningBowser => _spinningBowser != null && _spinningBowser.IsBeingSpun;
    private int JumpComboStage { get; set; }

    public string CurrentStateLabel
    {
        get
        {
            if (IsFirstPersonLooking) return "1st-Person Look";
            if (IsSpinningBowser) return "Spinning Bowser";
            if (_activePole != null) return _isPoleHandstand ? "Pole Handstand" : "Pole Climb";
            if (IsSwimming) return "Swimming";
            if (_isLedgeHanging) return "Ledge Hang";
            if (_isGroundPounding) return "Ground Pound";
            if (_isRolling) return "Odyssey Roll";
            if (_isSpinAttacking) return "Spin Attack";
            if (_isSlopeSliding) return "Slope Slide";
            if (_isSlideKicking) return "Slide Kick";
            if (_isBellySliding) return "Belly Slide";
            if (_isDiving) return "Dive";
            if (_isWallSliding) return "Wall Slide";
            if (_attackComboStage > 0) return $"Attack Stage {_attackComboStage}";
            if (!_cc.isGrounded)
            {
                if (_isLongJumping) return "Long Jump";
                if (_isBackflipping) return "Backflip";
                if (_isSideFlipping) return "Side Flip";
                if (JumpComboStage >= 3) return "Triple Flip";
                if (JumpComboStage == 2) return "Double Jump";
                return "Single Jump";
            }
            if (_carriedObject != null) return _planarVelocity.magnitude > 0.6f ? "Carry Walk" : "Carry Idle";
            if (IsCrouching) return _planarVelocity.magnitude > 0.6f ? "Crawl" : "Crouch";
            if (_planarVelocity.magnitude > maxRunSpeed * 0.55f) return "Run";
            if (_planarVelocity.magnitude > 0.5f) return "Walk";
            return "Idle";
        }
    }

    public void OnEnteredGameplay()
    {
        if (autoLockCursorOnPlay && !Application.isBatchMode)
        {
            ToggleCursorLockMode(true);
        }
    }

    private void RecordMoveStat(string key)
    {
        if (_playgroundMgr != null) _playgroundMgr.RecordMove(key);
    }

    public void BounceUpFromStomp(float height = 2.0f)
    {
        _isGroundPounding = false;
        _isDiving = false;
        _isSlideKicking = false;
        _jumpCutApplied = false;
        CurrentVerticalSpeed = Mathf.Sqrt(2f * Mathf.Abs(gravity) * height);
        if (motion != null) motion.TriggerJumpStretch();
    }

    public void TeleportAndFace(Vector3 worldPos, float yawDeg)
    {
        if (_cc != null) _cc.enabled = false;
        transform.position = worldPos;
        transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);
        _planarVelocity = Vector3.zero;
        CurrentVerticalSpeed = 0f;
        _isDiving = false;
        _isBellySliding = false;
        _isSlideKicking = false;
        _isGroundPounding = false;
        _isLedgeHanging = false;
        _isRolling = false;
        _isSpinAttacking = false;
        _isSlopeSliding = false;
        _activePole = null;
        _isPoleHandstand = false;
        if (_carriedObject != null)
        {
            _carriedObject.ResetCarriable();
            _carriedObject = null;
        }
        if (IsFirstPersonLooking)
        {
            IsFirstPersonLooking = false;
            SetHeroVisibility(true);
        }
        _activePlatform = null;
        CurrentCameraYaw = yawDeg;
        _smoothedCamY = worldPos.y + 1.1f;
        if (_cc != null) _cc.enabled = true;
    }

    public void OnBowserThrown()
    {
        _spinningBowser = null;
        if (motion != null) motion.isSpinningBowser = false;
    }

    public void SetBossHeld(Vector3 worldPos, float yawDeg)
    {
        if (_cc != null && _cc.enabled) _cc.enabled = false;
        transform.position = worldPos;
        transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);
        _planarVelocity = Vector3.zero;
        CurrentVerticalSpeed = 0f;
        _isDiving = false;
        _isBellySliding = false;
        _isGroundPounding = false;
        _isSlideKicking = false;
        _isRolling = false;
        _isSpinAttacking = false;
    }

    public void LaunchFromBossThrow(Vector3 velocity, int damageWedges = 1)
    {
        if (_cc != null && !_cc.enabled) _cc.enabled = true;
        _planarVelocity = new Vector3(velocity.x, 0f, velocity.z);
        CurrentVerticalSpeed = velocity.y;
        _isDiving = false;
        _isGroundPounding = false;
        if (damageWedges > 0)
        {
            currentHealth = Mathf.Max(0, currentHealth - damageWedges);
            _hurtInvulnTimer = hurtInvulnDuration;
            if (GameAudio.Instance != null) GameAudio.Instance.PlayHurtSound();
            if (impulseSource != null) impulseSource.GenerateImpulse(0.5f);
            if (currentHealth <= 0) RespawnAfterDeath();
        }
    }

    private void Awake()
    {
        instance = this;
        _cc = GetComponent<CharacterController>();
        if (_cc != null) _cc.slopeLimit = 50f;
        _spawnPosition = transform.position;
        _spawnRotation = transform.rotation;
        CurrentCameraYaw = transform.eulerAngles.y;
        _prevFacingYaw = CurrentCameraYaw;
        _smoothedCamY = transform.position.y + 1.1f;
        currentHealth = maxHealth;
        currentOxygenSeconds = maxOxygenSeconds;
        _heroRenderers = GetComponentsInChildren<Renderer>(true);

        if (motion == null) motion = GetComponent<ProceduralCharacterMotion>();
        if (impulseSource == null) impulseSource = GetComponent<CinemachineImpulseSource>();
        if (popProxy == null) popProxy = GetComponentInChildren<TransformScalePopRoot>();

        if (cameraYawProxy == null)
        {
            var proxy = transform.Find("CameraYawProxy");
            if (proxy != null) cameraYawProxy = proxy;
        }
    }

    private void Start()
    {
        if (GameManager.Instance == null)
        {
            var gmGo = new GameObject("GameManager");
            var gm = gmGo.AddComponent<GameManager>();
            gm.player = this;
        }
        _playgroundMgr = FindAnyObjectByType<TestPlaygroundManager>();
        ResolveCinemachineComponents();
        ApplyProxyRotationImmediate();
    }

    private void ResolveCinemachineComponents()
    {
        if (_cmCam != null && _cmFollow != null) return;
        foreach (var cam in FindObjectsByType<CinemachineCamera>(FindObjectsInactive.Include))
        {
            if (cam == null || !cam.name.Contains("Gameplay")) continue;
            _cmCam = cam;
            _cmFollow = cam.GetComponent<CinemachineThirdPersonFollow>();
            // Remove conflicting RotationComposer so cameraYawProxy 360° free orbit is never overridden!
            var rotComp = cam.GetComponent<CinemachineRotationComposer>();
            if (rotComp != null) Destroy(rotComp);

            _baseCameraFov = 54f;
            var lens = _cmCam.Lens;
            lens.FieldOfView = _baseCameraFov;
            _cmCam.Lens = lens;
            if (_cmFollow != null)
            {
                _cmFollow.CameraDistance = cameraDistance;
                _cmFollow.Damping = new Vector3(0.03f, 0.08f, 0.03f);
                _cmFollow.ShoulderOffset = new Vector3(0f, 0.44f, 0f);
                _cmFollow.VerticalArmLength = 0.22f;
                _cmFollow.CameraSide = 0.5f;
            }
            var mainCam = Camera.main;
            var brain = mainCam != null ? mainCam.GetComponent<CinemachineBrain>() : null;
            if (brain != null)
            {
                brain.DefaultBlend.Style = CinemachineBlendDefinition.Styles.EaseInOut;
                brain.DefaultBlend.Time = 0.45f;
            }
            break;
        }
    }

    public void ResetToSpawn()
    {
        if (_spinningBowser != null)
        {
            _spinningBowser = null;
        }
        if (_cc != null) _cc.enabled = false;
        transform.SetPositionAndRotation(_spawnPosition, _spawnRotation);
        if (_cc != null) _cc.enabled = true;

        _planarVelocity = Vector3.zero;
        CurrentVerticalSpeed = 0f;
        _wasGrounded = true;
        JumpComboStage = 0;
        IsCrouching = false;
        _isDiving = false;
        _isBellySliding = false;
        _isSlideKicking = false;
        _isGroundPounding = false;
        _isBackflipping = false;
        _isSideFlipping = false;
        _isLongJumping = false;
        _isWallSliding = false;
        _isLedgeHanging = false;
        _isRolling = false;
        _isSpinAttacking = false;
        _isSlopeSliding = false;
        _activePole = null;
        _isPoleHandstand = false;
        if (_carriedObject != null)
        {
            _carriedObject.ResetCarriable();
            _carriedObject = null;
        }
        IsFirstPersonLooking = false;
        _groundPoundStallTimer = 0f;
        _groundPoundImpactTimer = 0f;
        _attackComboStage = 0;
        _attackTimer = 0f;
        _activePlatform = null;
        IsSwimming = false;
        currentHealth = maxHealth;
        currentOxygenSeconds = maxOxygenSeconds;
        _hurtInvulnTimer = 0f;
        SetHeroVisibility(true);
        CurrentCameraYaw = transform.eulerAngles.y;
        _cameraPitch = 17f;
        _smoothedCamY = _spawnPosition.y + 1.1f;
        ApplyProxyRotationImmediate();
    }

    private void OnDisable()
    {
        if (!_cursorLockedMode) return;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        _cursorLockedMode = false;
    }

    private void Update()
    {
        var playing = GameManager.Instance == null || GameManager.Instance.currentState == GameState.Playing;
        if (!playing)
        {
            if (_cursorLockedMode || Cursor.lockState == CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                _cursorLockedMode = false;
            }
            UpdateMotionComponentOnly();
            return;
        }

        var dt = Time.deltaTime > 0.0001f ? Time.deltaTime : (Time.timeScale > 0f ? 0.016f * Time.timeScale : 0f);
        if (dt <= 0.00001f) return;

        if (_poleCooldownTimer > 0f) _poleCooldownTimer -= dt;
        if (_spinCooldownTimer > 0f) _spinCooldownTimer -= dt;

        // 0. Check Swimming vs Grounded State + Health Flash + Abyss Respawn
        UpdateDamageFlashAndAbyssCheck(dt);
        if ((hasWaterInCourse || WaterVolume.HasAny) && CheckAndHandleSwimmingState(dt))
        {
            UpdateMotionComponentOnly();
            return;
        }

        // 1. Handle Bowser Tail Grabbed & Spinning State (exclusive override!)
        if (_spinningBowser != null && _spinningBowser.IsBeingSpun)
        {
            HandleBowserSpinControl(dt);
            UpdateMotionComponentOnly();
            return;
        }

        if (_spinningBowser != null && !_spinningBowser.IsBeingSpun)
        {
            _spinningBowser = null;
        }

        // 2. Carry moving platform delta BEFORE locomotion (ShoveBlocks, Seesaws, Tilts, Lifts, Rotating Bridges)
        ApplyMovingPlatformCarry();

        // 3. Gather Inputs
        var rawInput = ReadMovementInput();
        var (jumpPressedThisFrame, jumpHeld) = ReadJumpInput();
        var crouchHeld = ReadCrouchHeld();
        var attackPressed = ReadAttackPressed();
        var divePressed = ReadDivePressed();
        // 8b. Spin Attack & Odyssey Roll disabled to keep moveset faithful to Super Mario 64.
        var rollPressed = false;
        var spinPressed = false;
        var firstPersonTogglePressed = ReadFirstPersonPressed();

        // Support automated Playtest CLI triggers
        if (debugGrabOrAttackTrigger)
        {
            debugGrabOrAttackTrigger = false;
            attackPressed = true;
        }

        if (jumpPressedThisFrame)
        {
            _lastJumpPressedTime = Time.time;
        }

        // 3b. First-Person C-Up Free Look Mode (`V`)
        if (firstPersonTogglePressed)
        {
            if (IsFirstPersonLooking)
            {
                ExitFirstPersonLook();
            }
            else if (_cc.isGrounded && !_isRolling && !_isSpinAttacking && !_isSlopeSliding && _activePole == null && _carriedObject == null)
            {
                IsFirstPersonLooking = true;
                _planarVelocity = Vector3.zero;
                CurrentVerticalSpeed = 0f;
                SetHeroVisibility(false);
                RecordMoveStat("FirstPersonLook");
            }
        }
        if (IsFirstPersonLooking)
        {
            if (jumpPressedThisFrame || attackPressed || divePressed || spinPressed)
            {
                ExitFirstPersonLook();
            }
            else
            {
                _planarVelocity = Vector3.zero;
                CurrentVerticalSpeed = -2f;
                _cc.Move(Vector3.down * (2f * dt));
                SetHeroVisibility(false);
                UpdateMotionComponentOnly();
                return;
            }
        }

        // 3c. Climbable Pole State (SM64 Climb / Spin / Top Handstand / Pole Jump)
        if (_activePole != null)
        {
            HandlePoleClimbState(rawInput, jumpPressedThisFrame, crouchHeld, dt);
            UpdateMotionComponentOnly();
            return;
        }

        // 4. Ledge Hang State Handling
        if (_ledgeCooldownTimer > 0f) _ledgeCooldownTimer -= dt;
        if (_isLedgeHanging)
        {
            HandleLedgeHangState(rawInput, jumpPressedThisFrame, crouchHeld);
            UpdateMotionComponentOnly();
            return;
        }

        // Clear stale carried reference if broken externally
        if (_carriedObject != null && _carriedObject.State != CarriableObject.CarryState.Carried)
        {
            _carriedObject = null;
        }

        // 5. Evaluate Grounded / Coyote State & Surface Slope
        var grounded = _cc.isGrounded;
        if (grounded)
        {
            _lastGroundedTime = Time.time;
            _wallKickSteerLockTimer = 0f;
            if (!_wasGrounded || _isGroundPounding)
            {
                OnJustLanded();
            }
            _landedTimer += dt;
        }
        else
        {
            CurrentGroundAngle = 0f;
            if (CurrentVerticalSpeed > 0.1f) _landedTimer = 0f;
        }

        var slopeNormal = Vector3.up;
        if (grounded && Physics.Raycast(transform.position + Vector3.up * 0.25f, Vector3.down, out var slopeHit, 0.95f, ~0, QueryTriggerInteraction.Ignore))
        {
            slopeNormal = slopeHit.normal;
            CurrentGroundAngle = Vector3.Angle(slopeNormal, Vector3.up);
        }

        // SM64 Steep Slope Slide (> 33.5 degrees) or Crouching Slide Downhill (> 15 degrees)
        if (grounded && _carriedObject == null && (CurrentGroundAngle > 33.5f || (CurrentGroundAngle > 15f && crouchHeld && !_isRolling)))
        {
            _isSlopeSliding = true;
            _isBellySliding = true;
            var slopeDown = Vector3.ProjectOnPlane(Vector3.down, slopeNormal);
            slopeDown.y = 0f;
            if (slopeDown.sqrMagnitude > 0.001f)
            {
                slopeDown.Normalize();
                _planarVelocity += slopeDown * (21f * dt);
                var maxSlide = Mathf.Lerp(13.5f, 18.5f, Mathf.Clamp01((CurrentGroundAngle - 15f) / 35f));
                if (_planarVelocity.magnitude > maxSlide) _planarVelocity = _planarVelocity.normalized * maxSlide;
            }
            RecordMoveStat("SlopeSlide");
        }
        else if (_isSlopeSliding && (!grounded || CurrentGroundAngle <= 14f))
        {
            _isSlopeSliding = false;
        }

        // 6. Camera-Relative Planar Move Direction
        var camForward = Quaternion.Euler(0f, CurrentCameraYaw, 0f) * Vector3.forward;
        var camRight = Quaternion.Euler(0f, CurrentCameraYaw, 0f) * Vector3.right;
        var moveInputDir = (camForward * rawInput.y + camRight * rawInput.x);
        var moveMagnitude = Mathf.Clamp01(rawInput.magnitude);
        if (moveInputDir.sqrMagnitude > 0.001f) moveInputDir.Normalize();

        if (_wallKickSteerLockTimer > 0f)
        {
            _wallKickSteerLockTimer -= dt;
            // Temporarily blend away from wall so holding direction toward wall doesn't kill the wall kick!
            moveInputDir = Vector3.Slerp(_wallKickSteerDir, moveInputDir, 0.35f).normalized;
        }

        // 6b. Latch onto ClimbablePole while jumping near or running directly into one
        if (TryLatchOntoPole(grounded, moveInputDir, moveMagnitude))
        {
            UpdateMotionComponentOnly();
            return;
        }

        // 7. Ground Pound Impact Timer, Crouch & Odyssey Roll Timers
        if (_groundPoundImpactTimer > 0f)
        {
            _groundPoundImpactTimer -= dt;
        }
        if (_isRolling)
        {
            _rollTimer -= dt;
            CheckMeleeHitbox(transform.position + transform.forward * 0.75f + Vector3.up * 0.55f, 1.25f);
            if (_rollTimer <= 0f || !grounded || _isSlopeSliding || _planarVelocity.magnitude < 4.0f)
            {
                _isRolling = false;
            }
        }
        if (_isSpinAttacking)
        {
            _spinTimer -= dt;
            CheckMeleeHitbox(transform.position + Vector3.up * 0.65f, 2.25f);
            if (_spinTimer <= 0f)
            {
                _isSpinAttacking = false;
            }
        }

        // Carry Drop on Crouch tap
        if (_carriedObject != null && grounded && ReadCrouchPressedThisFrame())
        {
            _carriedObject.DropAtFeet(transform.position + transform.forward * 0.95f + Vector3.up * 0.25f, transform.rotation);
            _carriedObject = null;
        }

        IsCrouching = grounded && crouchHeld && _carriedObject == null && !_isDiving && !_isBellySliding && !_isSlideKicking && !_isRolling && !_isSlopeSliding && _attackComboStage == 0;

        // 8. Combat Attacks (Throw Carried, Pickup, 3-Hit Punch-Kick, Air Kick, Sweep Kick, Slide Kick, Tail Grab)
        UpdateAttackState(dt);
        if (attackPressed && !_isGroundPounding)
        {
            TriggerMarioAttack(grounded, moveInputDir);
        }

        // 8b. Odyssey Spin Attack (`Z` / `X`) + Mid-Air Spin Hover
        if (spinPressed && _carriedObject == null && !_isGroundPounding && _spinCooldownTimer <= 0f)
        {
            TriggerSpinAttack(grounded, moveInputDir);
        }

        // 8c. Odyssey Roll (`LeftShift` on ground, `Crouch+E`, or Ground-Pound Impact Rollout!)
        if (rollPressed && grounded && _carriedObject == null && !_isGroundPounding && !_isSlopeSliding && !_isRolling)
        {
            StartOdysseyRoll(moveInputDir.sqrMagnitude > 0.05f ? moveInputDir : transform.forward);
        }

        // 9. Dedicated Dive (`E` / `Q`) — ALSO supports Pro Trick: Ground-Pound -> Dive Cancel!
        if (divePressed && _carriedObject == null)
        {
            if (_isGroundPounding && _groundPoundStallTimer > 0f)
            {
                // Ground-Pound -> Dive Cancel! Classic Odyssey / Modern SM64 movement tech!
                _isGroundPounding = false;
                _groundPoundStallTimer = 0f;
                var gpDiveDir = moveInputDir.sqrMagnitude > 0.05f ? moveInputDir : transform.forward;
                LaunchDive(gpDiveDir, bonusUpVel: 4.6f, speedBoost: 11.4f);
            }
            else if (!_isDiving && !_isBellySliding && !_isGroundPounding && !_isRolling)
            {
                var dDir = moveInputDir.sqrMagnitude > 0.05f ? moveInputDir : transform.forward;
                LaunchDive(dDir, bonusUpVel: grounded ? 4.4f : 2.8f, speedBoost: grounded ? 10.6f : 11.2f);
            }
        }

        // 10. Air Ground Pound (`C` / Ctrl / Shift in mid-air)
        if (!grounded && _carriedObject == null && ReadGroundPoundPressed() && !_isGroundPounding && !_isDiving && !_isSlideKicking)
        {
            StartGroundPound();
        }

        // 11. Jump & Acrobatics Execution
        var canCoyoteJump = (Time.time - _lastGroundedTime) <= coyoteTimeSeconds;
        var hasBufferedJump = (Time.time - _lastJumpPressedTime) <= jumpBufferSeconds;
        if (hasBufferedJump)
        {
            if (_isBellySliding || _isSlideKicking || _isSlopeSliding || _isRolling)
            {
                // Dive / Slide / Slope / Roll -> Long-Rollout Hop!
                var wasRollJump = _isRolling;
                _lastJumpPressedTime = -10f;
                _isBellySliding = false;
                _isSlideKicking = false;
                _isSlopeSliding = false;
                _isRolling = false;
                _isDiving = false;
                _isLongJumping = wasRollJump;
                _jumpCutApplied = false;
                CurrentVerticalSpeed = Mathf.Sqrt(2f * Mathf.Abs(gravity) * (wasRollJump ? 2.25f : 1.95f));
                _planarVelocity = transform.forward * Mathf.Max(_planarVelocity.magnitude * (wasRollJump ? 0.96f : 0.92f), wasRollJump ? 12.2f : 7.6f);
                JumpComboStage = 1;
                if (wasRollJump) RecordMoveStat("LongJump");
                if (motion != null) motion.TriggerJumpStretch(2);
                if (GameAudio.Instance != null) GameAudio.Instance.PlayJumpSound(2);
                if (VFXPool.Instance != null) VFXPool.Instance.SpawnFootstepPuff(transform.position);
            }
            else if (canCoyoteJump && !_isGroundPounding)
            {
                _lastJumpPressedTime = -10f;
                ExecuteJump(moveInputDir);
            }
            else if (!grounded && _carriedObject == null && (Time.time - _lastWallTouchTime) <= 0.22f && !_isGroundPounding)
            {
                _lastJumpPressedTime = -10f;
                ExecuteWallKick();
            }
        }

        // Odyssey Variable Jump Height: releasing Space while rising cuts upward lift crisply!
        if (!jumpHeld && !_jumpCutApplied && CurrentVerticalSpeed > 3.2f && !IsJumpUncuttable())
        {
            _jumpCutApplied = true;
            CurrentVerticalSpeed *= 0.56f;
        }

        // 12. Planar Physics + Steering + Skid Detection
        if (_isGroundPounding)
        {
            UpdateGroundPoundPhysics(dt);
        }
        else
        {
            UpdatePlanarLocomotion(grounded, moveInputDir, moveMagnitude, dt);
            UpdateVerticalPhysics(grounded, dt);
        }

        // 13. Wall Slide disabled (SM64 uses direct Wall Kick on impact instead of slow wall sliding)
        _isWallSliding = false;

        // 14. Execute CharacterController Move
        if (_cc == null || !_cc.enabled) return;
        var totalMotion = (_planarVelocity + Vector3.up * CurrentVerticalSpeed) * dt;
        var flags = _cc.Move(totalMotion);

        if ((flags & CollisionFlags.Above) != 0 && CurrentVerticalSpeed > 0f)
        {
            CurrentVerticalSpeed = 0f;
        }
        if ((flags & CollisionFlags.Below) != 0)
        {
            if (!_wasGrounded || _isGroundPounding)
            {
                OnJustLanded();
            }
            _wasGrounded = true;
        }
        else
        {
            _wasGrounded = false;
            // Try Ledge Grab while falling near platform edge!
            if (CurrentVerticalSpeed < 0.8f && _carriedObject == null && !_isGroundPounding && _ledgeCooldownTimer <= 0f)
            {
                TryCatchLedge(moveInputDir);
            }
        }

        UpdateMotionComponentOnly();
    }

    private void LateUpdate()
    {
        // Camera runs in LateUpdate AFTER CharacterController.Move -> ZERO frame lag!
        var playing = GameManager.Instance == null || GameManager.Instance.currentState == GameState.Playing;
        var dt = Time.deltaTime > 0.0001f ? Time.deltaTime : 0.016f;
        UpdateOdysseyCamera(dt, playing);
    }

    private bool IsJumpUncuttable()
    {
        return _isLongJumping || _isBackflipping || _isSideFlipping || JumpComboStage >= 3;
    }

    // -------------------------------------------------------------------------
    // BOWSER TAIL GRAB & 360° SPIN CONTROL
    // -------------------------------------------------------------------------
    private void HandleBowserSpinControl(float dt)
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        var gp = Gamepad.current;

        var spinInput = 0f;
        if (kb != null)
        {
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) spinInput += 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) spinInput -= 1f;
        }
        if (mouse != null)
        {
            var dx = mouse.delta.ReadValue().x;
            spinInput -= dx * 0.25f;
        }
        if (gp != null)
        {
            spinInput -= gp.leftStick.ReadValue().x;
        }

        var throwBtn = false || mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.leftButton.wasReleasedThisFrame) || kb != null && (kb.fKey.wasPressedThisFrame || kb.jKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame) || gp != null && (gp.buttonWest.wasPressedThisFrame || gp.buttonSouth.wasPressedThisFrame);
        if (debugGrabOrAttackTrigger)
        {
            debugGrabOrAttackTrigger = false;
            throwBtn = true;
        }

        _spinningBowser.FeedSpinInput(spinInput * 360f * dt, throwBtn);

        if (motion == null) return;
        motion.isGrounded = true;
        motion.isSpinningBowser = true;
        motion.normalizedSpeed = 0f;
    }

    // -------------------------------------------------------------------------
    // LEDGE GRAB & VAULT CLIMB
    // -------------------------------------------------------------------------
    private void TryCatchLedge(Vector3 moveDir)
    {
        var checkFwd = moveDir.sqrMagnitude > 0.05f ? moveDir.normalized : transform.forward;
        var chestOrigin = transform.position + Vector3.up * 1.15f;

        if (!Physics.Raycast(chestOrigin, checkFwd, out var wallHit, 0.72f)) return;
        if (wallHit.collider.isTrigger) return;
        if (Mathf.Abs(wallHit.normal.y) > 0.28f) return;

        // Cast down slightly past the wall surface from head height + 0.55m
        var probeTop = wallHit.point + Vector3.up * 0.85f - wallHit.normal * 0.28f;
        if (!Physics.Raycast(probeTop, Vector3.down, out var topHit, 1.05f)) return;
        if (topHit.collider.isTrigger || Vector3.Angle(topHit.normal, Vector3.up) > 28f) return;

        var ledgeHeightAboveFeet = topHit.point.y - transform.position.y;
        if (ledgeHeightAboveFeet < 0.75f || ledgeHeightAboveFeet > 1.72f) return;

        _isLedgeHanging = true;
        _isDiving = false;
        _isBackflipping = false;
        _isSideFlipping = false;
        _isLongJumping = false;
        _isRolling = false;
        _isSpinAttacking = false;
        _planarVelocity = Vector3.zero;
        CurrentVerticalSpeed = 0f;
        _ledgeTopPos = topHit.point;
        _ledgeForward = new Vector3(-wallHit.normal.x, 0f, -wallHit.normal.z).normalized;

        var hangPos = new Vector3(wallHit.point.x + wallHit.normal.x * 0.42f, topHit.point.y - 1.48f, wallHit.point.z + wallHit.normal.z * 0.42f);
        if (_cc != null) _cc.enabled = false;
        transform.position = hangPos;
        if (_ledgeForward.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(_ledgeForward, Vector3.up);
        if (_cc != null) _cc.enabled = true;

        if (GameAudio.Instance != null) GameAudio.Instance.PlayLedgeClimbSound();
    }

    private void HandleLedgeHangState(Vector2 rawInput, bool jumpPressed, bool crouchHeld)
    {
        _planarVelocity = Vector3.zero;
        CurrentVerticalSpeed = 0f;

        var camForward = Quaternion.Euler(0f, CurrentCameraYaw, 0f) * Vector3.forward;
        var camRight = Quaternion.Euler(0f, CurrentCameraYaw, 0f) * Vector3.right;
        var inputWorld = (camForward * rawInput.y + camRight * rawInput.x);
        var pushTowardLedge = inputWorld.magnitude > 0.3f && Vector3.Dot(inputWorld.normalized, _ledgeForward) > 0.35f;

        if (jumpPressed || pushTowardLedge)
        {
            // Vault crisply onto ledge top!
            _isLedgeHanging = false;
            _ledgeCooldownTimer = 0.28f;
            if (_cc != null) _cc.enabled = false;
            transform.position = _ledgeTopPos + _ledgeForward * 0.32f + Vector3.up * 0.06f;
            if (_cc != null) _cc.enabled = true;
            CurrentVerticalSpeed = jumpPressed ? Mathf.Sqrt(2f * Mathf.Abs(gravity) * 1.45f) : 0f;
            _planarVelocity = _ledgeForward * 3.8f;
            RecordMoveStat("LedgeClimb");
            if (GameAudio.Instance != null) GameAudio.Instance.PlayJumpSound(1);
            if (VFXPool.Instance != null) VFXPool.Instance.SpawnFootstepPuff(_ledgeTopPos);
            return;
        }

        if (!crouchHeld && (!(inputWorld.magnitude > 0.3f) ||
                            !(Vector3.Dot(inputWorld.normalized, _ledgeForward) < -0.45f))) return;
        // Drop cleanly down!
        _isLedgeHanging = false;
        _ledgeCooldownTimer = 0.42f;
        CurrentVerticalSpeed = -2.0f;
    }

    // -------------------------------------------------------------------------
    // FIRST-PERSON LOOK, CLIMBABLE POLES & ODYSSEY ROLL / SPIN ATTACK
    // -------------------------------------------------------------------------
    private void ExitFirstPersonLook()
    {
        IsFirstPersonLooking = false;
        SetHeroVisibility(true);
    }

    private bool TryLatchOntoPole(bool grounded, Vector3 moveInputDir, float moveMagnitude)
    {
        if (_poleCooldownTimer > 0f || _carriedObject != null || _isGroundPounding || _isSlopeSliding || _isRolling)
            return false;

        var pos = transform.position;
        var poles = ClimbablePole.ActivePoles;
        for (int i = 0; i < poles.Count; i++)
        {
            var pole = poles[i];
            if (pole == null || !pole.gameObject.activeInHierarchy) continue;
            var center = pole.transform.position;
            var toPoleXZ = new Vector3(center.x - pos.x, 0f, center.z - pos.z);
            var distXZ = toPoleXZ.magnitude;
            if (distXZ > pole.grabRadius) continue;
            if (pos.y < pole.WorldBottomY - 0.15f || pos.y > pole.WorldTopY + 0.15f) continue;

            // Avoid unwanted sticky snap when running past base on flat ground unless pushing directly into pole
            var airborneGrab = !grounded && pos.y > pole.WorldBottomY + 0.2f;
            var pushingIntoPole = moveMagnitude > 0.25f && distXZ > 0.05f && Vector3.Dot(moveInputDir, toPoleXZ / distXZ) > 0.55f;
            if (!airborneGrab && !pushingIntoPole) continue;

            _activePole = pole;
            _isPoleHandstand = false;
            _isDiving = false;
            _isBellySliding = false;
            _isSlideKicking = false;
            _isBackflipping = false;
            _isSideFlipping = false;
            _isLongJumping = false;
            _isSpinAttacking = false;
            _planarVelocity = Vector3.zero;
            CurrentVerticalSpeed = 0f;
            _poleHeight = Mathf.Clamp(pos.y, pole.WorldBottomY + 0.35f, pole.WorldTopY - 0.2f);
            var outward = -toPoleXZ;
            _poleAngleDeg = outward.sqrMagnitude > 0.001f
                ? Mathf.Atan2(outward.x, outward.z) * Mathf.Rad2Deg
                : (transform.eulerAngles.y + 180f);

            PositionHeroOnPole();
            RecordMoveStat("PoleClimb");
            if (GameAudio.Instance != null) GameAudio.Instance.PlayLedgeClimbSound();
            return true;
        }
        return false;
    }

    private void HandlePoleClimbState(Vector2 rawInput, bool jumpPressed, bool crouchHeld, float dt)
    {
        if (_activePole == null || !_activePole.gameObject.activeInHierarchy)
        {
            _activePole = null;
            _isPoleHandstand = false;
            return;
        }

        _planarVelocity = Vector3.zero;
        CurrentVerticalSpeed = 0f;

        var climbInput = rawInput.y;
        var spinInput = rawInput.x;
        if (Mathf.Abs(spinInput) > 0.1f && !_isPoleHandstand)
        {
            _poleAngleDeg += spinInput * 165f * dt;
        }

        if (_isPoleHandstand)
        {
            if (climbInput < -0.25f || crouchHeld)
            {
                _isPoleHandstand = false;
                _poleHeight = _activePole.WorldTopY - 0.35f;
            }
        }
        else
        {
            if (climbInput > 0.15f)
            {
                _poleHeight += climbInput * 4.9f * dt;
                if (_poleHeight >= _activePole.WorldTopY - 0.14f)
                {
                    _poleHeight = _activePole.WorldTopY;
                    _isPoleHandstand = true;
                    RecordMoveStat("PoleHandstand");
                    if (GameAudio.Instance != null) GameAudio.Instance.PlayLedgeClimbSound();
                }
            }
            else if (climbInput < -0.15f)
            {
                _poleHeight += climbInput * 6.4f * dt;
                if (_poleHeight <= _activePole.WorldBottomY + 0.16f)
                {
                    var radOut = Quaternion.Euler(0f, _poleAngleDeg, 0f) * Vector3.forward;
                    if (_cc != null) _cc.enabled = false;
                    transform.position = new Vector3(_activePole.transform.position.x, _activePole.WorldBottomY + 0.05f, _activePole.transform.position.z) + radOut * (_activePole.poleRadius + 0.65f);
                    if (_cc != null) _cc.enabled = true;
                    _activePole = null;
                    _isPoleHandstand = false;
                    _poleCooldownTimer = 0.45f;
                    return;
                }
            }
        }

        if (jumpPressed)
        {
            var kickDir = (Quaternion.Euler(0f, _poleAngleDeg, 0f) * Vector3.forward).normalized;
            var launchH = _isPoleHandstand ? 3.25f : 2.65f;
            if (_cc != null) _cc.enabled = false;
            transform.position = _activePole.GetSurfacePoint(_poleHeight, kickDir) + kickDir * 0.32f;
            transform.rotation = Quaternion.LookRotation(kickDir, Vector3.up);
            if (_cc != null) _cc.enabled = true;

            _activePole = null;
            _isPoleHandstand = false;
            _poleCooldownTimer = 0.45f;
            _jumpCutApplied = false;
            JumpComboStage = 2;
            CurrentVerticalSpeed = Mathf.Sqrt(2f * Mathf.Abs(gravity) * launchH);
            _planarVelocity = kickDir * 8.2f;
            if (motion != null) motion.TriggerJumpStretch(2);
            if (GameAudio.Instance != null)
            {
                GameAudio.Instance.PlayWallKickSound();
                GameAudio.Instance.PlayJumpSound(2);
            }
            if (VFXPool.Instance != null) VFXPool.Instance.SpawnFootstepPuff(transform.position);
            return;
        }

        PositionHeroOnPole();
    }

    private void PositionHeroOnPole()
    {
        if (_activePole == null) return;
        var outward = (Quaternion.Euler(0f, _poleAngleDeg, 0f) * Vector3.forward).normalized;
        if (_cc != null) _cc.enabled = false;
        if (_isPoleHandstand)
        {
            var c = _activePole.transform.position;
            transform.position = new Vector3(c.x, _activePole.WorldTopY + 0.08f, c.z);
            transform.rotation = Quaternion.LookRotation(-outward, Vector3.up);
        }
        else
        {
            transform.position = _activePole.GetSurfacePoint(_poleHeight, outward);
            transform.rotation = Quaternion.LookRotation(-outward, Vector3.up);
        }
        if (_cc != null) _cc.enabled = true;
    }

    private void StartOdysseyRoll(Vector3 dir)
    {
        var rollDir = dir.sqrMagnitude > 0.05f ? new Vector3(dir.x, 0f, dir.z).normalized : transform.forward;
        _isRolling = true;
        _rollTimer = 0.68f;
        IsCrouching = false;
        _isDiving = false;
        _isBellySliding = false;
        _isSlideKicking = false;
        _groundPoundImpactTimer = 0f;
        _attackComboStage = 0;

        _planarVelocity = rollDir * Mathf.Max(_planarVelocity.magnitude, 14.5f);
        transform.rotation = Quaternion.LookRotation(rollDir, Vector3.up);
        RecordMoveStat("OdysseyRoll");
        if (motion != null) motion.TriggerJumpStretch();
        if (GameAudio.Instance != null) GameAudio.Instance.PlaySkidSound();
        if (VFXPool.Instance != null) VFXPool.Instance.SpawnFootstepPuff(transform.position + Vector3.up * 0.25f);
        CheckMeleeHitbox(transform.position + rollDir * 0.85f + Vector3.up * 0.55f, 1.35f);
    }

    private void TriggerSpinAttack(bool grounded, Vector3 moveDir)
    {
        _isSpinAttacking = true;
        _spinTimer = 0.36f;
        _spinCooldownTimer = 0.50f;
        _attackComboStage = 7;
        _attackDuration = 0.36f;
        _attackTimer = 0.36f;
        _attackHitRegistered = false;

        if (!grounded && !_usedAirSpinHover)
        {
            _usedAirSpinHover = true;
            CurrentVerticalSpeed = Mathf.Max(CurrentVerticalSpeed, 4.6f);
        }
        if (moveDir.sqrMagnitude > 0.05f)
        {
            _planarVelocity = Vector3.MoveTowards(_planarVelocity, moveDir * maxRunSpeed, 3.5f);
        }

        RecordMoveStat("SpinAttack");
        if (motion != null) motion.TriggerJumpStretch();
        if (GameAudio.Instance != null) GameAudio.Instance.PlayJumpSound(1);
        if (VFXPool.Instance != null) VFXPool.Instance.SpawnPickupBurst(transform.position + Vector3.up * 0.6f, isStar: false);
        CheckMeleeHitbox(transform.position + Vector3.up * 0.65f, 2.25f);
    }

    private bool TryPickUpNearbyCarriable()
    {
        if (_carriedObject != null) return false;
        CarriableObject best = null;
        var bestDist = 1.85f;
        var chestPos = transform.position + transform.forward * 0.65f + Vector3.up * 0.5f;
        foreach (var carriable in FindObjectsByType<CarriableObject>(FindObjectsInactive.Exclude))
        {
            if (carriable == null || !carriable.CanBePickedUp) continue;
            var d = Vector3.Distance(chestPos, carriable.transform.position + Vector3.up * 0.35f);
            if (d < bestDist)
            {
                bestDist = d;
                best = carriable;
            }
        }
        if (best != null)
        {
            _carriedObject = best;
            _carriedObject.PickUp(transform);
            _attackComboStage = 0;
            return true;
        }
        return false;
    }

    // -------------------------------------------------------------------------
    // ODYSSEY FREE 360° CAMERA (LateUpdate)
    // -------------------------------------------------------------------------
    private void UpdateOdysseyCamera(float dt, bool playing)
    {
        ResolveCinemachineComponents();

        var yawBefore = CurrentCameraYaw;

        if (playing)
        {
            var mouse = Mouse.current;
            var kb = Keyboard.current;
            var gp = Gamepad.current;

            var altHeld = kb != null && (kb.leftAltKey.isPressed || kb.rightAltKey.isPressed);
            if (altHeld && _cursorLockedMode)
            {
                ToggleCursorLockMode(false);
            }

            if (debugOrbitDelta.sqrMagnitude > 0.0001f)
            {
                CurrentCameraYaw += debugOrbitDelta.x;
                _cameraPitch = Mathf.Clamp(_cameraPitch + debugOrbitDelta.y, IsFirstPersonLooking ? -68f : minCameraPitch, IsFirstPersonLooking ? 72f : maxCameraPitch);
                debugOrbitDelta = Vector2.zero;
                _manualOrbitCooldown = 1.45f;
                _isSnappingCamera = false;
            }

            if (mouse != null)
            {
                var overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

                // Toggle Cursor Free-Mouselook with Tab or F9, OR click GameView cleanly!
                if (kb != null && (kb.tabKey.wasPressedThisFrame || kb.f9Key.wasPressedThisFrame))
                {
                    ToggleCursorLockMode(!_cursorLockedMode);
                }
                else if (autoLockCursorOnPlay && !_cursorLockedMode && !altHeld && !overUi && !Application.isBatchMode
                         && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame))
                {
                    ToggleCursorLockMode(true);
                }

                // Mouse always orbits smoothly when locked OR right-dragged anywhere off UI!
                var rightHeld = mouse.rightButton.isPressed;
                var delta = mouse.delta.ReadValue();
                if ((_cursorLockedMode && Cursor.lockState == CursorLockMode.Locked) || (rightHeld && !overUi))
                {
                    if (delta.sqrMagnitude > 0.01f)
                    {
                        CurrentCameraYaw += delta.x * mouseSensitivity;
                        _cameraPitch = Mathf.Clamp(_cameraPitch - delta.y * mouseSensitivity, IsFirstPersonLooking ? -68f : minCameraPitch, IsFirstPersonLooking ? 72f : maxCameraPitch);
                        _manualOrbitCooldown = 1.45f;
                        _isSnappingCamera = false;
                    }
                }

                if (mouse.middleButton.wasPressedThisFrame && !IsFirstPersonLooking)
                {
                    SnapCameraBehindMario();
                }

                var scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f && !IsFirstPersonLooking)
                {
                    cameraDistance = Mathf.Clamp(cameraDistance - Mathf.Sign(scroll) * 0.72f, 2.4f, 14.0f);
                }
            }

            if (kb != null)
            {
                var yawDelta = 0f;
                if (kb.leftArrowKey.isPressed) yawDelta -= 1f;
                if (kb.rightArrowKey.isPressed) yawDelta += 1f;
                if (Mathf.Abs(yawDelta) > 0.01f)
                {
                    CurrentCameraYaw += yawDelta * keyboardOrbitSpeed * dt;
                    _manualOrbitCooldown = 1.45f;
                    _isSnappingCamera = false;
                }

                var pitchDelta = 0f;
                if (kb.upArrowKey.isPressed) pitchDelta += 1f;
                if (kb.downArrowKey.isPressed) pitchDelta -= 1f;
                if (Mathf.Abs(pitchDelta) > 0.01f)
                {
                    _cameraPitch = Mathf.Clamp(_cameraPitch + pitchDelta * (keyboardOrbitSpeed * 0.65f) * dt, IsFirstPersonLooking ? -68f : minCameraPitch, IsFirstPersonLooking ? 72f : maxCameraPitch);
                    _manualOrbitCooldown = 1.45f;
                }

                if (kb.rKey.wasPressedThisFrame && !IsFirstPersonLooking)
                {
                    SnapCameraBehindMario();
                }
            }

            if (gp != null)
            {
                var rStick = gp.rightStick.ReadValue();
                if (rStick.sqrMagnitude > 0.02f)
                {
                    CurrentCameraYaw += rStick.x * keyboardOrbitSpeed * 1.25f * dt;
                    _cameraPitch = Mathf.Clamp(_cameraPitch - rStick.y * (keyboardOrbitSpeed * 0.7f) * dt, IsFirstPersonLooking ? -68f : minCameraPitch, IsFirstPersonLooking ? 72f : maxCameraPitch);
                    _manualOrbitCooldown = 1.45f;
                    _isSnappingCamera = false;
                }
                if ((gp.rightStickButton.wasPressedThisFrame || gp.leftTrigger.wasPressedThisFrame) && !IsFirstPersonLooking)
                {
                    SnapCameraBehindMario();
                }
            }
        }

        if (Mathf.Abs(Mathf.DeltaAngle(yawBefore, CurrentCameraYaw)) > 0.4f)
        {
            RecordMoveStat("FreeCameraOrbit");
        }

        if (_manualOrbitCooldown > 0f)
        {
            _manualOrbitCooldown -= dt;
        }

        if (_isSnappingCamera)
        {
            CurrentCameraYaw = Mathf.LerpAngle(CurrentCameraYaw, _snapTargetYaw, 1f - Mathf.Exp(-14f * dt));
            _cameraPitch = Mathf.Lerp(_cameraPitch, 18f, 1f - Mathf.Exp(-12f * dt));
            if (Mathf.Abs(Mathf.DeltaAngle(CurrentCameraYaw, _snapTargetYaw)) < 0.8f) _isSnappingCamera = false;
        }
        else if (playing && !IsFirstPersonLooking && _manualOrbitCooldown <= 0f && autoFollowYawStrength > 0.01f)
        {
            var camRight = Quaternion.Euler(0f, CurrentCameraYaw, 0f) * Vector3.right;
            var lateralSpeed = Vector3.Dot(_planarVelocity, camRight);
            if (Mathf.Abs(lateralSpeed) > 1.8f)
            {
                CurrentCameraYaw += (lateralSpeed / maxRunSpeed) * (autoFollowYawStrength * 42f) * dt;
            }
        }

        // Smooth Odyssey Height Cushioning on CameraYawProxy (prevents horizon bob on hops!)
        var targetY = transform.position.y + (IsFirstPersonLooking ? 1.12f : (_cc.isGrounded ? 1.12f : 1.02f));
        var yTrackSpeed = _cc.isGrounded ? 14f : (CurrentVerticalSpeed < -5f ? 18f : 7.5f);
        _smoothedCamY = Mathf.Lerp(_smoothedCamY, targetY, 1f - Mathf.Exp(-yTrackSpeed * dt));
        if (cameraYawProxy != null)
        {
            var nextProxyPos = new Vector3(transform.position.x, _smoothedCamY, transform.position.z);
            if ((cameraYawProxy.position - nextProxyPos).sqrMagnitude > 36f && _cmCam != null)
            {
                _cmCam.PreviousStateIsValid = false;
            }
            cameraYawProxy.position = nextProxyPos;
        }

        if (_cmFollow != null)
        {
            if (IsFirstPersonLooking)
            {
                _cmFollow.CameraDistance = Mathf.Lerp(_cmFollow.CameraDistance, 0.08f, 18f * dt);
                _cmFollow.ShoulderOffset = Vector3.Lerp(_cmFollow.ShoulderOffset, new Vector3(0f, 0.34f, 0.16f), 18f * dt);
            }
            else
            {
                var spinBoost = IsSpinningBowser ? 2.4f : 0f;
                var speedZoom = Mathf.Clamp01(_planarVelocity.magnitude / maxRunSpeed) * 0.55f;
                _cmFollow.CameraDistance = Mathf.Lerp(_cmFollow.CameraDistance, cameraDistance + spinBoost + speedZoom, 10f * dt);
                _cmFollow.ShoulderOffset = Vector3.Lerp(_cmFollow.ShoulderOffset, new Vector3(0f, 0.44f, 0f), 12f * dt);
            }
        }

        if (_cmCam != null)
        {
            var speedRatio = Mathf.Clamp01(_planarVelocity.magnitude / (maxRunSpeed * 1.4f));
            var targetFov = IsFirstPersonLooking
                ? (_baseCameraFov - 8f)
                : (_baseCameraFov + speedRatio * 5.5f + (_isDiving || _isLongJumping || _isRolling ? 4.5f : 0f) + (IsSpinningBowser ? 5f : 0f));
            var lens = _cmCam.Lens;
            lens.FieldOfView = Mathf.Lerp(lens.FieldOfView, targetFov, 9f * dt);
            _cmCam.Lens = lens;
        }

        ApplyProxyRotationImmediate();

        // Fallback when dropped into a new scene without CinemachineCamera ("Gameplay"):
        // drive Camera.main directly from cameraYawProxy so the prefab works plug-and-play!
        if (_cmFollow == null && cameraYawProxy != null)
        {
            var mainCam = Camera.main;
            if (mainCam != null)
            {
                var spinBoost = IsSpinningBowser ? 2.4f : 0f;
                var speedZoom = Mathf.Clamp01(_planarVelocity.magnitude / maxRunSpeed) * 0.55f;
                var targetDistance = IsFirstPersonLooking ? 0.08f : cameraDistance + spinBoost + speedZoom;
                var shoulderOffset = IsFirstPersonLooking ? new Vector3(0f, 0.34f, 0.16f) : new Vector3(0f, 0.44f, 0f);

                var camRot = cameraYawProxy.rotation;
                var pivot = cameraYawProxy.position + camRot * (shoulderOffset + Vector3.up * 0.22f);
                var backDir = -(camRot * Vector3.forward);
                var finalDistance = targetDistance;

                if (!IsFirstPersonLooking && Physics.SphereCast(pivot, 0.22f, backDir, out var hit, targetDistance, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (!hit.transform.IsChildOf(transform) && hit.transform != transform)
                    {
                        finalDistance = Mathf.Max(0.45f, hit.distance - 0.1f);
                    }
                }

                mainCam.transform.SetPositionAndRotation(pivot + backDir * finalDistance, camRot);

                var speedRatio = Mathf.Clamp01(_planarVelocity.magnitude / (maxRunSpeed * 1.4f));
                var targetFov = IsFirstPersonLooking
                    ? (_baseCameraFov - 8f)
                    : (_baseCameraFov + speedRatio * 5.5f + (_isDiving || _isLongJumping || _isRolling ? 4.5f : 0f) + (IsSpinningBowser ? 5f : 0f));
                mainCam.fieldOfView = Mathf.Lerp(mainCam.fieldOfView, targetFov, 9f * dt);
            }
        }
    }

    private void ToggleCursorLockMode(bool lockCursor)
    {
        _cursorLockedMode = lockCursor;
        Cursor.lockState = lockCursor ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !lockCursor;
    }

    private void SnapCameraBehindMario()
    {
        _snapTargetYaw = transform.eulerAngles.y;
        _isSnappingCamera = true;
        _manualOrbitCooldown = 0.8f;
    }

    private void ApplyProxyRotationImmediate()
    {
        if (cameraYawProxy != null)
        {
            cameraYawProxy.rotation = Quaternion.Euler(_cameraPitch, CurrentCameraYaw, 0f);
        }
    }

    // -------------------------------------------------------------------------
    // JUMP & ACROBATICS
    // -------------------------------------------------------------------------
    private void ExecuteJump(Vector3 moveInputDir)
    {
        _isDiving = false;
        _isBellySliding = false;
        _isSlideKicking = false;
        _isBackflipping = false;
        _isSideFlipping = false;
        _isLongJumping = false;
        _isRolling = false;
        _isSlopeSliding = false;
        IsCrouching = false;
        _attackComboStage = 0;
        _jumpCutApplied = false;

        var planarSpeed = _planarVelocity.magnitude;

        // 1. Long Jump (Crouch held while moving > 2.2 m/s)
        if (_carriedObject == null && ReadCrouchHeld() && planarSpeed > 2.2f)
        {
            JumpComboStage = 1;
            _isLongJumping = true;
            CurrentVerticalSpeed = Mathf.Sqrt(2f * Mathf.Abs(gravity) * longJumpHeight);
            var jumpDir = moveInputDir.sqrMagnitude > 0.05f ? moveInputDir : transform.forward;
            _planarVelocity = jumpDir * (maxRunSpeed * longJumpSpeedMultiplier);
            transform.rotation = Quaternion.LookRotation(jumpDir, Vector3.up);

            RecordMoveStat("LongJump");
            if (motion != null) motion.TriggerJumpStretch(2);
            if (GameAudio.Instance != null) GameAudio.Instance.PlayJumpSound(2);
            if (VFXPool.Instance != null) VFXPool.Instance.SpawnFootstepPuff(transform.position);
            return;
        }

        // 2. Backflip (Crouch held while stationary/slow)
        if (_carriedObject == null && ReadCrouchHeld())
        {
            JumpComboStage = 1;
            _isBackflipping = true;
            CurrentVerticalSpeed = Mathf.Sqrt(2f * Mathf.Abs(gravity) * backflipHeight);
            _planarVelocity = -transform.forward * 2.9f;

            RecordMoveStat("Backflip");
            if (motion != null) motion.TriggerJumpStretch(3);
            if (GameAudio.Instance != null) GameAudio.Instance.PlayJumpSound(3);
            if (VFXPool.Instance != null) VFXPool.Instance.SpawnFootstepPuff(transform.position);
            return;
        }

        // 3. Side Somersault (Jump during skid reversal window!)
        if (_carriedObject == null && _skidReversalTimer > 0f && moveInputDir.sqrMagnitude > 0.05f)
        {
            JumpComboStage = 1;
            _isSideFlipping = true;
            _skidReversalTimer = 0f;
            CurrentVerticalSpeed = Mathf.Sqrt(2f * Mathf.Abs(gravity) * sideFlipHeight);
            _planarVelocity = moveInputDir * 5.6f;
            transform.rotation = Quaternion.LookRotation(moveInputDir, Vector3.up);

            RecordMoveStat("SideFlip");
            if (motion != null) motion.TriggerJumpStretch(3);
            if (GameAudio.Instance != null) GameAudio.Instance.PlayJumpSound(3);
            if (VFXPool.Instance != null) VFXPool.Instance.SpawnFootstepPuff(transform.position);
            return;
        }

        // 4. Ground-Pound Impact Boost Jump!
        if (_carriedObject == null && _groundPoundImpactTimer > 0f)
        {
            _groundPoundImpactTimer = 0f;
            JumpComboStage = 2;
            CurrentVerticalSpeed = Mathf.Sqrt(2f * Mathf.Abs(gravity) * 3.15f);
            RecordMoveStat("DoubleJump");
            if (motion != null) motion.TriggerJumpStretch(2);
            if (GameAudio.Instance != null) GameAudio.Instance.PlayJumpSound(2);
            if (VFXPool.Instance != null) VFXPool.Instance.SpawnFootstepPuff(transform.position);
            return;
        }

        // 5. Classic Single -> Double -> Triple Somersault Progression
        if (_carriedObject == null && _landedTimer <= comboWindowSeconds && JumpComboStage > 0)
        {
            JumpComboStage = (JumpComboStage == 2 && planarSpeed > 4.2f) ? 3 : 2;
        }
        else
        {
            JumpComboStage = 1;
        }

        var jumpH = JumpComboStage switch
        {
            3 => tripleJumpHeight,
            2 => doubleJumpHeight,
            _ => singleJumpHeight
        };
        CurrentVerticalSpeed = Mathf.Sqrt(2f * Mathf.Abs(gravity) * jumpH);

        switch (JumpComboStage)
        {
            case 3:
                RecordMoveStat("TripleJump");
                break;
            case 2:
                RecordMoveStat("DoubleJump");
                break;
            default:
                RecordMoveStat("SingleJump");
                break;
        }

        if (JumpComboStage == 3 && moveInputDir.sqrMagnitude > 0.05f)
        {
            _planarVelocity = Vector3.Max(_planarVelocity, moveInputDir * (maxRunSpeed * 1.12f));
        }

        if (motion != null) motion.TriggerJumpStretch(JumpComboStage);
        if (GameAudio.Instance != null) GameAudio.Instance.PlayJumpSound(JumpComboStage);
        if (VFXPool.Instance != null) VFXPool.Instance.SpawnFootstepPuff(transform.position);
    }

    private void ExecuteWallKick()
    {
        var kickOut = _lastWallNormal;
        kickOut.y = 0f;
        if (kickOut.sqrMagnitude < 0.01f) kickOut = -transform.forward;
        kickOut.Normalize();

        _isDiving = false;
        _isBackflipping = false;
        _isSideFlipping = false;
        _isLongJumping = false;
        _isWallSliding = false;
        _isRolling = false;
        _jumpCutApplied = false;
        JumpComboStage = 2;

        CurrentVerticalSpeed = Mathf.Sqrt(2f * Mathf.Abs(gravity) * 3.05f);
        _planarVelocity = kickOut * 9.0f;
        _wallKickSteerLockTimer = 0.16f;
        _wallKickSteerDir = kickOut;
        transform.rotation = Quaternion.LookRotation(kickOut, Vector3.up);
        _lastWallTouchTime = -10f;

        RecordMoveStat("WallKick");
        if (motion != null) motion.TriggerJumpStretch(2);
        if (GameAudio.Instance != null)
        {
            GameAudio.Instance.PlayWallKickSound();
            GameAudio.Instance.PlayJumpSound(2);
        }
        if (VFXPool.Instance != null) VFXPool.Instance.SpawnFootstepPuff(transform.position + Vector3.up * 0.7f);
    }

    private void LaunchDive(Vector3 dir, float bonusUpVel, float speedBoost)
    {
        _isDiving = true;
        _isBellySliding = false;
        _isSlideKicking = false;
        _isBackflipping = false;
        _isSideFlipping = false;
        _isLongJumping = false;
        _isRolling = false;
        _isSlopeSliding = false;
        _attackComboStage = 0;
        _jumpCutApplied = true;

        var diveDir = dir.sqrMagnitude > 0.05f ? dir.normalized : transform.forward;
        _planarVelocity = diveDir * Mathf.Max(_planarVelocity.magnitude, speedBoost);
        CurrentVerticalSpeed = Mathf.Max(CurrentVerticalSpeed, bonusUpVel);
        transform.rotation = Quaternion.LookRotation(diveDir, Vector3.up);

        RecordMoveStat("DiveBellySlide");
        if (motion != null) motion.TriggerJumpStretch();
        if (GameAudio.Instance != null) GameAudio.Instance.PlayJumpSound(2);
        if (VFXPool.Instance != null) VFXPool.Instance.SpawnFootstepPuff(transform.position + Vector3.up * 0.35f);

        CheckMeleeHitbox(transform.position + diveDir * 0.9f + Vector3.up * 0.7f, 1.35f);
    }

    private void StartGroundPound()
    {
        _isGroundPounding = true;
        _isDiving = false;
        _isBellySliding = false;
        _isSlideKicking = false;
        _isBackflipping = false;
        _isSideFlipping = false;
        _isLongJumping = false;
        _isRolling = false;
        _isSpinAttacking = false;
        _attackComboStage = 0;
        _groundPoundStallTimer = 0.22f;
        _planarVelocity = Vector3.zero;
        CurrentVerticalSpeed = 0f;

        RecordMoveStat("GroundPound");
        if (GameAudio.Instance != null) GameAudio.Instance.PlayJumpSound(1);
    }

    private void UpdateGroundPoundPhysics(float dt)
    {
        _planarVelocity = Vector3.zero;
        if (_groundPoundStallTimer > 0f)
        {
            _groundPoundStallTimer -= dt;
            CurrentVerticalSpeed = 0f;
        }
        else
        {
            CurrentVerticalSpeed = -27.5f;
        }
    }

    // -------------------------------------------------------------------------
    // COMBAT: 3-HIT PUNCH/KICK, SWEEP KICK, SLIDE KICK & BOWSER TAIL GRAB
    // -------------------------------------------------------------------------
    private void TriggerMarioAttack(bool grounded, Vector3 moveDir)
    {
        // 0. If carrying an object, Throw it crisply!
        if (_carriedObject != null)
        {
            var throwDir = moveDir.sqrMagnitude > 0.05f ? moveDir : transform.forward;
            throwDir.y = 0f;
            if (throwDir.sqrMagnitude < 0.001f) throwDir = transform.forward;
            throwDir.Normalize();
            transform.rotation = Quaternion.LookRotation(throwDir, Vector3.up);
            _carriedObject.ThrowForward(transform.position + throwDir * 0.95f + Vector3.up * 1.25f, throwDir);
            _carriedObject = null;
            _attackComboStage = 8;
            _attackDuration = 0.28f;
            _attackTimer = 0.28f;
            _attackHitRegistered = true;
            RecordMoveStat("CarryThrow");
            return;
        }

        // 1. Highest priority: check if behind Bowser to grab his Tail!
        if (TryGrabNearbyBowserTail()) return;

        if (grounded)
        {
            // 1b. Check SM64 Carriable Object pickup in front of Mario (Crate, Throwable Bob-omb, Rock)!
            if (!IsCrouching && _attackComboStage == 0 && TryPickUpNearbyCarriable())
            {
                return;
            }

            if (IsCrouching)
            {
                if (_planarVelocity.magnitude > 2.8f)
                {
                    // Slide Kick!
                    _isSlideKicking = true;
                    _isBellySliding = true;
                    _planarVelocity = (moveDir.sqrMagnitude > 0.05f ? moveDir : transform.forward) * 10.8f;
                    transform.rotation = Quaternion.LookRotation(_planarVelocity.normalized, Vector3.up);
                    _attackComboStage = 6;
                    _attackTimer = 0.42f;
                    _attackDuration = 0.42f;
                    _attackHitRegistered = false;
                    RecordMoveStat("SlideKick");
                    if (GameAudio.Instance != null) GameAudio.Instance.PlayJumpSound(2);
                    return;
                }

                // Stationary Crouch + Attack -> Sweep Kick!
                _attackComboStage = 5;
                _attackTimer = 0.34f;
                _attackDuration = 0.34f;
                _attackHitRegistered = false;
                RecordMoveStat("PunchKickCombo");
                if (GameAudio.Instance != null) GameAudio.Instance.PlayJumpSound(1);
                return;
            }

            // 3-Hit Punch -> Punch -> Roundhouse Kick!
            _attackComboStage = (_attackChainWindowTimer > 0f && _attackComboStage is > 0 and < 3)
                ? _attackComboStage + 1
                : 1;
            _attackDuration = _attackComboStage == 3 ? 0.32f : 0.24f;
            _attackTimer = _attackDuration;
            _attackChainWindowTimer = 0.42f;
            _attackHitRegistered = false;

            var lungeDir = moveDir.sqrMagnitude > 0.05f ? moveDir : transform.forward;
            _planarVelocity = lungeDir * (_attackComboStage == 3 ? 6.8f : 4.2f);
            transform.rotation = Quaternion.LookRotation(lungeDir, Vector3.up);
            if (_attackComboStage == 3) CurrentVerticalSpeed = Mathf.Max(CurrentVerticalSpeed, 3.2f);

            if (_attackComboStage >= 2) RecordMoveStat("PunchKickCombo");
            if (GameAudio.Instance != null) GameAudio.Instance.PlayJumpSound(_attackComboStage);
        }
        else
        {
            // Air Jump Kick (at most once per jump so spamming kick cannot fly upward)
            if (_usedAirKick || _attackTimer > 0f) return;
            _usedAirKick = true;
            _attackComboStage = 4;
            _attackDuration = 0.34f;
            _attackTimer = 0.34f;
            _attackHitRegistered = false;
            CurrentVerticalSpeed = Mathf.Max(CurrentVerticalSpeed, 3.8f);
            if (moveDir.sqrMagnitude > 0.05f)
            {
                _planarVelocity = moveDir * Mathf.Max(_planarVelocity.magnitude, 6.2f);
                transform.rotation = Quaternion.LookRotation(moveDir, Vector3.up);
            }
            RecordMoveStat("AirKick");
            if (GameAudio.Instance != null) GameAudio.Instance.PlayJumpSound(2);
        }
    }

    private bool TryGrabNearbyBowserTail()
    {
        foreach (var bowser in FindObjectsByType<BowserBossController>(FindObjectsInactive.Exclude))
        {
            if (bowser == null || !bowser.gameObject.activeInHierarchy) continue;
            if (!bowser.TryGrabTail(this)) continue;
            _spinningBowser = bowser;
            _planarVelocity = Vector3.zero;
            CurrentVerticalSpeed = 0f;
            _isDiving = false;
            _isBellySliding = false;
            _isSlideKicking = false;
            _isRolling = false;
            _isSpinAttacking = false;
            _attackComboStage = 0;
            return true;
        }
        return false;
    }

    private void UpdateAttackState(float dt)
    {
        if (_attackChainWindowTimer > 0f) _attackChainWindowTimer -= dt;
        if (_attackComboStage <= 0) return;

        _attackTimer -= dt;
        if (!_attackHitRegistered && _attackTimer <= _attackDuration * 0.72f)
        {
            _attackHitRegistered = true;
            var radius = _attackComboStage == 5 || _attackComboStage == 7 ? 2.1f : (_attackComboStage >= 3 ? 1.65f : 1.45f);
            var hitCenter = (_attackComboStage == 5 || _attackComboStage == 7)
                ? transform.position + Vector3.up * 0.45f
                : transform.position + transform.forward * 1.05f + Vector3.up * 0.75f;
            CheckMeleeHitbox(hitCenter, radius);
        }

        if (!(_attackTimer <= 0f)) return;
        _attackComboStage = 0;
        _isSlideKicking = false;
    }

    private void CheckMeleeHitbox(Vector3 center, float radius)
    {
        // Try Bowser Tail Grab even at Dive/Slide apex if behind Bowser!
        TryGrabNearbyBowserTail();

        var hits = Physics.OverlapSphere(center, radius);
        foreach (var col in hits)
        {
            if (col == null) continue;

            var crate = col.GetComponentInParent<DestructibleCrate>();
            if (crate != null)
            {
                crate.Smash(center);
                continue;
            }

            var goomba = col.GetComponentInParent<GoombaPatrol>();
            if (goomba != null)
            {
                goomba.OnAttacked();
                continue;
            }

            var bobomb = col.GetComponentInParent<BobOmbPatrol>();
            if (bobomb != null)
            {
                bobomb.OnMarioAttackHit();
                continue;
            }

            var bfElem = col.GetComponentInParent<BobOmbBattlefieldController>();
            if (bfElem != null)
            {
                bfElem.RegisterHitOnElement();
                continue;
            }

            var whomp = col.GetComponentInParent<WhompBossController>();
            if (whomp != null && IsGroundPoundingActive)
            {
                whomp.ForceGroundPoundHitFromMario();
                continue;
            }

            var elem = col.GetComponentInParent<WhompFortressElement>();
            if (elem != null)
            {
                switch (elem.hazardType)
                {
                    case FortressHazardType.KickableBoard:
                        elem.KnockDownImmediate();
                        break;
                    case FortressHazardType.BreakawayBrickWall:
                        elem.ShatterBreakawayWall();
                        break;
                    case FortressHazardType.SleepingPiranha:
                        elem.DefeatSleepingPiranha();
                        break;
                    case FortressHazardType.ShoveBlock:
                    case FortressHazardType.ThwompCrusher:
                    case FortressHazardType.RotatingBridge:
                    case FortressHazardType.TiltingPlank:
                    case FortressHazardType.FloatingIsle:
                    case FortressHazardType.SkyCannonLauncher:
                    case FortressHazardType.ElevatorLift:
                    case FortressHazardType.HootOwlCarry:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                continue;
            }

            var jrbElem = col.GetComponentInParent<JollyRogerBayController>();
            if (jrbElem != null && jrbElem.elementType == JollyRogerElementType.TreasureChestPuzzleMember)
            {
                jrbElem.InteractWithChest(this);
                continue;
            }

            var dwElem = col.GetComponentInParent<BowserDarkWorldElement>();
            if (dwElem != null && dwElem.hazardType == DarkWorldHazardType.PurpleSwitchCrystal)
            {
                dwElem.ActivateSwitch();
            }
        }
    }

    private void UpdatePlanarLocomotion(bool grounded, Vector3 moveInputDir, float moveMagnitude, float dt)
    {
        if (_isRolling)
        {
            // Sustain high-speed Odyssey Roll with gentle directional steering!
            if (moveMagnitude > 0.1f && moveInputDir.sqrMagnitude > 0.01f)
            {
                var desiredRot = Quaternion.LookRotation(moveInputDir, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, 1f - Mathf.Exp(-(turnSmoothSpeed * 0.45f) * dt));
            }
            var targetRollSpeed = Mathf.Max(12.8f, _planarVelocity.magnitude - 1.5f * dt);
            _planarVelocity = transform.forward * targetRollSpeed;
            return;
        }

        if (_isBellySliding || _isSlideKicking || _isSlopeSliding)
        {
            if (_isSlopeSliding && moveMagnitude > 0.1f && moveInputDir.sqrMagnitude > 0.01f)
            {
                _planarVelocity += moveInputDir * (6.5f * dt);
            }
            var slideDrag = _isSlopeSliding ? 1.1f : (_isSlideKicking ? 3.2f : 4.2f);
            _planarVelocity = Vector3.MoveTowards(_planarVelocity, Vector3.zero, slideDrag * dt);
            if (_planarVelocity.sqrMagnitude > 0.25f)
            {
                var slideFace = new Vector3(_planarVelocity.x, 0f, _planarVelocity.z).normalized;
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(slideFace, Vector3.up), 10f * dt);
            }

            if (!(_planarVelocity.magnitude < 1.3f) || !grounded || _isSlopeSliding) return;
            _isBellySliding = false;
            _isSlideKicking = false;
            _isDiving = false;
            return;
        }

        if (_isDiving && !grounded)
        {
            _planarVelocity = Vector3.MoveTowards(_planarVelocity, _planarVelocity.normalized * 11.4f, 6f * dt);
            return;
        }

        if (_skidReversalTimer > 0f) _skidReversalTimer -= dt;

        var topSpeed = IsCrouching ? crawlSpeed : (_carriedObject != null ? maxRunSpeed * 0.88f : maxRunSpeed);
        var targetVel = moveInputDir * (topSpeed * moveMagnitude);

        // Skid Reversal detection (Odyssey / SM64 pivot + dust puff + screech)
        if (grounded && moveMagnitude > 0.45f && _planarVelocity.magnitude > maxRunSpeed * 0.52f)
        {
            var dot = Vector3.Dot(_planarVelocity.normalized, moveInputDir);
            if (dot < -0.58f && _skidReversalTimer <= 0f)
            {
                _skidReversalTimer = 0.40f;
                if (GameAudio.Instance != null) GameAudio.Instance.PlaySkidSound();
                if (VFXPool.Instance != null) VFXPool.Instance.SpawnFootstepPuff(transform.position);
            }
        }

        var rate = grounded ? (moveMagnitude > 0.05f ? acceleration : deceleration) : airControl;
        _planarVelocity = Vector3.MoveTowards(_planarVelocity, targetVel, rate * dt);

        if (!(moveMagnitude > 0.05f) || !(moveInputDir.sqrMagnitude > 0.001f)) return;
        {
            var desiredRot = Quaternion.LookRotation(moveInputDir, Vector3.up);
            var rotRate = grounded ? (_planarVelocity.magnitude < 3.0f ? turnSmoothSpeed * 1.45f : turnSmoothSpeed) : turnSmoothSpeed * 0.55f;
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, 1f - Mathf.Exp(-rotRate * dt));
        }
    }

    private void UpdateVerticalPhysics(bool grounded, float dt)
    {
        if (grounded && CurrentVerticalSpeed < 0f)
        {
            CurrentVerticalSpeed = -3.8f;
            return;
        }

        // Odyssey Apex Float + Crisp Fall!
        var gScale = 1.0f;
        if (_isLongJumping) gScale = 0.78f;
        else if (Mathf.Abs(CurrentVerticalSpeed) < 3.2f) gScale = 0.68f;
        else if (CurrentVerticalSpeed < -1.2f) gScale = 1.24f;

        CurrentVerticalSpeed += gravity * gScale * dt;
        CurrentVerticalSpeed = Mathf.Max(CurrentVerticalSpeed, -29.0f);
    }

    private void OnJustLanded()
    {
        _landedTimer = 0f;
        _isBackflipping = false;
        _isSideFlipping = false;
        _isLongJumping = false;
        _isWallSliding = false;
        _usedAirSpinHover = false;
        _usedAirKick = false;
        _jumpCutApplied = false;

        if (_isGroundPounding)
        {
            _isGroundPounding = false;
            _groundPoundImpactTimer = 0.22f;
            _planarVelocity = Vector3.zero;

            if (impulseSource != null) impulseSource.GenerateImpulse(0.45f);
            if (GameAudio.Instance != null) GameAudio.Instance.PlayGroundPoundSound();
            if (VFXPool.Instance != null) VFXPool.Instance.SpawnPickupBurst(transform.position + Vector3.up * 0.15f, isStar: false);
            if (motion != null) motion.TriggerLandSquash(1.35f);

            CheckMeleeHitbox(transform.position, 2.35f);
            return;
        }

        if (_isDiving)
        {
            _isDiving = false;
            _isBellySliding = true;
            _planarVelocity = transform.forward * Mathf.Max(_planarVelocity.magnitude, 11.4f);
            if (VFXPool.Instance != null) VFXPool.Instance.SpawnFootstepPuff(transform.position);
            if (motion != null) motion.TriggerLandSquash(1.1f);
            return;
        }

        if (VFXPool.Instance != null) VFXPool.Instance.SpawnFootstepPuff(transform.position);
        if (motion != null) motion.TriggerLandSquash(0.85f);
    }

    // -------------------------------------------------------------------------
    // UNIVERSAL MOVING PLATFORM CARRY & WALL SLIDE TRACKING
    // -------------------------------------------------------------------------
    private void ApplyMovingPlatformCarry()
    {
        var platform = _activePlatform;
        if (platform == null || !_cc.isGrounded)
        {
            _activePlatform = null;
            return;
        }

        var platformPos = platform.position;
        var platformRot = platform.rotation;
        var deltaPos = platformPos - _lastPlatformWorldPos;
        var deltaRot = platformRot * Quaternion.Inverse(_lastPlatformWorldRot);

        if (deltaPos.sqrMagnitude < 4.5f)
        {
            if (deltaPos.sqrMagnitude > 0.000001f)
            {
                _cc.Move(deltaPos);
                // _cc.Move can trigger OnControllerColliderHit and clear/swap _activePlatform.
                if (_activePlatform != platform || platform == null) return;
            }
            var offsetFromPivot = transform.position - platformPos;
            var rotatedOffset = deltaRot * offsetFromPivot;
            var rotDeltaMove = rotatedOffset - offsetFromPivot;
            if (rotDeltaMove.sqrMagnitude > 0.000001f)
            {
                _cc.Move(rotDeltaMove);
                if (_activePlatform != platform || platform == null) return;
            }
            var yawAngle = deltaRot.eulerAngles.y;
            if (Mathf.Abs(Mathf.DeltaAngle(0f, yawAngle)) > 0.01f)
            {
                transform.Rotate(0f, yawAngle, 0f, Space.World);
            }
        }

        _lastPlatformWorldPos = platform.position;
        _lastPlatformWorldRot = platform.rotation;
    }

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (hit.collider == null) return;

        // Track ANY interactive moving platform (WF, BOB, JRB, BitDW!)
        if (hit.normal.y > 0.55f)
        {
            var tf = hit.collider.transform;
            var isDynamicPlatform =
                tf.GetComponentInParent<WhompFortressElement>() != null ||
                tf.GetComponentInParent<BobOmbBattlefieldController>() != null ||
                tf.GetComponentInParent<JollyRogerBayController>() != null ||
                tf.GetComponentInParent<BowserDarkWorldElement>() != null ||
                tf.name.Contains("Lift") || tf.name.Contains("Seesaw") || tf.name.Contains("Bridge");

            if (isDynamicPlatform)
            {
                if (_activePlatform != tf)
                {
                    _activePlatform = tf;
                    _lastPlatformWorldPos = tf.position;
                    _lastPlatformWorldRot = tf.rotation;
                }
            }
            else
            {
                _activePlatform = null;
            }
        }

        // Wall contact tracking for crisp Wall Slide + Wall Kick
        if (!(Mathf.Abs(hit.normal.y) < 0.35f) || hit.collider.isTrigger) return;
        _lastWallNormal = hit.normal;
        _lastWallTouchTime = Time.time;
    }

    // -------------------------------------------------------------------------
    // MOTION SYNC
    // -------------------------------------------------------------------------
    private void UpdateMotionComponentOnly()
    {
        if (motion == null) return;
        var dt = Time.deltaTime > 0.0001f ? Time.deltaTime : 0.016f;
        var currYaw = transform.eulerAngles.y;
        var yawRate = Mathf.DeltaAngle(_prevFacingYaw, currYaw) / dt;
        _prevFacingYaw = currYaw;

        motion.normalizedSpeed = Mathf.Clamp01(_planarVelocity.magnitude / Mathf.Max(0.1f, maxRunSpeed));
        motion.planarVelocity = _planarVelocity;
        motion.isGrounded = _cc.isGrounded;
        motion.jumpStage = JumpComboStage;
        motion.isDiving = _isDiving;
        motion.isCrouching = IsCrouching;
        motion.isSliding = _isBellySliding;
        motion.isWallSliding = _isWallSliding;
        motion.isLedgeHanging = _isLedgeHanging;
        motion.isSpinningBowser = IsSpinningBowser;
        motion.isSwimming = IsSwimming;
        motion.isSkidding = _skidReversalTimer > 0f;
        motion.turnBankRate = yawRate;
        motion.isGroundPounding = _isGroundPounding;
        motion.groundPoundStallNormalized = _isGroundPounding ? Mathf.Clamp01(_groundPoundStallTimer / 0.22f) : 0f;
        motion.isBackflipping = _isBackflipping;
        motion.isSideFlipping = _isSideFlipping;
        motion.isLongJumping = _isLongJumping;
        motion.isRolling = _isRolling;
        motion.isSpinAttacking = _isSpinAttacking;
        motion.isSlopeSliding = _isSlopeSliding;
        motion.isClimbingPole = _activePole != null;
        motion.isPoleHandstand = _isPoleHandstand;
        motion.poleClimbSpeed = _activePole != null ? Mathf.Clamp(ReadMovementInput().y, -1f, 1f) : 0f;
        motion.isCarryingObject = _carriedObject != null;
        motion.attackStage = _attackComboStage;
        motion.attackProgress = _attackDuration > 0f ? 1f - Mathf.Clamp01(_attackTimer / _attackDuration) : 0f;
    }

    // -------------------------------------------------------------------------
    // SWIMMING & HEALTH / DAMAGE
    // -------------------------------------------------------------------------
    public void HealHealth(int wedges)
    {
        currentHealth = Mathf.Clamp(currentHealth + wedges, 0, maxHealth);
    }

    public void RefillOxygen(float seconds)
    {
        currentOxygenSeconds = Mathf.Clamp(currentOxygenSeconds + seconds, 0f, maxOxygenSeconds);
    }

    public void TakeDamage(int damageWedges, Vector3 hazardSourcePos, bool ignoreInvuln = false)
    {
        if (GameManager.Instance != null && GameManager.Instance.currentState != GameState.Playing) return;
        if (!ignoreInvuln && _hurtInvulnTimer > 0f) return;

        // Release Bowser tail if hit mid-spin
        if (_spinningBowser != null)
        {
            _spinningBowser = null;
        }
        if (IsFirstPersonLooking) ExitFirstPersonLook();
        if (_cc != null && !_cc.enabled) _cc.enabled = true;
        _activePole = null;
        _isPoleHandstand = false;
        _isRolling = false;
        _isSpinAttacking = false;
        _isSlopeSliding = false;

        currentHealth = Mathf.Max(0, currentHealth - Mathf.Max(1, damageWedges));
        _hurtInvulnTimer = hurtInvulnDuration;

        var push = transform.position - hazardSourcePos;
        push.y = 0f;
        if (push.sqrMagnitude < 0.01f) push = -transform.forward;
        push.Normalize();
        _planarVelocity = push * 6.5f;
        CurrentVerticalSpeed = Mathf.Max(CurrentVerticalSpeed, 5.2f);
        _isDiving = false;
        _isBellySliding = false;
        _isGroundPounding = false;
        _isLedgeHanging = false;

        if (GameAudio.Instance != null) GameAudio.Instance.PlayHurtSound();
        if (impulseSource != null) impulseSource.GenerateImpulse(0.45f);
        if (VFXPool.Instance != null) VFXPool.Instance.SpawnPickupBurst(transform.position + Vector3.up * 0.7f, isStar: false);

        if (currentHealth <= 0) RespawnAfterDeath();
    }

    private void RespawnAfterDeath()
    {
        lives = Mathf.Max(0, lives - 1);
        if (lives == 0) lives = 4;
        currentHealth = maxHealth;
        currentOxygenSeconds = maxOxygenSeconds;
        IsSwimming = false;
        _planarVelocity = Vector3.zero;
        CurrentVerticalSpeed = 0f;
        _isDiving = false;
        _isBellySliding = false;
        _isGroundPounding = false;
        _isLedgeHanging = false;
        _isRolling = false;
        _isSpinAttacking = false;
        _isSlopeSliding = false;
        _activePole = null;
        _isPoleHandstand = false;
        if (IsFirstPersonLooking) ExitFirstPersonLook();
        if (_cc != null && !_cc.enabled) _cc.enabled = true;
        if (_carriedObject != null)
        {
            _carriedObject.ResetCarriable();
            _carriedObject = null;
        }
        _activePlatform = null;

        if (_cc != null) _cc.enabled = false;
        transform.SetPositionAndRotation(_spawnPosition, _spawnRotation);
        if (_cc != null) _cc.enabled = true;

        _hurtInvulnTimer = 1.2f;
        if (VFXPool.Instance != null) VFXPool.Instance.SpawnPickupBurst(_spawnPosition + Vector3.up * 0.8f, isStar: false);
    }

    private void UpdateDamageFlashAndAbyssCheck(float dt)
    {
        if (transform.position.y < abyssDeathY)
        {
            if (GameAudio.Instance != null) GameAudio.Instance.PlayHurtSound();
            RespawnAfterDeath();
            return;
        }

        if (_hurtInvulnTimer > 0f)
        {
            _hurtInvulnTimer -= dt;
            var flashOn = Mathf.Sin(Time.unscaledTime * 36f) > 0f;
            SetHeroVisibility(flashOn && !IsFirstPersonLooking);
        }
        else if (!IsFirstPersonLooking)
        {
            SetHeroVisibility(true);
        }
    }

    private void SetHeroVisibility(bool visible)
    {
        if (_heroRenderers == null) return;
        foreach (var r in _heroRenderers)
        {
            if (r != null && r.enabled != visible) r.enabled = visible;
        }
    }

    private bool CheckAndHandleSwimmingState(float dt)
    {
        var pos = transform.position;
        var effectiveSurfaceY = waterSurfaceY;
        var submerged = WaterVolume.TryGetWaterAt(pos, out effectiveSurfaceY);
        if (!submerged && hasWaterInCourse)
        {
            var insideBayBounds = pos.x >= waterBoundsX.x && pos.x <= waterBoundsX.y &&
                                  pos.z >= waterBoundsZ.x && pos.z <= waterBoundsZ.y;
            effectiveSurfaceY = waterSurfaceY;
            submerged = insideBayBounds && pos.y < effectiveSurfaceY - 0.15f;
        }

        if (!submerged)
        {
            if (IsSwimming)
            {
                IsSwimming = false;
                if (ReadJumpHeldOrPressed() && CurrentVerticalSpeed > 0.5f)
                {
                    CurrentVerticalSpeed = Mathf.Sqrt(2f * Mathf.Abs(gravity) * singleJumpHeight);
                    if (GameAudio.Instance != null) GameAudio.Instance.PlayJumpSound(1);
                }
            }
            currentOxygenSeconds = Mathf.MoveTowards(currentOxygenSeconds, maxOxygenSeconds, 12f * dt);
            return false;
        }

        IsSwimming = true;
        _isDiving = false;
        _isBellySliding = false;
        _isGroundPounding = false;
        _isWallSliding = false;
        _isLedgeHanging = false;
        _isRolling = false;
        _isSpinAttacking = false;
        _isSlopeSliding = false;
        RecordMoveStat("Swim");

        var metalActive = JollyRogerBayController.IsMetalMarioActive;
        var atSurface = pos.y >= effectiveSurfaceY - 0.45f;
        if (atSurface)
            currentOxygenSeconds = Mathf.MoveTowards(currentOxygenSeconds, maxOxygenSeconds, 14f * dt);
        else if (!metalActive)
            currentOxygenSeconds = Mathf.Max(0f, currentOxygenSeconds - dt);

        if (currentOxygenSeconds <= 0f && !metalActive)
        {
            currentOxygenSeconds = 4f;
            TakeDamage(1, pos, ignoreInvuln: true);
            return true;
        }

        var rawInput = ReadMovementInput();
        var camFwdXZ = Quaternion.Euler(0f, CurrentCameraYaw, 0f) * Vector3.forward;
        var camRightXZ = Quaternion.Euler(0f, CurrentCameraYaw, 0f) * Vector3.right;
        var planarInput = camFwdXZ * rawInput.y + camRightXZ * rawInput.x;
        var kb = Keyboard.current;

        var swimVelY = metalActive ? -8.5f : -0.35f;
        var swimFast = !metalActive && ((kb != null && kb.spaceKey.isPressed) || ReadJumpPressedOnly());
        if (swimFast)
        {
            swimVelY = Mathf.Lerp(2.4f, 4.6f, Mathf.Clamp01(rawInput.magnitude));
            _swimPitch = Mathf.MoveTowards(_swimPitch, 18f, 120f * dt);
        }
        else if (ReadCrouchHeld() && !metalActive)
        {
            swimVelY = -5.2f;
            _swimPitch = Mathf.MoveTowards(_swimPitch, -32f, 140f * dt);
        }
        else
        {
            _swimPitch = Mathf.MoveTowards(_swimPitch, 0f, 90f * dt);
        }

        var targetSpeed = metalActive ? 5.6f : (swimFast ? swimFlutterSpeed : swimStrokeSpeed);
        var desiredVel = planarInput.sqrMagnitude > 0.01f
            ? planarInput.normalized * (targetSpeed * Mathf.Clamp01(rawInput.magnitude))
            : Vector3.zero;

        _planarVelocity = Vector3.MoveTowards(_planarVelocity, desiredVel, 22f * dt);
        CurrentVerticalSpeed = Mathf.MoveTowards(CurrentVerticalSpeed, swimVelY, 20f * dt);

        var moveVec = _planarVelocity + Vector3.up * CurrentVerticalSpeed;
        if (pos.y + moveVec.y * dt > effectiveSurfaceY - 0.08f && !swimFast)
            moveVec.y = Mathf.Min(0f, moveVec.y);

        _cc.Move(moveVec * dt);

        if (planarInput.sqrMagnitude > 0.01f)
        {
            var lookRot = Quaternion.LookRotation(planarInput.normalized, Vector3.up) * Quaternion.Euler(-_swimPitch * 0.45f, 0f, 0f);
            transform.rotation = Quaternion.Slerp(transform.rotation, lookRot, 10f * dt);
        }

        if (ReadAttackPressed()) CheckMeleeHitbox(transform.position + transform.forward * 1.15f + Vector3.up * 0.6f, 1.55f);
        return true;
    }

    private bool ReadJumpPressedOnly() => (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) || debugJumpTrigger;
    private bool ReadJumpHeldOrPressed() => (Keyboard.current != null && Keyboard.current.spaceKey.isPressed) || debugJumpTrigger;

    // -------------------------------------------------------------------------
    // INPUT HELPERS
    // -------------------------------------------------------------------------
    private Vector2 ReadMovementInput()
    {
        if (debugMoveInput.sqrMagnitude > 0.001f) return Vector2.ClampMagnitude(debugMoveInput, 1f);
        var vec = Vector2.zero;
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.aKey.isPressed) vec.x -= 1f;
            if (kb.dKey.isPressed) vec.x += 1f;
            if (kb.sKey.isPressed) vec.y -= 1f;
            if (kb.wKey.isPressed) vec.y += 1f;
        }
        var gp = Gamepad.current;
        if (gp != null && vec.sqrMagnitude < 0.01f) vec = gp.leftStick.ReadValue();
        return Vector2.ClampMagnitude(vec, 1f);
    }

    private (bool pressedThisFrame, bool held) ReadJumpInput()
    {
        var pressed = false;
        var held = false;
        if (debugJumpTrigger)
        {
            debugJumpTrigger = false;
            pressed = true;
            held = true;
        }
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.spaceKey.wasPressedThisFrame) pressed = true;
            if (kb.spaceKey.isPressed) held = true;
        }
        var gp = Gamepad.current;
        if (gp == null) return (pressed, held);
        if (gp.buttonSouth.wasPressedThisFrame) pressed = true;
        if (gp.buttonSouth.isPressed) held = true;
        return (pressed, held);
    }

    private static bool ReadCrouchHeld()
    {
        var kb = Keyboard.current;
        if (kb != null && (kb.cKey.isPressed || kb.leftCtrlKey.isPressed)) return true;
        var gp = Gamepad.current;
        return gp != null && (gp.leftTrigger.isPressed || gp.buttonEast.isPressed);
    }

    private static bool ReadCrouchPressedThisFrame()
    {
        var kb = Keyboard.current;
        if (kb != null && (kb.cKey.wasPressedThisFrame || kb.leftCtrlKey.wasPressedThisFrame)) return true;
        var gp = Gamepad.current;
        return gp != null && (gp.leftTrigger.wasPressedThisFrame || gp.buttonEast.wasPressedThisFrame);
    }

    private bool ReadRollPressed()
    {
        if (debugRollTrigger)
        {
            debugRollTrigger = false;
            return true;
        }
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.leftShiftKey.wasPressedThisFrame) return true;
            if (ReadCrouchHeld() && kb.eKey.wasPressedThisFrame) return true;
        }
        var gp = Gamepad.current;
        return gp != null && (gp.rightTrigger.wasPressedThisFrame || (ReadCrouchHeld() && gp.buttonNorth.wasPressedThisFrame));
    }

    private bool ReadSpinPressed()
    {
        if (debugSpinAttackTrigger)
        {
            debugSpinAttackTrigger = false;
            return true;
        }
        var kb = Keyboard.current;
        if (kb != null && (kb.zKey.wasPressedThisFrame || kb.xKey.wasPressedThisFrame)) return true;
        var gp = Gamepad.current;
        return gp != null && gp.rightShoulder.wasPressedThisFrame;
    }

    private bool ReadFirstPersonPressed()
    {
        if (debugToggleFirstPerson)
        {
            debugToggleFirstPerson = false;
            return true;
        }
        var kb = Keyboard.current;
        if (kb != null && kb.vKey.wasPressedThisFrame) return true;
        var gp = Gamepad.current;
        return gp != null && gp.dpad.up.wasPressedThisFrame;
    }

    private bool ReadGroundPoundPressed()
    {
        if (debugGroundPoundTrigger)
        {
            debugGroundPoundTrigger = false;
            return true;
        }
        var kb = Keyboard.current;
        if (kb != null && (kb.leftCtrlKey.wasPressedThisFrame || kb.cKey.wasPressedThisFrame || kb.leftShiftKey.wasPressedThisFrame))
            return true;
        var gp = Gamepad.current;
        return gp != null && (gp.leftTrigger.wasPressedThisFrame || gp.buttonEast.wasPressedThisFrame);
    }

    private bool ReadDivePressed()
    {
        if (debugDiveTrigger)
        {
            debugDiveTrigger = false;
            return true;
        }
        var kb = Keyboard.current;
        if (kb != null && (kb.eKey.wasPressedThisFrame || kb.qKey.wasPressedThisFrame)) return true;
        var gp = Gamepad.current;
        return gp != null && gp.buttonNorth.wasPressedThisFrame;
    }

    private static bool ReadAttackPressed()
    {
        var mouse = Mouse.current;
        var overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        if (mouse != null && mouse.leftButton.wasPressedThisFrame && !overUi)
        {
            return true;
        }
        var kb = Keyboard.current;
        if (kb != null && (kb.fKey.wasPressedThisFrame || kb.jKey.wasPressedThisFrame)) return true;
        var gp = Gamepad.current;
        return gp != null && gp.buttonWest.wasPressedThisFrame;
    }
}
