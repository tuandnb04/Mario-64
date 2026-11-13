using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Authentic SM64 Bob-omb AI:
/// 1. Patrol: Wanders around home area winding its golden key.
/// 2. Chase & Armed Fuse: When Mario enters sight radius, hops with alert, chases Mario at sprint speed,
///    spins key rapidly, flashes urgent orange-red pulse on body, and detonates after fuse countdown!
/// 3. Carriable / Thrown: Seamless integration with CarriableObject so Mario can Grab (F/Click) & Hurl Bob-ombs!
/// </summary>
public class BobOmbPatrol : MonoBehaviour
{
    private enum BombAiState
    {
        Patrol,
        AlertHop,
        ChaseFuse
    }

    [FormerlySerializedAs("PatrolOffset")] public Vector3 patrolOffset = new(3.2f, 0f, 0f);
    [FormerlySerializedAs("PatrolSpeed")] public float patrolSpeed = 1.15f;
    [FormerlySerializedAs("WindupKey")] public Transform windupKey;
    [FormerlySerializedAs("LeftFoot")] public Transform leftFoot;
    [FormerlySerializedAs("RightFoot")] public Transform rightFoot;

    public float detectRange = 9.0f;
    public float chaseSpeed = 3.65f;
    public float fuseDuration = 3.0f;
    public bool respawnAfterExplode = true;
    public float respawnDelay = 7.0f;

    public string CurrentAiState => _state.ToString();
    public bool IsFuseArmed => _state == BombAiState.ChaseFuse;

    private BombAiState _state = BombAiState.Patrol;
    private Vector3 _origin;
    private Quaternion _originRot;
    private Vector3 _wanderTarget;
    private float _wanderTimer;
    private float _clock;
    private float _fuseElapsed;
    private float _alertElapsed;
    private float _groundY;
    private bool _exploded;
    private float _respawnTimer;

    private CarriableObject _carriable;
    private MeshRenderer _bodyRenderer;
    private Color _origBodyColor = new(0.10f, 0.12f, 0.16f, 1f);
    private MaterialPropertyBlock _mpb;
    private Collider[] _colliders;
    private Renderer[] _renderers;

    private void Awake()
    {
        CacheOriginAndParts();
    }

    private void CacheOriginAndParts()
    {
        if (_origin.sqrMagnitude < 0.0001f)
        {
            _origin = transform.position;
            _originRot = transform.rotation;
            _groundY = _origin.y;
        }
        _carriable ??= GetComponent<CarriableObject>();
        _colliders ??= GetComponentsInChildren<Collider>(true);
        _renderers ??= GetComponentsInChildren<Renderer>(true);
        _mpb ??= new MaterialPropertyBlock();

        if (_bodyRenderer == null)
        {
            var bodyTr = transform.Find("BobOmbBody");
            if (bodyTr != null) _bodyRenderer = bodyTr.GetComponent<MeshRenderer>();
        }
        if (_bodyRenderer != null && _bodyRenderer.sharedMaterial != null)
        {
            _origBodyColor = _bodyRenderer.sharedMaterial.color;
        }
        if (_wanderTarget.sqrMagnitude < 0.0001f) PickNewWanderTarget();
    }

    public void ResetBobOmb()
    {
        CacheOriginAndParts();
        _exploded = false;
        _state = BombAiState.Patrol;
        _fuseElapsed = 0f;
        _alertElapsed = 0f;
        _respawnTimer = 0f;
        _groundY = _origin.y;
        transform.SetPositionAndRotation(_origin, _originRot);
        transform.localScale = Vector3.one;
        SetBodyFlash(0f);
        SetPartsActive(true);
        PickNewWanderTarget();
        if (_carriable != null) _carriable.ResetCarriable();
    }

    private void PickNewWanderTarget()
    {
        var rad = Mathf.Clamp(patrolOffset.magnitude, 2.2f, 4.5f);
        var ang = Random.Range(0f, Mathf.PI * 2f);
        var r = Random.Range(rad * 0.4f, rad);
        _wanderTarget = _origin + new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
        _wanderTimer = Random.Range(1.5f, 2.8f);
    }

