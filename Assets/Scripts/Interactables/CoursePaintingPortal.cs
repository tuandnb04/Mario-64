using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Interactive SM64 Painting / Warp Pipe Portal linking Peach's Castle Hub with Courses.
/// Ripples visually when Mario approaches, checks SaveManager star gates (F8 overrides for QA),
/// and plays a warp transition into the target course scene.
/// </summary>
[RequireComponent(typeof(Collider))]
public class CoursePaintingPortal : MonoBehaviour
{
    private static readonly int RippleAmplitudeId = Shader.PropertyToID("_RippleAmplitude");
    private static readonly int RippleSpeedId = Shader.PropertyToID("_RippleSpeed");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private static bool _debugAllUnlocked;
    private static int _lastDebugToggleFrame = -1;

    [Header("Destination")]
    public string targetSceneName = "BobOmbBattlefield";
    public string courseTitle = "COURSE 1: BOB-OMB BATTLEFIELD";
    public int requiredStars;
    public bool isReturnToCastlePortal;

    [Header("Visuals")]
    public Renderer paintingCanvasRenderer;
    public Color portalThemeColor = new(0.25f, 0.78f, 0.35f, 1f);
    public Vector3 labelLocalOffset = new(0f, 3.1f, 0.15f);

    private MaterialPropertyBlock _mpb;
    private TextMeshPro _signLabel;
    private bool _isWarping;
    private float _lockedCooldown;
    private int _lastDisplayedStars = -1;
    private bool _lastDisplayedUnlockOverride;

    public static bool DebugAllUnlocked
    {
        get => _debugAllUnlocked;
        set => _debugAllUnlocked = value;
    }

    public bool IsUnlocked(int currentStars)
    {
        return _debugAllUnlocked || isReturnToCastlePortal || currentStars >= requiredStars;
    }

    private void Awake()
    {
        var col = GetComponent<Collider>();
        if (col != null)
        {
            col.isTrigger = true;
        }
        _mpb = new MaterialPropertyBlock();
    }

