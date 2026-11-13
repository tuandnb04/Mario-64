using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Easing curves and coroutine tween helper.
/// Runs on UNSCALED time by default so pause fades and UI motion survive Time.timeScale = 0.
/// Pass unscaled: false for gameplay tweens that should freeze under pause or hit-stop.
/// </summary>
public static class Ease
{
    public static float OutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        var f = 1f - t;
        return 1f - (f * f * f);
    }

    public static float OutBack(float t)
    {
        t = Mathf.Clamp01(t);
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        var tm1 = t - 1f;
        return 1f + c3 * (tm1 * tm1 * tm1) + c1 * (tm1 * tm1);
    }

    public static float OutBounceCustom(float t)
    {
        t = Mathf.Clamp01(t);
        const float n1 = 7.5625f;
        const float d1 = 2.75f;
        switch (t)
        {
            case < 1f / d1:
                return n1 * t * t;
            case < 2f / d1:
                t -= 1.5f / d1; return n1 * t * t + 0.75f;
            case < 2.5f / d1:
                t -= 2.25f / d1; return n1 * t * t + 0.9375f;
            default:
                t -= 2.625f / d1;
                return n1 * t * t + 0.984375f;
        }
    }

    public static IEnumerator Tween(
        float duration,
        Action<float> apply,
        Func<float, float> ease,
        bool unscaled = true)
    {
        if (duration <= 0.0001f)
        {
            apply?.Invoke(1f);
            yield break;
        }

        for (var t = 0f; t < 1f;)
        {
            apply?.Invoke(ease?.Invoke(t) ?? t);
            yield return null;
            var stepDt = unscaled
                ? (Time.unscaledDeltaTime > 0.0001f ? Time.unscaledDeltaTime : 0.016f)
                : (Time.deltaTime > 0.0001f ? Time.deltaTime : 0.016f * Time.timeScale);
            t += stepDt / duration;
        }

        apply?.Invoke(1f);
    }
}