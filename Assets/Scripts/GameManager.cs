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
    private static GameManager _instance;
    public static GameManager Instance
    {
        get
        {
            if (_instance == null) _instance = FindAnyObjectByType<GameManager>();
            return _instance;
        }
        private set => _instance = value;
    }

    [FormerlySerializedAs("CurrentState")] [Header("State & Progression")]
    public GameState currentState = GameState.Title;
    [FormerlySerializedAs("CoinsCollected")] public int coinsCollected;
    [FormerlySerializedAs("RedCoinsCollected")] public int redCoinsCollected;
    [FormerlySerializedAs("StarsCollected")] public int starsCollected;
    [FormerlySerializedAs("TargetStarsToWin")] public int targetStarsToWin = 3;
    public int currentActNumber = 1; // Strict SM64 Sequential Act Order: Act 1 -> Act 2 -> Act 3 -> Act 4 -> Act 5 -> Act 6!
    public string currentMissionTitle = "ACT 1";
    [FormerlySerializedAs("RunElapsedSeconds")] public float runElapsedSeconds;

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
    [FormerlySerializedAs("TimerText")] public TextMeshProUGUI timerText;
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

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        RefreshCollectibleRegistry();
        WireSliders();
        SetupActMissionState(1);
        SetStateImmediate(GameState.Title);
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

    /// <summary>
    /// Enforces Strict Super Mario 64 Sequential Mission Order (Act 1 -> Act 2 -> Act 3 -> Act 4 -> Act 5 -> Act 6):
    /// - Only the current Act's Power Star and Act-specific Boss / NPC / Tower are active!
    /// - You CANNOT grab Act 2..6 Stars while on Act 1!
    /// </summary>
    private void SetupActMissionState(int actNumber)
    {
        currentActNumber = Mathf.Clamp(actNumber, 1, Mathf.Max(1, targetStarsToWin));
        redCoinsCollected = 0;

        if (_allCollectibles.Count == 0) RefreshCollectibleRegistry();

        // Reset coins & configure Power Stars for the active Act
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
                    // Hide all stars initially; below we enable only if it's the current Act's open-world star
                    item.ResetCollectible();
                    item.gameObject.SetActive(false);
                    break;
                }
                case CollectibleKind.RedCoin:
                    item.ResetCollectible();
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
        var towerGo = GameObject.Find("FortressLookoutTower");
        if (towerGo != null)
        {
            towerGo.SetActive(currentActNumber >= 2);
        }

        foreach (var whomp in FindObjectsByType<WhompBossController>(FindObjectsInactive.Include))
        {
            if (whomp == null) continue;
            if (whomp.isKingBoss)
            {
                if (currentActNumber == 1)
                {
                    whomp.ResetBoss();
                }
                else
                {
                    whomp.gameObject.SetActive(false);
                }

                if (whomp.rewardStar != null) whomp.rewardStar.gameObject.SetActive(false);
            }
            else
            {
                whomp.ResetBoss();
            }
        }

        foreach (var wfElem in FindObjectsByType<WhompFortressElement>(FindObjectsInactive.Include))
        {
            if (wfElem == null) continue;
            wfElem.ResetElement();
            if (wfElem.hazardType == FortressHazardType.BreakawayBrickWall && wfElem.revealedStar != null)
            {
                wfElem.revealedStar.gameObject.SetActive(false);
            }
        }

        // 2. Configure Bob-omb Battlefield Act-Specific Elements:
        //    - Act 1 ("Big Bob-omb on the Summit"): King Bob-omb active on Summit!
        //    - Act 2 ("Footrace with Koopa the Quick"): King Bob-omb gone; Koopa the Quick spawns at Start Line!
        //    - Act 5 ("Mario Wings to the Sky"): Wing Cap Red Block spawns Star 5 after flying through 5 rings!
        //    - Act 6 ("Behind Chain Chomp's Gate"): Pounding Chain Chomp's stake 3 times releases Star 6!
        foreach (var bobElem in FindObjectsByType<BobOmbBattlefieldController>(FindObjectsInactive.Include))
        {
            if (bobElem == null) continue;
            bobElem.ResetBattlefieldElement();

            switch (bobElem.elementType)
            {
                case BattlefieldElementType.KingBobOmbBoss:
                    bobElem.gameObject.SetActive(currentActNumber == 1);
                    if (bobElem.rewardStar != null) bobElem.rewardStar.gameObject.SetActive(false);
                    break;
                case BattlefieldElementType.KoopaTheQuickRacer:
                    bobElem.gameObject.SetActive(currentActNumber == 2);
                    if (bobElem.rewardStar != null) bobElem.rewardStar.gameObject.SetActive(false);
                    break;
                case BattlefieldElementType.WingCapRedBlock:
                    bobElem.gameObject.SetActive(currentActNumber == 5);
                    if (bobElem.rewardStar != null) bobElem.rewardStar.gameObject.SetActive(false);
                    break;
                case BattlefieldElementType.ChainChompStake:
                    if (bobElem.rewardStar != null) bobElem.rewardStar.gameObject.SetActive(false);
                    break;
                case BattlefieldElementType.SkyCoinRingCenter:
                case BattlefieldElementType.RollingIronBall:
                case BattlefieldElementType.SeesawBridge:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        // 3. Configure Jolly Roger Bay Act-Specific Elements:
        //    - Act 1 ("Plunder in the Sunken Ship"): SunkenPirateShip_Act1 active on seabed; FloatingPirateShip_Acts2To6 hidden!
        //    - Act 2+ ("Can the Eel Come Out to Play?", etc.): Sunken ship raised! FloatingPirateShip_Acts2To6 active on bay surface!
        JollyRogerBayController.ResetAllChestGroups();
        var sunkenShipGo = GameObject.Find("SunkenPirateShip_Act1");
        if (sunkenShipGo != null) sunkenShipGo.SetActive(currentActNumber == 1);

        var floatingShipGo = GameObject.Find("FloatingPirateShip_Acts2To6");
        if (floatingShipGo != null) floatingShipGo.SetActive(currentActNumber >= 2);

        foreach (var jrbElem in FindObjectsByType<JollyRogerBayController>(FindObjectsInactive.Include))
        {
            if (jrbElem == null) continue;
            jrbElem.ResetBayElement();

            switch (jrbElem.elementType)
            {
                case JollyRogerElementType.TreasureChestPuzzleMember:
                    if (jrbElem.rewardStar != null) jrbElem.rewardStar.gameObject.SetActive(false);
                    break;
                case JollyRogerElementType.UnagiTheEel:
                    if (jrbElem.rewardStar != null) jrbElem.rewardStar.gameObject.SetActive(false);
                    break;
                case JollyRogerElementType.MetalCapGreenBlock:
                    jrbElem.gameObject.SetActive(currentActNumber == 6);
                    break;
                case JollyRogerElementType.JetStreamGeyser:
                    if (jrbElem.rewardStar != null) jrbElem.rewardStar.gameObject.SetActive(currentActNumber == 6);
                    break;
                case JollyRogerElementType.SnapClamShell:
                case JollyRogerElementType.FallingCavernPillar:
                case JollyRogerElementType.FloatingGalleonShip:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        // 4. Activate ONLY the current Act's direct platforming Power Star (if it isn't gated behind a Boss / Red Coins / Race / Wall / Stake / Chests / Eel)
        foreach (var item in _allCollectibles)
        {
            if (item == null || item.kind != CollectibleKind.PowerStar) continue;
            if (item.missionActIndex != currentActNumber) continue;

            // Check if this star is gated by a boss, red coins, race, breakaway wall, wing cap, chain chomp stake, treasure chests, or Unagi the Eel
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

            item.gameObject.SetActive(!isGated);
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

        if (sfxSlider == null) return;
        {
            sfxSlider.SetValueWithoutNotify(GameAudio.Instance != null ? GameAudio.Instance.SfxVol : 0.90f);
            sfxSlider.onValueChanged.RemoveAllListeners();
            sfxSlider.onValueChanged.AddListener(val =>
            {
                if (GameAudio.Instance != null) GameAudio.Instance.SetSfxVolume(val);
            });
        }
    }

    private float _unscaledClock;

    private void Update()
    {
        var unscaledDt = Time.unscaledDeltaTime > 0.0001f ? Time.unscaledDeltaTime : 0.016f;
        var scaledDt = Time.deltaTime > 0.0001f ? Time.deltaTime : (Time.timeScale > 0f ? 0.016f * Time.timeScale : 0f);
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
                runElapsedSeconds += scaledDt;
                UpdateTimerDisplay();

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

        // Smooth count-up on coin counter
        if (coinCounterText == null) return;
        _displayedCoinCount = Mathf.MoveTowards(_displayedCoinCount, coinsCollected, unscaledDt * 25f);
        coinCounterText.text = $"COINS  {Mathf.RoundToInt(_displayedCoinCount):00}";
    }

    private static bool WasStartKeyPressed()
    {
        var kb = Keyboard.current;
        if (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame))
            return true;
        var gp = Gamepad.current;
        return gp != null && (gp.buttonSouth.wasPressedThisFrame || gp.startButton.wasPressedThisFrame);
    }

    private void StartGameFromTitle()
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

    private void PauseGame()
    {
        if (GameAudio.Instance != null) GameAudio.Instance.PlayUIClick();
        TransitionToState(GameState.Paused);
    }

    private void ResumeGame()
    {
        if (GameAudio.Instance != null) GameAudio.Instance.PlayUIClick();
        TransitionToState(GameState.Playing);
    }

    private void TriggerWinEnding()
    {
        TransitionToState(GameState.Ended);
    }

    private void ReturnToTitle()
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

    public void QuitGame()
    {
        if (GameAudio.Instance != null) GameAudio.Instance.PlayUIClick();
#if UNITY_EDITOR
        Debug.Log("[GameManager] Quit requested inside Editor — returning to Title.");
        ReturnToTitle();
#else
            Application.Quit();
#endif
    }

    private void ResetRunStateClean()
    {
        coinsCollected = 0;
        redCoinsCollected = 0;
        starsCollected = 0;
        runElapsedSeconds = 0f;
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

        if (GameAudio.Instance != null)
        {
            GameAudio.Instance.SetPausedDuck(newState == GameState.Paused);
        }

        if (newState == GameState.Ended && endedSummaryText != null)
        {
            var mins = Mathf.FloorToInt(runElapsedSeconds / 60f);
            var secs = runElapsedSeconds % 60f;
            endedSummaryText.text =
                $"COURTYARD CLEAR!\n" +
                $"POWER STARS: {starsCollected}/{targetStarsToWin}   •   COINS: {coinsCollected}\n" +
                $"CLEAR TIME: {mins:00}:{secs:00.0}   •   RANK: LOCAL DEVICE RECORD";
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
                if (player != null) player.RefillOxygen(4.5f); // 1 SM64 Power Wedge refill!
                if (GameAudio.Instance != null) GameAudio.Instance.PlayPickupBlip();
                if (VFXPool.Instance != null) VFXPool.Instance.SpawnPickupBurst(worldPos, isStar: false);
                SpawnFloatingWorldText(worldPos, "+1 COIN", Palette.AccentStarGold);
                AnimateStarPowerBar(Mathf.Clamp01( starPowerFillBar != null ? starPowerFillBar.fillAmount + 0.08f : 0.2f ));
                break;
            }
            case CollectibleKind.RedCoin:
            {
                coinsCollected += 2;
                redCoinsCollected += 1;
                if (player != null) player.RefillOxygen(9.0f); // 2 SM64 Power Wedges refill!
                var pitchBoost = redCoinsCollected * 0.06f;
                if (GameAudio.Instance != null) GameAudio.Instance.PlayPickupBlip(pitchBoost);
                if (VFXPool.Instance != null) VFXPool.Instance.SpawnPickupBurst(worldPos, isStar: false);
                SpawnFloatingWorldText(worldPos, $"RED COIN {redCoinsCollected}/8", Palette.CharacterPrimary);
                AnimateStarPowerBar(Mathf.Clamp01(starPowerFillBar != null ? starPowerFillBar.fillAmount + 0.12f : 0.35f));

                if (redCoinsCollected >= 8 && secretRedCoinStar != null && !secretRedCoinStar.IsCollected)
                {
                    if (secretRedCoinStar.missionActIndex == currentActNumber || targetStarsToWin <= 3)
                    {
                        secretRedCoinStar.gameObject.SetActive(true);
                        TriggerMilestoneSequence("SECRET STAR REVEALED!", "8/8 RED COINS COLLECTED");
                    }
                }

                break;
            }
            case CollectibleKind.PowerStar:
            {
                starsCollected += 1;
                coinsCollected += 5;
                var starTitle = item != null && !string.IsNullOrEmpty(item.starTitle) ? item.starTitle : $"POWER STAR #{starsCollected}";
                SpawnFloatingWorldText(worldPos, "+1 POWER STAR!", Palette.AccentStarGold);
                TriggerMilestoneSequence($"ACT {currentActNumber} CLEAR! ({starsCollected}/{targetStarsToWin})", starTitle);

                if (starsCollected >= targetStarsToWin)
                {
                    StartCoroutine(DelayedWinRoutine(2.1f));
                }
                else
                {
                    var nextAct = Mathf.Clamp(currentActNumber + 1, 1, targetStarsToWin);
                    StartCoroutine(AdvanceToNextActMissionRoutine(nextAct, 1.85f));
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

        SetupActMissionState(nextAct);

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
            coinCounterText.text = $"COINS  {coinsCollected:00}";
        if (starCounterText != null)
            starCounterText.text = $"STARS  {starsCollected}/{targetStarsToWin}  •  ACT {currentActNumber}";
        if (redCoinCounterText != null)
            redCoinCounterText.text = $"RED COINS  {redCoinsCollected} / 8";
        UpdateTimerDisplay();
    }

    private void UpdateTimerDisplay()
    {
        if (timerText == null) return;
        var mins = Mathf.FloorToInt(runElapsedSeconds / 60f);
        var secs = runElapsedSeconds % 60f;
        if (player != null && player.hasWaterInCourse)
        {
            var wedges = player.OxygenWedges;
            var airLabel = JollyRogerBayController.IsMetalMarioActive
                ? "<color=#38E878>METAL CAP ACTIVE</color>"
                : (player.IsSwimming ? $"<color=#54E2FF>OXYGEN {wedges}/8</color>" : $"AIR {wedges}/8");
            timerText.text = $"TIME {mins:00}:{secs:00.0}  •  {airLabel}";

            if (starPowerFillBar != null && player.IsSwimming)
            {
                starPowerFillBar.fillAmount = player.OxygenNormalized;
            }
        }
        else
        {
            timerText.text = $"TIME  {mins:00}:{secs:00.0}";
        }
    }
}