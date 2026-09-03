using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Everything for sale, in the order it should be shown. One asset, wired wherever a screen needs
/// it — the shop, the level select, the dev panel.
///
/// A list asset rather than a Resources folder scan: the order here is the order on screen, which is
/// a design decision worth being able to see and drag, and a scan would silently pick up an item
/// left half-authored in a scratch folder.
/// </summary>
[CreateAssetMenu(menuName = "Bus Runner/Shop/Catalog", fileName = "ShopCatalog")]
public class ShopCatalog : ScriptableObject
{
    [Tooltip("Display order. Mixed categories are fine — the screens filter by category themselves.")]
    [SerializeField] List<ShopItem> items = new List<ShopItem>();

    [Header("Loadout")]
    [Tooltip("How many powers may be equipped at once. Equipping into a full loadout takes off " +
             "whichever was equipped longest ago, so at one slot this reads as a straight swap. " +
             "Raise it when there are enough powers for choosing between them to be interesting.")]
    [Min(1)]
    [SerializeField] int powerSlots = 1;

    public IReadOnlyList<ShopItem> Items => items;

    /// <summary>Equipped-power limit. Floored at one — a loadout of zero would make powers unusable.</summary>
    public int PowerSlots => Mathf.Max(1, powerSlots);

    /// <summary>
    /// Items of one category, in catalogue order. Allocates a list per call, which is fine for the
    /// menu screens that use it — none of them are built per frame.
    /// </summary>
    public List<ShopItem> ItemsIn(ShopCategory category)
    {
        var found = new List<ShopItem>();
        foreach (var item in items)
            if (item != null && item.Category == category) found.Add(item);
        return found;
    }

    /// <summary>The item with this save id, or null. Linear — the catalogue is tens of entries, not thousands.</summary>
    public ShopItem Find(string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return null;

        foreach (var item in items)
            if (item != null && item.ItemId == itemId) return item;
        return null;
    }

    /// <summary>
    /// Every level the player may currently start, by scene name. The list MenuController's unlock
    /// logic reads — a level with no LevelUnlockItem in the catalogue is not mentioned here at all,
    /// which is why the caller treats an unlisted level as open rather than as locked.
    /// </summary>
    public List<string> UnlockedScenes()
    {
        var open = new List<string>();
        foreach (var item in items)
        {
            if (item is LevelUnlockItem level && level.IsUnlocked && !string.IsNullOrEmpty(level.SceneName))
                open.Add(level.SceneName);
        }
        return open;
    }

    /// <summary>
    /// The unlock that gates this scene, or null if nothing does. What the level select reads to put
    /// a price on a locked row — it needs the item itself, not merely the fact that one exists.
    /// </summary>
    public LevelUnlockItem UnlockFor(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return null;

        foreach (var item in items)
            if (item is LevelUnlockItem level && level.SceneName == sceneName) return level;
        return null;
    }

    /// <summary>Whether this scene is gated at all. An ungated level is playable without a purchase.</summary>
    public bool IsGated(string sceneName) => UnlockFor(sceneName) != null;

#if UNITY_EDITOR
    /// <summary>
    /// Duplicate ids are the one authoring mistake that corrupts the save rather than merely looking
    /// wrong: two items sharing an id means buying either grants both, forever. Caught here, where
    /// the second one is dragged in.
    /// </summary>
    void OnValidate()
    {
        var seen = new HashSet<string>();
        foreach (var item in items)
        {
            if (item == null) continue;
            if (seen.Add(item.ItemId)) continue;

            Debug.LogError($"[ShopCatalog] Two items share the id '{item.ItemId}' — buying either " +
                           $"would grant both. Give one of them its own id.", item);
        }
    }
#endif
}
