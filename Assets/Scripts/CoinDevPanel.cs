using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Developer controls for the coin save: put the one-time coins back in the world and zero what they
/// paid, without hunting through PlayerPrefs or reinstalling.
///
/// Permanent and Special coins are collected once per save, ever. That is correct for a player and
/// hostile to testing — walk a level twice and the second pass has nothing purple or gold left in it.
/// These buttons are how that gets undone.
///
/// Switched off in one place. Untick <see cref="devControlsEnabled"/> and the panel and every button
/// that opens it disappear at startup, so shipping does not depend on remembering to delete anything.
/// A release build hides them regardless unless <see cref="allowInReleaseBuilds"/> is explicitly on —
/// an unticked box in a scene is easy to lose track of, a development-only build is not.
/// </summary>
public class CoinDevPanel : MonoBehaviour
{
    [Header("Availability")]
    [Tooltip("The switch. Off: this panel and its launch buttons are deactivated at startup and " +
             "nothing here can be reached.")]
    [SerializeField] bool devControlsEnabled = true;

    [Tooltip("Leave off to ship. On, the controls also appear in a release build — only wanted if a " +
             "tester needs them on a device.")]
    [SerializeField] bool allowInReleaseBuilds;

    [Tooltip("Buttons elsewhere in the UI that open this panel. Hidden along with it, otherwise the " +
             "menu keeps a button that leads nowhere.")]
    [SerializeField] GameObject[] launchers;

    [Header("Readout")]
    [Tooltip("Reports the save state and confirms what each button did.")]
    [SerializeField] TMP_Text status;

    [Tooltip("Shown instead of the state line while a wipe is armed.")]
    [SerializeField] float confirmWindow = 4f;

    // Which wipe is armed, and until when. Every button here throws away progress the tester may have
    // spent a run collecting, so the first press asks and the second acts.
    enum Pending { None, Permanent, Special, Unlocks, Everything }

    Pending armed = Pending.None;
    float armedUntil;

    /// <summary>Whether the controls may be used at all in this build.</summary>
    public bool Available =>
        devControlsEnabled && (allowInReleaseBuilds || Application.isEditor || Debug.isDebugBuild);

    /// <summary>
    /// Shows or hides the launch buttons to match <see cref="Available"/>, and reports what it decided.
    ///
    /// Called by MenuController rather than run from Awake, because this panel is switched off in the
    /// scene — every panel is, until one is shown — and Awake does not run on a component of an
    /// inactive object. Gating itself here would have left the button on screen leading to a panel
    /// that never checked.
    /// </summary>
    public bool ApplyAvailability()
    {
        bool available = Available;
        foreach (var launcher in launchers)
            if (launcher != null) launcher.SetActive(available);

        if (!available) gameObject.SetActive(false);
        return available;
    }

    void OnEnable()
    {
        // Disarmed on the way in: an armed wipe left over from the last visit must not be completed by
        // whatever the next press happens to be.
        armed = Pending.None;
        CoinWallet.BalanceChanged += HandleBalanceChanged;
        ShowState();
    }

    void OnDisable()
    {
        CoinWallet.BalanceChanged -= HandleBalanceChanged;
    }

    void Update()
    {
        // Lets the arming lapse, so a panel left open does not sit primed indefinitely.
        if (armed == Pending.None || Time.unscaledTime < armedUntil) return;

        armed = Pending.None;
        ShowState();
    }

    void HandleBalanceChanged(CoinType type, int balance)
    {
        if (armed == Pending.None) ShowState();
    }

    // Wired to the buttons by MenuSetup.
    public void ResetPermanent() => Request(Pending.Permanent);
    public void ResetSpecial() => Request(Pending.Special);

    /// <summary>Everything bought — powers, level unlocks and consumable stocks. Coins are left alone.</summary>
    public void ResetUnlocks() => Request(Pending.Unlocks);

    public void ResetEverything() => Request(Pending.Everything);

    void Request(Pending action)
    {
        if (armed != action)
        {
            armed = action;
            armedUntil = Time.unscaledTime + confirmWindow;
            Report($"PRESS {Name(action)} AGAIN TO WIPE");
            return;
        }

        armed = Pending.None;

        int cleared;
        switch (action)
        {
            case Pending.Permanent:
                cleared = CoinWallet.ResetType(CoinType.Permanent);
                break;
            case Pending.Special:
                cleared = CoinWallet.ResetType(CoinType.Special);
                break;
            case Pending.Unlocks:
                // Coins deliberately untouched. Wiping what was bought without refunding what it cost
                // is the useful shape for testing a shop: the tester keeps the balance they earned and
                // gets to make the purchase again.
                cleared = PlayerInventory.ResetAll();
                break;
            default:
                // Everything means everything. Clearing the coins but leaving the purchases behind
                // would read as a wipe that quietly kept half the save.
                cleared = CoinWallet.ResetAll() + PlayerInventory.ResetAll();
                break;
        }

        Debug.Log($"[CoinDevPanel] Reset {Name(action)} — {cleared} record(s) cleared. " +
                  "Reload the level to see any coins back.");
        Report($"{Name(action)} RESET — {cleared} RECORD(S) CLEARED. RELOAD THE LEVEL.");
    }

    static string Name(Pending action) => action switch
    {
        Pending.Permanent => "PERMANENT",
        Pending.Special => "SPECIAL",
        Pending.Unlocks => "UNLOCKS",
        Pending.Everything => "ALL PROGRESS",
        _ => "NOTHING",
    };

    /// <summary>
    /// Balance and taken-count per type, then what has been bought. Both counts come from the index
    /// each system keeps, so they report what a reset would actually be able to clear rather than
    /// what happens to be in the level or the catalogue.
    /// </summary>
    void ShowState()
    {
        if (status == null) return;

        var text = new System.Text.StringBuilder();
        foreach (var type in CoinWallet.Types)
        {
            text.Append(type).Append("  held ").Append(CoinWallet.Balance(type));
            if (type != CoinType.Respawnable)
                text.Append("   taken ").Append(CoinWallet.CollectedCount(type));
            text.Append('\n');
        }

        text.Append("owned ").Append(PlayerInventory.OwnedIds().Length)
            .Append("   stocked ").Append(PlayerInventory.StockedIds().Length);

        status.text = text.ToString().TrimEnd();
    }

    void Report(string message)
    {
        if (status != null) status.text = message;
    }
}
