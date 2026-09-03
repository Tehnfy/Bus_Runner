using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Which powers the player currently has equipped, as opposed to which ones they own.
///
/// Owning and equipping are deliberately separate. PlayerInventory records the purchase forever;
/// this records the much smaller, changeable choice of which of those purchases is actually active.
/// That split is the whole point of a loadout — buying a second power should be a real decision
/// about what to give up, not an automatic upgrade.
///
/// Static and PlayerPrefs-backed for the same reasons CoinWallet and PlayerInventory are: nothing to
/// lose when a scene unloads, and no ordering question about when it exists.
///
/// Order matters here, unlike in PlayerInventory. The list is kept oldest-first so that equipping
/// into a full loadout can evict the thing equipped longest ago — which, at the default one slot,
/// is what makes tapping Equip on a second power read as a straight swap.
///
/// This knows nothing about what a power does, or whether it is owned. ShopScreen refuses to equip
/// something unowned, and gameplay checks ownership as well as equipped state, so a stale id left by
/// a save edit or a dev wipe cannot hand out an ability.
/// </summary>
public static class PowerLoadout
{
    /// <summary>Raised when a power is equipped or unequipped, with its id and the new state.</summary>
    public static event Action<string, bool> EquippedChanged;

    const string EquippedKey = "shop.equipped";
    const char Separator = ';';

    // Cached rather than re-read per call: PlayerController asks IsEquipped on every jump press that
    // did not find ground, and re-splitting a PlayerPrefs string on each of those is wasted work.
    static List<string> equipped;
    static bool dirty;

    /// <summary>Everything equipped, oldest first. A copy — callers cannot edit the loadout by proxy.</summary>
    public static string[] EquippedIds()
    {
        EnsureLoaded();
        return equipped.ToArray();
    }

    public static int EquippedCount
    {
        get { EnsureLoaded(); return equipped.Count; }
    }

    public static bool IsEquipped(string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return false;
        EnsureLoaded();
        return equipped.Contains(itemId);
    }

    /// <summary>
    /// Equips a power, making room by unequipping the oldest if every slot is taken.
    ///
    /// Evicting rather than refusing is what makes one slot feel like a loadout instead of a lock:
    /// the player taps Equip on the power they want and it replaces what was there, with no separate
    /// unequip step to discover first.
    /// </summary>
    /// <param name="slots">How many may be equipped at once. Clamped to at least one.</param>
    /// <returns>What was displaced, or null. Returned so a UI can say what it took off.</returns>
    public static string Equip(string itemId, int slots)
    {
        if (string.IsNullOrEmpty(itemId)) return null;

        EnsureLoaded();
        if (equipped.Contains(itemId)) return null;

        slots = Mathf.Max(1, slots);

        // A while, not an if: the limit can be lowered between sessions, and a save written when it
        // was three has to shed two on the next equip rather than one.
        string displaced = null;
        while (equipped.Count >= slots)
        {
            displaced = equipped[0];
            equipped.RemoveAt(0);
            EquippedChanged?.Invoke(displaced, false);
        }

        equipped.Add(itemId);
        Save();
        EquippedChanged?.Invoke(itemId, true);
        return displaced;
    }

    public static void Unequip(string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return;

        EnsureLoaded();
        if (!equipped.Remove(itemId)) return;

        Save();
        EquippedChanged?.Invoke(itemId, false);
    }

    /// <summary>
    /// Takes everything off. For the dev panel's unlock wipe, which clears what the player owns —
    /// leaving powers equipped that are no longer owned would be a save that disagrees with itself.
    /// </summary>
    public static int Clear()
    {
        EnsureLoaded();
        if (equipped.Count == 0) return 0;

        var removed = equipped.ToArray();
        equipped.Clear();
        Save();

        foreach (var id in removed) EquippedChanged?.Invoke(id, false);
        return removed.Length;
    }

    /// <summary>
    /// Drops anything the predicate says is no longer held. The counterpart to Clear for the case
    /// where a single item was revoked rather than the whole inventory.
    /// </summary>
    public static int DropUnowned(Func<string, bool> stillOwned)
    {
        if (stillOwned == null) return 0;

        EnsureLoaded();
        int dropped = 0;
        for (int i = equipped.Count - 1; i >= 0; i--)
        {
            if (stillOwned(equipped[i])) continue;

            string id = equipped[i];
            equipped.RemoveAt(i);
            dropped++;
            EquippedChanged?.Invoke(id, false);
        }

        if (dropped > 0) Save();
        return dropped;
    }

    /// <summary>
    /// Writes to disk if anything changed. Same contract and rhythm as CoinWallet.Flush and
    /// PlayerInventory.Flush, driven by the same CoinWalletFlusher lifecycle hooks.
    /// </summary>
    public static void Flush()
    {
        if (!dirty) return;
        dirty = false;
        PlayerPrefs.Save();
    }

    static void Save()
    {
        PlayerPrefs.SetString(EquippedKey, string.Join(Separator, equipped));
        dirty = true;
    }

    static void EnsureLoaded()
    {
        if (equipped != null) return;

        equipped = new List<string>();
        string raw = PlayerPrefs.GetString(EquippedKey, string.Empty);
        if (raw.Length == 0) return;

        equipped.AddRange(raw.Split(Separator, StringSplitOptions.RemoveEmptyEntries));
    }
}
