using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime & Editor procedural sprite factory for Super Mario 64 / 64 DS HUD artwork.
/// Produces clean anti-aliased RGBA32 textures/sprites:
/// - Rounded badge cards, metallic circular bezel ring, and 8 radial pie wedges for SM64's Power Meter
/// - Iconic 5-point Power Star icon, shiny Gold/Red coin badges, and Mario 'M' Cap icon
/// </summary>
public static class UISpriteFactory
{
    private static Sprite roundedBadge;
    private static Sprite ringFrame;
    private static Sprite starIcon;
    private static Sprite yellowCoinIcon;
    private static Sprite redCoinIcon;
    private static Sprite marioCapIcon;
    private static readonly Dictionary<int, Sprite> PieWedges = new();

    public static Sprite GetRoundedBadge()
    {
        if (roundedBadge != null) return roundedBadge;
        const int size = 64;
        const int radius = 16;
        var tex = NewTex("SM64_RoundedBadge", size, size);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var d = RoundedBoxSdf(x + 0.5f - size * 0.5f, y + 0.5f - size * 0.5f, size * 0.5f - 1.5f, size * 0.5f - 1.5f, radius);
                var alpha = Mathf.Clamp01(0.8f - d);
                var isBorder = d is > -2.5f and <= 0.8f;
                var col = isBorder ? new Color(1f, 1f, 1f, alpha) : new Color(0.95f, 0.96f, 0.99f, alpha);
                tex.SetPixel(x, y, col);
            }
        }
        tex.Apply();
        roundedBadge = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        return roundedBadge;
    }

    public static Sprite GetPowerRingFrame()
    {
        if (ringFrame != null) return ringFrame;
        const int size = 160;
        var tex = NewTex("SM64_PowerRing", size, size);
        const float center = (size - 1) * 0.5f;
        const float outerR = size * 0.485f;
        const float midR = size * 0.415f;
        const float innerR = size * 0.18f;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var r = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                if (r > outerR + 1f)
                {
                    tex.SetPixel(x, y, Color.clear);
                    continue;
                }

                var edgeAlpha = Mathf.Clamp01((outerR + 0.8f) - r);
                switch (r)
                {
                    case >= midR:
                    {
                        // Iconic deep royal-blue metallic outer bezel ring with top sheen
                        var highlight = Mathf.Clamp01((y - center) / outerR) * 0.28f;
                        var rim = new Color(0.11f + highlight, 0.24f + highlight * 0.8f, 0.56f + highlight, edgeAlpha);
                        tex.SetPixel(x, y, rim);
                        break;
                    }
                    case <= innerR:
                    {
                        var hub = new Color(0.10f, 0.14f, 0.26f, 0.96f);
                        tex.SetPixel(x, y, hub);
                        break;
                    }
                    default:
                        tex.SetPixel(x, y, new Color(0.06f, 0.09f, 0.17f, 0.88f));
                        break;
                }
            }
        }
        tex.Apply();
        ringFrame = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return ringFrame;
    }

    /// <summary>
    /// Generates one 45-degree radial wedge sprite (index 0..7 clockwise from 12 o'clock)
    /// so all 8 slices snap together cleanly into SM64's Power Meter wheel.
    /// </summary>
    public static Sprite GetPowerWedge(int wedgeIndex)
    {
        wedgeIndex = ((wedgeIndex % 8) + 8) % 8;
        if (PieWedges.TryGetValue(wedgeIndex, out var existing) && existing != null) return existing;

        const int size = 128;
        var tex = NewTex($"SM64_Wedge_{wedgeIndex}", size, size);
        const float center = (size - 1) * 0.5f;
        const float rMin = size * 0.165f;
        const float rMax = size * 0.435f;

        // Clockwise from 12 o'clock: segment k spans [k*45, (k+1)*45] clockwise
        var startDeg = wedgeIndex * 45f + 2.4f;
        var endDeg = (wedgeIndex + 1) * 45f - 2.4f;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = x - center;
                var dy = y - center;
                var r = Mathf.Sqrt(dx * dx + dy * dy);
                if (r is < rMin - 1f or > rMax + 1f)
                {
                    tex.SetPixel(x, y, Color.clear);
                    continue;
                }

                // Angle clockwise from top (+Y axis): atan2(dx, dy) in [0, 360)
                var deg = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg;
                if (deg < 0f) deg += 360f;

                var angMargin = Mathf.Min(deg - startDeg, endDeg - deg);
                if (angMargin < -1.2f)
                {
                    tex.SetPixel(x, y, Color.clear);
                    continue;
                }

                var radialAlpha = Mathf.Clamp01(Mathf.Min(r - rMin + 0.8f, rMax - r + 0.8f));
                var angleAlpha = Mathf.Clamp01(angMargin * 1.3f + 0.8f);
                var alpha = Mathf.Min(radialAlpha, angleAlpha);

                // Subtle 3D glossy highlight near outer top edge (stays white for tint multiplication!)
                var shade = 0.84f + 0.16f * Mathf.Clamp01((r - rMin) / (rMax - rMin));
                tex.SetPixel(x, y, new Color(shade, shade, Mathf.Min(1f, shade + 0.04f), alpha));
            }
        }
        tex.Apply();
        var sp = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        PieWedges[wedgeIndex] = sp;
        return sp;
    }

    private static Dictionary<string, Sprite> sm64DSSprites;

    private static Sprite TryLoadSm64DsSprite(string spriteName)
    {
        if (sm64DSSprites != null) return sm64DSSprites.GetValueOrDefault(spriteName);
        sm64DSSprites = new Dictionary<string, Sprite>();
        var all = Resources.LoadAll<Sprite>("Sprites/SM64DS_HUD");
        if (all == null) return sm64DSSprites.GetValueOrDefault(spriteName);
        foreach (var sp in all)
        {
            if (sp != null)
                sm64DSSprites.TryAdd(sp.name, sp);
        }
        return sm64DSSprites.GetValueOrDefault(spriteName);
    }

    public static Sprite GetKeyIcon(bool gold = true)
    {
        return TryLoadSm64DsSprite(gold ? "GoldKey" : "SilverKey") ?? GetStarIcon();
    }

    public static Sprite GetStarIcon()
    {
        var real = TryLoadSm64DsSprite("GoldStar");
        if (real != null) return real;
        if (starIcon != null) return starIcon;
        const int size = 72;
        var tex = NewTex("SM64_StarIcon", size, size);
        const float center = (size - 1) * 0.5f;
        const float outer = size * 0.44f;
        const float inner = size * 0.19f;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = x - center;
                var dy = y - center;
                var a = Mathf.Atan2(dy, dx);
                var r = Mathf.Sqrt(dx * dx + dy * dy);

                var sector = Mathf.Repeat(a - Mathf.PI * 0.5f, Mathf.PI * 2f / 5f);
                const float half = Mathf.PI / 5f;
                var t = Mathf.Abs(sector - half) / half;
                var starR = Mathf.Lerp(inner, outer, Mathf.Pow(t, 0.92f));
                var alpha = Mathf.Clamp01((starR - r) * 0.9f + 0.8f);

                if (alpha <= 0.01f)
                {
                    tex.SetPixel(x, y, Color.clear);
                    continue;
                }

                // Eye slits & golden highlight
                var leftEye = Vector2.Distance(new Vector2(x, y), new Vector2(center - 4.5f, center + 1f)) < 2.3f;
                var rightEye = Vector2.Distance(new Vector2(x, y), new Vector2(center + 4.5f, center + 1f)) < 2.3f;
                var edgeDark = starR - r < 2.6f;
                var col = (leftEye || rightEye)
                    ? new Color(0.14f, 0.11f, 0.08f, alpha)
                    : (edgeDark
                        ? new Color(0.86f, 0.54f, 0.06f, alpha)
                        : new Color(1.0f, 0.88f, 0.22f, alpha));
                tex.SetPixel(x, y, col);
            }
        }
        tex.Apply();
        starIcon = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return starIcon;
    }

    public static Sprite GetCoinIcon(bool redCoin)
    {
        var real = TryLoadSm64DsSprite(redCoin ? "RedCoin" : "TopGoldCoin");
        if (real != null) return real;
        switch (redCoin)
        {
            case false when yellowCoinIcon != null:
                return yellowCoinIcon;
            case true when redCoinIcon != null:
                return redCoinIcon;
        }

        const int size = 64;
        var tex = NewTex(redCoin ? "SM64_RedCoinIcon" : "SM64_GoldCoinIcon", size, size);
        const float center = (size - 1) * 0.5f;
        const float rx = size * 0.34f;
        const float ry = size * 0.42f;
        var cFill = redCoin ? new Color(0.95f, 0.22f, 0.20f) : new Color(1.0f, 0.82f, 0.12f);
        var cRim = redCoin ? new Color(0.65f, 0.08f, 0.08f) : new Color(0.78f, 0.48f, 0.04f);

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var nx = (x - center) / rx;
                var ny = (y - center) / ry;
                var d = Mathf.Sqrt(nx * nx + ny * ny);
                var alpha = Mathf.Clamp01((1.04f - d) * 16f);
                if (alpha <= 0.01f)
                {
                    tex.SetPixel(x, y, Color.clear);
                    continue;
                }

                var isSlit = Mathf.Abs(x - center) < 2.6f && Mathf.Abs(y - center) < ry * 0.48f;
                var isRim = d > 0.82f;
                var col = isSlit ? cRim * 1.15f : (isRim ? cRim : cFill);
                col.a = alpha;
                tex.SetPixel(x, y, col);
            }
        }
        tex.Apply();
        var sp = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        if (redCoin) redCoinIcon = sp;
        else yellowCoinIcon = sp;
        return sp;
    }

    public static Sprite GetMarioCapIcon()
    {
        var real = TryLoadSm64DsSprite("MarioHead") ?? TryLoadSm64DsSprite("MarioCap");
        if (real != null) return real;
        if (marioCapIcon != null) return marioCapIcon;
        const int size = 64;
        var tex = NewTex("SM64_MarioCapIcon", size, size);
        const float cx = (size - 1) * 0.5f;
        const float cy = (size - 1) * 0.46f;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = x - cx;
                var dy = y - cy;
                var inDome = (dx * dx) / (23f * 23f) + (dy * dy) / (19f * 19f) <= 1.0f && dy > -6f;
                var inBrim = (dx * dx) / (25f * 25f) + ((y - (cy - 5f)) * (y - (cy - 5f))) / (9f * 9f) <= 1.0f && y <= cy - 1f;
                if (!inDome && !inBrim)
                {
                    tex.SetPixel(x, y, Color.clear);
                    continue;
                }

                var badgeDist = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy + 5f));
                if (badgeDist < 9.5f)
                {
                    var isM = Mathf.Abs(dx) < 5.5f && Mathf.Abs(y - (cy + 5f)) < 4.5f &&
                              (Mathf.Abs(dx) > 2.8f || Mathf.Abs(dx) < 1.2f || (y > cy + 5f && Mathf.Abs(dx) < 3f));
                    tex.SetPixel(x, y, isM ? new Color(0.88f, 0.14f, 0.14f, 1f) : Color.white);
                }
                else if (inBrim)
                {
                    tex.SetPixel(x, y, new Color(0.68f, 0.08f, 0.10f, 1f));
                }
                else
                {
                    tex.SetPixel(x, y, new Color(0.92f, 0.16f, 0.18f, 1f));
                }
            }
        }
        tex.Apply();
        marioCapIcon = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return marioCapIcon;
    }

    private static Texture2D NewTex(string name, int w, int h)
    {
        return new Texture2D(w, h, TextureFormat.RGBA32, mipChain: false)
        {
            name = name,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
    }

    private static float RoundedBoxSdf(float px, float py, float halfW, float halfH, float r)
    {
        var qx = Mathf.Abs(px) - halfW + r;
        var qy = Mathf.Abs(py) - halfH + r;
        var outDist = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
        var inDist = Mathf.Min(Mathf.Max(qx, qy), 0f);
        return outDist + inDist - r;
    }
}
