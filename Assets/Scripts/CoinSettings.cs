using System;
using UnityEngine;

/// <summary>
/// One asset holding how all three coin types look and behave, so the design is dialled in from a
/// single inspector rather than hunted across three prefabs and three materials.
///
/// Editing a style writes the colour and glow straight into that type's material, so the scene view
/// updates as the slider moves. The motion and respawn values are read at runtime by <see cref="Coin"/>.
/// </summary>
[CreateAssetMenu(fileName = "CoinSettings", menuName = "Bus Runner/Coin Settings")]
public class CoinSettings : ScriptableObject
{
    [Serializable]
    public class Style
    {
        public CoinType type;

        [Header("Look")]
        [Tooltip("Material this type's prefab uses. Written to by the colour and glow values below.")]
        public Material material;
        [Tooltip("Body colour of the coin.")]
        public Color baseColor = Color.white;
        [Tooltip("Colour of the glow. Multiplied by the intensity below before it reaches the material.")]
        public Color emissionColor = Color.white;
        [Range(0f, 20f)]
        [Tooltip("How hard the coin glows. Values above 1 push the material into HDR, which is what " +
                 "the Bloom override in BusRunnerVolumeProfile picks up as a halo — below 1 there is " +
                 "colour but no bloom.")]
        public float emissionIntensity = 3f;

        [Header("Motion")]
        [Tooltip("Degrees per second about the coin's own up axis. Negative spins the other way.")]
        public float spinSpeed = 90f;
        [Tooltip("How far the coin rises and falls from where it was placed, in metres. 0 holds it still.")]
        public float hoverAmplitude = 0.15f;
        [Tooltip("Full up-and-down cycles per second.")]
        public float hoverFrequency = 0.5f;

        [Header("Rules")]
        [Tooltip("On: back every time the level loads. Off: once taken, gone for good on this save.")]
        public bool respawnsEachRun;
        [Tooltip("Added to this type's balance per coin.")]
        public int value = 1;

        [Tooltip("Spawned where the coin was when it is collected. Per type rather than per coin, " +
                 "so it sits beside the colours it should match and all coins of a type agree. " +
                 "Empty means no effect.")]
        public GameObject pickupEffect;

        [Tooltip("Seconds before the pickup effect is destroyed.")]
        public float pickupEffectLifetime = 1f;
    }

    [SerializeField]
    Style[] styles =
    {
        new Style
        {
            type = CoinType.Permanent,
            baseColor = new Color(0.62f, 0.31f, 0.86f),
            emissionColor = new Color(0.55f, 0.24f, 0.95f),
            emissionIntensity = 3f,
            respawnsEachRun = false,
        },
        new Style
        {
            type = CoinType.Respawnable,
            baseColor = new Color(0.78f, 0.80f, 0.85f),
            emissionColor = new Color(0.72f, 0.78f, 0.88f),
            emissionIntensity = 2f,
            respawnsEachRun = true,
        },
        new Style
        {
            type = CoinType.Special,
            baseColor = new Color(1f, 0.76f, 0.24f),
            emissionColor = new Color(1f, 0.68f, 0.15f),
            emissionIntensity = 5f,
            respawnsEachRun = false,
            value = 1,
        },
    };

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    /// <summary>The style for a type, or null if the asset has no entry for it.</summary>
    public Style For(CoinType type)
    {
        if (styles == null) return null;
        foreach (var style in styles)
            if (style != null && style.type == type) return style;
        return null;
    }

    /// <summary>
    /// Pushes every style's colour and glow into its material.
    ///
    /// URP's Lit shader ignores _EmissionColor entirely unless the _EMISSION keyword is on, which is
    /// the trap the checkpoint lamp already hit — so the keyword is set here rather than trusted from
    /// however the material was authored.
    /// </summary>
    /// <param name="save">
    /// Write each touched material to disk. Needed because the _EMISSION keyword does not reliably
    /// survive on its own: measured, after a setup pass that only marked the materials dirty and called
    /// AssetDatabase.SaveAssets, all three read back with the keyword off and no glow. SetDirty followed
    /// by SaveAssetIfDirty per material survives even a forced reimport.
    ///
    /// Off by default, because the live-preview path runs inside OnValidate and writing assets during
    /// validation is not something to do casually — an unsaved dirty material still previews correctly
    /// and gets written with the next project save.
    /// </param>
    public void ApplyToMaterials(bool save = false)
    {
        if (styles == null) return;

        foreach (var style in styles)
        {
            if (style?.material == null) continue;

            style.material.SetColor(BaseColorId, style.baseColor);

            if (style.emissionIntensity > 0f)
            {
                style.material.EnableKeyword("_EMISSION");
                style.material.globalIlluminationFlags &= ~MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                style.material.SetColor(EmissionColorId, style.emissionColor * style.emissionIntensity);
            }
            else
            {
                style.material.SetColor(EmissionColorId, Color.black);
                style.material.DisableKeyword("_EMISSION");
            }

#if UNITY_EDITOR
            // Writing properties on a material asset does not reliably mark it for saving, so a
            // dialled-in glow would be lost on the next editor restart without this.
            UnityEditor.EditorUtility.SetDirty(style.material);
            if (save) UnityEditor.AssetDatabase.SaveAssetIfDirty(style.material);
#endif
        }
    }

#if UNITY_EDITOR
    // Live preview: dragging the glow slider repaints the coins in the scene view immediately,
    // which is the whole point of keeping the three styles in one asset.
    void OnValidate()
    {
        ApplyToMaterials();
    }
#endif
}