    private void Start()
    {
        EnsureSignLabel();
        RefreshSignText(force: true);
    }

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.f8Key.wasPressedThisFrame && Time.frameCount != _lastDebugToggleFrame)
        {
            _lastDebugToggleFrame = Time.frameCount;
            _debugAllUnlocked = !_debugAllUnlocked;
            if (GameManager.Instance != null)
            {
                var msg = _debugAllUnlocked ? "ALL COURSE PORTALS UNLOCKED (QA)" : "STAR GATES RESTORED";
                GameManager.Instance.SpawnFloatingWorldText(transform.position + Vector3.up * 2.2f, msg, Palette.AccentStarGold);
            }
            if (GameAudio.Instance != null) GameAudio.Instance.PlayUIClick();
        }

        var dt = Time.deltaTime > 0.0001f ? Time.deltaTime : 0.016f;
        if (_lockedCooldown > 0f) _lockedCooldown -= dt;

        RefreshSignText(force: false);
        UpdateSignFacing();
        UpdatePaintingRipple();
    }

    private void EnsureSignLabel()
    {
        if (_signLabel != null) return;
        var existing = transform.Find("PortalSignLabel");
        GameObject labelGo;
        if (existing != null)
        {
            labelGo = existing.gameObject;
        }
        else
        {
            labelGo = new GameObject("PortalSignLabel");
            labelGo.transform.SetParent(transform, false);
            labelGo.transform.localPosition = labelLocalOffset;
        }

        _signLabel = labelGo.GetComponent<TextMeshPro>();
        if (_signLabel == null) _signLabel = labelGo.AddComponent<TextMeshPro>();
        var font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (font != null) _signLabel.font = font;
        _signLabel.fontSize = 4.2f;
        _signLabel.fontStyle = FontStyles.Bold;
        _signLabel.alignment = TextAlignmentOptions.Center;
        _signLabel.textWrappingMode = TextWrappingModes.NoWrap;
    }

    private void RefreshSignText(bool force)
    {
        if (_signLabel == null) return;
        var currentStars = SaveManager.Instance.TotalStarsCollected;
        if (!force && currentStars == _lastDisplayedStars && _debugAllUnlocked == _lastDisplayedUnlockOverride)
            return;

        _lastDisplayedStars = currentStars;
        _lastDisplayedUnlockOverride = _debugAllUnlocked;

        var unlocked = IsUnlocked(currentStars);
        if (isReturnToCastlePortal)
        {
            _signLabel.text = $"<color=#FFD83D>{courseTitle}</color>\n<size=72%><color=#FAF3E8>ENTER TO RETURN</color></size>";
            return;
        }

        if (unlocked)
        {
            var reqText = requiredStars <= 0 ? "OPEN" : $"{currentStars}/{requiredStars} STARS UNLOCKED";
            _signLabel.text = $"<color=#FAF3E8>{courseTitle}</color>\n<size=75%><color=#6BCB77>{reqText}</color></size>";
        }
        else
        {
            var starWord = requiredStars == 1 ? "STAR" : "STARS";
            _signLabel.text = $"<color=#FAF3E8>{courseTitle}</color>\n<size=75%><color=#FF6B6B>LOCKED • NEED {requiredStars} {starWord} ({currentStars}/{requiredStars})</color></size>";
        }
    }

    private void UpdateSignFacing()
    {
        if (_signLabel == null || Camera.main == null) return;
        var toCam = _signLabel.transform.position - Camera.main.transform.position;
        toCam.y = 0f;
        if (toCam.sqrMagnitude > 0.01f)
        {
            _signLabel.transform.rotation = Quaternion.LookRotation(toCam.normalized, Vector3.up);
        }
    }

    private void UpdatePaintingRipple()
    {
        if (paintingCanvasRenderer == null) return;

        var hero = MarioRemakeController.Instance;
        var proximity = 0f;
        if (hero != null)
        {
            var dist = Vector3.Distance(hero.transform.position + Vector3.up * 0.8f, transform.position);
            proximity = Mathf.Clamp01(1f - (dist / 6.5f));
        }

        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        paintingCanvasRenderer.GetPropertyBlock(_mpb);
        _mpb.SetColor(BaseColorId, portalThemeColor);
        _mpb.SetFloat(RippleAmplitudeId, Mathf.Lerp(0.010f, 0.068f, proximity));
        _mpb.SetFloat(RippleSpeedId, Mathf.Lerp(3.2f, 9.8f, proximity));
        paintingCanvasRenderer.SetPropertyBlock(_mpb);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_isWarping) return;
        if (GameManager.Instance != null && GameManager.Instance.currentState != GameState.Playing) return;

        var hero = other.GetComponent<MarioRemakeController>();
        if (hero == null) hero = other.GetComponentInParent<MarioRemakeController>();
        if (hero == null) return;

        var currentStars = SaveManager.Instance.TotalStarsCollected;
        if (!IsUnlocked(currentStars))
        {
            if (_lockedCooldown > 0f) return;
            _lockedCooldown = 0.85f;
            if (GameAudio.Instance != null) GameAudio.Instance.PlaySwitchTick(urgent: false);
            if (GameManager.Instance != null)
            {
                var starWord = requiredStars == 1 ? "POWER STAR!" : "POWER STARS!";
                GameManager.Instance.SpawnFloatingWorldText(
                    transform.position + Vector3.up * 1.8f,
                    $"NEED {requiredStars} {starWord}",
                    Palette.CharacterPrimary);
            }

            var cc = hero.GetComponent<CharacterController>();
            var pushBack = hero.transform.position - transform.position;
            pushBack.y = 0f;
            if (pushBack.sqrMagnitude < 0.01f) pushBack = -hero.transform.forward;
            pushBack.Normalize();
            if (cc != null && cc.enabled)
            {
                cc.Move(pushBack * 1.35f);
            }
            return;
        }

        StartCoroutine(EnterPaintingRoutine(hero));
    }

    private IEnumerator EnterPaintingRoutine(MarioRemakeController hero)
    {
        _isWarping = true;

        if (GameAudio.Instance != null) GameAudio.Instance.PlayPaintingWarp();
        if (VFXPool.Instance != null) VFXPool.Instance.SpawnPickupBurst(transform.position, isStar: true);

        if (hero != null)
        {
            var cc = hero.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            var startPos = hero.transform.position;
            var targetPos = transform.position;
            var startScale = hero.transform.localScale;
            var endScale = startScale * 0.25f;

            var elapsed = 0f;
            const float duration = 0.45f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Ease.OutCubic(Mathf.Clamp01(elapsed / duration));
                if (hero != null)
                {
                    hero.transform.position = Vector3.Lerp(startPos, targetPos, t);
                    hero.transform.localScale = Vector3.Lerp(startScale, endScale, t);
                }
                yield return null;
            }
        }

        if (!string.IsNullOrEmpty(targetSceneName))
        {
            SaveManager.Instance.RecordLastCourse(targetSceneName, autoSave: true);
            GameManager.SwitchCourseScene(targetSceneName);
        }
        else
        {
            _isWarping = false;
        }
    }
}
