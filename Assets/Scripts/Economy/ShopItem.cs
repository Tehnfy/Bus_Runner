using UnityEngine;

/// <summary>
/// One thing the player can buy, authored as an asset rather than written in code — so adding a
/// booster is a right-click in the Project window, not a recompile.
///
/// A subclass supplies two things: which <see cref="Category"/> it belongs to, and what
/// <see cref="Grant"/> actually does. Everything else — price, ownership, affordability, the
/// purchase itself — is the same for all of them and lives here or in ShopService.
///
/// The currency is not authorable. It is derived from the category through CurrencyRules, so a
/// power priced in silver is not a mistake anyone can make in the inspector.
/// </summary>
public abstract class ShopItem : ScriptableObject
{
    [Tooltip("Stable save key. Never change it once the item has shipped — the whole inventory is " +
             "keyed on this string, and renaming it makes every copy the player owns disappear. " +
             "The asset file can be renamed freely; this cannot.")]
    [SerializeField] string itemId;

    [Header("Presentation")]
    [SerializeField] string displayName;
    [TextArea(2, 4)]
    [SerializeField] string description;
    [SerializeField] Sprite icon;

    [Header("Price")]
    [Tooltip("Cost in this item's currency, which follows from its category — silver for " +
             "consumables, purple for powers, gold for progression.")]
    [Min(0)]
    [SerializeField] int cost = 1;

    [Header("Availability")]
    [Tooltip("Optional. Hidden and unbuyable until this other item is owned — the seam a power tree " +
             "hangs off. Leave empty for anything available from the start.")]
    [SerializeField] ShopItem prerequisite;

    [Tooltip("Off keeps a finished item out of the shop without deleting it, which is how something " +
             "gets built and tested an update before it goes on sale.")]
    [SerializeField] bool available = true;

    /// <summary>Save key. Falls back to the asset name so a half-authored item is still addressable.</summary>
    public string ItemId => string.IsNullOrEmpty(itemId) ? name : itemId;

    public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
    public string Description => description;
    public Sprite Icon => icon;
    public int Cost => cost;
    public ShopItem Prerequisite => prerequisite;
    public bool Available => available;

    /// <summary>Which kind of thing this is, and therefore which coin buys it.</summary>
    public abstract ShopCategory Category { get; }

    /// <summary>The coin this costs. Derived, never authored — see CurrencyRules.</summary>
    public CoinType Currency => CurrencyRules.CurrencyFor(Category);

    /// <summary>
    /// Whether buying this again would be meaningless. A power or a level unlock is owned once;
    /// a consumable never is, which is why the base answer is "no" and only one-time items override.
    /// </summary>
    public virtual bool IsOwned => false;

    /// <summary>
    /// Hands the item over. Called by ShopService only after the coins have actually been taken, so
    /// an implementation may assume it has been paid for and should never re-check affordability.
    /// </summary>
    public abstract void Grant();

    /// <summary>
    /// Undoes <see cref="Grant"/>, for the dev panel and for a refund path if one ever exists. No
    /// coins are returned — refunding is a policy decision and belongs with the caller, not here.
    /// </summary>
    public abstract void Revoke();

#if UNITY_EDITOR
    /// <summary>
    /// Two authoring traps, caught where they are made rather than at runtime: a blank id silently
    /// falls back to the asset name and then breaks the day the asset is renamed, and an item that
    /// requires itself can never be bought at all.
    /// </summary>
    void OnValidate()
    {
        if (string.IsNullOrEmpty(itemId)) itemId = name;

        if (prerequisite == this)
        {
            Debug.LogWarning($"[{name}] An item cannot be its own prerequisite — clearing it.", this);
            prerequisite = null;
        }
    }
#endif
}
