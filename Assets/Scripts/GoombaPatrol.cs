using System;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// Classic SM64 Goomba AI:
/// 1. Patrol: Wanders around home anchor with jaunty waddle & ear/foot bounce.
/// 2. Alert (!): When Mario enters sight radius, hops upward 0.28m in surprise & snaps to face Mario!
/// 3. Chase: Charges eagerly after Mario with ground-snap edge awareness (never runs off cliffs).
/// 4. Defeat: Flattens like a pancake when Stomped / Ground-Pounded before bursting into +1 Coin,
///    or spins away when Punched / Kicked / Rolled!
/// </summary>
public class GoombaPatrol : MonoBehaviour
{
    private enum AiState
    {
        Patrol,
        AlertHop,
        Chase,
        Squished,
        KickFly
    }

    public Vector3 patrolOffset = new(2.6f, 0f, 0f);
    public float patrolSpeed = 1.3f;
    public float detectRange = 8.5f;
    public float chaseSpeed = 4.3f;
    public float maxLeashRadius = 11.5f;
    public bool respawnInPlayground;

    public string CurrentAiState => _state.ToString();

    private AiState _state = AiState.Patrol;
    private Vector3 _origin;
    private Quaternion _originRot;
    private Vector3 _wanderTarget;
    private float _wanderTimer;
    private float _clock;
    private float _stateTimer;
    private bool _defeated;
    private float _respawnTimer;
    private float _currentGroundY;
    private Vector3 _kickVel;
    private Collider[] _colliders;
    private Renderer[] _renderers;

    private void Awake()
    {
        _origin = transform.position;
        _originRot = transform.rotation;
        _currentGroundY = _origin.y;
        _colliders = GetComponentsInChildren<Collider>(true);
        _renderers = GetComponentsInChildren<Renderer>(true);
        PickNewWanderTarget();
    }

    public void ResetGoomba()
    {
        _defeated = false;
        _state = AiState.Patrol;
        _stateTimer = 0f;
        _respawnTimer = 0f;
        transform.SetPositionAndRotation(_origin, _originRot);
        _currentGroundY = _origin.y;
        transform.localScale = Vector3.one;
        SetVisualSolid(true);
        PickNewWanderTarget();
        gameObject.SetActive(true);
    }

    private void PickNewWanderTarget()
    {
        var baseRadius = Mathf.Clamp(patrolOffset.magnitude, 2.0f, 4.2f);
        var ang = Random.Range(0f, Mathf.PI * 2f);
        var r = Random.Range(baseRadius * 0.45f, baseRadius);
        _wanderTarget = _origin + new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
        _wanderTimer = Random.Range(1.4f, 2.5f);
    }

    private void Update()
    {
        var dt = Time.deltaTime > 0.0001f ? Time.deltaTime : (Time.timeScale > 0f ? 0.016f * Time.timeScale : 0f);
        if (dt <= 0.00001f) return;

        if (_origin.sqrMagnitude < 0.0001f)
        {
            _origin = transform.position;
            _currentGroundY = _origin.y;
        }

        if (_defeated)
        {
            if (!respawnInPlayground) return;
            _respawnTimer -= dt;
            if (_respawnTimer <= 0f) ResetGoomba();
            return;
        }

        _clock += dt * patrolSpeed;
        _stateTimer += dt;

        var hero = MarioRemakeController.Instance;
        switch (_state)
        {
            case AiState.Patrol:
                UpdatePatrol(hero, dt);
                break;
            case AiState.AlertHop:
                UpdateAlertHop(hero, dt);
                break;
            case AiState.Chase:
                UpdateChase(hero, dt);
                break;
            case AiState.Squished:
                UpdateSquished();
                return;
            case AiState.KickFly:
                UpdateKickFly(dt);
                return;
            default:
                throw new ArgumentOutOfRangeException();
        }

        if (hero == null || !(Vector3.Distance(hero.transform.position + Vector3.up * 0.45f,
                transform.position + Vector3.up * 0.45f) < 1.15f)) return;
        var isStomp = hero.IsStompingDown && hero.transform.position.y >= _currentGroundY + 0.35f;
        if (isStomp)
        {
            hero.BounceUpFromStomp(1.75f);
            TriggerSquishDefeat();
        }
        else if (hero.IsAttackingOrSliding)
        {
            TriggerKickDefeat(hero.transform.position);
        }
        else
        {
            hero.TakeDamage(1, transform.position);
        }
    }

    private void UpdatePatrol(MarioRemakeController hero, float dt)
    {
        _wanderTimer -= dt;
        var toTarget = _wanderTarget - transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.25f || _wanderTimer <= 0f)
        {
            PickNewWanderTarget();
            toTarget = _wanderTarget - transform.position;
            toTarget.y = 0f;
        }

        MoveWithGroundCheck(toTarget, 1.9f * patrolSpeed, 9.5f, dt);

