using System;

/// <summary>
/// Central decoupled event bus (Observer Pattern) for gameplay events.
/// Allows Audio, UI, VFX, and Level controllers to react without tight coupling.
/// </summary>
public static class GameEvents
{
    public static event Action<int> OnCoinCollected;
    public static event Action<int> OnStarCollected;
    public static event Action<int, int> OnPlayerHealthChanged;
    public static event Action OnPlayerDamaged;
    public static event Action<string> OnLevelCompleted;

    public static void RaiseCoinCollected(int totalCoins) => OnCoinCollected?.Invoke(totalCoins);
    public static void RaiseStarCollected(int totalStars) => OnStarCollected?.Invoke(totalStars);
    public static void RaisePlayerHealthChanged(int current, int max) => OnPlayerHealthChanged?.Invoke(current, max);
    public static void RaisePlayerDamaged() => OnPlayerDamaged?.Invoke();
    public static void RaiseLevelCompleted(string levelName) => OnLevelCompleted?.Invoke(levelName);
}