    private void Update()
    {
        CacheOriginAndParts();
        var dt = Time.deltaTime > 0.0001f ? Time.deltaTime : (Time.timeScale > 0f ? 0.016f * Time.timeScale : 0f);
        if (dt <= 0.00001f) return;

        // Yield transform control when carried or thrown by Mario
        if (_carriable != null && _carriable.State != CarriableObject.CarryState.Idle)
        {
            if (windupKey != null) windupKey.Rotate(0f, 0f, 520f * dt, Space.Self);
            if (_carriable.State == CarriableObject.CarryState.Carried)
            {
                _fuseElapsed += dt;
                SetBodyFlash(Mathf.PingPong(_fuseElapsed * 6f, 1f));
            }
            return;
        }

        if (_exploded)
        {
            if (respawnAfterExplode)
            {
                _respawnTimer -= dt;
                if (_respawnTimer <= 0f) ResetBobOmb();
            }
            return;
        }

        _clock += dt * patrolSpeed;
        var hero = MarioRemakeController.Instance;

        switch (_state)
        {
            case BombAiState.Patrol:
                UpdatePatrol(hero, dt);
                break;
            case BombAiState.AlertHop:
                UpdateAlertHop(hero, dt);
                break;
            case BombAiState.ChaseFuse:
                UpdateChaseFuse(hero, dt);
                break;
        }
    }

    private void UpdatePatrol(MarioRemakeController hero, float dt)
    {
        SetBodyFlash(0f);
        _wanderTimer -= dt;
        var toTarget = _wanderTarget - transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.25f || _wanderTimer <= 0f)
        {
            PickNewWanderTarget();
            toTarget = _wanderTarget - transform.position;
            toTarget.y = 0f;
        }

        StepToward(toTarget, 1.85f * patrolSpeed, 8f, 180f, dt);

