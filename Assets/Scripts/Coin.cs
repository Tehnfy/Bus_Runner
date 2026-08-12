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
             "automatically when the coin is placed; run Bus Runner > Repair Coin IDs if two coins " +
             "ever end up sharing one, which duplicating a placed coin will do.")]
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
    }

    void Start()
    {
        // A one-time coin already banked on this save never appears again. Done in Start rather than
        // Awake so the wallet's first read happens after any scene-load bookkeeping.
        if (!respawnsEachRun && CoinWallet.IsCollected(SceneName, coinId))
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
        if (!respawnsEachRun) CoinWallet.MarkCollected(SceneName, coinId);

        gameObject.SetActive(false);
    }

    static string SceneName => SceneManager.GetActiveScene().name;

#if UNITY_EDITOR
    /// <summary>
    /// Gives a newly placed coin an identity. Only scene instances get one — the prefab asset must
    /// stay blank, or every coin dragged out of it would share the same id and the first one taken
    /// would hide the rest.
    /// </summary>
    void OnValidate()
    {
        if (string.IsNullOrEmpty(coinId)
            && !UnityEditor.EditorUtility.IsPersistent(this)
            && gameObject.scene.IsValid())
        {
            coinId = System.Guid.NewGuid().ToString("N");
            UnityEditor.EditorUtility.SetDirty(this);
        }
    }
#endif
}