        if (!CanDetectHero(hero, detectRange)) return;
        _state = AiState.AlertHop;
        _stateTimer = 0f;
        if (GameAudio.Instance != null) GameAudio.Instance.PlayPickupBlip(-0.15f);
    }

    private void UpdateAlertHop(MarioRemakeController hero, float dt)
    {
        if (hero != null)
        {
            var toHero = hero.transform.position - transform.position;
            toHero.y = 0f;
            if (toHero.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(toHero.normalized, Vector3.up), 1f - Mathf.Exp(-18f * dt));
            }
        }

        var t = Mathf.Clamp01(_stateTimer / 0.24f);
        var hop = Mathf.Sin(t * Mathf.PI) * 0.36f;
        transform.position = new Vector3(transform.position.x, _currentGroundY + hop, transform.position.z);
        transform.localScale = new Vector3(1f - hop * 0.25f, 1f + hop * 0.45f, 1f - hop * 0.25f);

        if (!(t >= 1f)) return;
        transform.localScale = Vector3.one;
        _state = AiState.Chase;
        _stateTimer = 0f;
    }

    private void UpdateChase(MarioRemakeController hero, float dt)
    {
        if (hero == null || Vector3.Distance(hero.transform.position, _origin) > maxLeashRadius + 2f || Mathf.Abs(hero.transform.position.y - _currentGroundY) > 4.5f)
        {
            _state = AiState.Patrol;
            _stateTimer = 0f;
            transform.localRotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            PickNewWanderTarget();
            return;
        }

        var toHero = hero.transform.position - transform.position;
        toHero.y = 0f;
        MoveWithGroundCheck(toHero, chaseSpeed, 15f, dt);

        var tilt = Mathf.Sin(_clock * 16f) * 8f;
        transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, tilt);
    }

    private void MoveWithGroundCheck(Vector3 flatDir, float speed, float turnRate, float dt)
    {
        if (flatDir.sqrMagnitude < 0.0001f) return;
        var dir = flatDir.normalized;
        var nextRot = Quaternion.LookRotation(dir, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, nextRot, 1f - Mathf.Exp(-turnRate * dt));

        var candidate = transform.position + dir * (speed * dt);
        candidate.y = _currentGroundY;

        var flatFromOrigin = new Vector2(candidate.x - _origin.x, candidate.z - _origin.z);
        if (flatFromOrigin.magnitude > maxLeashRadius)
        {
            PickNewWanderTarget();
            return;
        }

        // Check ground ahead so Goombas stay cleanly on bridges & ledges
        if (TrySampleGround(candidate, out var groundY))
        {
            _currentGroundY = Mathf.Lerp(_currentGroundY, groundY, 1f - Mathf.Exp(-14f * dt));
            var bounce = Mathf.Abs(Mathf.Sin(_clock * (speed > 2.5f ? 16f : 10f))) * (speed > 2.5f ? 0.11f : 0.06f);
            transform.position = new Vector3(candidate.x, _currentGroundY + bounce, candidate.z);
        }
        else if (_state == AiState.Patrol)
        {
            PickNewWanderTarget();
        }
    }

    private bool TrySampleGround(Vector3 worldPos, out float groundY)
    {
        var origin = new Vector3(worldPos.x, _currentGroundY + 1.1f, worldPos.z);
        if (Physics.Raycast(origin, Vector3.down, out var hit, 2.4f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.GetComponentInParent<MarioRemakeController>() == null &&
                hit.collider.GetComponentInParent<GoombaPatrol>() != this)
            {
                groundY = hit.point.y;
                return true;
            }
        }
        groundY = _currentGroundY;
        return false;
    }

    private bool CanDetectHero(MarioRemakeController hero, float range)
    {
        if (hero == null || hero.transform.position.y < _currentGroundY - 2.0f) return false;
        var d = Vector2.Distance(new Vector2(hero.transform.position.x, hero.transform.position.z), new Vector2(transform.position.x, transform.position.z));
        return d < range && Mathf.Abs(hero.transform.position.y - _currentGroundY) < 3.5f;
    }

    public void OnAttacked()
    {
        if (_defeated || _state == AiState.Squished || _state == AiState.KickFly) return;
        TriggerSquishDefeat();
    }

    private void TriggerSquishDefeat()
    {
        if (_defeated || _state == AiState.Squished) return;
        _state = AiState.Squished;
        _stateTimer = 0f;
        transform.position = new Vector3(transform.position.x, _currentGroundY, transform.position.z);
        transform.localScale = new Vector3(1.38f, 0.22f, 1.38f);
        if (GameAudio.Instance != null) GameAudio.Instance.PlayEnemyDefeatSound();
        AwardDefeatRewards();
    }

    private void TriggerKickDefeat(Vector3 attackerPos)
    {
        if (_defeated || _state == AiState.KickFly) return;
        _state = AiState.KickFly;
        _stateTimer = 0f;
        var push = (transform.position - attackerPos);
        push.y = 0f;
        if (push.sqrMagnitude < 0.001f) push = transform.forward;
        _kickVel = push.normalized * 11f + Vector3.up * 6.5f;
        if (GameAudio.Instance != null) GameAudio.Instance.PlayEnemyDefeatSound();
        AwardDefeatRewards();
    }

    private static void AwardDefeatRewards()
    {
        if (GameManager.Instance == null) return;
        GameManager.Instance.coinsCollected += 1;
        GameManager.Instance.TriggerHitStop(0.03f);
    }

    private void UpdateSquished()
    {
        if (_stateTimer < 0.26f) return;
        FinishDefeat();
    }

    private void UpdateKickFly(float dt)
    {
        _kickVel.y -= 24f * dt;
        transform.position += _kickVel * dt;
        transform.Rotate(540f * dt, 360f * dt, 0f, Space.Self);
        if (_stateTimer >= 0.34f) FinishDefeat();
    }

    private void FinishDefeat()
    {
        _defeated = true;
        _respawnTimer = 4.0f;
        transform.localScale = Vector3.one;
        if (VFXPool.Instance != null) VFXPool.Instance.SpawnPickupBurst(transform.position + Vector3.up * 0.45f, isStar: false);
        SetVisualSolid(false);
    }

    private void SetVisualSolid(bool active)
    {
        if (_colliders != null)
            foreach (var c in _colliders) if (c != null) c.enabled = active;
        if (_renderers == null) return;
        foreach (var r in _renderers) if (r != null) r.enabled = active;
    }
}
