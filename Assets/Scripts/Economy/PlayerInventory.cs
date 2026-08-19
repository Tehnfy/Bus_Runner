using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What the player owns, as opposed to what they can afford. Two kinds of record:
///
///   owned   a one-time purchase — a power, a level unlock. Present or not.
///   stock   a consumable — how many are banked.
///
/// Static and PlayerPrefs-backed for the same reasons CoinWallet is: nothing to lose when a scene
/// unloads, and no ordering question about when it exists.
///
/// This deliberately knows nothing about prices, categories or currencies. It records what was
/// granted; ShopService decides whether granting was allowed. Keeping that split is what lets a
/// reward, a debug command or a save-import grant an item without inventing a fake transaction.
///
/// Every write is mirrored into an index, exactly as CoinWallet does for its collected marks and
/// for the same reason: PlayerPrefs cannot be enumerated, so a key written without an index entry
/// is write-only — readable if you already know the id, unreachable if you do not, and impossible
/// to clear from a reset that cannot know every id the game has ever shipped.
/// </summary>
public static class PlayerInventory
{
    /// <summary>Raised when a one-time item is gained or removed, with its id and whether it is now owned.</summary>
    public static event Action<string, bool> OwnedChanged;

    /// <summary>Raised when a consumable's stock changes, with its id and the new count.</summary>
    public static event Action<string, int> StockChanged;

    const string OwnedKeyPrefix = "shop.owned.";
    const string StockKeyPrefix = "shop.stock.";
    const string OwnedIndexKey = "shop.ownedIndex";
    const string StockIndexKey = "shop.stockIndex";
    const char IndexSeparator = ';';

    static bool dirty;

    // ---- one-time purchases -------------------------------------------------------------------

    public static bool IsOwned(string itemId) =>
        !string.IsNullOrEmpty(itemId) && PlayerPrefs.GetInt(OwnedKeyPrefix + itemId, 0) == 1;

    /// <summary>
    /// Records a one-time item as owned. Idempotent — granting something already held is a no-op
    /// rather than an error, because a reward path and a purchase path may both reach the same item.
    /// </summary>
    public static void MarkOwned(string itemId)
    {
        if (string.IsNullOrEmpty(itemId) || IsOwned(itemId)) return;

        PlayerPrefs.SetInt(OwnedKeyPrefix + itemId, 1);
        AddToIndex(OwnedIndexKey, itemId);
        dirty = true;
        OwnedChanged?.Invoke(itemId, true);
    }

    public static void ForgetOwned(string itemId)
    {
        if (string.IsNullOrEmpty(itemId) || !IsOwned(itemId)) return;

        PlayerPrefs.DeleteKey(OwnedKeyPrefix + itemId);
        RemoveFromIndex(OwnedIndexKey, itemId);
        dirty = true;
        OwnedChanged?.Invoke(itemId, false);
    }

    /// <summary>Every one-time item held. Reachable only because of the index.</summary>
    public static string[] OwnedIds() => ReadIndex(OwnedIndexKey);

    // ---- consumables --------------------------------------------------------------------------

    public static int Stock(string itemId) =>
        string.IsNullOrEmpty(itemId) ? 0 : PlayerPrefs.GetInt(StockKeyPrefix + itemId, 0);

    /// <summary>
    /// Adds to a consumable's stock. Clamped at zero for the same reason CoinWallet.Add is: a
    /// negative that ran past zero would leave a count no sequence of legitimate play could reach.
    /// </summary>
    public static void AddStock(string itemId, int amount)
    {
        if (string.IsNullOrEmpty(itemId) || amount == 0) return;

        int now = Mathf.Max(0, Stock(itemId) + amount);
        PlayerPrefs.SetInt(StockKeyPrefix + itemId, now);
        AddToIndex(StockIndexKey, itemId);
        dirty = true;
        StockChanged?.Invoke(itemId, now);
    }

    /// <summary>
    /// Spends one or more of a consumable. Returns false and changes nothing if the stock does not
    /// cover it — the same all-or-nothing contract as CoinWallet.TrySpend, so a caller never has to
    /// unpick a partial consume.
    /// </summary>
    public static bool TryConsume(string itemId, int amount = 1)
    {
        if (amount <= 0) return true;
        if (Stock(itemId) < amount) return false;

        AddStock(itemId, -amount);
        return true;
    }

    /// <summary>Every consumable that has ever been stocked, including ones now at zero.</summary>
    public static string[] StockedIds() => ReadIndex(StockIndexKey);

    /// <summary>
    /// Drops a consumable entirely — its count and its index entry, not just the count.
    ///
    /// The counterpart to ForgetOwned, and separate from AddStock reaching zero on purpose: a
    /// consumable at zero is still something the player has bought before, which a shop wants to
    /// know. This is for the case where the item itself is gone — cut from the game, or renamed,
    /// leaving a key nothing in the catalogue will ever claim again.
    /// </summary>
    public static void ForgetStock(string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return;

        PlayerPrefs.DeleteKey(StockKeyPrefix + itemId);
        RemoveFromIndex(StockIndexKey, itemId);
        dirty = true;
        StockChanged?.Invoke(itemId, 0);
    }

    // ---- housekeeping -------------------------------------------------------------------------

    /// <summary>
    /// Writes to disk if anything changed. Same contract as CoinWallet.Flush and driven by the same
    /// CoinWalletFlusher lifecycle hooks — buying is rare enough that this could flush immediately,
    /// but having two different persistence rhythms in one save is how they drift apart.
    /// </summary>
    public static void Flush()
    {
        if (!dirty) return;
        dirty = false;
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Clears everything owned and every consumable stock. For the dev panel and for a real
    /// "erase progress" option later. Announces each removal so any open screen re-reads.
    /// </summary>
    public static int ResetAll()
    {
        int cleared = 0;

        foreach (var id in OwnedIds())
        {
            PlayerPrefs.DeleteKey(OwnedKeyPrefix + id);
            cleared++;
            OwnedChanged?.Invoke(id, false);
        }
        PlayerPrefs.DeleteKey(OwnedIndexKey);

        foreach (var id in StockedIds())
        {
            PlayerPrefs.DeleteKey(StockKeyPrefix + id);
            cleared++;
            StockChanged?.Invoke(id, 0);
        }
        PlayerPrefs.DeleteKey(StockIndexKey);

        dirty = true;
        Flush();
        return cleared;
    }

    // ---- index --------------------------------------------------------------------------------

    static string[] ReadIndex(string key)
    {
        string raw = PlayerPrefs.GetString(key, string.Empty);
        return raw.Length == 0
            ? Array.Empty<string>()
            : raw.Split(IndexSeparator, StringSplitOptions.RemoveEmptyEntries);
    }

    static void AddToIndex(string key, string entry)
    {
        // Guarded against a repeat: a consumable is re-stocked constantly, and an index growing an
        // entry per purchase would eventually outgrow what PlayerPrefs will hold.
        foreach (var existing in ReadIndex(key))
            if (existing == entry) return;

        string raw = PlayerPrefs.GetString(key, string.Empty);
        PlayerPrefs.SetString(key, raw.Length == 0 ? entry : raw + IndexSeparator + entry);
    }

    static void RemoveFromIndex(string key, string entry)
    {
        var kept = new List<string>();
        foreach (var existing in ReadIndex(key))
            if (existing != entry) kept.Add(existing);

        PlayerPrefs.SetString(key, string.Join(IndexSeparator, kept));
    }
}
