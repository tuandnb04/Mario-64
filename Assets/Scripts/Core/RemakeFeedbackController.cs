using System.Collections;
using UnityEngine;

/// <summary>
/// Modern remake game-feel controller subscribing to GameEvents.
/// Provides hitstop/time-scale punch on key gameplay moments without coupling to Mario or GameManager.
/// </summary>
public class RemakeFeedbackController : MonoBehaviour
{
    [SerializeField] private float damageHitstopDuration = 0.06f;
    [SerializeField] private float starCollectSlowMoDuration = 0.18f;
    [SerializeField] private float starCollectTimeScale = 0.35f;

    private static readonly WaitForSecondsRealtime s_waitDamage = new WaitForSecondsRealtime(0.06f);
    private Coroutine _timeScaleRoutine;

    private void OnEnable()
    {
        GameEvents.OnPlayerDamaged += HandlePlayerDamaged;
        GameEvents.OnStarCollected += HandleStarCollected;
    }

    private void OnDisable()
    {
        GameEvents.OnPlayerDamaged -= HandlePlayerDamaged;
        GameEvents.OnStarCollected -= HandleStarCollected;
        Time.timeScale = 1f;
    }

    private void HandlePlayerDamaged()
    {
        TriggerTimeScalePulse(0.05f, damageHitstopDuration);
    }

    private void HandleStarCollected(int totalStars)
    {
        TriggerTimeScalePulse(starCollectTimeScale, starCollectSlowMoDuration);
    }

    private void TriggerTimeScalePulse(float targetScale, float duration)
    {
        if (_timeScaleRoutine != null)
            StopCoroutine(_timeScaleRoutine);
        _timeScaleRoutine = StartCoroutine(TimeScalePulseRoutine(targetScale, duration));
    }

    private IEnumerator TimeScalePulseRoutine(float targetScale, float duration)
    {
        Time.timeScale = targetScale;
        if (Mathf.Approximately(duration, 0.06f))
            yield return s_waitDamage;
        else
            yield return new WaitForSecondsRealtime(duration);
        Time.timeScale = 1f;
        _timeScaleRoutine = null;
    }
}

