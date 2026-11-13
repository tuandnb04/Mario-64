using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Test Playground HUD, Zone Teleporters & Live Moveset Checklist Tracker for TestPlayground.unity.
/// </summary>
public class TestPlaygroundManager : MonoBehaviour
{
    [Serializable]
    public struct ZoneWaypoint
    {
        public string zoneName;
        public Vector3 position;
        public float yawDeg;
    }

    public ZoneWaypoint[] zones;
    public TextMeshProUGUI telemetryText;
    public TextMeshProUGUI checklistText;

    private static readonly string[] ChecklistOrder =
    {
        "SingleJump", "DoubleJump", "TripleJump", "LongJump", "Backflip", "SideFlip",
        "WallKick", "LedgeClimb", "GroundPound", "DiveBellySlide", "OdysseyRoll", "SpinAttack",
        "PunchKickCombo", "AirKick", "SlideKick", "SlopeSlide",
        "PoleClimb", "PoleHandstand", "CarryThrow", "Swim", "FirstPersonLook", "FreeCameraOrbit"
    };

    private readonly HashSet<string> _achievedMoves = new();
    private float _prevCamYaw;
    private bool _camTrackInit;

    private void Start()
    {
        if (GameManager.Instance != null && GameManager.Instance.currentState == GameState.Title)
        {
            // Keep title selectable OR auto-ready for playground testing!
        }
        WireZoneButtons();
        RefreshTelemetryUI();
    }

    public void RecordMove(string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        _achievedMoves.Add(key);
    }

    public bool HasMove(string key) => _achievedMoves.Contains(key);
    public int AchievedCount => _achievedMoves.Count;

    public void TeleportToZone(int index)
    {
        var hero = MarioRemakeController.Instance;
        if (hero == null || zones == null || index < 0 || index >= zones.Length) return;
        hero.TeleportAndFace(zones[index].position, zones[index].yawDeg);
        if (GameAudio.Instance != null) GameAudio.Instance.PlayUIClick();
    }

    public void ResetPlaygroundEnvironment()
    {
        foreach (var c in FindObjectsByType<CarriableObject>(FindObjectsInactive.Include))
            if (c != null) c.ResetCarriable();
        foreach (var cr in FindObjectsByType<DestructibleCrate>(FindObjectsInactive.Include))
            if (cr != null) cr.ResetCrate();
        foreach (var g in FindObjectsByType<GoombaPatrol>(FindObjectsInactive.Include))
            if (g != null) g.ResetGoomba();
        foreach (var bob in FindObjectsByType<BobOmbPatrol>(FindObjectsInactive.Include))
            if (bob != null) bob.gameObject.SetActive(true);
        foreach (var bowser in FindObjectsByType<BowserBossController>(FindObjectsInactive.Include))
            if (bowser != null) bowser.ResetBoss();
        TeleportToZone(0);
    }

    private void WireZoneButtons()
    {
        if (zones == null) return;
        for (var i = 0; i < zones.Length; i++)
        {
            var tf = transform.Find($"TestHUDRoot/ZoneBar/ZoneBtn_{i}");
            if (tf == null) continue;
            var btn = tf.GetComponent<Button>();
            if (btn == null) continue;
            var idx = i;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => TeleportToZone(idx));
        }
        var rstTf = transform.Find("TestHUDRoot/ZoneBar/ZoneBtn_Reset");
        if (rstTf == null || !rstTf.TryGetComponent<Button>(out var rBtn)) return;
        rBtn.onClick.RemoveAllListeners();
        rBtn.onClick.AddListener(ResetPlaygroundEnvironment);
    }

    private void Update()
    {
        var hero = MarioRemakeController.Instance;
        var gm = GameManager.Instance;
        var kb = Keyboard.current;

        if (gm != null && gm.currentState == GameState.Playing && kb != null)
        {
            if (kb.digit1Key.wasPressedThisFrame) TeleportToZone(0);
            else if (kb.digit2Key.wasPressedThisFrame) TeleportToZone(1);
            else if (kb.digit3Key.wasPressedThisFrame) TeleportToZone(2);
            else if (kb.digit4Key.wasPressedThisFrame) TeleportToZone(3);
            else if (kb.digit5Key.wasPressedThisFrame) TeleportToZone(4);
            else if (kb.digit6Key.wasPressedThisFrame) TeleportToZone(5);
            else if (kb.digit7Key.wasPressedThisFrame) TeleportToZone(6);
            else if (kb.digit8Key.wasPressedThisFrame) TeleportToZone(7);
            else if (kb.digit9Key.wasPressedThisFrame) TeleportToZone(8);
            else if (kb.digit0Key.wasPressedThisFrame) ResetPlaygroundEnvironment();
        }

        if (hero == null) return;
        if (!_camTrackInit)
        {
            _prevCamYaw = hero.CurrentCameraYaw;
            _camTrackInit = true;
        }
        if (Mathf.Abs(Mathf.DeltaAngle(_prevCamYaw, hero.CurrentCameraYaw)) > 8f)
        {
            RecordMove("FreeCameraOrbit");
            _prevCamYaw = hero.CurrentCameraYaw;
        }
        RefreshTelemetryUI();
    }

    private void RefreshTelemetryUI()
    {
        var hero = MarioRemakeController.Instance;
        if (hero == null) return;

        if (telemetryText != null)
        {
            var camMode = hero.IsFirstPersonLooking ? "1ST-PERSON (C-UP)" : (Cursor.lockState == CursorLockMode.Locked ? "FREE 360° [MOUSE LOCKED]" : "FREE 360° [CLICK/DRAG]");
            telemetryText.text =
                $"<b>SM64 + ODYSSEY SANDBOX GYM</b>  |  State: <color=#FFD93D>{hero.CurrentStateLabel}</color>\n" +
                $"Speed: {hero.CurrentPlanarSpeed:F1} m/s  |  VertY: {hero.CurrentVerticalSpeed:F1}  |  Slope: {hero.CurrentGroundAngle:F0}°  |  Cam: {camMode}\n" +
                $"<b>Controls:</b> WASD Move | Mouse Free Orbit (Tab Unlock, V 1st-Person, Scroll Zoom) | Space Jump/WallKick | C Crouch/GP | Shift/C+E Roll | Z Spin | E/R-Click Dive | F/L-Click Punch/Carry/Throw | Keys 1-9 Teleport | 0 Reset";
        }

        if (checklistText == null) return;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"<b>MOVES TESTED ({_achievedMoves.Count}/{ChecklistOrder.Length}):</b>");
        for (var i = 0; i < ChecklistOrder.Length; i++)
        {
            var key = ChecklistOrder[i];
            var ok = _achievedMoves.Contains(key);
            sb.Append(ok ? $"<color=#6BCB77>[OK] {key}</color>" : $"<color=#C0C8D8>[ ] {key}</color>");
            if (i % 2 == 1) sb.AppendLine();
            else sb.Append("   ");
        }
        checklistText.text = sb.ToString();
    }
}
