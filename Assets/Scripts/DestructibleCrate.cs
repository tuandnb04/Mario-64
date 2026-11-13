using System;
using System.Collections.Generic;
using Mario64Remake;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Smashable crate / block for punches, kicks, rolls, spin attacks, dives, and ground pounds.
/// </summary>
public class DestructibleCrate : MonoBehaviour
{
    public bool respawnAfterBreak = true;
    public float respawnDelay = 4.0f;
    public int rewardCoins = 1;
    public StarCoinCollectible revealedStar;

    private bool _broken;
    private float _respawnTimer;
    private Collider[] _colliders;
    private Renderer[] _renderers;

    private void Awake()
    {
        _colliders = GetComponentsInChildren<Collider>(true);
        _renderers = GetComponentsInChildren<Renderer>(true);
    }

    public void ResetCrate()
    {
        _broken = false;
        _respawnTimer = 0f;
        SetActiveState(true);
    }

    public void Smash(Vector3 hitPos)
    {
        if (_broken) return;
        _broken = true;
        _respawnTimer = respawnDelay;
        SetActiveState(false);

        if (GameAudio.Instance != null) GameAudio.Instance.PlayEnemyDefeatSound();
        if (VFXPool.Instance != null) VFXPool.Instance.SpawnPickupBurst(transform.position + Vector3.up * 0.45f, isStar: false);
        if (GameManager.Instance != null)
        {
            if (rewardCoins > 0) GameManager.Instance.coinsCollected += rewardCoins;
            GameManager.Instance.TriggerHitStop(0.03f);
        }
        if (revealedStar != null && !revealedStar.IsCollected)
            revealedStar.gameObject.SetActive(true);
    }

    private void Update()
    {
        if (!_broken || !respawnAfterBreak) return;
        _respawnTimer -= Time.deltaTime;
        if (_respawnTimer <= 0f) ResetCrate();
    }

    private void SetActiveState(bool active)
    {
        if (_colliders != null)
        {
            foreach (var c in _colliders)
                if (c != null) c.enabled = active;
        }
        if (_renderers != null)
        {
            foreach (var r in _renderers)
                if (r != null) r.enabled = active;
        }
    }
}
