using System;
using UnityEngine;

/// <summary>The three pickup currencies. Each keeps its own balance.</summary>
public enum CoinType
{
    /// <summary>Purple. Collected once per level, ever — never comes back.</summary>
    Permanent,
    /// <summary>Silver. Back in place every time the level is loaded.</summary>
    Respawnable,
    /// <summary>The gate for specific content — new maps and the like. One-time, like Permanent.</summary>
    Special,
}

/// <summary>
/// The player's coin balances and which one-time coins they have already taken. Global and
/// persistent: balances survive scene loads and app restarts, so coins earned in one level spend
/// in any other.
///
/// This is the seam the unlock system plugs into. Unlocks should read <see cref="Balance"/> and
/// call <see cref="TrySpend"/>, and nothing here should ever learn what an unlock is.
///
/// Static rather than a singleton component, matching InputBindings: there is no scene object to
/// lose, and no ordering question about when it exists.
/// </summary>
public static class CoinWallet
{
    /// <summary>Raised whenever a balance changes, with the type and its new value.</summary>
    public static event Action<CoinType, int> BalanceChanged;

    const string BalanceKeyPrefix = "coins.balance.";
    const string CollectedKeyPrefix = "coins.taken.";

    static readonly CoinType[] AllTypes = (CoinType[])Enum.GetValues(typeof(CoinType));

    static int[] balances;

    // What the player has picked up since the current run started. Not persisted and not spendable —
    // it exists so the in-level HUD can answer "how am I doing this run" rather than showing a
    // lifetime total that barely moves.
    static int[] session;

    static bool dirty;

    /// <summary>Every type, for callers that want to enumerate without depending on the enum's shape.</summary>
    public static CoinType[] Types => AllTypes;

    /// <summary>Everything the player has ever banked of this type, across every level and session.</summary>
    public static int Balance(CoinType type)
    {
        EnsureLoaded();
        return balances[(int)type];
    }

    /// <summary>Picked up since the last <see cref="BeginSession"/> — this run only.</summary>
    public static int SessionTotal(CoinType type)
    {
        EnsureLoaded();
        return session[(int)type];
    }

    /// <summary>
    /// Zeroes the run tally. Called by RunManager as a level comes up, so the HUD counts this attempt
    /// rather than the last one. Deliberately not called on a checkpoint respawn: dying does not undo
    /// the coins already gathered, so the tally carries on across it.
    /// </summary>
    public static void BeginSession()
    {
        EnsureLoaded();
        for (int i = 0; i < session.Length; i++) session[i] = 0;

        // Announced, so a readout already on screen drops back to zero rather than showing the
        // previous run's tally until the next pickup.
        foreach (var type in AllTypes) BalanceChanged?.Invoke(type, balances[(int)type]);
    }

    public static void Add(CoinType type, int amount)
    {
        if (amount == 0) return;
        EnsureLoaded();

        int index = (int)type;
        // Clamped at zero so a negative Add can never leave a balance the player could not have
        // reached by spending.
        balances[index] = Mathf.Max(0, balances[index] + amount);
        PlayerPrefs.SetInt(BalanceKeyPrefix + type, balances[index]);
        dirty = true;

        // Gains only. Spending in the shop is not something the run tally should count backwards.
        if (amount > 0) session[index] += amount;

        BalanceChanged?.Invoke(type, balances[index]);
    }

    public static bool CanAfford(CoinType type, int amount) => Balance(type) >= amount;

    /// <summary>Spends if the balance covers it. Returns false and changes nothing if it does not.</summary>
    public static bool TrySpend(CoinType type, int amount)
    {
        if (amount <= 0) return true;
        if (!CanAfford(type, amount)) return false;

        Add(type, -amount);
        return true;
    }

    /// <summary>
    /// Whether a one-time coin has already been taken. Keyed on the scene as well as the coin, so
    /// two levels may reuse an id without colliding.
    /// </summary>
    public static bool IsCollected(string scene, string coinId) =>
        !string.IsNullOrEmpty(coinId) && PlayerPrefs.GetInt(CollectedKey(scene, coinId), 0) == 1;

    public static void MarkCollected(string scene, string coinId)
    {
        if (string.IsNullOrEmpty(coinId)) return;
        PlayerPrefs.SetInt(CollectedKey(scene, coinId), 1);
        dirty = true;
    }

    /// <summary>
    /// Writes to disk if anything changed. Cheap to call and safe to call often — collecting a coin
    /// only marks the wallet dirty, because flushing on every pickup would be a whole-file write per
    /// coin. CoinWalletFlusher calls this when the app is backgrounded or quits.
    /// </summary>
    public static void Flush()
    {
        if (!dirty) return;
        dirty = false;
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Wipes balances and every collected mark, for testing. Levels are not enumerable from here, so
    /// this uses PlayerPrefs.DeleteKey per balance and asks the caller to clear collected marks by
    /// scene — or DeleteAll if they are willing to lose key bindings too.
    /// </summary>
    public static void ResetBalances()
    {
        EnsureLoaded();
        foreach (var type in AllTypes)
        {
            balances[(int)type] = 0;
            PlayerPrefs.DeleteKey(BalanceKeyPrefix + type);
            BalanceChanged?.Invoke(type, 0);
        }
        dirty = true;
        Flush();
    }

    static string CollectedKey(string scene, string coinId) => CollectedKeyPrefix + scene + "." + coinId;

    static void EnsureLoaded()
    {
        if (balances != null) return;

        balances = new int[AllTypes.Length];
        session = new int[AllTypes.Length];
        foreach (var type in AllTypes)
            balances[(int)type] = PlayerPrefs.GetInt(BalanceKeyPrefix + type, 0);
    }
}
