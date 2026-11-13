using UnityEngine;

/// <summary>
/// Art Bible — "Sunlit Odyssey Courtyard"
/// 1. Style statement: Stylized cel-shaded 3D adventure platformer: crisp toon lighting bands, warm sunlit courtyard stone, vibrant royal turf, and expressive tactile proportions.
/// 2. Palette: 7 core roles + helpers derived from the bible — single source of truth for all materials, sky, fog, lighting, VFX, and UI.
/// 3. Mood: Golden afternoon key light (#FFF2DC) at 42 deg elevation casting crisp warm-cool separated shadows (#2E3852), gradient sky-to-turf bounce, and glowing golden star energy (#FFD83D).
/// </summary>
public static class Palette
{
    // Core Art Bible Roles
    public static readonly Color FloorGrass         = Hex("#3B9E52"); // Royal emerald turf
    public static readonly Color WallsStone         = Hex("#EADBC4"); // Warm castle limestone / cobblestone
    public static readonly Color CharacterPrimary   = Hex("#E63946"); // Hero crimson cap & shirt
    public static readonly Color PropsTerracotta    = Hex("#D96B43"); // Castle roofs, bridges, brick blocks, Bob-omb accents
    public static readonly Color ShadowSlate        = Hex("#2E3852"); // Deep royal slate-navy shadow & outlines (never pure black)
    public static readonly Color AccentStarGold     = Hex("#FFD83D"); // Power Stars, coins, speed pads, XP/milestone highlights

    // Supporting Harmonic Tones from the same family
    public static readonly Color KeyLightWarm       = Hex("#FFF2DC"); // Warm afternoon sun key light
    public static readonly Color NeutralLight       = Hex("#FAF3E8"); // Cream parchment for UI text & clouds

    private static Color Hex(string hex)
    {
        return ColorUtility.TryParseHtmlString(hex, out var c) ? c : new Color(1f, 0f, 1f, 1f);
    }

    public static Color WithAlpha(Color c, float alpha)
    {
        return new Color(c.r, c.g, c.b, alpha);
    }
}