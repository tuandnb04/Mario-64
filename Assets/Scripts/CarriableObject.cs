using System;
using System.Collections.Generic;
using Mario64Remake;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Classic SM64 Carriable Object (Wooden Crate, Metal Box, Throwable Bob-omb, Rock):
/// Hero can Grab/Lift overhead, Carry around, Place down cleanly, or Hurl forward!
/// </summary>
public class CarriableObject : MonoBehaviour
{
    public enum CarryState
    {
        Idle,
        Carried,
        ThrownFlight,
        BrokenRespawning
    }

    [Header("Carriable Settings")]
    public string displayName = "Wooden Box";
    public bool explodeOnImpact;
    public bool breakOnImpact = true;
    public bool respawnAfterBreak = true;
    public float respawnDelay = 3.5f;
    public float throwSpeed = 14.5f;
    public float throwUpVelocity = 5.4f;
    public float carryHeightOffset = 0.48f;
    public int coinBonusOnBreak = 2;

    public CarryState State { get; private set; } = CarryState.Idle;
    public bool CanBePickedUp => State == CarryState.Idle && gameObject.activeInHierarchy && _regrabCooldown <= 0f;

    private Vector3 _originPos;
    private Quaternion _originRot;
    private Vector3 _originScale;
    private Collider[] _colliders;
    private Renderer[] _renderers;
    private Transform _carryAnchor;
    private Vector3 _flightVelocity;
    private float _flightTimer;
    private float _regrabCooldown;
    private float _respawnTimer;

    private void Awake()
    {
        CacheTransformAndParts();
    }

    private void CacheTransformAndParts()
    {
        if (_originScale.sqrMagnitude < 0.0001f)
        {
            _originPos = transform.position;
            _originRot = transform.rotation;
            _originScale = transform.localScale;
        }
        _colliders ??= GetComponentsInChildren<Collider>(true);
        _renderers ??= GetComponentsInChildren<Renderer>(true);
    }

    public void ResetCarriable()
    {
        CacheTransformAndParts();
        State = CarryState.Idle;
        _carryAnchor = null;
        _flightVelocity = Vector3.zero;
        _flightTimer = 0f;
        _regrabCooldown = 0f;
        _respawnTimer = 0f;
        transform.SetPositionAndRotation(_originPos, _originRot);
        transform.localScale = _originScale;
        SetVisibleAndSolid(visible: true, solid: true);
        gameObject.SetActive(true);
    }

    public void PickUp(Transform heroTransform)
    {
        CacheTransformAndParts();
        State = CarryState.Carried;
        _carryAnchor = heroTransform;
        _regrabCooldown = 0.25f;
        SetVisibleAndSolid(visible: true, solid: false);
        if (GameAudio.Instance != null) GameAudio.Instance.PlayLedgeClimbSound();
    }

    public void DropAtFeet(Vector3 dropPos, Quaternion heroRot)
    {
        State = CarryState.Idle;
        _carryAnchor = null;
        _regrabCooldown = 0.35f;
        transform.SetPositionAndRotation(dropPos, heroRot);
        SetVisibleAndSolid(visible: true, solid: true);
        if (GameAudio.Instance != null) GameAudio.Instance.PlayUIClick();
    }

    public void ThrowForward(Vector3 startPos, Vector3 forwardDir)
    {
        State = CarryState.ThrownFlight;
        _carryAnchor = null;
        _flightTimer = 0f;
        _regrabCooldown = 0.45f;
        forwardDir.y = 0f;
        if (forwardDir.sqrMagnitude < 0.001f) forwardDir = Vector3.forward;
        forwardDir.Normalize();

        transform.position = startPos;
        _flightVelocity = forwardDir * throwSpeed + Vector3.up * throwUpVelocity;
        SetVisibleAndSolid(visible: true, solid: false);
        if (GameAudio.Instance != null) GameAudio.Instance.PlayWallKickSound();
    }

