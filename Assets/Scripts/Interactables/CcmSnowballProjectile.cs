using UnityEngine;

public class CcmSnowballProjectile : MonoBehaviour
{
    public Vector3 velocity;
    private float _life;

    private void Update()
    {
        var dt = Time.deltaTime > 0.0001f ? Time.deltaTime : 0.016f;
        _life += dt;
        velocity.y -= 6.5f * dt;
        transform.position += velocity * dt;
        if (_life > 3.2f)
        {
            Destroy(gameObject);
            return;
        }

        var hero = MarioRemakeController.Instance;
        if (hero == null ||
            !(Vector3.Distance(hero.transform.position + Vector3.up * 0.7f, transform.position) < 0.92f)) return;
        hero.TakeDamage(1, transform.position);
        if (VFXPool.Instance != null) VFXPool.Instance.SpawnPickupBurst(transform.position, isStar: false);
        Destroy(gameObject);
    }
}

