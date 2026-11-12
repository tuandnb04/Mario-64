using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Stylized Bob-omb Sentinel patrolling the courtyard bridge & garden.
/// Waddles procedurally and winds its golden key on the back.
/// </summary>
public class BobOmbPatrol : MonoBehaviour
{
    [FormerlySerializedAs("PatrolOffset")] public Vector3 patrolOffset = new(3.2f, 0f, 0f);
    [FormerlySerializedAs("PatrolSpeed")] public float patrolSpeed = 1.15f;
    [FormerlySerializedAs("WindupKey")] public Transform windupKey;
    [FormerlySerializedAs("LeftFoot")] public Transform leftFoot;
    [FormerlySerializedAs("RightFoot")] public Transform rightFoot;

    private Vector3 _origin;
    private float _clock;

    private void Awake()
    {
        _origin = transform.position;
    }

    private void Update()
    {
        if (_origin.sqrMagnitude < 0.0001f) _origin = transform.position;
        var dt = Time.deltaTime > 0.0001f ? Time.deltaTime : (Time.timeScale > 0f ? 0.016f * Time.timeScale : 0f);
        _clock += dt * patrolSpeed;

        var s = Mathf.Sin(_clock * Mathf.PI);
        var nextPos = _origin + patrolOffset * s;
        var delta = nextPos - transform.position;
        delta.y = 0f;
        if (delta.sqrMagnitude > 0.0001f)
        {
            var targetRot = Quaternion.LookRotation(delta.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 1f - Mathf.Exp(-10f * dt));
        }

        var waddleY = Mathf.Abs(Mathf.Sin(_clock * 14f)) * 0.06f;
        transform.position = new Vector3(nextPos.x, _origin.y + waddleY, nextPos.z);

        if (windupKey != null)
        {
            windupKey.Rotate(0f, 0f, 180f * dt, Space.Self);
        }

        var footAngle = Mathf.Sin(_clock * 14f) * 28f;
        if (leftFoot != null) leftFoot.localRotation = Quaternion.Euler(footAngle, 0f, 0f);
        if (rightFoot != null) rightFoot.localRotation = Quaternion.Euler(-footAngle, 0f, 0f);
    }

    public void OnMarioAttackHit()
    {
        if (GameAudio.Instance != null) GameAudio.Instance.PlayGroundPoundSound();
        if (VFXPool.Instance != null) VFXPool.Instance.SpawnPickupBurst(transform.position + Vector3.up * 0.5f, isStar: false);
        if (GameManager.Instance != null)
        {
            GameManager.Instance.coinsCollected += 2;
            GameManager.Instance.TriggerHitStop(0.045f);
        }
        gameObject.SetActive(false);
    }
}