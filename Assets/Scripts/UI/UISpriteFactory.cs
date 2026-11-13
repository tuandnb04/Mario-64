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
    private static Sprite courseMedallion;
    private static Sprite starIcon;
    private static Sprite yellowCoinIcon;
    private static Sprite redCoinIcon;
    private static Sprite marioCapIcon;
    private static Sprite heartIcon;
    private static readonly Dictionary<int, Sprite> PieWedges = new();
    private static readonly Dictionary<int, Sprite> OdysseyRingSegments = new();

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
        const float baseOuterR = size * 0.435f;
        const float midR = size * 0.345f;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = x - center;
                var dy = y - center;
                var r = Mathf.Sqrt(dx * dx + dy * dy);
                var angle = Mathf.Atan2(dy, dx);
                // Scalloped cloud/cog silhouette like SM64DS golden Power Meter frame
                var lobe = Mathf.Abs(Mathf.Sin(angle * 4f)) * (size * 0.042f);
                var outerR = baseOuterR + lobe;

                if (r > outerR + 1.2f)
                {
                    tex.SetPixel(x, y, Color.clear);
                    continue;
                }

                var edgeAlpha = Mathf.Clamp01((outerR + 0.9f) - r);
                if (r >= outerR - 4.2f)
                {
                    // Thick cartoon black outline
                    tex.SetPixel(x, y, new Color(0.06f, 0.06f, 0.09f, edgeAlpha));
                }
                else if (r >= midR + 3.0f)
                {
                    // Warm golden-tan SM64DS bezel with top highlight
                    var shade = Mathf.Clamp01(dy / outerR) * 0.16f;
                    var gold = new Color(0.90f + shade * 0.5f, 0.70f + shade * 0.5f, 0.30f + shade * 0.3f, 1f);
                    tex.SetPixel(x, y, gold);
                }
                else if (r >= midR)
                {
                    // Inner dark border separating bezel from blue wedges
                    tex.SetPixel(x, y, new Color(0.07f, 0.08f, 0.12f, 1f));
                }
                else
                {
                    // Deep navy backing behind the 8 wedges
                    tex.SetPixel(x, y, new Color(0.07f, 0.13f, 0.28f, 0.95f));
                }
            }
        }
        tex.Apply();
        ringFrame = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return ringFrame;
    }

    public static Sprite GetCourseMedallionIcon()
    {
        if (courseMedallion != null) return courseMedallion;
        const int size = 160;
        var tex = NewTex("SM64_CourseMedallion", size, size);
        const float center = (size - 1) * 0.5f;
        const float outerR = size * 0.48f;
        const float innerR = size * 0.25f;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = x - center;
                var dy = y - center;
                var r = Mathf.Sqrt(dx * dx + dy * dy);
                if (r > outerR + 1.2f)
                {
                    tex.SetPixel(x, y, Color.clear);
                    continue;
                }

                var edgeAlpha = Mathf.Clamp01((outerR + 0.9f) - r);
                if (r >= outerR - 3.5f)
                {
                    tex.SetPixel(x, y, new Color(0.28f, 0.20f, 0.06f, edgeAlpha));
                }
                else if (r >= innerR)
                {
                    var sheen = Mathf.Clamp01((dy + dx * 0.4f) / outerR) * 0.18f;
                    tex.SetPixel(x, y, new Color(0.78f + sheen, 0.62f + sheen * 0.8f, 0.20f + sheen * 0.4f, 1f));
                }
                else
                {
                    tex.SetPixel(x, y, new Color(0.28f, 0.21f, 0.08f, 1f));
                }
            }
        }
        tex.Apply();
        courseMedallion = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return courseMedallion;
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

    public static Sprite GetOdysseyRingSegment(int segmentIndex)
    {
        segmentIndex = ((segmentIndex % 8) + 8) % 8;
        if (OdysseyRingSegments.TryGetValue(segmentIndex, out var existing) && existing != null) return existing;

        const int size = 128;
        var tex = NewTex($"Odyssey_RingSeg_{segmentIndex}", size, size);
        const float center = (size - 1) * 0.5f;
        const float rMin = size * 0.34f;
        const float rMax = size * 0.455f;

        var startDeg = segmentIndex * 45f + 4.2f;
        var endDeg = (segmentIndex + 1) * 45f - 4.2f;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = x - center;
                var dy = y - center;
                var r = Mathf.Sqrt(dx * dx + dy * dy);
                if (r is < rMin - 1.5f or > rMax + 1.5f)
                {
                    tex.SetPixel(x, y, Color.clear);
                    continue;
                }

                var deg = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg;
                if (deg < 0f) deg += 360f;

                var angMargin = Mathf.Min(deg - startDeg, endDeg - deg);
                if (angMargin < -1.5f)
                {
                    tex.SetPixel(x, y, Color.clear);
                    continue;
                }

                var radialAlpha = Mathf.Clamp01(Mathf.Min(r - rMin + 0.9f, rMax - r + 0.9f));
                var angleAlpha = Mathf.Clamp01(angMargin * 1.2f + 0.9f);
                var alpha = Mathf.Min(radialAlpha, angleAlpha);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        tex.Apply();
        var sp = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        OdysseyRingSegments[segmentIndex] = sp;
        return sp;
    }

    public static Sprite GetHeartIcon()
    {
        if (heartIcon != null) return heartIcon;
        const int size = 128;
        var tex = NewTex("Odyssey_HeartIcon", size, size);
        const float center = (size - 1) * 0.5f;
        const float scale = size * 0.34f;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var nx = Mathf.Abs((x - center) / scale);
                var ny = ((y - center) + size * 0.045f) / scale;
                var val = nx * nx + ny * ny - 1f;
                var f = val * val * val - nx * nx * ny * ny * ny;

                if (f <= 0f)
                {
                    tex.SetPixel(x, y, Color.white);
                }
                else if (f < 0.22f)
                {
                    var outlineAlpha = 1f - Mathf.Clamp01((f - 0.10f) / 0.12f);
                    var col = Color.Lerp(Color.white, new Color(0.08f, 0.09f, 0.12f, 1f), Mathf.Clamp01(f / 0.08f));
                    col.a = outlineAlpha;
                    tex.SetPixel(x, y, col);
                }
                else
                {
                    tex.SetPixel(x, y, Color.clear);
                }
            }
        }

        tex.Apply();
        heartIcon = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return heartIcon;
    }

    public static Sprite GetKeyIcon(bool gold = true)
    {
        return GetStarIcon();
    }

    public static Sprite GetStarIcon()
    {
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
