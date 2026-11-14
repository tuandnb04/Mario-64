using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mario64Remake;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using UnityEngine.UI;

public enum GameState
{
    Title,
    Playing,
    Paused,
    Ended
}

/// <summary>
/// Game Shell & Feel Orchestrator (game-shell.md + game-feel.md):
/// - 4-state shell: Title, Playing, Paused, Ended
/// - 0.3s fade transitions on unscaled time
/// - Title backdrop camera slow orbit (~5 deg/s) over the live courtyard
/// - Pulsing prompt text (0.5 <-> 1.0 over 1.6s unscaled)
/// - Pickup recipe: scale-pop collector (1 -> 1.25 -> 1, 0.15s OutBack), 12 palette particles, blip, floating "+1 COIN" / "+1 STAR" rising ~0.8 units over 0.6s
/// - Animated Star Power fill bar (0.3s OutCubic) with non-null generated Sprite
/// - Worked milestone sequence (flash -> sting -> banner OutBack -> confetti -> bar tween -> hold)
/// - Settings sliders bound to MusicVol & SFXVol on GameAudio
/// </summary>
public class GameManager : MonoBehaviour
{
    private static GameManager instance;
    private static readonly int FaceDilate = Shader.PropertyToID("_FaceDilate");
    private static readonly int OutlineWidth = Shader.PropertyToID("_OutlineWidth");
    private static readonly int OutlineColor = Shader.PropertyToID("_OutlineColor");

    public static GameManager Instance
    {
        get
        {
            if (instance == null) instance = FindAnyObjectByType<GameManager>();
            return instance;
        }
        private set => instance = value;
    }

    [FormerlySerializedAs("CurrentState")] [Header("State & Progression")]
    public GameState currentState = GameState.Title;
    [FormerlySerializedAs("CoinsCollected")] public int coinsCollected;
    [FormerlySerializedAs("RedCoinsCollected")] public int redCoinsCollected;
    [FormerlySerializedAs("StarsCollected")] public int starsCollected;
    [FormerlySerializedAs("TargetStarsToWin")] public int targetStarsToWin = 3;
    public int currentActNumber = 1; // Whomp King (Act 1) vs Lookout Tower (Act 2+); open-world Stars collectible in any order!
    public string currentMissionTitle = "ACT 1";

    [FormerlySerializedAs("Player")] [Header("Scene References")]
    public MarioRemakeController player;
    [FormerlySerializedAs("TitleOrbitRig")] public Transform titleOrbitRig;
    [FormerlySerializedAs("TitleVCamObject")] public GameObject titleVCamObject;
    [FormerlySerializedAs("GameplayVCamObject")] public GameObject gameplayVCamObject;
    [FormerlySerializedAs("SecretRedCoinStar")] public StarCoinCollectible secretRedCoinStar;

    [FormerlySerializedAs("MainCanvas")] [Header("UI Panels")]
    public Canvas mainCanvas;
    [FormerlySerializedAs("TitlePanel")] public GameObject titlePanel;
    [FormerlySerializedAs("HUDPanel")] public GameObject hudPanel;
    [FormerlySerializedAs("PausePanel")] public GameObject pausePanel;
    [FormerlySerializedAs("EndedPanel")] public GameObject endedPanel;
    [FormerlySerializedAs("TransitionFadeOverlay")] public Image transitionFadeOverlay;
    [FormerlySerializedAs("MilestoneFlashOverlay")] public Image milestoneFlashOverlay;

    [FormerlySerializedAs("CoinCounterText")] [Header("HUD Elements")]
    public TextMeshProUGUI coinCounterText;
    [FormerlySerializedAs("StarCounterText")] public TextMeshProUGUI starCounterText;
    [FormerlySerializedAs("RedCoinCounterText")] public TextMeshProUGUI redCoinCounterText;
    [FormerlySerializedAs("StarPowerFillBar")] public Image starPowerFillBar;
    [FormerlySerializedAs("MilestoneBannerRoot")] public RectTransform milestoneBannerRoot;
    [FormerlySerializedAs("MilestoneBannerTitle")] public TextMeshProUGUI milestoneBannerTitle;
    [FormerlySerializedAs("MilestoneBannerSubtitle")] public TextMeshProUGUI milestoneBannerSubtitle;

    [FormerlySerializedAs("TitlePromptText")] [Header("Shell Elements")]
    public TextMeshProUGUI titlePromptText;
    [FormerlySerializedAs("EndedSummaryText")] public TextMeshProUGUI endedSummaryText;
    [FormerlySerializedAs("MusicSlider")] public Slider musicSlider;
    [FormerlySerializedAs("SFXSlider")] public Slider sfxSlider;

    private readonly List<StarCoinCollectible> _allCollectibles = new();
    private Coroutine _barTweenRoutine;
    private Coroutine _milestoneRoutine;
    private Coroutine _fadeRoutine;
    private Coroutine _hitStopRoutine;
    private float _displayedCoinCount;
    private float _targetFillAmount;

    // SM64 / 64 DS Runtime HUD Widgets
    private readonly Image[] _powerMeterWedges = new Image[8];
    private TextMeshProUGUI _powerMeterCenterText;
    private TextMeshProUGUI _livesCounterText;
    private GameObject _coinBadgeGo;
    private GameObject _starBadgeGo;
    private readonly Image[] _redCoinSlots = new Image[8];
    private RectTransform _powerMeterRoot;
    private TextMeshProUGUI _actMissionBadgeText;
    private TextMeshProUGUI _titleActPreviewText;
    private readonly List<Image> _titleStarButtonsBg = new();
    private Material _sm64OutlinedFontMat;
    private Sprite _runtimePowerBarSprite;
    private bool _sm64HudStyled;

