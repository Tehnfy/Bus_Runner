using UnityEngine;

/// <summary>
/// Put this on anything that should throw a visual effect when the player hits it — a building, a
/// gate, a lamp post, a canopy.
///
/// The effect belongs to the object, not to the player. PlayerController asks the thing it just hit
/// what should happen, exactly as it already asks CanopyBooster whether a contact should launch the
/// runner. Adding a reacting obstacle is then dropping this component on its prefab, with nothing
/// central to edit.
///
/// Leave effectPrefab empty and it draws the placeholder burst instead, so a prefab reacts visibly
/// before any art exists for it.
/// </summary>
public class HitEffect : MonoBehaviour
{
    [Tooltip("Spawned at the point of contact, rotated to face out along the surface normal. Leave " +
             "empty to use the placeholder burst below.")]
    [SerializeField] GameObject effectPrefab;

    [Tooltip("Seconds before the spawned effect is destroyed. Keep it short — a long life leaves " +
             "debris hanging at the old impact site well after the player has respawned.")]
    [SerializeField] float lifetime = 1.2f;

    [Tooltip("Contacts inside this window are ignored. OnControllerColliderHit reports the same " +
             "surface several times across a single touchdown, so without this one impact spawns a " +
             "stream of effects. Same reason CanopyBooster has retriggerDelay.")]
    [SerializeField] float retriggerDelay = 0.25f;

    [Header("Placeholder burst")]
    [Tooltip("Only used when no effect prefab is wired.")]
    [SerializeField] Color placeholderColor = new Color(1f, 0.72f, 0.25f, 1f);
    [Range(1, 40)]
    [SerializeField] int placeholderCount = 10;
    [SerializeField] float placeholderSpeed = 6f;
    [SerializeField] float placeholderSize = 0.12f;

    float nextPlayAt;

    /// <summary>
    /// Plays the effect and claims the trigger, so one impact produces one burst however many
    /// contacts the controller reports for it. Returns false when the window has not elapsed.
    ///
    /// Same claim-on-success contract as CanopyBooster.TryConsumeBounce, so the two read alike at
    /// the call site.
    /// </summary>
    public bool TryPlay(Vector3 point, Vector3 normal)
    {
        if (Time.time < nextPlayAt) return false;
        nextPlayAt = Time.time + Mathf.Max(0f, retriggerDelay);

        Play(point, normal);
        return true;
    }

    /// <summary>
    /// Plays regardless of the retrigger window. For the one-shot callers — a coin taken, a
    /// checkpoint passed — which already guarantee they fire once and would only be held back by it.
    /// </summary>
    public void Play(Vector3 point, Vector3 normal)
    {
        if (effectPrefab != null)
        {
            HitEffects.Spawn(effectPrefab, point, normal, lifetime);
            return;
        }

        HitEffects.PlaceholderBurst(point, normal, placeholderColor, placeholderCount,
                                    placeholderSpeed, placeholderSize, lifetime);
    }
}
