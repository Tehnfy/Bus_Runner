using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// A single coin in a level. Spins, hovers, and pays into <see cref="CoinWallet"/> when the runner
/// touches it.
///
/// Type decides everything else: the balance it pays into, whether it comes back next run, and — via
/// <see cref="CoinSettings"/> — how it looks and moves. A one-time coin checks the wallet on Start
/// and switches itself off if it has already been taken on this save.
/// </summary>
[RequireComponent(typeof(Collider))]
public class Coin : MonoBehaviour
{
    [SerializeField] CoinType type = CoinType.Respawnable;

    [Tooltip("Shared look-and-motion asset. Left empty, the coin falls back to the values below and " +
             "does not glow.")]
    [SerializeField] CoinSettings settings;

    [Tooltip("Stable identity for a one-time coin, so the save knows which one was taken. Assigned " +
             "automatically when the coin is placed, but NOT when one is duplicated — a copy arrives " +
             "with this field already filled, and two coins sharing an id means taking either hides " +
             "both. Run Bus Runner > Coins > Fix Duplicate Coin IDs after duplicating.")]
    [SerializeField] string coinId;

    [Header("Fallbacks")]
    [Tooltip("Used only when no CoinSettings is wired.")]
    [SerializeField] float fallbackSpinSpeed = 90f;
    [SerializeField] float fallbackHoverAmplitude = 0.15f;
    [SerializeField] float fallbackHoverFrequency = 0.5f;

    // Where the level designer put it. Hover is measured from here, so it never drifts.
    Vector3 anchor;
    float spinSpeed;
    float hoverAmplitude;
    float hoverFrequency;
    bool respawnsEachRun;
    int value = 1;

    // Offset into the hover cycle, derived from the anchor so a row of coins ripples instead of
    // bobbing in lockstep. Deterministic, so it looks the same every run.
    float hoverPhase;

    bool taken;

    public CoinType Type => type;
    public string CoinId => coinId;

    void Awake()
    {
        anchor = transform.position;
        hoverPhase = anchor.x * 0.7f + anchor.z * 1.3f;
        ReadStyle();
        ForceTrigger();
    }

    /// <summary>
    /// Coerces every collider on the coin to a trigger, loudly.
    ///
    /// This is not tidiness — a solid coin is a lethal coin. The CharacterController ignores triggers
    /// when it moves, so OnTriggerEnter fires and OnControllerColliderHit never sees the coin. Leave one
    /// solid and the controller collides with it instead: PlayerController's wall test asks only whether
    /// the surface faces back down the lane and whether its top clears stepOffset, and a coin at chest
    /// height answers yes to both. The run ends on a pickup.
    ///
    /// Reset covers a freshly added component and nothing else, so swapping the collider afterwards —
    /// capsule for box, say — brings a solid one back with no warning. Hence a check at Awake.
    /// </summary>
    void ForceTrigger()
    {
        foreach (var collider in GetComponents<Collider>())
        {
            if (collider.isTrigger) continue;

            collider.isTrigger = true;
            Debug.LogWarning($"[Coin] '{name}' had a solid {collider.GetType().Name} — forced to a trigger. " +
                             "A solid coin registers as a frontal wall and kills the run. Tick Is Trigger " +
                             "on the prefab so this does not rely on a runtime fix.", this);
        }
    }

    void Start()
    {
        // A one-time coin already banked on this save never appears again. Done in Start rather than
        // Awake so the wallet's first read happens after any scene-load bookkeeping.
        if (!respawnsEachRun && CoinWallet.IsCollected(type, SceneName, coinId))
            gameObject.SetActive(false);
    }

    void ReadStyle()
    {
        var style = settings != null ? settings.For(type) : null;
        if (style == null)
        {
            spinSpeed = fallbackSpinSpeed;
            hoverAmplitude = fallbackHoverAmplitude;
            hoverFrequency = fallbackHoverFrequency;
            respawnsEachRun = type == CoinType.Respawnable;
            value = 1;
            return;
        }

        spinSpeed = style.spinSpeed;
        hoverAmplitude = style.hoverAmplitude;
        hoverFrequency = style.hoverFrequency;
        respawnsEachRun = style.respawnsEachRun;
        value = Mathf.Max(0, style.value);

        ApplyMaterial();
    }

    /// <summary>
    /// Puts this type's material on the coin, so the type dropdown is the only thing a level designer
    /// touches — pick Special and it turns gold on the spot.
    ///
    /// Assigns sharedMaterial rather than material. The three materials are assets shared by every coin
    /// of that type, which is what keeps them one place to tune; reading .material would mint a private
    /// copy per coin and quietly cut each one off from CoinSettings.
    /// </summary>
    void ApplyMaterial()
    {
        var style = settings != null ? settings.For(type) : null;
        if (style?.material == null) return;

        var renderer = GetComponentInChildren<MeshRenderer>(true);
        if (renderer == null || renderer.sharedMaterial == style.material) return;

        renderer.sharedMaterial = style.material;

#if UNITY_EDITOR
        if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(renderer);
#endif
    }

    void Update()
    {
        // Rotated about world up, not the coin's own axis: the prefab is laid on its side to face the
        // camera, so spinning about transform.up would tumble it end over end.
        if (spinSpeed != 0f) transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);

        if (hoverAmplitude == 0f || hoverFrequency == 0f) return;

        float offset = Mathf.Sin((Time.time * hoverFrequency + hoverPhase) * Mathf.PI * 2f) * hoverAmplitude;
        transform.position = new Vector3(anchor.x, anchor.y + offset, anchor.z);
    }

    void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        if (taken || !other.CompareTag("Player")) return;

        // Latched before anything else. A capsule can report entry twice on the frame it arrives,
        // and without this the coin would pay out twice.
        taken = true;

        CoinWallet.Add(type, value);
        if (!respawnsEachRun) CoinWallet.MarkCollected(type, SceneName, coinId);

        gameObject.SetActive(false);
    }

    static string SceneName => SceneManager.GetActiveScene().name;

#if UNITY_EDITOR
    /// <summary>
    /// Gives a newly placed coin an identity. Only coins living in a real, open scene get one — the
    /// prefab assets must stay blank, or every coin dragged out of one would share the same id and the
    /// first taken would hide the rest.
    ///
    /// The preview-scene test is what makes that true in practice. Editing a prefab asset in code goes
    /// through PrefabUtility.LoadPrefabContents, which opens the prefab in a hidden preview scene — and
    /// there the coin is NOT persistent and its scene IS valid, so the two checks below both pass and an
    /// id gets minted straight into the asset on save. Measured: three variants came out carrying ids,
    /// and blanking them by hand simply produced three new ones.
    /// </summary>
    void OnValidate()
    {
        if (string.IsNullOrEmpty(coinId)
            && !UnityEditor.EditorUtility.IsPersistent(this)
            && !UnityEditor.SceneManagement.EditorSceneManager.IsPreviewSceneObject(this)
            && gameObject.scene.IsValid())
        {
            coinId = System.Guid.NewGuid().ToString("N");
            UnityEditor.EditorUtility.SetDirty(this);
        }

        // Material follows the type, immediately. This was briefly deferred through
        // EditorApplication.delayCall out of caution about touching renderer state inside OnValidate —
        // but delayCall needs an editor tick to fire, and measured, the swap simply never happened.
        // Assigning a material reference is not one of the things OnValidate forbids, so it goes here.
        ApplyMaterial();
    }
#endif
}
