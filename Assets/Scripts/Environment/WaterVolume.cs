using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plug-and-play water volume. Drop Water.prefab into any scene and scale it freely;
/// MarioRemakeController automatically detects it and enters swimming mode when submerged.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public class WaterVolume : MonoBehaviour
{
    private static readonly List<WaterVolume> ActiveVolumes = new();

    private BoxCollider _box;

    public static bool HasAny => ActiveVolumes.Count > 0;

    public float SurfaceY => _box != null ? _box.bounds.max.y : transform.position.y;

    private void Awake()
    {
        _box = GetComponent<BoxCollider>();
        if (_box != null) _box.isTrigger = true;
    }

    private void OnEnable()
    {
        if (_box == null) _box = GetComponent<BoxCollider>();
        if (_box != null) _box.isTrigger = true;
        if (!ActiveVolumes.Contains(this)) ActiveVolumes.Add(this);
    }

    private void OnDisable()
    {
        ActiveVolumes.Remove(this);
    }

    public bool ContainsXZ(Vector3 worldPos)
    {
        if (_box == null) return false;
        var b = _box.bounds;
        return worldPos.x >= b.min.x && worldPos.x <= b.max.x &&
               worldPos.z >= b.min.z && worldPos.z <= b.max.z &&
               worldPos.y >= b.min.y - 0.5f;
    }

    public static bool TryGetWaterAt(Vector3 worldPos, out float surfaceY)
    {
        for (var i = 0; i < ActiveVolumes.Count; i++)
        {
            var vol = ActiveVolumes[i];
            if (vol == null || !vol.isActiveAndEnabled) continue;
            if (!vol.ContainsXZ(worldPos)) continue;
            var sy = vol.SurfaceY;
            if (worldPos.y < sy - 0.15f)
            {
                surfaceY = sy;
                return true;
            }
        }

        surfaceY = 0f;
        return false;
    }

    private void OnDrawGizmosSelected()
    {
        var box = _box != null ? _box : GetComponent<BoxCollider>();
        if (box == null) return;
        Gizmos.color = new Color(0.15f, 0.65f, 1f, 0.35f);
        Gizmos.DrawCube(box.bounds.center, box.bounds.size);
        Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.9f);
        Gizmos.DrawWireCube(box.bounds.center, box.bounds.size);
    }
}

