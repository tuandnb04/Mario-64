using UnityEngine;
using UnityEngine.Serialization;

public enum CollectibleKind
{
    YellowCoin,
    RedCoin,
    PowerStar
}

/// <summary>
/// Interactive Collectible (Yellow Coin, Red Coin, or Power Star).
/// Spins and bobs smoothly in the courtyard. On trigger with Player, fires GameManager pickup/milestone feel.
/// </summary>
[RequireComponent(typeof(Collider))]
public class StarCoinCollectible : MonoBehaviour
{
    [FormerlySerializedAs("Kind")] public CollectibleKind kind = CollectibleKind.YellowCoin;
    [FormerlySerializedAs("StarTitle")] public string starTitle = "COURTYARD POWER STAR";
    public int missionActIndex = 1; // 1..6 for ordered Act missions
    [FormerlySerializedAs("SpinSpeed")] public float spinSpeed = 120f;
    [FormerlySerializedAs("BobAmplitude")] public float bobAmplitude = 0.16f;
    [FormerlySerializedAs("BobFrequency")] public float bobFrequency = 1.4f;

    private Vector3 _initialPosition;
    private float _phaseOffset;

    public bool IsCollected { get; private set; }

    private void Awake()
    {
        _initialPosition = transform.position;
        _phaseOffset = (transform.position.x * 0.37f) + (transform.position.z * 0.53f);
        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private float _localClock;

    public void ResetCollectible()
    {
        if (_initialPosition.sqrMagnitude < 0.0001f) _initialPosition = transform.position;
        IsCollected = false;
        transform.position = _initialPosition;
        gameObject.SetActive(true);
    }

    private void Update()
    {
        if (IsCollected) return;
        if (_initialPosition.sqrMagnitude < 0.0001f)
        {
            _initialPosition = transform.position;
            _phaseOffset = (transform.position.x * 0.37f) + (transform.position.z * 0.53f);
        }
        var dt = Time.deltaTime > 0.0001f ? Time.deltaTime : (Time.timeScale > 0f ? 0.016f * Time.timeScale : 0f);
        _localClock += dt;
        transform.Rotate(0f, spinSpeed * dt, 0f, Space.World);
        var yOffset = Mathf.Sin((_localClock + _phaseOffset) * bobFrequency * Mathf.PI * 2f) * bobAmplitude;
        transform.position = _initialPosition + new Vector3(0f, yOffset, 0f);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (IsCollected) return;

        var isPlayer = other.GetComponent<MarioRemakeController>() != null ||
                       other.GetComponentInParent<MarioRemakeController>() != null ||
                       other.name.Contains("Mario");

        if (!isPlayer) return;

        Collect();
    }

    private void Collect()
    {
        if (IsCollected) return;
        IsCollected = true;

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnCollectiblePickedUp(this);
        }

        gameObject.SetActive(false);
    }

    /// <summary>
    /// Marks this star or coin as already collected when restoring persisted SaveManager state.
    /// </summary>
    public void MarkCollectedFromSave()
    {
        IsCollected = true;
        gameObject.SetActive(false);
    }
}