    private void Update()
    {
        var dt = Time.deltaTime > 0.0001f ? Time.deltaTime : (Time.timeScale > 0f ? 0.016f * Time.timeScale : 0f);
        if (dt <= 0.00001f) return;

        if (_regrabCooldown > 0f) _regrabCooldown -= dt;

        switch (State)
        {
            case CarryState.Idle:
                break;

            case CarryState.Carried:
                if (_carryAnchor == null)
                {
                    DropAtFeet(transform.position, transform.rotation);
                    break;
                }
                transform.position = _carryAnchor.position + Vector3.up * (1.85f + carryHeightOffset);
                transform.rotation = _carryAnchor.rotation;
                break;

            case CarryState.ThrownFlight:
                UpdateThrownFlight(dt);
                break;

            case CarryState.BrokenRespawning:
                if (respawnAfterBreak)
                {
                    _respawnTimer -= dt;
                    if (_respawnTimer <= 0f)
                    {
                        ResetCarriable();
                    }
                }
                break;
        }
    }

    private void UpdateThrownFlight(float dt)
    {
        _flightTimer += dt;
        _flightVelocity.y -= 24f * dt;
        var step = _flightVelocity * dt;
        var dist = step.magnitude;
        transform.Rotate(360f * dt, 180f * dt, 0f, Space.Self);

        if (dist > 0.001f)
        {
            var hits = Physics.SphereCastAll(transform.position, 0.42f, step.normalized, dist, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (h.collider == null) continue;
                if (h.collider.GetComponentInParent<MarioRemakeController>() != null) continue;

                transform.position = h.point + h.normal * 0.45f;
                HandleImpactAt(transform.position);
                return;
            }
        }

        transform.position += step;
        if (_flightTimer > 3.0f || transform.position.y < -60f)
        {
            HandleImpactAt(transform.position);
        }
    }

    private void HandleImpactAt(Vector3 pos)
    {
        // Smash nearby crates, enemies, and trigger bomb blasts
        foreach (var crate in FindObjectsByType<DestructibleCrate>(FindObjectsInactive.Exclude))
        {
            if (crate != null && Vector3.Distance(crate.transform.position, pos) < 2.3f)
                crate.Smash(pos);
        }
        foreach (var bob in FindObjectsByType<BobOmbPatrol>(FindObjectsInactive.Exclude))
        {
            if (bob != null && Vector3.Distance(bob.transform.position, pos) < 2.6f)
                bob.OnMarioAttackHit();
        }
        foreach (var goomba in FindObjectsByType<GoombaPatrol>(FindObjectsInactive.Exclude))
        {
            if (goomba != null && Vector3.Distance(goomba.transform.position, pos) < 2.6f)
                goomba.OnAttacked();
        }
        foreach (var wf in FindObjectsByType<WhompFortressElement>(FindObjectsInactive.Exclude))
        {
            if (wf != null && wf.hazardType == FortressHazardType.BreakawayBrickWall &&
                Vector3.Distance(wf.transform.position, pos) < 3.4f)
            {
                wf.ShatterBreakawayWall();
            }
        }

        if (explodeOnImpact || breakOnImpact)
        {
            if (GameAudio.Instance != null)
            {
                if (explodeOnImpact) GameAudio.Instance.PlayBowserBombExplosionSound();
                else GameAudio.Instance.PlayEnemyDefeatSound();
            }
            if (VFXPool.Instance != null)
                VFXPool.Instance.SpawnPickupBurst(pos + Vector3.up * 0.35f, isStar: explodeOnImpact);
            if (GameManager.Instance != null && coinBonusOnBreak > 0)
            {
                GameManager.Instance.coinsCollected += coinBonusOnBreak;
                GameManager.Instance.TriggerHitStop(0.035f);
            }
            if (respawnAfterBreak)
            {
                State = CarryState.BrokenRespawning;
                _respawnTimer = respawnDelay;
                SetVisibleAndSolid(visible: false, solid: false);
            }
            else
            {
                State = CarryState.BrokenRespawning;
                SetVisibleAndSolid(visible: false, solid: false);
            }
        }
        else
        {
            State = CarryState.Idle;
            _flightVelocity = Vector3.zero;
            SetVisibleAndSolid(visible: true, solid: true);
        }
    }

    private void SetVisibleAndSolid(bool visible, bool solid)
    {
        if (_renderers != null)
        {
            foreach (var r in _renderers)
                if (r != null) r.enabled = visible;
        }
        if (_colliders != null)
        {
            foreach (var c in _colliders)
                if (c != null) c.enabled = solid;
        }
    }
}
