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

    private void Start()
    {
        RefreshCollectibleRegistry();
        EnsureSm64StyledHUD();
        WireSliders();
        SetupActMissionState(1);
        SetStateImmediate(GameState.Title);
        var sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        bool isJrb = !string.IsNullOrEmpty(sceneName) && sceneName.Contains("JollyRoger");
        bool isBitDw = !string.IsNullOrEmpty(sceneName) && (sceneName.Contains("Bowser") || sceneName.Contains("DarkWorld"));
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
                isBitDw ? Sm64CourseTheme.DarkWorldOrgan :
                (isJrb ? Sm64CourseTheme.OceanBayWaltz : Sm64CourseTheme.FortressMarch));
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

    public void SetupActMissionState(int actNumber, bool isFreshRun = true)
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
                bool wallStarDone = wfElem.revealedStar != null && wfElem.revealedStar.IsCollected;
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

        // 4. Activate uncollected open-world Power Stars across all 3 SM64 Courses!
        foreach (var item in _allCollectibles)
        {
            if (item == null || item.kind != CollectibleKind.PowerStar) continue;
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

            if (isWhompsFortress)
            {
                bool wallBrokenStarReady = false;
                foreach (var wfElem in FindObjectsByType<WhompFortressElement>(FindObjectsInactive.Include))
                {
                    if (wfElem != null && wfElem.hazardType == FortressHazardType.BreakawayBrickWall && wfElem.revealedStar == item && !wfElem.gameObject.activeSelf)
                        wallBrokenStarReady = true;
                }
                bool redCoinStarReady = (item == secretRedCoinStar && redCoinsCollected >= 8);

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
                bool redCoinStarReady = (item == secretRedCoinStar && redCoinsCollected >= 8);
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
                if (GameAudio.Instance != null) GameAudio.Instance.SetMusicVolume(val);
            });
        }

        if (sfxSlider != null)
        {
            sfxSlider.SetValueWithoutNotify(GameAudio.Instance != null ? GameAudio.Instance.SfxVol : 0.90f);
            sfxSlider.onValueChanged.RemoveAllListeners();
            sfxSlider.onValueChanged.AddListener(val =>
            {
                if (GameAudio.Instance != null) GameAudio.Instance.SetSfxVolume(val);
            });
        }

        BindModalButton(pausePanel, "PauseModalCard/ResumeButton", ResumeGame);
        BindModalButton(pausePanel, "PauseModalCard/RestartButton", RestartRun);
        BindModalButton(pausePanel, "PauseModalCard/QuitTitleButton", ReturnToTitle);
        BindModalButton(endedPanel, "EndedModalCard/EndedRestartButton", RestartRun);
        BindModalButton(endedPanel, "EndedModalCard/EndedTitleButton", ReturnToTitle);
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
        SetupActMissionState(currentActNumber);
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

    public void PauseGame()
    {
        if (GameAudio.Instance != null) GameAudio.Instance.PlayUIClick();
        TransitionToState(GameState.Paused);
    }

    public void ResumeGame()
    {
        if (GameAudio.Instance != null) GameAudio.Instance.PlayUIClick();
        TransitionToState(GameState.Playing);
    }

    private void TriggerWinEnding()
    {
        TransitionToState(GameState.Ended);
    }

    public void ReturnToTitle()
    {
        if (GameAudio.Instance != null) GameAudio.Instance.PlayUIClick();
        ResetRunStateClean();
        TransitionToState(GameState.Title);
    }

    public void RestartRun()
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
            bool isBowserCourse = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name.Contains("Bowser");
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
                var starTitle = item != null && !string.IsNullOrEmpty(item.starTitle) ? item.starTitle : $"POWER STAR #{starsCollected}";
                SpawnFloatingWorldText(worldPos, "+1 POWER STAR!", Palette.AccentStarGold);
                TriggerMilestoneSequence($"STAR #{starsCollected}/{targetStarsToWin} COLLECTED!", starTitle);

                bool isBowserKeyWin = item != null && (item.gameObject.name.Contains("BigKey") || starTitle.Contains("BIG KEY"));
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
                    bool hasTower = GameObject.Find("LookoutTower_Summit") != null || GameObject.Find("FortressLookoutTower") != null;
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

    private void SpawnFloatingWorldText(Vector3 worldPos, string text, Color color)
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
        if (coinCounterText != null)
            coinCounterText.text = $"× {coinsCollected:00}";
        if (starCounterText != null)
            starCounterText.text = $"× {starsCollected}/{targetStarsToWin}";
        if (redCoinCounterText != null)
            redCoinCounterText.text = $"× {redCoinsCollected}/8";
        if (_livesCounterText != null)
        {
            var lifeCount = player != null ? Mathf.Max(0, player.lives) : 4;
            _livesCounterText.text = $"× {lifeCount}";
        }
        if (_actMissionBadgeText != null)
        {
            _actMissionBadgeText.text = $"ACT {currentActNumber}: {currentMissionTitle}";
        }
        if (starPowerFillBar != null && player != null && player.hasWaterInCourse && player.IsSwimming)
        {
            starPowerFillBar.fillAmount = player.OxygenNormalized;
        }
        UpdateSm64PowerMeterVisual();
    }

    private void UpdateSm64PowerMeterVisual()
    {
        if (_powerMeterRoot == null) return;
        var isSwimAir = player != null && player.hasWaterInCourse && player.IsSwimming && !JollyRogerBayController.IsMetalMarioActive;
        var wedgesActive = player != null ? (isSwimAir ? player.OxygenWedges : player.HealthWedges) : 8;

        Color activeColor;
        if (isSwimAir)
        {
            activeColor = wedgesActive <= 2
                ? Color.Lerp(new Color(0.20f, 0.90f, 1.0f), new Color(1.0f, 0.25f, 0.35f), 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 11f))
                : new Color(0.24f, 0.88f, 1.0f);
        }
        else if (wedgesActive >= 7) activeColor = new Color(0.22f, 0.62f, 1.0f);
        else if (wedgesActive >= 5) activeColor = new Color(0.24f, 0.90f, 0.38f);
        else if (wedgesActive >= 3) activeColor = new Color(1.0f, 0.82f, 0.14f);
        else
        {
            var pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 12f);
            activeColor = Color.Lerp(new Color(0.96f, 0.20f, 0.14f), new Color(1.0f, 0.65f, 0.18f), pulse);
        }

        var emptyColor = new Color(0.11f, 0.14f, 0.22f, 0.42f);
        for (var i = 0; i < 8; i++)
        {
            var img = _powerMeterWedges[i];
            if (img == null) continue;
            img.color = i < wedgesActive ? activeColor : emptyColor;
        }

        if (_powerMeterCenterText != null)
        {
            _powerMeterCenterText.text = isSwimAir ? "AIR" : (JollyRogerBayController.IsMetalMarioActive ? "MTL" : "PWR");
            _powerMeterCenterText.color = isSwimAir ? new Color(0.55f, 0.95f, 1.0f) : new Color(1f, 0.92f, 0.45f);
        }
    }

    private void EnsureSm64StyledHUD()
    {
        if (_sm64HudStyled) return;
        _sm64HudStyled = true;

        if (mainCanvas == null) mainCanvas = FindAnyObjectByType<Canvas>();
        if (mainCanvas == null) return;

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

        // 1. Build iconic SM64 8-Wedge Circular Power Meter (Top-Center inside HUDPanel)
        if (hudPanel != null && _powerMeterRoot == null)
        {
            var meterGo = new GameObject("SM64PowerMeter", typeof(RectTransform), typeof(Image));
            meterGo.transform.SetParent(hudPanel.transform, false);
            _powerMeterRoot = meterGo.GetComponent<RectTransform>();
            _powerMeterRoot.anchorMin = new Vector2(0.5f, 1f);
            _powerMeterRoot.anchorMax = new Vector2(0.5f, 1f);
            _powerMeterRoot.pivot = new Vector2(0.5f, 1f);
            _powerMeterRoot.anchoredPosition = new Vector2(0f, -14f);
            _powerMeterRoot.sizeDelta = new Vector2(114f, 114f);

            var bezel = meterGo.GetComponent<Image>();
            bezel.sprite = UISpriteFactory.GetPowerRingFrame();
            bezel.color = Color.white;
            bezel.raycastTarget = false;

            for (var i = 0; i < 8; i++)
            {
                var wGo = new GameObject($"Wedge_{i}", typeof(RectTransform), typeof(Image));
                wGo.transform.SetParent(_powerMeterRoot, false);
                var wRt = wGo.GetComponent<RectTransform>();
                wRt.anchorMin = Vector2.zero;
                wRt.anchorMax = Vector2.one;
                wRt.offsetMin = new Vector2(8f, 8f);
                wRt.offsetMax = new Vector2(-8f, -8f);
                var wImg = wGo.GetComponent<Image>();
                wImg.sprite = UISpriteFactory.GetPowerWedge(i);
                wImg.raycastTarget = false;
                _powerMeterWedges[i] = wImg;
            }

            var centerGo = new GameObject("MeterCenterLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
            centerGo.transform.SetParent(_powerMeterRoot, false);
            var cRt = centerGo.GetComponent<RectTransform>();
            cRt.anchorMin = Vector2.zero;
            cRt.anchorMax = Vector2.one;
            cRt.offsetMin = Vector2.zero;
            cRt.offsetMax = Vector2.zero;
            _powerMeterCenterText = centerGo.GetComponent<TextMeshProUGUI>();
            StyleSm64Tmp(_powerMeterCenterText, defaultFont, 15f, new Color(1f, 0.92f, 0.45f), TextAlignmentOptions.Center);
            _powerMeterCenterText.text = "PWR";
        }

        // 2. Enhance Top-Left SM64 Counters Card with Cap/Star/Coin icons, Lives & Act Badge
        if (hudPanel != null)
        {
            var statsCard = hudPanel.transform.Find("TopLeftStatsCard");
            if (statsCard != null)
            {
                var cardImg = statsCard.GetComponent<Image>();
                if (cardImg != null && badgeSprite != null)
                {
                    cardImg.sprite = badgeSprite;
                    cardImg.type = Image.Type.Sliced;
                    cardImg.color = new Color(0.06f, 0.11f, 0.25f, 0.78f);
                }

                var cardRt = statsCard.GetComponent<RectTransform>();
                if (cardRt != null)
                {
                    cardRt.sizeDelta = new Vector2(330f, 178f);
                    cardRt.anchoredPosition = new Vector2(18f, -14f);
                }

                static void PinTopLeft(RectTransform rt, Vector2 pos, Vector2 size)
                {
                    rt.anchorMin = new Vector2(0f, 1f);
                    rt.anchorMax = new Vector2(0f, 1f);
                    rt.pivot = new Vector2(0f, 1f);
                    rt.anchoredPosition = pos;
                    rt.sizeDelta = size;
                }

                // Shift existing TMP counters right to make room for icons & Lives row
                if (starCounterText != null)
                {
                    PinTopLeft(starCounterText.rectTransform, new Vector2(64f, -14f), new Vector2(108f, 34f));
                    StyleSm64Tmp(starCounterText, defaultFont, 26f, new Color(1f, 0.86f, 0.18f), TextAlignmentOptions.Left);
                    EnsureCounterIcon(statsCard, "SM64StarBadge", UISpriteFactory.GetStarIcon(), new Vector2(18f, -14f), 34f);
                }

                if (coinCounterText != null)
                {
                    PinTopLeft(coinCounterText.rectTransform, new Vector2(64f, -52f), new Vector2(108f, 32f));
                    StyleSm64Tmp(coinCounterText, defaultFont, 24f, new Color(1f, 0.88f, 0.24f), TextAlignmentOptions.Left);
                    EnsureCounterIcon(statsCard, "SM64CoinBadge", UISpriteFactory.GetCoinIcon(false), new Vector2(20f, -52f), 30f);
                }

                if (redCoinCounterText != null)
                {
                    PinTopLeft(redCoinCounterText.rectTransform, new Vector2(215f, -52f), new Vector2(98f, 32f));
                    StyleSm64Tmp(redCoinCounterText, defaultFont, 22f, new Color(1f, 0.42f, 0.36f), TextAlignmentOptions.Left);
                    EnsureCounterIcon(statsCard, "SM64RedCoinBadge", UISpriteFactory.GetCoinIcon(true), new Vector2(180f, -52f), 28f);
                }

                // Lives badge at top-right of TopLeftStatsCard
                if (_livesCounterText == null)
                {
                    EnsureCounterIcon(statsCard, "SM64MarioCapBadge", UISpriteFactory.GetMarioCapIcon(), new Vector2(178f, -14f), 34f);
                    var livesGo = new GameObject("SM64LivesText", typeof(RectTransform), typeof(TextMeshProUGUI));
                    livesGo.transform.SetParent(statsCard, false);
                    var lRt = livesGo.GetComponent<RectTransform>();
                    lRt.anchorMin = new Vector2(0f, 1f);
                    lRt.anchorMax = new Vector2(0f, 1f);
                    lRt.pivot = new Vector2(0f, 1f);
                    lRt.anchoredPosition = new Vector2(216f, -14f);
                    lRt.sizeDelta = new Vector2(90f, 34f);
                    _livesCounterText = livesGo.GetComponent<TextMeshProUGUI>();
                    StyleSm64Tmp(_livesCounterText, defaultFont, 25f, new Color(1f, 0.96f, 0.96f), TextAlignmentOptions.Left);
                }

                // Act Mission label above bottom bar
                if (_actMissionBadgeText == null)
                {
                    var actGo = new GameObject("SM64ActMissionText", typeof(RectTransform), typeof(TextMeshProUGUI));
                    actGo.transform.SetParent(statsCard, false);
                    _actMissionBadgeText = actGo.GetComponent<TextMeshProUGUI>();
                }
                if (_actMissionBadgeText != null)
                {
                    var aRt = _actMissionBadgeText.GetComponent<RectTransform>();
                    aRt.anchorMin = new Vector2(0f, 1f);
                    aRt.anchorMax = new Vector2(1f, 1f);
                    aRt.pivot = new Vector2(0.5f, 1f);
                    aRt.anchoredPosition = new Vector2(0f, -92f);
                    aRt.sizeDelta = new Vector2(-28f, 36f);
                    _actMissionBadgeText.textWrappingMode = TextWrappingModes.Normal;
                    StyleSm64Tmp(_actMissionBadgeText, defaultFont, 14f, new Color(0.75f, 0.90f, 1.0f), TextAlignmentOptions.Left);
                }
                var barBg = statsCard.Find("StarPowerBarBG");
                if (barBg == null && starPowerFillBar != null) barBg = starPowerFillBar.transform.parent;
                if (barBg != null)
                {
                    var barBgRt = barBg.GetComponent<RectTransform>();
                    if (barBgRt != null)
                    {
                        barBgRt.anchorMin = new Vector2(0f, 0f);
                        barBgRt.anchorMax = new Vector2(1f, 0f);
                        barBgRt.pivot = new Vector2(0.5f, 0f);
                        barBgRt.anchoredPosition = new Vector2(0f, 10f);
                        barBgRt.sizeDelta = new Vector2(-28f, 10f);
                    }
                }
            }

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
        StyleModalCard(pausePanel, "PauseModalCard", badgeSprite);
        StyleModalCard(endedPanel, "EndedModalCard", badgeSprite);

        UpdateSm64PowerMeterVisual();
    }

    private static void EnsureCounterIcon(Transform parent, string name, Sprite icon, Vector2 topLeftPos, float size)
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
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = topLeftPos;
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

        if (tmp.fontSharedMaterial != null && tmp.fontSharedMaterial.HasProperty("_OutlineWidth"))
        {
            if (_sm64OutlinedFontMat == null)
            {
                _sm64OutlinedFontMat = new Material(tmp.fontSharedMaterial);
                _sm64OutlinedFontMat.SetColor("_OutlineColor", new Color(0.05f, 0.06f, 0.12f, 1f));
                _sm64OutlinedFontMat.SetFloat("_OutlineWidth", 0.24f);
                if (_sm64OutlinedFontMat.HasProperty("_FaceDilate"))
                    _sm64OutlinedFontMat.SetFloat("_FaceDilate", 0.06f);
            }
            tmp.fontSharedMaterial = _sm64OutlinedFontMat;
        }

        var shadow = tmp.GetComponent<Shadow>();
        if (shadow == null) shadow = tmp.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
        shadow.effectDistance = new Vector2(2f, -2f);
    }

    private static void StyleModalCard(GameObject panel, string cardName, Sprite badgeSprite)
    {
        if (panel == null || badgeSprite == null) return;
        var card = panel.transform.Find(cardName);
        if (card == null) return;
        var img = card.GetComponent<Image>();
        if (img != null)
        {
            img.sprite = badgeSprite;
            img.type = Image.Type.Sliced;
            img.color = new Color(0.08f, 0.15f, 0.36f, 0.94f);
        }
    }

    private void BuildSm64StarSelectOverlay(Sprite badgeSprite, TMP_FontAsset font)
    {
        if (titlePanel == null) return;

        var header = titlePanel.transform.Find("TitleHeaderScrim");
        if (header != null && badgeSprite != null)
        {
            var hImg = header.GetComponent<Image>();
            if (hImg != null)
            {
                hImg.sprite = badgeSprite;
                hImg.type = Image.Type.Sliced;
                hImg.color = new Color(0.08f, 0.16f, 0.42f, 0.88f);
            }
            foreach (var tmp in header.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                StyleSm64Tmp(tmp, font, tmp.name.Contains("Main") ? 38f : 18f,
                    tmp.name.Contains("Main") ? new Color(1f, 0.88f, 0.22f) : Color.white,
                    TextAlignmentOptions.Center);
            }
        }

        var promptScrim = titlePanel.transform.Find("TitlePromptScrim");
        if (promptScrim != null && badgeSprite != null)
        {
            var pImg = promptScrim.GetComponent<Image>();
            if (pImg != null)
            {
                pImg.sprite = badgeSprite;
                pImg.type = Image.Type.Sliced;
                pImg.color = new Color(0.11f, 0.52f, 0.20f, 0.92f);
            }
            var pRt = promptScrim.GetComponent<RectTransform>();
            if (pRt != null)
            {
                pRt.anchoredPosition = new Vector2(0f, 22f);
                pRt.sizeDelta = new Vector2(420f, 50f);
            }
            var pBtn = promptScrim.GetComponent<Button>();
            if (pBtn == null) pBtn = promptScrim.gameObject.AddComponent<Button>();
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
            titlePromptText.text = "LET'S-A GO!  [SPACE / ENTER]";
            StyleSm64Tmp(titlePromptText, font, 20f, Color.white, TextAlignmentOptions.Center);
        }

        if (titlePanel.transform.Find("SM64StarSelectBar") != null) return;

        var barGo = new GameObject("SM64StarSelectBar", typeof(RectTransform), typeof(Image));
        barGo.transform.SetParent(titlePanel.transform, false);
        var bRt = barGo.GetComponent<RectTransform>();
        bRt.anchorMin = new Vector2(0.5f, 0f);
        bRt.anchorMax = new Vector2(0.5f, 0f);
        bRt.pivot = new Vector2(0.5f, 0f);
        bRt.anchoredPosition = new Vector2(0f, 82f);
        bRt.sizeDelta = new Vector2(680f, 94f);
        var bImg = barGo.GetComponent<Image>();
        bImg.sprite = badgeSprite;
        bImg.type = Image.Type.Sliced;
        bImg.color = new Color(0.06f, 0.11f, 0.28f, 0.86f);

        var previewGo = new GameObject("ActPreviewText", typeof(RectTransform), typeof(TextMeshProUGUI));
        previewGo.transform.SetParent(barGo.transform, false);
        var pvRt = previewGo.GetComponent<RectTransform>();
        pvRt.anchorMin = new Vector2(0f, 1f);
        pvRt.anchorMax = new Vector2(1f, 1f);
        pvRt.pivot = new Vector2(0.5f, 1f);
        pvRt.anchoredPosition = new Vector2(0f, -6f);
        pvRt.sizeDelta = new Vector2(-24f, 26f);
        _titleActPreviewText = previewGo.GetComponent<TextMeshProUGUI>();
        StyleSm64Tmp(_titleActPreviewText, font, 16f, new Color(1f, 0.90f, 0.34f), TextAlignmentOptions.Center);

        var starCount = Mathf.Clamp(targetStarsToWin, 1, 6);
        _titleStarButtonsBg.Clear();
        const float spacing = 104f;
        var startX = -((starCount - 1) * spacing) * 0.5f;

        for (var i = 0; i < starCount; i++)
        {
            var actNum = i + 1;
            var starTitle = GetStarTitleForAct(actNum);
            var btnGo = new GameObject($"StarSelectBtn_{actNum}", typeof(RectTransform), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(barGo.transform, false);
            var rt = btnGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(startX + i * spacing, 10f);
            rt.sizeDelta = new Vector2(92f, 52f);

            var bg = btnGo.GetComponent<Image>();
            bg.sprite = badgeSprite;
            bg.type = Image.Type.Sliced;
            _titleStarButtonsBg.Add(bg);

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(btnGo.transform, false);
            var iRt = iconGo.GetComponent<RectTransform>();
            iRt.anchorMin = new Vector2(0f, 0.5f);
            iRt.anchorMax = new Vector2(0f, 0.5f);
            iRt.pivot = new Vector2(0.5f, 0.5f);
            iRt.anchoredPosition = new Vector2(24f, 0f);
            iRt.sizeDelta = new Vector2(30f, 30f);
            var iconImg = iconGo.GetComponent<Image>();
            iconImg.sprite = UISpriteFactory.GetStarIcon();
            iconImg.raycastTarget = false;

            var lblGo = new GameObject("Num", typeof(RectTransform), typeof(TextMeshProUGUI));
            lblGo.transform.SetParent(btnGo.transform, false);
            var nRt = lblGo.GetComponent<RectTransform>();
            nRt.anchorMin = new Vector2(0.5f, 0f);
            nRt.anchorMax = new Vector2(1f, 1f);
            nRt.offsetMin = new Vector2(2f, 0f);
            nRt.offsetMax = new Vector2(-4f, 0f);
            var numTmp = lblGo.GetComponent<TextMeshProUGUI>();
            StyleSm64Tmp(numTmp, font, 20f, Color.white, TextAlignmentOptions.Center);
            numTmp.text = $"{actNum}";

            var capturedAct = actNum;
            var capturedTitle = starTitle;
            var btn = btnGo.GetComponent<Button>();
            btn.onClick.AddListener(() => SelectActFromTitle(capturedAct, capturedTitle));
        }

        RefreshTitleActSelectionVisual();
        BuildCourseSwitcherRibbon(badgeSprite, font);
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
        rRt.sizeDelta = new Vector2(820f, 40f);

        (string sceneName, string label)[] courses =
        {
            ("BobOmbBattlefield", "1. BOB-OMB"),
            ("WhompsFortress", "2. WHOMP'S"),
            ("JollyRogerBay", "3. JOLLY ROGER"),
            ("BowserDarkWorld", "4. DARK WORLD"),
            ("TestPlayground", "5. TEST GYM")
        };

        var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        const float step = 160f;
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
            rt.sizeDelta = new Vector2(150f, 34f);

            var img = bGo.GetComponent<Image>();
            if (badgeSprite != null) { img.sprite = badgeSprite; img.type = Image.Type.Sliced; }
            bool isCurr = activeScene == scName;
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
            StyleSm64Tmp(tmp, font, 12.5f, Color.white, TextAlignmentOptions.Center);
            tmp.text = lbl;

            var targetScene = scName;
            bGo.GetComponent<Button>().onClick.AddListener(() => SwitchCourseScene(targetScene));
        }
    }

    public static void SwitchCourseScene(string sceneName)
    {
        if (GameAudio.Instance != null) GameAudio.Instance.PlayUIClick();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Time.timeScale = 1f;
        UnityEngine.SceneManagement.SceneManager.LoadScene(sceneName);
    }

    private string GetStarTitleForAct(int actNum)
    {
        foreach (var c in _allCollectibles)
        {
            if (c != null && c.kind == CollectibleKind.PowerStar && c.missionActIndex == actNum)
            {
                return !string.IsNullOrEmpty(c.starTitle) ? c.starTitle : $"ACT {actNum}";
            }
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
        for (var i = 0; i < _titleStarButtonsBg.Count; i++)
        {
            var bg = _titleStarButtonsBg[i];
            if (bg == null) continue;
            var selected = (i + 1) == currentActNumber;
            bg.color = selected
                ? new Color(0.86f, 0.58f, 0.08f, 0.96f)
                : new Color(0.14f, 0.24f, 0.48f, 0.85f);
        }
        if (_titleActPreviewText != null)
        {
            _titleActPreviewText.text = $"STAR {currentActNumber}: {GetStarTitleForAct(currentActNumber)}";
        }
    }
}