        if (CanSpotHero(hero))
        {
            _state = BombAiState.AlertHop;
            _alertElapsed = 0f;
            if (GameAudio.Instance != null) GameAudio.Instance.PlayPickupBlip(0.15f);
        }
    }

    private void UpdateAlertHop(MarioRemakeController hero, float dt)
    {
        _alertElapsed += dt;
        if (hero != null)
        {
            var dir = hero.transform.position - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir.normalized, Vector3.up), 1f - Mathf.Exp(-18f * dt));
        }
        var t = Mathf.Clamp01(_alertElapsed / 0.22f);
        var hop = Mathf.Sin(t * Mathf.PI) * 0.34f;
        transform.position = new Vector3(transform.position.x, _groundY + hop, transform.position.z);
        if (windupKey != null) windupKey.Rotate(0f, 0f, 420f * dt, Space.Self);

        if (t >= 1f)
        {
            _state = BombAiState.ChaseFuse;
            _fuseElapsed = 0f;
        }
    }

    private void UpdateChaseFuse(MarioRemakeController hero, float dt)
    {
        _fuseElapsed += dt;
        var urgency = Mathf.Clamp01(_fuseElapsed / fuseDuration);
        var pulse = Mathf.PingPong(_fuseElapsed * Mathf.Lerp(4f, 18f, urgency), 1f);
        SetBodyFlash(pulse);

        var scaleSwell = 1f + pulse * urgency * 0.16f;
        transform.localScale = new Vector3(scaleSwell, 1f + (1f - pulse) * urgency * 0.08f, scaleSwell);

        if (hero != null)
        {
            var toHero = hero.transform.position - transform.position;
            toHero.y = 0f;
            StepToward(toHero, Mathf.Lerp(chaseSpeed, chaseSpeed * 1.25f, urgency), 12f, 520f, dt);

            var contactDist = Vector3.Distance(hero.transform.position + Vector3.up * 0.45f, transform.position + Vector3.up * 0.5f);
            if (contactDist < 1.18f)
            {
                if (hero.IsAttackingOrSliding || hero.IsStompingDown)
                {
                    if (hero.IsStompingDown) hero.BounceUpFromStomp(1.65f);
                    OnMarioAttackHit();
                    return;
                }
                Detonate(damageHero: true);
                return;
            }
        }

        if (_fuseElapsed >= fuseDuration)
        {
            Detonate(damageHero: true);
        }
    }

    private void StepToward(Vector3 flatDir, float speed, float turnRate, float keySpinDegPerSec, float dt)
    {
        if (flatDir.sqrMagnitude > 0.0001f)
        {
            var dir = flatDir.normalized;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir, Vector3.up), 1f - Mathf.Exp(-turnRate * dt));
            var candidate = transform.position + dir * (speed * dt);
            candidate.y = _groundY;

            if (Vector2.Distance(new Vector2(candidate.x, candidate.z), new Vector2(_origin.x, _origin.z)) <= 12f &&
                TrySampleGround(candidate, out var gy))
            {
                _groundY = Mathf.Lerp(_groundY, gy, 1f - Mathf.Exp(-14f * dt));
                var waddleY = Mathf.Abs(Mathf.Sin(_clock * (speed > 2.5f ? 18f : 12f))) * 0.07f;
                transform.position = new Vector3(candidate.x, _groundY + waddleY, candidate.z);
            }
            else if (_state == BombAiState.Patrol)
            {
                PickNewWanderTarget();
            }
        }

        if (windupKey != null) windupKey.Rotate(0f, 0f, keySpinDegPerSec * dt, Space.Self);
        var footAngle = Mathf.Sin(_clock * (speed > 2.5f ? 20f : 14f)) * 32f;
        if (leftFoot != null) leftFoot.localRotation = Quaternion.Euler(footAngle, 0f, 0f);
        if (rightFoot != null) rightFoot.localRotation = Quaternion.Euler(-footAngle, 0f, 0f);
    }

    private bool TrySampleGround(Vector3 pos, out float gy)
    {
        var rayOrigin = new Vector3(pos.x, _groundY + 1.1f, pos.z);
        if (Physics.Raycast(rayOrigin, Vector3.down, out var hit, 2.5f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.GetComponentInParent<MarioRemakeController>() == null &&
                hit.collider.GetComponentInParent<BobOmbPatrol>() != this)
            {
                gy = hit.point.y;
                return true;
            }
        }
        gy = _groundY;
        return false;
    }

    private bool CanSpotHero(MarioRemakeController hero)
    {
        if (hero == null) return false;
        var flat = Vector2.Distance(new Vector2(hero.transform.position.x, hero.transform.position.z), new Vector2(transform.position.x, transform.position.z));
        return flat < detectRange && Mathf.Abs(hero.transform.position.y - _groundY) < 3.5f;
    }

    private void SetBodyFlash(float flash01)
    {
        if (_bodyRenderer == null || _mpb == null) return;
        var c = Color.Lerp(_origBodyColor, new Color(1.0f, 0.25f, 0.05f, 1f), flash01);
        _mpb.SetColor("_BaseColor", c);
        _mpb.SetColor("_Color", c);
        _mpb.SetColor("_EmissionColor", new Color(1.0f, 0.35f, 0.05f) * (flash01 * 1.6f));
        _bodyRenderer.SetPropertyBlock(_mpb);
    }

    private void Detonate(bool damageHero)
    {
        if (_exploded) return;
        _exploded = true;
        _respawnTimer = respawnDelay;
        transform.localScale = Vector3.one;
        SetBodyFlash(0f);

        if (GameAudio.Instance != null) GameAudio.Instance.PlayGroundPoundSound();
        if (VFXPool.Instance != null) VFXPool.Instance.SpawnPickupBurst(transform.position + Vector3.up * 0.6f, isStar: true);

        var hero = MarioRemakeController.Instance;
        if (damageHero && hero != null && Vector3.Distance(hero.transform.position, transform.position) < 2.35f)
        {
            hero.TakeDamage(1, transform.position);
        }
        SetPartsActive(false);
    }

    public void OnMarioAttackHit()
    {
        if (_exploded) return;
        _exploded = true;
        _respawnTimer = respawnDelay;
        transform.localScale = Vector3.one;
        SetBodyFlash(0f);
        if (GameAudio.Instance != null) GameAudio.Instance.PlayEnemyDefeatSound();
        if (VFXPool.Instance != null) VFXPool.Instance.SpawnPickupBurst(transform.position + Vector3.up * 0.5f, isStar: false);
        if (GameManager.Instance != null)
        {
            GameManager.Instance.coinsCollected += 2;
            GameManager.Instance.TriggerHitStop(0.045f);
        }
        SetPartsActive(false);
    }

    private void SetPartsActive(bool active)
    {
        if (_colliders != null)
            foreach (var c in _colliders) if (c != null) c.enabled = active;
        if (_renderers != null)
            foreach (var r in _renderers) if (r != null) r.enabled = active;
    }
}