    private void Awake()
    {
        if (instance != null && instance != this && instance.gameObject.scene == gameObject.scene)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    private static bool IsCastleHubScene(string sceneName)
    {
        return !string.IsNullOrEmpty(sceneName) && (sceneName.Contains("Castle") || sceneName.Contains("Hub"));
    }

    private void RestoreSavedProgressForScene(string sceneName)
    {
        if (SaveManager.Instance == null) return;
        SaveManager.Instance.RecordLastCourse(sceneName, autoSave: false);

        var courseSavedCount = 0;
        foreach (var item in _allCollectibles)
        {
            if (item == null || item.kind != CollectibleKind.PowerStar) continue;
            if (SaveManager.Instance.IsStarCollected(sceneName, item.missionActIndex))
            {
                item.MarkCollectedFromSave();
                courseSavedCount++;
            }
        }

        starsCollected = IsCastleHubScene(sceneName) ? SaveManager.Instance.TotalStarsCollected : courseSavedCount;
    }

    private void Start()
    {
        var sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        var isCastleHub = IsCastleHubScene(sceneName);
        if (player == null) player = FindAnyObjectByType<MarioRemakeController>();
        RefreshCollectibleRegistry();
        RestoreSavedProgressForScene(sceneName);
        EnsureSm64StyledHUD();
        WireSliders();
        SetupActMissionState(1, isFreshRun: false);
        SetStateImmediate(titlePanel != null ? GameState.Title : GameState.Playing);
        var isJrb = !string.IsNullOrEmpty(sceneName) && sceneName.Contains("JollyRoger");
        var isBitDw = !string.IsNullOrEmpty(sceneName) && (sceneName.Contains("Bowser") || sceneName.Contains("DarkWorld"));
        var isCcm = !string.IsNullOrEmpty(sceneName) && sceneName.Contains("Cool");
        if (isJrb && titleOrbitRig != null)
        {
            titleOrbitRig.position = new Vector3(16f, 10f, -32f);
            if (titleOrbitRig.childCount > 0)
            {
                var cam = titleOrbitRig.GetChild(0);
                cam.localPosition = new Vector3(-10f, 7f, -14f);
                cam.LookAt(titleOrbitRig.position);
            }
        }
        if (GameAudio.Instance != null)
        {
            GameAudio.Instance.SetCourseTheme(
                isCastleHub ? Sm64CourseTheme.PeachCastleWaltz :
                (isBitDw ? Sm64CourseTheme.DarkWorldOrgan :
                (isCcm ? Sm64CourseTheme.CoolMountainBell :
                (isJrb ? Sm64CourseTheme.OceanBayWaltz : Sm64CourseTheme.FortressMarch))));
        }
    }

    private void RefreshCollectibleRegistry()
    {
        _allCollectibles.Clear();
        var found = FindObjectsByType<StarCoinCollectible>(FindObjectsInactive.Include);
        _allCollectibles.AddRange(found);

        // Automatically assign missionActIndex (1..6) from Star name/title if not set
        var autoIdx = 1;
        foreach (var c in _allCollectibles)
        {
            if (c == null || c.kind != CollectibleKind.PowerStar) continue;
            var nm = c.gameObject.name;
            if (nm.Contains("Star1")) c.missionActIndex = 1;
            else if (nm.Contains("Star2")) c.missionActIndex = 2;
            else if (nm.Contains("Star3")) c.missionActIndex = 3;
            else if (nm.Contains("Star4")) c.missionActIndex = 4;
            else if (nm.Contains("Star5")) c.missionActIndex = 5;
            else if (nm.Contains("Star6")) c.missionActIndex = 6;
            else
            {
                c.missionActIndex = autoIdx++;
            }
        }
    }

    private void SetupActMissionState(int actNumber, bool isFreshRun = true)
    {
        currentActNumber = Mathf.Clamp(actNumber, 1, Mathf.Max(1, targetStarsToWin));
        if (isFreshRun) redCoinsCollected = 0;

        if (_allCollectibles.Count == 0) RefreshCollectibleRegistry();

        // Check if this scene is Whomp's Fortress (supports non-linear open-world Stars!)
        var towerGo = (from tf in FindObjectsByType<Transform>(FindObjectsInactive.Include) where tf != null && (tf.name == "FortressLookoutTower" || tf.name == "LookoutTower_Summit") select tf.gameObject).FirstOrDefault();
        var isWhompsFortress = towerGo != null;

        // Configure Power Stars & Red Coins (preserve already-collected state across mid-run Act transitions!)
        currentMissionTitle = $"ACT {currentActNumber}";
        foreach (var item in _allCollectibles.Where(item => item != null))
        {
            switch (item.kind)
            {
                case CollectibleKind.PowerStar:
                {
                    if (item.missionActIndex == currentActNumber)
                    {
                        currentMissionTitle = !string.IsNullOrEmpty(item.starTitle) ? item.starTitle : $"ACT {currentActNumber}";
                    }
                    if (isFreshRun) item.ResetCollectible();
                    item.gameObject.SetActive(false);
                    break;
                }
                case CollectibleKind.RedCoin:
                    if (isFreshRun) item.ResetCollectible();
                    break;
                case CollectibleKind.YellowCoin:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        // 1. Configure Whomp's Fortress Act-Specific Elements:
        //    - Act 1 ("Chip Off Whomp's Block"): Whomp King Boss active on Summit; Lookout Tower hidden!
        //    - Act 2+ ("To the Top of the Fortress"): Whomp King gone; Lookout Tower appears on Summit!
        if (towerGo != null)
        {
            towerGo.SetActive(currentActNumber >= 2);
        }

        StarCoinCollectible star1Obj = null;
        foreach (var sc in _allCollectibles.Where(sc => sc != null && sc.kind == CollectibleKind.PowerStar))
        {
            switch (sc.missionActIndex)
            {
                case 1:
                    star1Obj = sc;
                    break;
                case 2:
                    break;
            }
        }

        foreach (var whomp in FindObjectsByType<WhompBossController>(FindObjectsInactive.Include))
        {
            if (whomp == null) continue;
            if (whomp.isKingBoss)
            {
                var kingStillPending = currentActNumber == 1 && (star1Obj == null || !star1Obj.IsCollected);
                if (kingStillPending)
                {
                    whomp.ResetBoss();
                }
                else
                {
                    whomp.gameObject.SetActive(false);
                }

                if (whomp.rewardStar != null) whomp.rewardStar.gameObject.SetActive(false);
            }
            else if (isFreshRun)
            {
                whomp.ResetBoss();
            }
        }

        foreach (var wfElem in FindObjectsByType<WhompFortressElement>(FindObjectsInactive.Include))
        {
            if (wfElem == null) continue;
            if (wfElem.hazardType == FortressHazardType.BreakawayBrickWall)
            {
                var wallStarDone = wfElem.revealedStar != null && wfElem.revealedStar.IsCollected;
                if (isFreshRun || (!wallStarDone && wfElem.gameObject.activeSelf))
                {
                    wfElem.ResetElement();
                    if (wfElem.revealedStar != null) wfElem.revealedStar.gameObject.SetActive(false);
                }
                else if (wallStarDone && wfElem.revealedStar != null)
                {
                    wfElem.revealedStar.gameObject.SetActive(false);
                }
            }
            else if (isFreshRun)
            {
                wfElem.ResetElement();
            }
        }

        // 2. Configure Bob-omb Battlefield Act-Specific Elements (Non-Linear SM64 Open World!):
        //    - Act 1: King Bob-omb active on Summit; Act 2+: Koopa the Quick spawns at Start Bridge!
        //    - Stars 3, 4, 5, 6 remain open in the world simultaneously!
        foreach (var bobElem in FindObjectsByType<BobOmbBattlefieldController>(FindObjectsInactive.Include))
        {
            if (bobElem == null) continue;
            if (isFreshRun) bobElem.ResetBattlefieldElement();

            switch (bobElem.elementType)
            {
                case BattlefieldElementType.KingBobOmbBoss:
                {
                    var star1Done = bobElem.rewardStar != null && bobElem.rewardStar.IsCollected;
                    bobElem.gameObject.SetActive(currentActNumber == 1 && !star1Done);
                    if (bobElem.rewardStar != null) bobElem.rewardStar.gameObject.SetActive(false);
                    break;
                }
                case BattlefieldElementType.KoopaTheQuickRacer:
                {
                    bobElem.gameObject.SetActive(currentActNumber >= 2);
                    if (bobElem.rewardStar != null && bobElem.rewardStar.IsCollected) bobElem.rewardStar.gameObject.SetActive(false);
                    break;
                }
                case BattlefieldElementType.WingCapRedBlock:
                case BattlefieldElementType.ChainChompStake:
                    bobElem.gameObject.SetActive(true);
                    if (isFreshRun && bobElem.rewardStar != null) bobElem.rewardStar.gameObject.SetActive(false);
                    break;
                case BattlefieldElementType.SkyCoinRingCenter:
                case BattlefieldElementType.RollingIronBall:
                case BattlefieldElementType.SeesawBridge:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        // 3. Configure Jolly Roger Bay Act-Specific Elements (Non-Linear SM64 Open World!):
        //    - Act 1: SunkenPirateShip_Act1 on seabed; Act 2+: FloatingPirateShip_Acts2To6 on surface!
        //    - Stars 2 (Unagi), 3 (Ocean Cave), 5 (Stone Pillar), 6 (Jet Stream) open simultaneously!
        if (isFreshRun) JollyRogerBayController.ResetAllChestGroups();
        var sunkenShipGo = (from tf in FindObjectsByType<Transform>(FindObjectsInactive.Include) where tf != null && tf.name == "SunkenPirateShip_Act1" select tf.gameObject).FirstOrDefault();
        if (sunkenShipGo != null) sunkenShipGo.SetActive(currentActNumber == 1);

        var floatingShipGo = (from tf in FindObjectsByType<Transform>(FindObjectsInactive.Include) where tf != null && tf.name == "FloatingPirateShip_Acts2To6" select tf.gameObject).FirstOrDefault();
        if (floatingShipGo != null) floatingShipGo.SetActive(currentActNumber >= 2);

        foreach (var jrbElem in FindObjectsByType<JollyRogerBayController>(FindObjectsInactive.Include))
        {
            if (jrbElem == null) continue;
            if (isFreshRun) jrbElem.ResetBayElement();

            switch (jrbElem.elementType)
            {
                case JollyRogerElementType.TreasureChestPuzzleMember:
                case JollyRogerElementType.UnagiTheEel:
                    if (isFreshRun && jrbElem.rewardStar != null) jrbElem.rewardStar.gameObject.SetActive(false);
                    break;
                case JollyRogerElementType.MetalCapGreenBlock:
                    jrbElem.gameObject.SetActive(true);
                    break;
                case JollyRogerElementType.JetStreamGeyser:
                    if (jrbElem.rewardStar != null) jrbElem.rewardStar.gameObject.SetActive(!jrbElem.rewardStar.IsCollected);
                    break;
                case JollyRogerElementType.SnapClamShell:
                case JollyRogerElementType.FallingCavernPillar:
                case JollyRogerElementType.FloatingGalleonShip:
                case JollyRogerElementType.ShipCabinPortal:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        if (isFreshRun)
        {
            foreach (var ccmElem in FindObjectsByType<CoolCoolMountainController>(FindObjectsInactive.Include))
            {
                if (ccmElem != null) ccmElem.ResetCcmElement();
            }
        }

        // 4. Activate uncollected open-world Power Stars across all 3 SM64 Courses!
        foreach (var item in _allCollectibles.Where(item => item != null && item.kind == CollectibleKind.PowerStar))
        {
            if (item.IsCollected)
            {
                item.gameObject.SetActive(false);
                continue;
            }

            // Check if this star is gated by a Boss / Red Coins / Race / Wall / Wing Cap / Stake / Chests / Eel
            var isGated = (item == secretRedCoinStar);
            foreach (var whomp in FindObjectsByType<WhompBossController>(FindObjectsInactive.Include))
            {
                if (whomp != null && whomp.rewardStar == item) isGated = true;
            }
            foreach (var wfElem in FindObjectsByType<WhompFortressElement>(FindObjectsInactive.Include))
            {
                if (wfElem != null && wfElem.revealedStar == item) isGated = true;
            }
            foreach (var bobElem in FindObjectsByType<BobOmbBattlefieldController>(FindObjectsInactive.Include))
            {
                if (bobElem != null && bobElem.rewardStar == item) isGated = true;
            }
            foreach (var jrbElem in FindObjectsByType<JollyRogerBayController>(FindObjectsInactive.Include))
            {
                if (jrbElem != null && jrbElem.rewardStar == item &&
                    jrbElem.elementType != JollyRogerElementType.JetStreamGeyser)
                {
                    isGated = true;
                }
            }
            foreach (var bowserBoss in FindObjectsByType<BowserBossController>(FindObjectsInactive.Include))
            {
                if (bowserBoss != null && bowserBoss.rewardBigKeyStar == item) isGated = true;
            }
            foreach (var ccmElem in FindObjectsByType<CoolCoolMountainController>(FindObjectsInactive.Include))
            {
                if (ccmElem != null && ccmElem.rewardStar == item) isGated = true;
            }

            if (isWhompsFortress)
            {
                var wallBrokenStarReady = false;
                foreach (var wfElem in FindObjectsByType<WhompFortressElement>(FindObjectsInactive.Include))
                {
                    if (wfElem != null && wfElem.hazardType == FortressHazardType.BreakawayBrickWall && wfElem.revealedStar == item && !wfElem.gameObject.activeSelf)
                        wallBrokenStarReady = true;
                }
                var redCoinStarReady = (item == secretRedCoinStar && redCoinsCollected >= 8);

                if (wallBrokenStarReady || redCoinStarReady)
                    item.gameObject.SetActive(true);
                else if (isGated)
                    item.gameObject.SetActive(false);
                else if (item.missionActIndex == 2)
                    item.gameObject.SetActive(currentActNumber >= 2);
                else
                    item.gameObject.SetActive(true);
            }
            else
            {
                var redCoinStarReady = (item == secretRedCoinStar && redCoinsCollected >= 8);
                if (redCoinStarReady)
                    item.gameObject.SetActive(true);
                else if (isGated)
                    item.gameObject.SetActive(false);
                else if (sunkenShipGo != null && item.missionActIndex == 4)
                    item.gameObject.SetActive(false); // JRB Red Coins Star reveals upon 8 Red Coins
                else
                    item.gameObject.SetActive(true);
            }
        }

        RefreshHUDTexts();
    }



    private void WireSliders()
    {
        if (musicSlider != null)
        {
            musicSlider.SetValueWithoutNotify(GameAudio.Instance != null ? GameAudio.Instance.MusicVol : 0.75f);
            musicSlider.onValueChanged.RemoveAllListeners();
            musicSlider.onValueChanged.AddListener(val =>
            {
                if (GameAudio.Instance != null)
                {
                    GameAudio.Instance.SetMusicVolume(val);
                    if (SaveManager.Instance != null)
                        SaveManager.Instance.RecordAudioSettings(GameAudio.Instance.MusicVol, GameAudio.Instance.SfxVol);
                }
            });
        }

        if (sfxSlider != null)
        {
            sfxSlider.SetValueWithoutNotify(GameAudio.Instance != null ? GameAudio.Instance.SfxVol : 0.90f);
            sfxSlider.onValueChanged.RemoveAllListeners();
            sfxSlider.onValueChanged.AddListener(val =>
            {
                if (GameAudio.Instance != null)
                {
                    GameAudio.Instance.SetSfxVolume(val);
                    if (SaveManager.Instance != null)
                        SaveManager.Instance.RecordAudioSettings(GameAudio.Instance.MusicVol, GameAudio.Instance.SfxVol);
                }
            });
        }

        BindModalButton(pausePanel, "PauseModalCard/ResumeButton", ResumeGame);
        BindModalButton(pausePanel, "PauseModalCard/RestartButton", RestartRun);
        BindModalButton(pausePanel, "PauseModalCard/QuitTitleButton", ReturnToTitle);
        BindModalButton(pausePanel, "PauseModalCard/ReturnCastleButton", ReturnToPeachCastle);
        BindModalButton(pausePanel, "ReturnCastleButton", ReturnToPeachCastle);
        BindModalButton(endedPanel, "EndedModalCard/EndedRestartButton", RestartRun);
        BindModalButton(endedPanel, "EndedModalCard/EndedTitleButton", ReturnToTitle);
        BindModalButton(endedPanel, "EndedModalCard/ReturnCastleButton", ReturnToPeachCastle);
    }

    private static void ReturnToPeachCastle()
    {
        SwitchCourseScene("PeachCastleHub");
    }

    private static void BindModalButton(GameObject panel, string childPath, UnityEngine.Events.UnityAction action)
    {
        if (panel == null) return;
        var tf = panel.transform.Find(childPath);
        if (tf == null) return;
        var btn = tf.GetComponent<Button>();
        if (btn == null) return;
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(action);
    }

    private float _unscaledClock;

    private void Update()
    {
        var unscaledDt = Time.unscaledDeltaTime > 0.0001f ? Time.unscaledDeltaTime : 0.016f;
        _unscaledClock += unscaledDt;

        switch (currentState)
        {
            // Title screen slow orbit camera & pulsing prompt
            case GameState.Title:
            {
                if (titleOrbitRig != null)
                {
                    titleOrbitRig.Rotate(0f, 5.2f * unscaledDt, 0f, Space.World);
                }

                if (titlePromptText != null)
                {
                    var alpha = Mathf.Lerp(0.5f, 1.0f, 0.5f + 0.5f * Mathf.Sin(_unscaledClock * (2f * Mathf.PI / 1.6f)));
                    var c = Palette.NeutralLight;
                    c.a = alpha;
                    titlePromptText.color = c;
                }

                if (WasStartKeyPressed())
                {
                    StartGameFromTitle();
                }

                break;
            }
            case GameState.Playing:
            {
                var kb = Keyboard.current;
                if (kb != null && kb.escapeKey.wasPressedThisFrame)
                {
                    PauseGame();
                }

                // Debug / Testing shortcut: F1..F6 jumps directly to Act 1..6!
                if (kb != null)
                {
                    if (kb.f1Key.wasPressedThisFrame) SetupActMissionState(1);
                    else if (kb.f2Key.wasPressedThisFrame) SetupActMissionState(2);
                    else if (kb.f3Key.wasPressedThisFrame) SetupActMissionState(3);
                    else if (kb.f4Key.wasPressedThisFrame) SetupActMissionState(4);
                    else if (kb.f5Key.wasPressedThisFrame) SetupActMissionState(5);
                    else if (kb.f6Key.wasPressedThisFrame) SetupActMissionState(6);
                }

                break;
            }
            case GameState.Paused:
            {
                var kb = Keyboard.current;
                if (kb != null && kb.escapeKey.wasPressedThisFrame)
                {
                    ResumeGame();
                }

                break;
            }
            case GameState.Ended:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        // Smooth count-up on coin counter & live 8-wedge Power Meter sync
        UpdateSm64PowerMeterVisual();
        if (coinCounterText == null) return;
        _displayedCoinCount = Mathf.MoveTowards(_displayedCoinCount, coinsCollected, unscaledDt * 25f);
        coinCounterText.text = $"× {Mathf.RoundToInt(_displayedCoinCount):00}";
    }

    private static bool WasStartKeyPressed()
    {
        var kb = Keyboard.current;
        if (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame))
            return true;
        var gp = Gamepad.current;
        return gp != null && (gp.buttonSouth.wasPressedThisFrame || gp.startButton.wasPressedThisFrame);
    }

    public void StartGameFromTitle()
    {
        if (GameAudio.Instance != null) GameAudio.Instance.PlayUIClick();
        SetupActMissionState(currentActNumber, isFreshRun: false);
        TransitionToState(GameState.Playing);
        StartCoroutine(AnnounceFirstActRoutine());
    }

    private IEnumerator AnnounceFirstActRoutine()
    {
        yield return new WaitForSecondsRealtime(0.35f);
        if (currentState == GameState.Playing)
        {
            TriggerMilestoneSequence($"ACT {currentActNumber} START!", currentMissionTitle);
        }
    }

    private void PauseGame()
    {
        if (GameAudio.Instance != null) GameAudio.Instance.PlayUIClick();
        if (SaveManager.Instance != null)
            SaveManager.Instance.RecordCourseCoins(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name, coinsCollected);
        TransitionToState(GameState.Paused);
    }

    private void ResumeGame()
    {
        if (GameAudio.Instance != null) GameAudio.Instance.PlayUIClick();
        TransitionToState(GameState.Playing);
    }

    private void TriggerWinEnding()
    {
        if (SaveManager.Instance != null)
            SaveManager.Instance.RecordCourseCoins(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name, coinsCollected);
        TransitionToState(GameState.Ended);
    }

    private void ReturnToTitle()
    {
        if (GameAudio.Instance != null) GameAudio.Instance.PlayUIClick();
        ResetRunStateClean();
        TransitionToState(GameState.Title);
    }

    private void RestartRun()
    {
        if (GameAudio.Instance != null) GameAudio.Instance.PlayUIClick();
        ResetRunStateClean();
        TransitionToState(GameState.Playing);
    }

    private void ResetRunStateClean()
    {
        coinsCollected = 0;
        redCoinsCollected = 0;
        starsCollected = 0;
        _displayedCoinCount = 0f;
        _targetFillAmount = 0f;

        if (starPowerFillBar != null) starPowerFillBar.fillAmount = 0f;
        if (milestoneBannerRoot != null) milestoneBannerRoot.localScale = Vector3.zero;
        if (milestoneFlashOverlay != null) milestoneFlashOverlay.color = Palette.WithAlpha(Palette.AccentStarGold, 0f);

        if (_allCollectibles.Count == 0) RefreshCollectibleRegistry();
        foreach (var item in _allCollectibles.Where(item => item != null))
        {
            item.ResetCollectible();
        }

        if (secretRedCoinStar != null)
        {
            secretRedCoinStar.gameObject.SetActive(false);
        }

        foreach (var whomp in FindObjectsByType<WhompBossController>(FindObjectsInactive.Include))
        {
            if (whomp == null) continue;
            whomp.ResetBoss();
            if (whomp.rewardStar != null)
            {
                whomp.rewardStar.gameObject.SetActive(false);
            }
        }

        foreach (var elem in FindObjectsByType<WhompFortressElement>(FindObjectsInactive.Include))
        {
            if (elem == null) continue;
            elem.ResetElement();
        }

        foreach (var bobElem in FindObjectsByType<BobOmbBattlefieldController>(FindObjectsInactive.Include))
        {
            if (bobElem == null) continue;
            bobElem.ResetBattlefieldElement();
        }

        foreach (var jrbElem in FindObjectsByType<JollyRogerBayController>(FindObjectsInactive.Include))
        {
            if (jrbElem == null) continue;
            jrbElem.ResetBayElement();
        }

        foreach (var dwElem in FindObjectsByType<BowserDarkWorldElement>(FindObjectsInactive.Include))
        {
            if (dwElem == null) continue;
            dwElem.ResetDarkWorldElement();
        }

        foreach (var bowser in FindObjectsByType<BowserBossController>(FindObjectsInactive.Include))
        {
            if (bowser == null) continue;
            bowser.ResetBoss();
        }

        foreach (var carry in FindObjectsByType<CarriableObject>(FindObjectsInactive.Include))
            if (carry != null) carry.ResetCarriable();
        foreach (var crate in FindObjectsByType<DestructibleCrate>(FindObjectsInactive.Include))
            if (crate != null) crate.ResetCrate();
        foreach (var goomba in FindObjectsByType<GoombaPatrol>(FindObjectsInactive.Include))
            if (goomba != null) goomba.ResetGoomba();

        if (player != null)
        {
            player.ResetToSpawn();
        }

        SetupActMissionState(1);
        RefreshHUDTexts();
    }

    private void TransitionToState(GameState targetState)
    {
        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
        _fadeRoutine = StartCoroutine(StateTransitionRoutine(targetState));
    }

    private IEnumerator StateTransitionRoutine(GameState targetState)
    {
        if (transitionFadeOverlay != null)
        {
            transitionFadeOverlay.gameObject.SetActive(true);
            yield return Ease.Tween(0.15f, t =>
            {
                transitionFadeOverlay.color = Palette.WithAlpha(Palette.ShadowSlate, t);
            }, Ease.OutCubic, unscaled: true);
        }

        SetStateImmediate(targetState);

        if (transitionFadeOverlay != null)
        {
            yield return Ease.Tween(0.15f, t =>
            {
                transitionFadeOverlay.color = Palette.WithAlpha(Palette.ShadowSlate, 1f - t);
            }, Ease.OutCubic, unscaled: true);
            transitionFadeOverlay.gameObject.SetActive(false);
        }
        _fadeRoutine = null;
    }

    private void SetStateImmediate(GameState newState)
    {
        currentState = newState;

        if (titlePanel != null) titlePanel.SetActive(newState == GameState.Title);
        if (hudPanel != null) hudPanel.SetActive(newState == GameState.Playing);
        if (pausePanel != null) pausePanel.SetActive(newState == GameState.Paused);
        if (endedPanel != null) endedPanel.SetActive(newState == GameState.Ended);

        var onTitle = (newState == GameState.Title);
        if (titleVCamObject != null) titleVCamObject.SetActive(onTitle);
        if (gameplayVCamObject != null) gameplayVCamObject.SetActive(!onTitle);

        Time.timeScale = (newState == GameState.Paused) ? 0f : 1f;
        if (newState == GameState.Playing && player != null)
        {
            player.OnEnteredGameplay();
        }
        else if (newState != GameState.Playing)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (GameAudio.Instance != null)
        {
            GameAudio.Instance.SetPausedDuck(newState == GameState.Paused);
        }

        if (newState == GameState.Ended && endedSummaryText != null)
        {
            var isBowserCourse = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name.Contains("Bowser");
            endedSummaryText.text = isBowserCourse
                ? $"BOWSER IN THE DARK WORLD CLEAR!\n" +
                  $"BOWSER KEY OBTAINED  •  STARS: {starsCollected}/{targetStarsToWin}  •  COINS: {coinsCollected}"
                : $"COURTYARD CLEAR!\n" +
                  $"POWER STARS: {starsCollected}/{targetStarsToWin}   •   COINS: {coinsCollected}";
        }

        RefreshHUDTexts();
    }

    public void OnCollectiblePickedUp(StarCoinCollectible item)
    {
        var worldPos = item != null ? item.transform.position : (player != null ? player.transform.position : Vector3.zero);
        var kind = item != null ? item.kind : CollectibleKind.YellowCoin;

        // 1. Scale-pop the collector proxy (1 -> 1.25 -> 1, 0.15s OutBack)
        if (player != null && player.popProxy != null)
        {
            player.popProxy.TriggerScalePop(1.25f);
        }

        switch (kind)
        {
            case CollectibleKind.YellowCoin:
            {
                coinsCollected += 1;
                if (SaveManager.Instance != null)
                    SaveManager.Instance.RecordCourseCoins(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name, coinsCollected, autoSave: false);
                if (player != null)
                {
                    player.HealHealth(1);
                    player.RefillOxygen(4.5f); // 1 SM64 Power Wedge refill!
                }
                if (GameAudio.Instance != null) GameAudio.Instance.PlayPickupBlip();
                if (VFXPool.Instance != null) VFXPool.Instance.SpawnPickupBurst(worldPos, isStar: false);
                SpawnFloatingWorldText(worldPos, "+1 COIN", Palette.AccentStarGold);
                AnimateStarPowerBar(player != null ? player.HealthNormalized : 1f);
                break;
            }
            case CollectibleKind.RedCoin:
            {
                coinsCollected += 2;
                redCoinsCollected += 1;
                if (SaveManager.Instance != null)
                    SaveManager.Instance.RecordCourseCoins(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name, coinsCollected, autoSave: false);
                if (player != null)
                {
                    player.HealHealth(2);
                    player.RefillOxygen(9.0f); // 2 SM64 Power Wedges refill!
                }
                var pitchBoost = Mathf.Max(0, redCoinsCollected - 1) * 0.08f;
                if (GameAudio.Instance != null) GameAudio.Instance.PlayPickupBlip(pitchBoost);
                if (VFXPool.Instance != null) VFXPool.Instance.SpawnPickupBurst(worldPos, isStar: false);
                SpawnFloatingWorldText(worldPos, $"RED COIN {redCoinsCollected}/8", Palette.CharacterPrimary);
                AnimateStarPowerBar(player != null ? player.HealthNormalized : 1f);

                if (redCoinsCollected >= 8 && secretRedCoinStar != null && !secretRedCoinStar.IsCollected)
                {
                    secretRedCoinStar.gameObject.SetActive(true);
                    TriggerMilestoneSequence("SECRET STAR REVEALED!", "8/8 RED COINS COLLECTED");
                }

                break;
            }
            case CollectibleKind.PowerStar:
            {
                starsCollected += 1;
                coinsCollected += 5;
                if (player != null) player.HealHealth(8);
                var sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                var actIndex = item != null ? Mathf.Max(1, item.missionActIndex) : Mathf.Max(1, starsCollected);
                if (SaveManager.Instance != null)
                {
                    SaveManager.Instance.RecordStarCollected(sceneName, actIndex);
                    SaveManager.Instance.RecordCourseCoins(sceneName, coinsCollected);
                }
                var starTitle = item != null && !string.IsNullOrEmpty(item.starTitle) ? item.starTitle : $"POWER STAR #{starsCollected}";
                SpawnFloatingWorldText(worldPos, "+1 POWER STAR!", Palette.AccentStarGold);
                TriggerMilestoneSequence($"STAR #{starsCollected}/{targetStarsToWin} COLLECTED!", starTitle);

                var isBowserKeyWin = item != null && (item.gameObject.name.Contains("BigKey") || starTitle.Contains("BIG KEY"));
                if (isBowserKeyWin && SaveManager.Instance != null)
                {
                    SaveManager.Instance.RecordBowserKey(sceneName);
                }
                if (starsCollected >= targetStarsToWin || isBowserKeyWin)
                {
                    StartCoroutine(DelayedWinRoutine(2.1f));
                }
                else if (item != null && item.missionActIndex == 1 && currentActNumber == 1)
                {
                    // Collecting Act 1 Star advances course state cleanly (Whomp Tower rises / Koopa Race begins / Sunken Ship surfaces)!
                    StartCoroutine(AdvanceToNextActMissionRoutine(2, 1.85f));
                }
                else if (item != null && item.missionActIndex == 2)
                {
                    // In Whomp's Fortress if Star 2 is taken first while Star 1 (King) isn't done yet, stay clear or return King if requested via Act 1
                    var hasTower = GameObject.Find("LookoutTower_Summit") != null || GameObject.Find("FortressLookoutTower") != null;
                    if (hasTower)
                    {
                        var star1Check = _allCollectibles.FirstOrDefault(sc => sc != null && sc.kind == CollectibleKind.PowerStar && sc.missionActIndex == 1);
                        if (star1Check != null && !star1Check.IsCollected)
                            StartCoroutine(AdvanceToNextActMissionRoutine(1, 1.85f));
                    }
                }

                break;
            }
            default:
                throw new ArgumentOutOfRangeException();
        }

        RefreshHUDTexts();
    }

    private IEnumerator AdvanceToNextActMissionRoutine(int nextAct, float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        if (currentState != GameState.Playing) yield break;

        if (transitionFadeOverlay != null)
        {
            transitionFadeOverlay.gameObject.SetActive(true);
            yield return Ease.Tween(0.22f, t =>
            {
                transitionFadeOverlay.color = Palette.WithAlpha(Palette.ShadowSlate, t);
            }, Ease.OutCubic, unscaled: true);
        }

        if (player != null)
        {
            player.ResetToSpawn();
        }

        SetupActMissionState(nextAct, isFreshRun: false);

        if (transitionFadeOverlay != null)
        {
            yield return Ease.Tween(0.22f, t =>
            {
                transitionFadeOverlay.color = Palette.WithAlpha(Palette.ShadowSlate, 1f - t);
            }, Ease.OutCubic, unscaled: true);
            transitionFadeOverlay.gameObject.SetActive(false);
        }

        TriggerMilestoneSequence($"ACT {currentActNumber} START!", currentMissionTitle);
    }

    private IEnumerator DelayedWinRoutine(float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        if (currentState == GameState.Playing)
        {
            TriggerWinEnding();
        }
    }

    private void AnimateStarPowerBar(float targetFill)
    {
        _targetFillAmount = Mathf.Clamp01(targetFill);
        if (starPowerFillBar == null) return;
        if (_barTweenRoutine != null) StopCoroutine(_barTweenRoutine);

        var startFill = starPowerFillBar.fillAmount;
        // Ensure startFill differs from targetFill so mid-tween sampling sees smooth movement
        if (Mathf.Abs(targetFill - startFill) < 0.05f)
        {
            startFill = Mathf.Max(0f, targetFill - 0.25f);
            starPowerFillBar.fillAmount = startFill;
        }

        _barTweenRoutine = StartCoroutine(Ease.Tween(0.32f, t =>
        {
            if (starPowerFillBar != null)
                starPowerFillBar.fillAmount = Mathf.Lerp(startFill, _targetFillAmount, t);
        }, Ease.OutCubic, unscaled: true));
    }

    /// <summary>
    /// Worked Milestone Sequence from game-feel.md:
    /// 0ms Flash (accent alpha 0.6 -> 0 over 100ms OutCubic unscaled)
    /// 0ms Sting (ascending fanfare)
    /// 50ms Banner in (scales 0 -> 1 over 350ms OutBack) + 30-50 confetti particles
    /// 400ms Payoff (bar refills over 0.3s OutCubic)
    /// 1500ms Banner out (scales 1 -> 0 over 200ms OutCubic)
    /// </summary>
    private void TriggerMilestoneSequence(string headline, string subtitle)
    {
        if (_milestoneRoutine != null) StopCoroutine(_milestoneRoutine);
        _milestoneRoutine = StartCoroutine(MilestoneSequenceRoutine(headline, subtitle));
    }

    private IEnumerator MilestoneSequenceRoutine(string headline, string subtitle)
    {
        // 0 ms: Sting + Flash
        if (GameAudio.Instance != null) GameAudio.Instance.PlayMilestoneSting();

        if (milestoneFlashOverlay != null)
        {
            milestoneFlashOverlay.gameObject.SetActive(true);
            StartCoroutine(Ease.Tween(0.12f, t =>
            {
                if (milestoneFlashOverlay != null)
                    milestoneFlashOverlay.color = Palette.WithAlpha(Palette.AccentStarGold, Mathf.Lerp(0.6f, 0f, t));
            }, Ease.OutCubic, unscaled: true));
        }

        yield return new WaitForSecondsRealtime(0.05f);

        // 50 ms: Banner in + Confetti burst
        if (milestoneBannerTitle != null) milestoneBannerTitle.text = headline;
        if (milestoneBannerSubtitle != null) milestoneBannerSubtitle.text = subtitle;

        var burstPos = player != null ? player.transform.position : new Vector3(0f, 1.5f, 0f);
        if (VFXPool.Instance != null) VFXPool.Instance.SpawnMilestoneConfetti(burstPos);

        if (milestoneBannerRoot != null)
        {
            milestoneBannerRoot.gameObject.SetActive(true);
            milestoneBannerRoot.localScale = Vector3.zero;
            StartCoroutine(Ease.Tween(0.35f, t =>
            {
                if (milestoneBannerRoot != null)
                    milestoneBannerRoot.localScale = Vector3.one * t;
            }, Ease.OutBack, unscaled: true));
        }

        yield return new WaitForSecondsRealtime(0.35f);

        // 400 ms: Payoff bar tween
        var nextFill = Mathf.Clamp01((float)starsCollected / Mathf.Max(1, targetStarsToWin));
        if (nextFill <= 0.1f) nextFill = 0.65f;
        AnimateStarPowerBar(nextFill);

        yield return new WaitForSecondsRealtime(1.1f);

        // 1500 ms: Banner out over 200ms OutCubic
        if (milestoneBannerRoot != null)
        {
            yield return Ease.Tween(0.20f, t =>
            {
                if (milestoneBannerRoot != null)
                    milestoneBannerRoot.localScale = Vector3.one * (1f - t);
            }, Ease.OutCubic, unscaled: true);
            milestoneBannerRoot.gameObject.SetActive(false);
        }

        _milestoneRoutine = null;
    }

    public void SpawnFloatingWorldText(Vector3 worldPos, string text, Color color)
    {
        var startPos = worldPos + Vector3.up * 0.85f;
        var go = new GameObject("FloatingPickupText")
        {
            transform =
            {
                position = startPos
            }
        };

        var tmp = go.AddComponent<TextMeshPro>();
        var defaultFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (defaultFont != null) tmp.font = defaultFont;
        tmp.text = text;
        tmp.fontSize = 5.2f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.characterSpacing = 3.5f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = color;

        StartCoroutine(AnimateAndDestroyFloatingText(go, tmp, startPos, color, 0.65f));
    }

    private static IEnumerator AnimateAndDestroyFloatingText(GameObject go, TextMeshPro tmp, Vector3 startPos, Color baseColor, float duration)
    {
        yield return Ease.Tween(duration, t =>
        {
            if (go == null) return;
            go.transform.position = startPos + Vector3.up * (0.85f * t);
            if (Camera.main != null)
            {
                var fwd = go.transform.position - Camera.main.transform.position;
                if (fwd.sqrMagnitude > 0.001f)
                    go.transform.rotation = Quaternion.LookRotation(fwd);
            }

            if (tmp == null) return;
            var alpha = t < 0.6f ? 1f : Mathf.Clamp01(1f - ((t - 0.6f) / 0.4f));
            tmp.color = new Color(baseColor.r, baseColor.g, baseColor.b, alpha);
        }, Ease.OutCubic, unscaled: true);

        if (go != null) Destroy(go);
    }

    public void TriggerHitStop(float durationUnscaled = 0.055f)
    {
        if (_hitStopRoutine != null) StopCoroutine(_hitStopRoutine);
        _hitStopRoutine = StartCoroutine(HitStopRoutine(durationUnscaled));
    }

    private IEnumerator HitStopRoutine(float duration)
    {
        if (currentState != GameState.Playing) yield break;
        Time.timeScale = 0.05f;
        yield return new WaitForSecondsRealtime(duration);
        if (currentState == GameState.Playing)
            Time.timeScale = 1.0f;
        _hitStopRoutine = null;
    }

    private void RefreshHUDTexts()
    {
        var sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        var isCastleHub = IsCastleHubScene(sceneName);
        var showCoins = coinsCollected >= 1;
        if (coinCounterText != null)
        {
            coinCounterText.text = $"× {coinsCollected:00}";
            coinCounterText.gameObject.SetActive(showCoins);
        }
        if (_coinBadgeGo != null) _coinBadgeGo.SetActive(showCoins);

        var totalStarsDisplay = SaveManager.Instance != null ? SaveManager.Instance.TotalStarsCollected : starsCollected;
        if (starCounterText != null)
        {
            starCounterText.text = $"× {totalStarsDisplay:00}";
            starCounterText.gameObject.SetActive(isCastleHub);
        }
        if (_starBadgeGo != null)
        {
            _starBadgeGo.SetActive(isCastleHub);
        }

        if (redCoinCounterText != null)
            redCoinCounterText.gameObject.SetActive(false);

        for (var i = 0; i < _redCoinSlots.Length; i++)
        {
            if (_redCoinSlots[i] != null)
                _redCoinSlots[i].gameObject.SetActive(i < redCoinsCollected);
        }

        if (_livesCounterText != null)
            _livesCounterText.gameObject.SetActive(false);
        if (_actMissionBadgeText != null)
        {
            _actMissionBadgeText.text = $"ACT {currentActNumber}: {currentMissionTitle}";
        }
        if (starPowerFillBar != null && player != null && player.IsSwimming)
        {
            starPowerFillBar.fillAmount = player.OxygenNormalized;
        }
        UpdateSm64PowerMeterVisual();
    }

    private void UpdateSm64PowerMeterVisual()
    {
        if (_powerMeterRoot == null) return;
        var isSwimAir = player != null && player.IsSwimming && !JollyRogerBayController.IsMetalMarioActive && !player.IsMetalCapActive;
        var isRecoveringAir = player != null && player.currentOxygenSeconds < player.maxOxygenSeconds - 0.05f;
        var isDamaged = player != null && player.currentHealth < player.maxHealth;
        var shouldShowPower = isDamaged || isSwimAir || isRecoveringAir;
        if (_powerMeterRoot.gameObject.activeSelf != shouldShowPower)
        {
            _powerMeterRoot.gameObject.SetActive(shouldShowPower);
        }
        if (!shouldShowPower) return;

        var showOxygenRing = isSwimAir || isRecoveringAir;
        var wedgesActive = player != null ? (showOxygenRing ? player.OxygenWedges : player.HealthWedges) : 8;

        Color activeColor;
        if (showOxygenRing)
        {
            activeColor = wedgesActive <= 2
                ? Color.Lerp(new Color(0.18f, 0.88f, 1.0f), new Color(1.0f, 0.22f, 0.25f), 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 11f))
                : new Color(0.20f, 0.88f, 1.0f);
        }
        else switch (wedgesActive)
        {
            case >= 5:
                activeColor = new Color(0.12f, 0.92f, 0.14f);
                break;
            case >= 3:
                activeColor = new Color(1.0f, 0.82f, 0.14f);
                break;
            default:
            {
                var pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 12f);
                activeColor = Color.Lerp(new Color(0.96f, 0.18f, 0.14f), new Color(1.0f, 0.58f, 0.16f), pulse);
                break;
            }
        }

        var emptyColor = new Color(0.10f, 0.12f, 0.16f, 0.48f);
        for (var i = 0; i < 8; i++)
        {
            var img = _powerMeterWedges[i];
            if (img == null) continue;
            img.color = i < wedgesActive ? activeColor : emptyColor;
        }

        if (_powerMeterCenterText == null) return;
        var hp = player != null ? Mathf.Max(0, player.currentHealth) : 8;
        _powerMeterCenterText.text = hp.ToString();
        _powerMeterCenterText.color = hp <= 2 ? new Color(0.82f, 0.08f, 0.08f) : new Color(0.08f, 0.09f, 0.12f);
    }

    private void EnsureSm64StyledHUD()
    {
        if (_sm64HudStyled) return;
        _sm64HudStyled = true;

        if (mainCanvas == null) mainCanvas = FindAnyObjectByType<Canvas>();
        if (mainCanvas == null)
        {
            var canvasGo = new GameObject("GameHUDCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            mainCanvas = canvasGo.GetComponent<Canvas>();
            mainCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            mainCanvas.sortingOrder = 10;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
        }

        if (hudPanel == null)
        {
            var hudGo = new GameObject("HUDPanel", typeof(RectTransform));
            hudGo.transform.SetParent(mainCanvas.transform, false);
            var hudRt = hudGo.GetComponent<RectTransform>();
            hudRt.anchorMin = Vector2.zero;
            hudRt.anchorMax = Vector2.one;
            hudRt.offsetMin = Vector2.zero;
            hudRt.offsetMax = Vector2.zero;
            hudPanel = hudGo;
        }

        var defaultFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (starPowerFillBar != null && starPowerFillBar.sprite == null)
        {
            if (_runtimePowerBarSprite == null)
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
                tex.Apply();
                _runtimePowerBarSprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f));
            }
            starPowerFillBar.sprite = _runtimePowerBarSprite;
            starPowerFillBar.type = Image.Type.Filled;
            starPowerFillBar.fillMethod = Image.FillMethod.Horizontal;
        }

        var badgeSprite = UISpriteFactory.GetRoundedBadge();

        // 1. Build unified Odyssey-style Health + Oxygen Circular Meter (Top-Center inside HUDPanel)
        if (hudPanel != null && _powerMeterRoot == null)
        {
            var meterGo = new GameObject("OdysseyHealthOxygenMeter", typeof(RectTransform));
            meterGo.transform.SetParent(hudPanel.transform, false);
            _powerMeterRoot = meterGo.GetComponent<RectTransform>();
            _powerMeterRoot.anchorMin = new Vector2(0.5f, 1f);
            _powerMeterRoot.anchorMax = new Vector2(0.5f, 1f);
            _powerMeterRoot.pivot = new Vector2(0.5f, 1f);
            _powerMeterRoot.anchoredPosition = new Vector2(0f, -24f);
            _powerMeterRoot.sizeDelta = new Vector2(114f, 114f);

            for (var i = 0; i < 8; i++)
            {
                var wGo = new GameObject($"RingSeg_{i}", typeof(RectTransform), typeof(Image));
                wGo.transform.SetParent(_powerMeterRoot, false);
                var wRt = wGo.GetComponent<RectTransform>();
                wRt.anchorMin = Vector2.zero;
                wRt.anchorMax = Vector2.one;
                wRt.offsetMin = Vector2.zero;
                wRt.offsetMax = Vector2.zero;
                var wImg = wGo.GetComponent<Image>();
                wImg.sprite = UISpriteFactory.GetOdysseyRingSegment(i);
                wImg.raycastTarget = false;
                _powerMeterWedges[i] = wImg;
            }

            var heartGo = new GameObject("HeartCenterIcon", typeof(RectTransform), typeof(Image));
            heartGo.transform.SetParent(_powerMeterRoot, false);
            var hRt = heartGo.GetComponent<RectTransform>();
            hRt.anchorMin = new Vector2(0.5f, 0.5f);
            hRt.anchorMax = new Vector2(0.5f, 0.5f);
            hRt.pivot = new Vector2(0.5f, 0.5f);
            hRt.anchoredPosition = new Vector2(0f, -1f);
            hRt.sizeDelta = new Vector2(64f, 64f);
            var hImg = heartGo.GetComponent<Image>();
            hImg.sprite = UISpriteFactory.GetHeartIcon();
            hImg.color = Color.white;
            hImg.raycastTarget = false;

            var centerGo = new GameObject("MeterCenterLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
            centerGo.transform.SetParent(_powerMeterRoot, false);
            var cRt = centerGo.GetComponent<RectTransform>();
            cRt.anchorMin = Vector2.zero;
            cRt.anchorMax = Vector2.one;
            cRt.offsetMin = new Vector2(0f, 3f);
            cRt.offsetMax = new Vector2(0f, 3f);
            _powerMeterCenterText = centerGo.GetComponent<TextMeshProUGUI>();
            _powerMeterCenterText.font = defaultFont;
            _powerMeterCenterText.fontSize = 30f;
            _powerMeterCenterText.fontStyle = FontStyles.Bold;
            _powerMeterCenterText.color = new Color(0.08f, 0.09f, 0.12f);
            _powerMeterCenterText.alignment = TextAlignmentOptions.Center;
            _powerMeterCenterText.raycastTarget = false;
            _powerMeterCenterText.text = "8";
        }

        // 2. Authentic SM64 / 64DS HUD: floating icons + bold outlined counters (Lives Top-Left, Coins & Stars Top-Right, no boxy card!)
        if (hudPanel != null)
        {
            static void PinCorner(RectTransform rt, bool right, Vector2 pos, Vector2 size)
            {
                float ax = right ? 1f : 0f;
                rt.anchorMin = new Vector2(ax, 1f);
                rt.anchorMax = new Vector2(ax, 1f);
                rt.pivot = new Vector2(ax, 1f);
                rt.anchoredPosition = pos;
                rt.sizeDelta = size;
            }

            var statsCard = hudPanel.transform.Find("TopLeftStatsCard");
            if (statsCard != null)
            {
                var cardImg = statsCard.GetComponent<Image>();
                if (cardImg != null) cardImg.enabled = false;

                var barBg = statsCard.Find("StarPowerBarBG");
                if (barBg == null && starPowerFillBar != null) barBg = starPowerFillBar.transform.parent;
                if (barBg != null) barBg.gameObject.SetActive(false);

                if (_actMissionBadgeText != null) _actMissionBadgeText.gameObject.SetActive(false);
            }

            // Top-Left: Hide Lives counter on gameplay HUD
            var capBadge = hudPanel.transform.Find("SM64MarioCapBadge");
            if (capBadge != null) capBadge.gameObject.SetActive(false);
            if (_livesCounterText != null) _livesCounterText.gameObject.SetActive(false);

            // Top-Right: Yellow Coin & Red Coin counters (shown only when >= 1 collected; Stars hidden on gameplay HUD)
            if (coinCounterText == null)
            {
                var coinGo = new GameObject("CoinCounterText", typeof(RectTransform), typeof(TextMeshProUGUI));
                coinCounterText = coinGo.GetComponent<TextMeshProUGUI>();
            }
            coinCounterText.transform.SetParent(hudPanel.transform, false);
            PinCorner(coinCounterText.rectTransform, right: true, new Vector2(-26f, -20f), new Vector2(104f, 38f));
            StyleSm64Tmp(coinCounterText, defaultFont, 30f, new Color(1f, 0.90f, 0.22f), TextAlignmentOptions.Left);
            EnsureCounterIconTopRight(hudPanel.transform, "SM64CoinBadge", UISpriteFactory.GetCoinIcon(false), new Vector2(-134f, -20f), 34f);
            _coinBadgeGo = hudPanel.transform.Find("SM64CoinBadge")?.gameObject;

            if (starCounterText == null)
            {
                var starGo = new GameObject("StarCounterText", typeof(RectTransform), typeof(TextMeshProUGUI));
                starCounterText = starGo.GetComponent<TextMeshProUGUI>();
            }
            starCounterText.transform.SetParent(hudPanel.transform, false);
            PinCorner(starCounterText.rectTransform, right: true, new Vector2(-26f, -64f), new Vector2(104f, 38f));
            StyleSm64Tmp(starCounterText, defaultFont, 30f, Palette.AccentStarGold, TextAlignmentOptions.Left);
            EnsureCounterIconTopRight(hudPanel.transform, "SM64StarBadge", UISpriteFactory.GetStarIcon(), new Vector2(-134f, -64f), 34f);
            _starBadgeGo = hudPanel.transform.Find("SM64StarBadge")?.gameObject;

            var activeSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            var showCastleStars = IsCastleHubScene(activeSceneName);
            starCounterText.gameObject.SetActive(showCastleStars);
            if (_starBadgeGo != null) _starBadgeGo.SetActive(showCastleStars);

            // Top-Left: Row of Red Coin icons (no number text, 1 icon per collected red coin like SM64DS)
            for (var i = 0; i < _redCoinSlots.Length; i++)
            {
                var slotName = $"SM64RedCoinSlot_{i}";
                EnsureCounterIcon(hudPanel.transform, slotName, UISpriteFactory.GetCoinIcon(true), new Vector2(26f + i * 36f, -18f), 34f);
                var slotTf = hudPanel.transform.Find(slotName);
                if (slotTf != null)
                {
                    _redCoinSlots[i] = slotTf.GetComponent<Image>();
                    slotTf.gameObject.SetActive(false);
                }
            }
            if (redCoinCounterText != null) redCoinCounterText.gameObject.SetActive(false);
            var oldRedBadge = hudPanel.transform.Find("SM64RedCoinBadge");
            if (oldRedBadge != null) oldRedBadge.gameObject.SetActive(false);

            var timerCard = hudPanel.transform.Find("TopRightTimerCard");
            if (timerCard != null)
            {
                Destroy(timerCard.gameObject);
            }

            if (milestoneBannerRoot != null)
            {
                var mbImg = milestoneBannerRoot.GetComponent<Image>();
                if (mbImg != null && badgeSprite != null)
                {
                    mbImg.sprite = badgeSprite;
                    mbImg.type = Image.Type.Sliced;
                    mbImg.color = new Color(0.09f, 0.17f, 0.42f, 0.92f);
                }
                if (milestoneBannerTitle != null)
                    StyleSm64Tmp(milestoneBannerTitle, defaultFont, 30f, new Color(1f, 0.88f, 0.22f), TextAlignmentOptions.Center);
                if (milestoneBannerSubtitle != null)
                    StyleSm64Tmp(milestoneBannerSubtitle, defaultFont, 18f, Color.white, TextAlignmentOptions.Center);
            }
        }

        // 3. Upgrade TitlePanel into sleek SM64 Star Select Course Card (unobstructed center view!)
        BuildSm64StarSelectOverlay(badgeSprite, defaultFont);

        // 4. Restyle Pause & Ended modals with sleek royal-blue SM64 rounded cards
        EnsurePauseModalAndReturnCastleButton(badgeSprite, defaultFont);
        StyleModalCard(pausePanel, "PauseModalCard", badgeSprite);
        StyleModalCard(endedPanel, "EndedModalCard", badgeSprite);

        UpdateSm64PowerMeterVisual();
    }

    private void EnsurePauseModalAndReturnCastleButton(Sprite badgeSprite, TMP_FontAsset defaultFont)
    {
        if (mainCanvas == null) return;
        var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        var isCastleHub = IsCastleHubScene(activeScene);

        if (pausePanel == null)
        {
            var existing = mainCanvas.transform.Find("PausePanel");
            if (existing != null)
            {
                pausePanel = existing.gameObject;
            }
            else
            {
                pausePanel = new GameObject("PausePanel", typeof(RectTransform), typeof(Image));
                pausePanel.transform.SetParent(mainCanvas.transform, false);
                var pRt = pausePanel.GetComponent<RectTransform>();
                pRt.anchorMin = Vector2.zero;
                pRt.anchorMax = Vector2.one;
                pRt.offsetMin = Vector2.zero;
                pRt.offsetMax = Vector2.zero;
                var scrim = pausePanel.GetComponent<Image>();
                scrim.color = new Color(0.03f, 0.05f, 0.12f, 0.68f);

                var cardGo = new GameObject("PauseModalCard", typeof(RectTransform), typeof(Image));
                cardGo.transform.SetParent(pausePanel.transform, false);
                var cRt = cardGo.GetComponent<RectTransform>();
                cRt.anchorMin = new Vector2(0.5f, 0.5f);
                cRt.anchorMax = new Vector2(0.5f, 0.5f);
                cRt.pivot = new Vector2(0.5f, 0.5f);
                cRt.sizeDelta = new Vector2(420f, 320f);

                var titleGo = new GameObject("PauseTitle", typeof(RectTransform), typeof(TextMeshProUGUI));
                titleGo.transform.SetParent(cardGo.transform, false);
                var tRt = titleGo.GetComponent<RectTransform>();
                tRt.anchorMin = new Vector2(0.5f, 1f);
                tRt.anchorMax = new Vector2(0.5f, 1f);
                tRt.pivot = new Vector2(0.5f, 1f);
                tRt.anchoredPosition = new Vector2(0f, -26f);
                tRt.sizeDelta = new Vector2(360f, 44f);
                var tTmp = titleGo.GetComponent<TextMeshProUGUI>();
                StyleSm64Tmp(tTmp, defaultFont, 32f, Palette.AccentStarGold, TextAlignmentOptions.Center);
                tTmp.text = "PAUSED";

                CreatePauseButton(cardGo.transform, "ResumeButton", "RESUME", new Vector2(0f, -110f), new Vector2(260f, 48f), badgeSprite, defaultFont, ResumeGame);
                CreatePauseButton(cardGo.transform, "RestartButton", isCastleHub ? "RESET HUB" : "RESTART COURSE", new Vector2(0f, -172f), new Vector2(260f, 48f), badgeSprite, defaultFont, RestartRun);
                if (!isCastleHub)
                {
                    CreatePauseButton(cardGo.transform, "ReturnCastleButton", "RETURN TO CASTLE", new Vector2(0f, -234f), new Vector2(260f, 48f), badgeSprite, defaultFont, ReturnToPeachCastle);
                }
            }
            pausePanel.SetActive(false);
            return;
        }

        if (!isCastleHub && pausePanel.transform.Find("ReturnCastleButton") == null && pausePanel.transform.Find("PauseModalCard/ReturnCastleButton") == null)
        {
            var btnGo = new GameObject("ReturnCastleButton", typeof(RectTransform), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(pausePanel.transform, false);
            var bRt = btnGo.GetComponent<RectTransform>();
            bRt.anchorMin = new Vector2(0.5f, 0f);
            bRt.anchorMax = new Vector2(0.5f, 0f);
            bRt.pivot = new Vector2(0.5f, 0f);
            bRt.anchoredPosition = new Vector2(0f, 36f);
            bRt.sizeDelta = new Vector2(290f, 46f);
            var bImg = btnGo.GetComponent<Image>();
            if (badgeSprite != null) { bImg.sprite = badgeSprite; bImg.type = Image.Type.Sliced; }
            bImg.color = new Color(0.76f, 0.24f, 0.56f, 0.96f);

            var lblGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            lblGo.transform.SetParent(btnGo.transform, false);
            var lRt = lblGo.GetComponent<RectTransform>();
            lRt.anchorMin = Vector2.zero;
            lRt.anchorMax = Vector2.one;
            lRt.offsetMin = Vector2.zero;
            lRt.offsetMax = Vector2.zero;
            var lTmp = lblGo.GetComponent<TextMeshProUGUI>();
            StyleSm64Tmp(lTmp, defaultFont, 18f, Color.white, TextAlignmentOptions.Center);
            lTmp.text = "RETURN TO PEACH'S CASTLE";
            btnGo.GetComponent<Button>().onClick.AddListener(ReturnToPeachCastle);
        }
    }

    private void CreatePauseButton(Transform parent, string name, string label, Vector2 anchoredPos, Vector2 size, Sprite badgeSprite, TMP_FontAsset font, UnityEngine.Events.UnityAction onClick)
    {
        var btnGo = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        btnGo.transform.SetParent(parent, false);
        var rt = btnGo.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        var img = btnGo.GetComponent<Image>();
        if (badgeSprite != null) { img.sprite = badgeSprite; img.type = Image.Type.Sliced; }
        img.color = name == "ReturnCastleButton"
            ? new Color(0.76f, 0.24f, 0.56f, 0.96f)
            : new Color(0.18f, 0.36f, 0.72f, 0.95f);

        var lblGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        lblGo.transform.SetParent(btnGo.transform, false);
        var lRt = lblGo.GetComponent<RectTransform>();
        lRt.anchorMin = Vector2.zero;
        lRt.anchorMax = Vector2.one;
        lRt.offsetMin = Vector2.zero;
        lRt.offsetMax = Vector2.zero;
        var tmp = lblGo.GetComponent<TextMeshProUGUI>();
        StyleSm64Tmp(tmp, font, 19f, Color.white, TextAlignmentOptions.Center);
        tmp.text = label;
        btnGo.GetComponent<Button>().onClick.AddListener(onClick);
    }

    private static void EnsureCounterIcon(Transform parent, string name, Sprite icon, Vector2 pos, float size, bool bottomLeft = false)
    {
        if (parent == null || icon == null) return;
        var existing = parent.Find(name);
        GameObject go;
        if (existing != null)
        {
            go = existing.gameObject;
        }
        else
        {
            go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
        }
        var rt = go.GetComponent<RectTransform>();
        float ay = bottomLeft ? 0f : 1f;
        rt.anchorMin = new Vector2(0f, ay);
        rt.anchorMax = new Vector2(0f, ay);
        rt.pivot = new Vector2(0f, ay);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(size, size);
        var img = go.GetComponent<Image>();
        img.sprite = icon;
        img.preserveAspect = true;
        img.raycastTarget = false;
    }

    private static void EnsureCounterIconTopRight(Transform parent, string name, Sprite icon, Vector2 pos, float size, bool bottomRight = false)
    {
        if (parent == null || icon == null) return;
        var existing = parent.Find(name);
        var go = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(Image));
        if (existing == null) go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        float ay = bottomRight ? 0f : 1f;
        rt.anchorMin = new Vector2(1f, ay);
        rt.anchorMax = new Vector2(1f, ay);
        rt.pivot = new Vector2(1f, ay);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(size, size);
        var img = go.GetComponent<Image>();
        img.sprite = icon;
        img.preserveAspect = true;
        img.raycastTarget = false;
    }

    private void StyleSm64Tmp(TextMeshProUGUI tmp, TMP_FontAsset font, float size, Color color, TextAlignmentOptions align)
    {
        if (tmp == null) return;
        if (font != null && tmp.font == null) tmp.font = font;
        tmp.fontSize = size;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = color;
        tmp.alignment = align;

        if (tmp.fontSharedMaterial != null && tmp.fontSharedMaterial.HasProperty(OutlineWidth))
        {
            if (_sm64OutlinedFontMat == null)
            {
                _sm64OutlinedFontMat = new Material(tmp.fontSharedMaterial);
                _sm64OutlinedFontMat.SetColor(OutlineColor, new Color(0.04f, 0.04f, 0.08f, 1f));
                _sm64OutlinedFontMat.SetFloat(OutlineWidth, 0.34f);
                if (_sm64OutlinedFontMat.HasProperty(FaceDilate))
                    _sm64OutlinedFontMat.SetFloat(FaceDilate, 0.12f);
            }
            tmp.fontSharedMaterial = _sm64OutlinedFontMat;
        }

        var shadow = tmp.GetComponent<Shadow>();
        if (shadow == null) shadow = tmp.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.9f);
        shadow.effectDistance = new Vector2(3f, -3f);
    }

    private static void StyleModalCard(GameObject panel, string cardName, Sprite badgeSprite)
    {
        if (panel == null || badgeSprite == null) return;
        var card = panel.transform.Find(cardName);
        if (card == null) return;
        var img = card.GetComponent<Image>();
        if (img == null) return;
        img.sprite = badgeSprite;
        img.type = Image.Type.Sliced;
        img.color = new Color(0.08f, 0.15f, 0.36f, 0.94f);
    }

    private void BuildSm64StarSelectOverlay(Sprite badgeSprite, TMP_FontAsset font)
    {
        if (titlePanel == null) return;

        // Clean white SM64DS Star Select background
        var panelImg = titlePanel.GetComponent<Image>();
        if (panelImg == null) panelImg = titlePanel.AddComponent<Image>();
        panelImg.enabled = true;
        panelImg.sprite = null;
        panelImg.color = new Color(0.97f, 0.97f, 0.97f, 1f);

        var header = titlePanel.transform.Find("TitleHeaderScrim");
        if (header != null) header.gameObject.SetActive(false);

        var promptScrim = titlePanel.transform.Find("TitlePromptScrim");
        if (promptScrim != null)
        {
            var pImg = promptScrim.GetComponent<Image>();
            if (pImg != null) pImg.enabled = false;
            var pRt = promptScrim.GetComponent<RectTransform>();
            if (pRt != null)
            {
                pRt.anchorMin = new Vector2(0.5f, 0f);
                pRt.anchorMax = new Vector2(0.5f, 0f);
                pRt.pivot = new Vector2(0.5f, 0f);
                pRt.anchoredPosition = new Vector2(0f, 18f);
                pRt.sizeDelta = new Vector2(460f, 40f);
            }
            var pBtn = promptScrim.GetComponent<Button>() ?? promptScrim.gameObject.AddComponent<Button>();
            pBtn.onClick.RemoveAllListeners();
            pBtn.onClick.AddListener(StartGameFromTitle);
        }
        if (titlePromptText != null)
        {
            var ptRt = titlePromptText.rectTransform;
            ptRt.anchorMin = Vector2.zero;
            ptRt.anchorMax = Vector2.one;
            ptRt.pivot = new Vector2(0.5f, 0.5f);
            ptRt.offsetMin = Vector2.zero;
            ptRt.offsetMax = Vector2.zero;
            ptRt.localScale = Vector3.one;
            titlePromptText.margin = Vector4.zero;
            titlePromptText.text = "PRESS SPACE / CLICK MEDALLION TO START";
            StyleSm64PlainTmp(titlePromptText, font, 16f, new Color(0.22f, 0.22f, 0.26f), TextAlignmentOptions.Center);
        }

        if (titlePanel.transform.Find("SM64StarSelectRoot") != null) return;

        var rootGo = new GameObject("SM64StarSelectRoot", typeof(RectTransform));
        rootGo.transform.SetParent(titlePanel.transform, false);
        var rootRt = rootGo.GetComponent<RectTransform>();
        rootRt.anchorMin = Vector2.zero;
        rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = Vector2.zero;
        rootRt.offsetMax = Vector2.zero;

        // 1. Top Row: 1..6 Stars
        const int starCount = 6;
        _titleStarButtonsBg.Clear();
        const float spacing = 112f;
        var startX = -((starCount - 1) * spacing) * 0.5f;

        for (var i = 0; i < starCount; i++)
        {
            var actNum = i + 1;
            var starTitle = GetStarTitleForAct(actNum);

            var numGo = new GameObject($"StarNum_{actNum}", typeof(RectTransform), typeof(TextMeshProUGUI));
            numGo.transform.SetParent(rootGo.transform, false);
            var nRt = numGo.GetComponent<RectTransform>();
            nRt.anchorMin = new Vector2(0.5f, 1f);
            nRt.anchorMax = new Vector2(0.5f, 1f);
            nRt.pivot = new Vector2(0.5f, 0.5f);
            nRt.anchoredPosition = new Vector2(startX + i * spacing, -48f);
            nRt.sizeDelta = new Vector2(60f, 36f);
            var numTmp = numGo.GetComponent<TextMeshProUGUI>();
            StyleSm64PlainTmp(numTmp, font, 26f, new Color(0.08f, 0.08f, 0.10f), TextAlignmentOptions.Center);
            numTmp.text = $"{actNum}";

            var btnGo = new GameObject($"StarSelectBtn_{actNum}", typeof(RectTransform), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(rootGo.transform, false);
            var rt = btnGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(startX + i * spacing, -106f);
            rt.sizeDelta = new Vector2(78f, 78f);

            var starImg = btnGo.GetComponent<Image>();
            starImg.sprite = UISpriteFactory.GetStarIcon();
            starImg.preserveAspect = true;
            _titleStarButtonsBg.Add(starImg);

            var btn = btnGo.GetComponent<Button>();
            btn.onClick.AddListener(() => SelectActFromTitle(actNum, starTitle));
        }

        // 2. Act Mission Title
        var previewGo = new GameObject("ActPreviewText", typeof(RectTransform), typeof(TextMeshProUGUI));
        previewGo.transform.SetParent(rootGo.transform, false);
        var pvRt = previewGo.GetComponent<RectTransform>();
        pvRt.anchorMin = new Vector2(0.5f, 1f);
        pvRt.anchorMax = new Vector2(0.5f, 1f);
        pvRt.pivot = new Vector2(0.5f, 0.5f);
        pvRt.anchoredPosition = new Vector2(0f, -172f);
        pvRt.sizeDelta = new Vector2(720f, 40f);
        _titleActPreviewText = previewGo.GetComponent<TextMeshProUGUI>();
        StyleSm64PlainTmp(_titleActPreviewText, font, 26f, new Color(0.07f, 0.07f, 0.09f), TextAlignmentOptions.Center);

        // 3. Score Row: SCORE [Coin] x 00
        var scoreLblGo = new GameObject("ScoreLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
        scoreLblGo.transform.SetParent(rootGo.transform, false);
        var slRt = scoreLblGo.GetComponent<RectTransform>();
        slRt.anchorMin = new Vector2(0.5f, 1f);
        slRt.anchorMax = new Vector2(0.5f, 1f);
        slRt.pivot = new Vector2(0.5f, 0.5f);
        slRt.anchoredPosition = new Vector2(-58f, -216f);
        slRt.sizeDelta = new Vector2(140f, 36f);
        var slTmp = scoreLblGo.GetComponent<TextMeshProUGUI>();
        StyleSm64PlainTmp(slTmp, font, 22f, new Color(0.08f, 0.08f, 0.10f), TextAlignmentOptions.Right);
        slTmp.text = "SCORE";

        var scoreCoinGo = new GameObject("ScoreCoinIcon", typeof(RectTransform), typeof(Image));
        scoreCoinGo.transform.SetParent(rootGo.transform, false);
        var scRt = scoreCoinGo.GetComponent<RectTransform>();
        scRt.anchorMin = new Vector2(0.5f, 1f);
        scRt.anchorMax = new Vector2(0.5f, 1f);
        scRt.pivot = new Vector2(0.5f, 0.5f);
        scRt.anchoredPosition = new Vector2(36f, -216f);
        scRt.sizeDelta = new Vector2(34f, 34f);
        var scImg = scoreCoinGo.GetComponent<Image>();
        scImg.sprite = UISpriteFactory.GetCoinIcon(false);
        scImg.preserveAspect = true;

        var scoreValGo = new GameObject("ScoreValue", typeof(RectTransform), typeof(TextMeshProUGUI));
        scoreValGo.transform.SetParent(rootGo.transform, false);
        var svRt = scoreValGo.GetComponent<RectTransform>();
        svRt.anchorMin = new Vector2(0.5f, 1f);
        svRt.anchorMax = new Vector2(0.5f, 1f);
        svRt.pivot = new Vector2(0.5f, 0.5f);
        svRt.anchoredPosition = new Vector2(102f, -216f);
        svRt.sizeDelta = new Vector2(96f, 36f);
        var svTmp = scoreValGo.GetComponent<TextMeshProUGUI>();
        StyleSm64Tmp(svTmp, font, 28f, new Color(1f, 0.42f, 0.14f), TextAlignmentOptions.Left);
        var activeCourseName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        var bestCourseCoins = SaveManager.Instance != null
            ? Mathf.Max(coinsCollected, SaveManager.Instance.GetBestCourseCoins(activeCourseName))
            : coinsCollected;
        svTmp.text = $"× {bestCourseCoins:00}";

        // 4. Center Course Medallion (clickable to start!) + Course Name
        var medGo = new GameObject("CourseMedallion", typeof(RectTransform), typeof(Image), typeof(Button));
        medGo.transform.SetParent(rootGo.transform, false);
        var mRt = medGo.GetComponent<RectTransform>();
        mRt.anchorMin = new Vector2(0.5f, 0.5f);
        mRt.anchorMax = new Vector2(0.5f, 0.5f);
        mRt.pivot = new Vector2(0.5f, 0.5f);
        mRt.anchoredPosition = new Vector2(0f, -22f);
        mRt.sizeDelta = new Vector2(148f, 148f);
        var mImg = medGo.GetComponent<Image>();
        mImg.sprite = UISpriteFactory.GetCourseMedallionIcon();
        mImg.preserveAspect = true;
        medGo.GetComponent<Button>().onClick.AddListener(StartGameFromTitle);

        var courseWordGo = new GameObject("CourseWord", typeof(RectTransform), typeof(TextMeshProUGUI));
        courseWordGo.transform.SetParent(medGo.transform, false);
        var cwRt = courseWordGo.GetComponent<RectTransform>();
        cwRt.anchorMin = new Vector2(0.5f, 1f);
        cwRt.anchorMax = new Vector2(0.5f, 1f);
        cwRt.pivot = new Vector2(0.5f, 0.5f);
        cwRt.anchoredPosition = new Vector2(0f, -26f);
        cwRt.sizeDelta = new Vector2(130f, 26f);
        var cwTmp = courseWordGo.GetComponent<TextMeshProUGUI>();
        StyleSm64Tmp(cwTmp, font, 16f, new Color(1f, 0.88f, 0.18f), TextAlignmentOptions.Center);
        cwTmp.text = "COURSE";

        var courseNumGo = new GameObject("CourseNum", typeof(RectTransform), typeof(TextMeshProUGUI));
        courseNumGo.transform.SetParent(medGo.transform, false);
        var cnRt = courseNumGo.GetComponent<RectTransform>();
        cnRt.anchorMin = Vector2.zero;
        cnRt.anchorMax = Vector2.one;
        cnRt.offsetMin = Vector2.zero;
        cnRt.offsetMax = Vector2.zero;
        var cnTmp = courseNumGo.GetComponent<TextMeshProUGUI>();
        StyleSm64Tmp(cnTmp, font, 34f, new Color(1f, 0.36f, 0.14f), TextAlignmentOptions.Center);
        cnTmp.text = "1";

        var courseNameGo = new GameObject("CourseName", typeof(RectTransform), typeof(TextMeshProUGUI));
        courseNameGo.transform.SetParent(rootGo.transform, false);
        var cnaRt = courseNameGo.GetComponent<RectTransform>();
        cnaRt.anchorMin = new Vector2(0.5f, 0.5f);
        cnaRt.anchorMax = new Vector2(0.5f, 0.5f);
        cnaRt.pivot = new Vector2(0.5f, 0.5f);
        cnaRt.anchoredPosition = new Vector2(0f, -124f);
        cnaRt.sizeDelta = new Vector2(640f, 40f);
        var cnaTmp = courseNameGo.GetComponent<TextMeshProUGUI>();
        StyleSm64PlainTmp(cnaTmp, font, 26f, new Color(0.07f, 0.07f, 0.09f), TextAlignmentOptions.Center);
        cnaTmp.text = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name.ToUpperInvariant();

        // 5. Bottom-Left Lives & Bottom-Right Stars
        EnsureCounterIcon(rootGo.transform, "TitleLivesIcon", UISpriteFactory.GetMarioCapIcon(), new Vector2(28f, 64f), 44f, bottomLeft: true);
        var tLivesGo = new GameObject("TitleLivesText", typeof(RectTransform), typeof(TextMeshProUGUI));
        tLivesGo.transform.SetParent(rootGo.transform, false);
        var tlRt = tLivesGo.GetComponent<RectTransform>();
        tlRt.anchorMin = Vector2.zero;
        tlRt.anchorMax = Vector2.zero;
        tlRt.pivot = Vector2.zero;
        tlRt.anchoredPosition = new Vector2(80f, 22f);
        tlRt.sizeDelta = new Vector2(120f, 40f);
        var tlTmp = tLivesGo.GetComponent<TextMeshProUGUI>();
        StyleSm64Tmp(tlTmp, font, 30f, new Color(1f, 0.44f, 0.14f), TextAlignmentOptions.Left);
        var titleLives = player != null ? Mathf.Max(0, player.lives) : 4;
        tlTmp.text = $"× {titleLives}";

        EnsureCounterIconTopRight(rootGo.transform, "TitleStarsIcon", UISpriteFactory.GetStarIcon(), new Vector2(-114f, 64f), 42f, bottomRight: true);
        var tStarsGo = new GameObject("TitleStarsText", typeof(RectTransform), typeof(TextMeshProUGUI));
        tStarsGo.transform.SetParent(rootGo.transform, false);
        var tsRt = tStarsGo.GetComponent<RectTransform>();
        tsRt.anchorMin = new Vector2(1f, 0f);
        tsRt.anchorMax = new Vector2(1f, 0f);
        tsRt.pivot = new Vector2(1f, 0f);
        tsRt.anchoredPosition = new Vector2(-24f, 22f);
        tsRt.sizeDelta = new Vector2(92f, 40f);
        var tsTmp = tStarsGo.GetComponent<TextMeshProUGUI>();
        StyleSm64Tmp(tsTmp, font, 30f, new Color(1f, 0.44f, 0.14f), TextAlignmentOptions.Left);
        var totalSavedStars = SaveManager.Instance != null
            ? Mathf.Max(starsCollected, SaveManager.Instance.TotalStarsCollected)
            : starsCollected;
        tsTmp.text = $"× {totalSavedStars}";

        RefreshTitleActSelectionVisual();
        BuildCourseSwitcherRibbon(badgeSprite, font);
    }

    private static void StyleSm64PlainTmp(TextMeshProUGUI tmp, TMP_FontAsset font, float size, Color color, TextAlignmentOptions align)
    {
        if (tmp == null) return;
        if (font != null && tmp.font == null) tmp.font = font;
        tmp.fontSize = size;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = color;
        tmp.alignment = align;
    }

    private void BuildCourseSwitcherRibbon(Sprite badgeSprite, TMP_FontAsset font)
    {
        if (titlePanel == null || titlePanel.transform.Find("SM64CourseSelectRow") != null) return;
        var rowGo = new GameObject("SM64CourseSelectRow", typeof(RectTransform));
        rowGo.transform.SetParent(titlePanel.transform, false);
        var rRt = rowGo.GetComponent<RectTransform>();
        rRt.anchorMin = new Vector2(0.5f, 0f);
        rRt.anchorMax = new Vector2(0.5f, 0f);
        rRt.pivot = new Vector2(0.5f, 0f);
        rRt.anchoredPosition = new Vector2(0f, 182f);
        rRt.sizeDelta = new Vector2(980f, 40f);

        (string sceneName, string label)[] courses =
        {
            ("PeachCastleHub", "0. CASTLE"),
            ("BobOmbBattlefield", "1. BOB-OMB"),
            ("WhompsFortress", "2. WHOMP'S"),
            ("JollyRogerBay", "3. JOLLY ROGER"),
            ("CoolCoolMountain", "4. COOL COOL"),
            ("BowserDarkWorld", "5. DARK WORLD"),
            ("Test", "6. TEST GYM")
        };

        var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        const float step = 136f;
        var x0 = -((courses.Length - 1) * step) * 0.5f;
        for (var i = 0; i < courses.Length; i++)
        {
            var (scName, lbl) = courses[i];
            var bGo = new GameObject($"CourseBtn_{scName}", typeof(RectTransform), typeof(Image), typeof(Button));
            bGo.transform.SetParent(rowGo.transform, false);
            var rt = bGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x0 + i * step, 0f);
            rt.sizeDelta = new Vector2(128f, 34f);

            var img = bGo.GetComponent<Image>();
            if (badgeSprite != null) { img.sprite = badgeSprite; img.type = Image.Type.Sliced; }
            var isCurr = activeScene == scName;
            img.color = isCurr
                ? new Color(0.76f, 0.24f, 0.56f, 0.96f)
                : new Color(0.11f, 0.16f, 0.34f, 0.88f);

            var tGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            tGo.transform.SetParent(bGo.transform, false);
            var tRt = tGo.GetComponent<RectTransform>();
            tRt.anchorMin = Vector2.zero;
            tRt.anchorMax = Vector2.one;
            tRt.offsetMin = Vector2.zero;
            tRt.offsetMax = Vector2.zero;
            var tmp = tGo.GetComponent<TextMeshProUGUI>();
            StyleSm64Tmp(tmp, font, 11.8f, Color.white, TextAlignmentOptions.Center);
            tmp.text = lbl;

            var targetScene = scName;
            bGo.GetComponent<Button>().onClick.AddListener(() => SwitchCourseScene(targetScene));
        }
    }

    public static void SwitchCourseScene(string sceneName)
    {
        if (instance != null && SaveManager.Instance != null)
        {
            var activeCourse = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            SaveManager.Instance.RecordCourseCoins(activeCourse, instance.coinsCollected);
        }
        if (GameAudio.Instance != null) GameAudio.Instance.PlayUIClick();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Time.timeScale = 1f;
        UnityEngine.SceneManagement.SceneManager.LoadScene(sceneName);
    }

    private string GetStarTitleForAct(int actNum)
    {
        foreach (var c in _allCollectibles.Where(c => c != null && c.kind == CollectibleKind.PowerStar && c.missionActIndex == actNum))
        {
            return !string.IsNullOrEmpty(c.starTitle) ? c.starTitle : $"ACT {actNum}";
        }

        return $"ACT {actNum}";
    }

    private void SelectActFromTitle(int actNum, string title)
    {
        if (GameAudio.Instance != null) GameAudio.Instance.PlayUIClick();
        SetupActMissionState(actNum, isFreshRun: false);
        if (_titleActPreviewText != null) _titleActPreviewText.text = $"STAR {actNum}: {title}";
        RefreshTitleActSelectionVisual();
    }

    private void RefreshTitleActSelectionVisual()
    {
        var sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        for (var i = 0; i < _titleStarButtonsBg.Count; i++)
        {
            var starImg = _titleStarButtonsBg[i];
            if (starImg == null) continue;
            var actNum = i + 1;
            var selected = actNum == currentActNumber;
            var collected = SaveManager.Instance != null && SaveManager.Instance.IsStarCollected(sceneName, actNum);
            if (collected)
            {
                starImg.color = new Color(1f, 0.92f, 0.24f, 1f);
            }
            else
            {
                starImg.color = selected
                    ? Color.white
                    : new Color(0.55f, 0.60f, 0.72f, 0.78f);
            }
            starImg.transform.localScale = selected ? Vector3.one * 1.15f : Vector3.one;
        }
        if (_titleActPreviewText != null)
        {
            _titleActPreviewText.text = $"STAR {currentActNumber}: {GetStarTitleForAct(currentActNumber)}".ToUpperInvariant();
        }
    }
}