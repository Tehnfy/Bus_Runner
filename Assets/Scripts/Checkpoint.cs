using UnityEngine;

/// <summary>
/// Trigger volume dressed as a bus stop. Passing it moves the respawn point here.
/// </summary>
[RequireComponent(typeof(Collider))]
public class Checkpoint : MonoBehaviour
{
    [Tooltip("Optional explicit respawn spot. Falls back to this transform, lifted clear of the ground.")]
    [SerializeField] Transform spawnAnchor;

    [Tooltip("Lift applied when falling back to this transform, so the player does not spawn inside the floor.")]
    [SerializeField] float spawnLift = 0.1f;

    [Header("Lamp")]
    [Tooltip("Light switched on once this checkpoint has been reached. Found in the children if left empty.")]
    [SerializeField] Light lamp;
    [Tooltip("Renderer standing in for the bulb. Its emission is driven alongside the light, so the lamp " +
             "reads as lit from any angle rather than only where the light happens to fall.")]
    [SerializeField] Renderer lampRenderer;
    [SerializeField] Color lampColour = new Color(1f, 0.82f, 0.42f);
    [SerializeField] float lampIntensity = 3.5f;
    [Tooltip("Multiplier on the emissive colour. Above 1 so the bar reads as a light source rather than " +
             "a pale panel — but not far above: at 6 every channel clipped and the warm amber came out " +
             "as flat white. 2 keeps the blue channel below 1, which is what preserves the colour.")]
    [SerializeField] float lampEmission = 2f;

    static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    Material lampMaterial;

    /// <summary>True once the player has passed this checkpoint. Drives the lamp; nothing else reads it.</summary>
    public bool Reached { get; private set; }

    public Vector3 SpawnPosition =>
        spawnAnchor != null
            ? spawnAnchor.position
            : new Vector3(transform.position.x, transform.position.y + spawnLift, transform.position.z);

    void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    void Awake()
    {
        if (lamp == null) lamp = GetComponentInChildren<Light>(true);
        // Dark until earned. Set here rather than trusted from the scene, so a checkpoint left lit in
        // the editor for authoring still starts a run switched off.
        ApplyLamp(false);
    }

    /// <summary>
    /// Called by RunManager the first time the player passes. Kept out of OnTriggerEnter so run state
    /// stays the single authority on what counts as reached — the trigger only reports the crossing.
    /// </summary>
    public void MarkReached()
    {
        if (Reached) return;
        Reached = true;
        ApplyLamp(true);
    }

    void ApplyLamp(bool on)
    {
        if (lamp != null)
        {
            lamp.color = lampColour;
            lamp.intensity = lampIntensity;
            lamp.enabled = on;
        }

        if (lampRenderer == null || !Application.isPlaying) return;

        // A per-renderer material instance, so lighting one checkpoint does not light them all — which is
        // what writing to the shared asset would do.
        lampMaterial ??= lampRenderer.material;

        // The keyword is the part that actually matters, and the part that caught me out: URP's Lit
        // shader ignores _EmissionColor entirely unless _EMISSION is enabled. Setting the colour alone
        // left the bar rendering pure black while every value read back correct — enabling it on the
        // material asset did not survive the asset save, so it is done here where nothing can undo it.
        if (on)
        {
            lampMaterial.EnableKeyword("_EMISSION");
            lampMaterial.SetColor(EmissionColorId, lampColour * lampEmission);
        }
        else
        {
            lampMaterial.SetColor(EmissionColorId, Color.black);
            lampMaterial.DisableKeyword("_EMISSION");
        }
    }

    void OnDestroy()
    {
        // Reading .material minted a copy that belongs to nobody else, so it has to be cleaned up.
        if (lampMaterial != null) Destroy(lampMaterial);
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        if (RunManager.Instance != null) RunManager.Instance.ReachCheckpoint(this);
    }
}
