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

    // Which one-time coins have been taken, per type, as a list of "scene.id" entries. PlayerPrefs
    // cannot be enumerated, so without this index a collected mark is write-only: findable if you
    // already know the coin, unreachable otherwise — and the dev reset has to reach every one of them
    // without knowing which levels exist.
    const string CollectedIndexPrefix = "coins.takenIndex.";
    const char IndexSeparator = ';';

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

    public static void Add(CoinType type, int amount) => Add(type, amount, countsAsPickup: true);

    /// <summary>
    /// Puts coins back without counting them as picked up this run.
    ///
    /// For unwinding a payment that could not be completed — a multi-currency price where the second
    /// coin failed after the first was taken. Routed through here rather than through Add because a
    /// refund is not a pickup: adding it normally would inflate the run tally on the HUD, so a failed
    /// purchase would read as having earned the player coins.
    /// </summary>
    public static void Refund(CoinType type, int amount)
    {
        if (amount <= 0) return;
        Add(type, amount, countsAsPickup: false);
    }

    static void Add(CoinType type, int amount, bool countsAsPickup)
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
        if (amount > 0 && countsAsPickup) session[index] += amount;

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
    /// Whether a one-time coin has already been taken. Keyed on the type and the scene as well as the
    /// coin: two levels may reuse an id without colliding, and the type in the key is what lets the
    /// dev reset clear the Permanent marks without touching the Special ones.
    /// </summary>
    public static bool IsCollected(CoinType type, string scene, string coinId) =>
        !string.IsNullOrEmpty(coinId) && PlayerPrefs.GetInt(CollectedKey(type, scene, coinId), 0) == 1;

    public static void MarkCollected(CoinType type, string scene, string coinId)
    {
        if (string.IsNullOrEmpty(coinId)) return;

        PlayerPrefs.SetInt(CollectedKey(type, scene, coinId), 1);
        AddToIndex(type, Entry(scene, coinId));
        dirty = true;
    }

    /// <summary>How many one-time coins of this type the save says have been taken.</summary>
    public static int CollectedCount(CoinType type) => ReadIndex(type).Length;

    /// <summary>
    /// Forgets a single collected mark. Deletes the pre-type key shape as well, so a coin taken
    /// before the type was part of the key does not stay invisible forever.
    /// </summary>
    public static void ForgetCollected(CoinType type, string scene, string coinId)
    {
        if (string.IsNullOrEmpty(coinId)) return;

        PlayerPrefs.DeleteKey(CollectedKey(type, scene, coinId));
        PlayerPrefs.DeleteKey(CollectedKeyPrefix + Entry(scene, coinId));   // legacy, typeless
        RemoveFromIndex(type, Entry(scene, coinId));
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
    /// Puts one coin type back to how a fresh save finds it: balance at zero, and every one-time coin
    /// of that type placed in the world again.
    ///
    /// Both halves, deliberately. Clearing the marks alone leaves the coins collectable a second time
    /// with the balance they already paid still banked, so a few resets and the save reads forty
    /// Permanents with none left in the level to explain them.
    ///
    /// Respawnable coins never record a mark, so for that type this only zeroes the balance.
    /// </summary>
    /// <returns>How many collected marks were cleared.</returns>
    public static int ResetType(CoinType type)
    {
        EnsureLoaded();
        ZeroBalance(type);
        int cleared = ResetCollected(type);
        dirty = true;
        Flush();
        return cleared;
    }

    /// <summary>Every type, balances and marks together.</summary>
    public static int ResetAll()
    {
        int cleared = 0;
        foreach (var type in AllTypes) cleared += ResetType(type);
        return cleared;
    }

    /// <summary>
    /// Clears the collected marks for one type, so its one-time coins appear again. Reachable only
    /// through the index — a mark written before that index existed is not enumerable, which is why
    /// the editor-side reset also sweeps the coins actually placed in the open scene.
    /// </summary>
    /// <returns>How many marks were cleared.</returns>
    public static int ResetCollected(CoinType type)
    {
        var entries = ReadIndex(type);
        foreach (var entry in entries)
        {
            PlayerPrefs.DeleteKey(CollectedKeyPrefix + type + "." + entry);
            PlayerPrefs.DeleteKey(CollectedKeyPrefix + entry);   // legacy, typeless
        }

        PlayerPrefs.DeleteKey(CollectedIndexPrefix + type);
        dirty = true;
        return entries.Length;
    }

    static void ZeroBalance(CoinType type)
    {
        balances[(int)type] = 0;
        session[(int)type] = 0;
        PlayerPrefs.DeleteKey(BalanceKeyPrefix + type);
        BalanceChanged?.Invoke(type, 0);
    }

    static string Entry(string scene, string coinId) => scene + "." + coinId;

    static string CollectedKey(CoinType type, string scene, string coinId) =>
        CollectedKeyPrefix + type + "." + Entry(scene, coinId);

    static string[] ReadIndex(CoinType type)
    {
        string raw = PlayerPrefs.GetString(CollectedIndexPrefix + type, string.Empty);
        return raw.Length == 0
            ? Array.Empty<string>()
            : raw.Split(IndexSeparator, StringSplitOptions.RemoveEmptyEntries);
    }

    static void AddToIndex(CoinType type, string entry)
    {
        string key = CollectedIndexPrefix + type;
        string raw = PlayerPrefs.GetString(key, string.Empty);

        // Guarded against a repeat: the same coin can be marked again after a reset, and an index
        // growing a duplicate entry per reset would eventually outgrow what PlayerPrefs will store.
        foreach (var existing in ReadIndex(type))
            if (existing == entry) return;

        PlayerPrefs.SetString(key, raw.Length == 0 ? entry : raw + IndexSeparator + entry);
    }

    static void RemoveFromIndex(CoinType type, string entry)
    {
        var kept = new System.Collections.Generic.List<string>();
        foreach (var existing in ReadIndex(type))
            if (existing != entry) kept.Add(existing);

        PlayerPrefs.SetString(CollectedIndexPrefix + type, string.Join(IndexSeparator, kept));
    }

    static void EnsureLoaded()
    {
        if (balances != null) return;

        balances = new int[AllTypes.Length];
        session = new int[AllTypes.Length];
        foreach (var type in AllTypes)
            balances[(int)type] = PlayerPrefs.GetInt(BalanceKeyPrefix + type, 0);
    }
}
