using UnityEngine;

/// <summary>
/// Drives one shard of a placeholder burst: flings it, shrinks it, then destroys it.
///
/// A component on the shard rather than a coroutine on the spawner, because the spawner is static
/// and a static class cannot run coroutines. The alternative was a self-installing runner object
/// like CoinWalletFlusher, which is a lot of machinery for something that only has to move a cube
/// for a fraction of a second.
///
/// Unscaled time: a crash may coincide with a slow-motion or paused clock, and feedback for an
/// impact the player just had should not stretch with it.
/// </summary>
public class BurstDecay : MonoBehaviour
{
    Vector3 velocity;
    float gravity;
    float lifetime;
    float born;
    Vector3 startScale;

    /// <summary>Called by HitEffects immediately after the shard is made.</summary>
    public void Launch(Vector3 initialVelocity, float gravityPull, float life)
    {
        velocity = initialVelocity;
        gravity = gravityPull;
        lifetime = Mathf.Max(0.01f, life);
        born = Time.unscaledTime;
        startScale = transform.localScale;
    }

    void Update()
    {
        float age = Time.unscaledTime - born;
        if (age >= lifetime)
        {
            Destroy(gameObject);
            return;
        }

        float dt = Time.unscaledDeltaTime;
        velocity += Vector3.down * (gravity * dt);
        transform.position += velocity * dt;

        // Shrinking to nothing is what ends the shard visually. Fading would mean a transparent
        // material and a render-queue change per shard, which is far more setup than a placeholder
        // is worth.
        transform.localScale = startScale * (1f - age / lifetime);
    }
}
