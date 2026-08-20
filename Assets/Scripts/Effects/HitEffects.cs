using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns impact effects. The one place that knows how an effect gets into the world, so every
/// caller — crashes, canopy bounces, coins, checkpoints — behaves identically.
///
/// Everything is spawned unparented, at a world position. That is deliberate: the crash path
/// disables the PlayerController and hands the body to the ragdoll, so an effect parented to the
/// player would be disabled or dragged around mid-burst. Parenting to the thing that was hit is no
/// better, since a coin deactivates itself the moment it is taken.
/// </summary>
public static class HitEffects
{
    // One material per colour, not one per shard and not one for all of them.
    //
    // Per shard would mint a copy for every cube and leak a material per burst. A single shared one
    // was worse in a way that only showed up under test: re-tinting it per burst recoloured every
    // shard already in the air, so a crash into a gate turned the building dust that was still
    // falling blue. Keyed by colour it stays bounded — one entry per distinct obstacle colour.
    static readonly Dictionary<Color, Material> placeholderMaterials = new Dictionary<Color, Material>();

    /// <summary>
    /// Instantiates an authored effect prefab at a contact, aimed out along the surface normal, and
    /// destroys it after <paramref name="lifetime"/>.
    /// </summary>
    public static void Spawn(GameObject prefab, Vector3 point, Vector3 normal, float lifetime)
    {
        if (prefab == null) return;

        var rotation = normal.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(normal)
            : Quaternion.identity;

        var instance = Object.Instantiate(prefab, point, rotation);
        if (lifetime > 0f) Object.Destroy(instance, lifetime);
    }

    /// <summary>
    /// The stand-in for artwork: a handful of small cubes thrown out of the contact along the
    /// surface normal, shrinking as they go.
    ///
    /// Cubes because a primitive arrives with a working renderer and needs no imported asset. It is
    /// meant to look provisional — the point is that the hook fires and fires in the right place,
    /// visible long before anyone has authored a particle.
    /// </summary>
    public static void PlaceholderBurst(
        Vector3 point, Vector3 normal, Color color, int count, float speed, float size, float lifetime)
    {
        if (count <= 0) return;

        var outward = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up;
        var material = PlaceholderMaterial(color);

        for (int i = 0; i < count; i++)
        {
            var shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shard.name = "HitBurstShard";

            // The collider has to go. A primitive ships with one, and a shower of colliders erupting
            // out of the player's own contact point would shove the CharacterController around and
            // could register as fresh impacts of its own.
            var collider = shard.GetComponent<Collider>();
            if (collider != null) Object.Destroy(collider);

            shard.transform.position = point;
            shard.transform.localScale = Vector3.one * size;
            shard.GetComponent<MeshRenderer>().sharedMaterial = material;

            // Spread around the normal rather than straight along it, so a burst reads as a burst
            // instead of a single line of cubes leaving the wall.
            var direction = (outward + Random.insideUnitSphere * 0.8f).normalized;
            shard.AddComponent<BurstDecay>()
                 .Launch(direction * speed, gravityPull: speed * 0.6f, life: lifetime);
        }
    }

    /// <summary>
    /// The material for a given colour, tinted and made to glow. Cached per colour.
    ///
    /// URP's Lit shader ignores _EmissionColor unless the _EMISSION keyword is enabled — the same
    /// trap Checkpoint.ApplyLamp documents, and the reason the coin materials needed a keyword pass.
    /// Setting the colour alone produces shards that are simply flat.
    /// </summary>
    static Material PlaceholderMaterial(Color color)
    {
        // A destroyed material can still be a live dictionary key across a play-mode exit, so the
        // null check is on the value, not just on the lookup succeeding.
        if (placeholderMaterials.TryGetValue(color, out var cached) && cached != null) return cached;

        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var material = new Material(shader) { name = $"HitBurstPlaceholder ({ColorUtility.ToHtmlStringRGB(color)})" };

        material.color = color;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", color * 2f);

        placeholderMaterials[color] = material;
        return material;
    }
}
