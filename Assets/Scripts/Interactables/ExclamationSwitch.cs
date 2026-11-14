using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Classic SM64-style Floor '!' Switch.
/// Presses down when Mario steps on or Ground Pounds it, firing OnPressed.
/// </summary>
public class ExclamationSwitch : MonoBehaviour
{
    [SerializeField] private Transform buttonCap;
    [SerializeField] private float pressedOffsetY = -0.14f;
    [SerializeField] private bool stayPressed = true;
    [SerializeField] private float resetDelay = 8f;

    public UnityEvent onPressed;

    private Vector3 _capInitialLocalPos;
    private bool _isPressed;
    private float _resetTimer;

    private void Awake()
    {
        if (buttonCap != null)
            _capInitialLocalPos = buttonCap.localPosition;
    }

    private void Update()
    {
        if (_isPressed && !stayPressed)
        {
            _resetTimer -= Time.deltaTime;
            if (_resetTimer <= 0f)
                ResetSwitch();
        }

        if (buttonCap != null)
        {
            Vector3 target = _isPressed
                ? _capInitialLocalPos + new Vector3(0f, pressedOffsetY, 0f)
                : _capInitialLocalPos;
            buttonCap.localPosition = Vector3.Lerp(buttonCap.localPosition, target, 14f * Time.deltaTime);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_isPressed) return;
        if (other.GetComponent<MarioRemakeController>() != null || other.CompareTag("Player"))
        {
            Press();
        }
    }

    public void Press()
    {
        if (_isPressed) return;
        _isPressed = true;
        _resetTimer = resetDelay;
        if (GameAudio.Instance != null)
            GameAudio.Instance.PlayPowerUpCap();
        if (VFXPool.Instance != null)
            VFXPool.Instance.SpawnPickupBurst(transform.position + Vector3.up * 0.5f, isStar: true);
        onPressed?.Invoke();
    }

    public void ResetSwitch()
    {
        _isPressed = false;
    }
}
