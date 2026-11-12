using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Scale-pop proxy for the collector (game-feel.md recipe: 1 -> 1.25 -> 1 over 0.15s OutBack).
/// Records baseline scale on Awake so it always returns to the exact pre-pop baseline scale within 0.5s.
/// </summary>
public class TransformScalePopRoot : MonoBehaviour
{
    [FormerlySerializedAs("PopMultiplier")] public float popMultiplier = 1.25f;
    [FormerlySerializedAs("PopDuration")] public float popDuration = 0.15f;

    private Vector3 _baselineScale;
    private Coroutine _popRoutine;

    private void Awake()
    {
        _baselineScale = transform.localScale;
    }

    public void TriggerScalePop(float customMultiplier = -1f)
    {
        if (_baselineScale.sqrMagnitude < 0.0001f)
            _baselineScale = transform.localScale;

        if (_popRoutine != null)
        {
            StopCoroutine(_popRoutine);
            transform.localScale = _baselineScale;
        }
        var mult = customMultiplier > 0f ? customMultiplier : popMultiplier;
        _popRoutine = StartCoroutine(PopRoutine(mult));
    }

    private IEnumerator PopRoutine(float mult)
    {
        var peak = _baselineScale * mult;
        var half = popDuration * 0.45f;
        var back = popDuration * 0.55f;

        yield return Ease.Tween(half, t =>
        {
            transform.localScale = Vector3.LerpUnclamped(_baselineScale, peak, Ease.OutBack(t));
        }, Ease.OutCubic, unscaled: true);

        yield return Ease.Tween(back, t =>
        {
            transform.localScale = Vector3.LerpUnclamped(peak, _baselineScale, t);
        }, Ease.OutCubic, unscaled: true);

        transform.localScale = _baselineScale;
        _popRoutine = null;
    }
}