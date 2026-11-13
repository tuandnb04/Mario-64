using UnityEngine;

/// <summary>
/// Classic SM64 Climbable Pole (also supports Handstand at the top tip and outward Pole Jump).
/// </summary>
public class ClimbablePole : MonoBehaviour
{
    public float poleRadius = 0.34f;
    public float grabRadius = 0.95f;
    public float bottomLocalY;
    public float topLocalY = 6.0f;
    public bool autoDetectBounds = true;

    private void Awake()
    {
        if (autoDetectBounds)
        {
            SyncBoundsFromRenderers();
        }
    }

    private void SyncBoundsFromRenderers()
    {
        var rends = GetComponentsInChildren<Renderer>(true);
        if (rends == null || rends.Length == 0) return;

        var minY = float.MaxValue;
        var maxY = float.MinValue;
        var maxR = 0.25f;
        foreach (var r in rends)
        {
            if (r == null) continue;
            var b = r.bounds;
            minY = Mathf.Min(minY, b.min.y);
            maxY = Mathf.Max(maxY, b.max.y);
            maxR = Mathf.Max(maxR, Mathf.Max(b.extents.x, b.extents.z));
        }

        if (maxY > minY + 0.5f)
        {
            bottomLocalY = minY - transform.position.y;
            topLocalY = maxY - transform.position.y - 0.12f;
            poleRadius = Mathf.Clamp(maxR, 0.22f, 0.65f);
            grabRadius = Mathf.Max(0.95f, poleRadius + 0.68f);
        }
    }

    public float WorldBottomY => transform.position.y + bottomLocalY;
    public float WorldTopY => transform.position.y + topLocalY;

    public Vector3 GetSurfacePoint(float worldY, Vector3 outwardDir)
    {
        outwardDir.y = 0f;
        if (outwardDir.sqrMagnitude < 0.001f) outwardDir = Vector3.back;
        outwardDir.Normalize();
        var clampedY = Mathf.Clamp(worldY, WorldBottomY + 0.25f, WorldTopY);
        var center = transform.position;
        return new Vector3(center.x, clampedY, center.z) + outwardDir * (poleRadius + 0.34f);
    }
}
