using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One thing the player can buy, authored as an asset rather than written in code — so adding a
/// booster is a right-click in the Project window, not a recompile.
///
/// A subclass supplies two things: which <see cref="Category"/> it belongs to, and what
/// <see cref="Grant"/> actually does. Everything else — price, ownership, affordability, the
/// purchase itself — is the same for all of them and lives here or in ShopService.
///
/// The <em>primary</em> currency is not authorable. It is derived from the category through
/// CurrencyRules, so a power priced in silver is not a mistake anyone can make in the inspector.
/// Anything beyond that goes in <see cref="extraCosts"/>, where the currency <em>is</em> chosen by
/// hand — a level unlock that costs gold and silver is a real design, and the category rule exists
/// to fix what a thing is fundamentally bought with, not to cap a price at one coin.
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
    [Tooltip("Cost in this item's primary currency, which follows from its category — silver for " +
             "consumables, purple for powers, gold for progression.")]
    [Min(0)]
    [SerializeField] int cost = 1;

    [Tooltip("Further coins charged alongside the one above, for anything priced in more than one " +
             "currency. Leave empty for a single-currency price. An entry naming the primary " +
             "currency again is added to it rather than charged separately, so a price never asks " +
             "for the same coin twice.")]
    [SerializeField] List<CoinCost> extraCosts = new List<CoinCost>();

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

    /// <summary>The primary coin this costs. Derived, never authored — see CurrencyRules.</summary>
    public CoinType Currency => CurrencyRules.CurrencyFor(Category);

    // Built once and kept, because Price is read on every shop redraw — which happens on every
    // balance change, for every row — and rebuilding a list there would allocate per coin picked up.
    [NonSerialized] List<CoinCost> price;

    /// <summary>
    /// The full price: the primary cost first, then the extras, with duplicate currencies merged and
    /// zero-amount lines dropped.
    ///
    /// The primary comes first on purpose — it is the coin the category says this is really bought
    /// with, and every screen that shows a price shows it left to right in this order.
    ///
    /// An empty list means free, and is a legitimate state: an item at zero cost with no extras is
    /// how something gets handed over through the normal purchase path without charging for it.
    /// </summary>
    public IReadOnlyList<CoinCost> Price
    {
        get
        {
            if (price != null) return price;

            price = new List<CoinCost>(1 + (extraCosts?.Count ?? 0));
            Charge(price, Currency, cost);

            if (extraCosts != null)
                foreach (var extra in extraCosts) Charge(price, extra.currency, extra.amount);

            return price;
        }
    }

    /// <summary>How much of one coin this costs, zero if it is not part of the price.</summary>
    public int CostIn(CoinType currency)
    {
        foreach (var part in Price)
            if (part.currency == currency) return part.amount;
        return 0;
    }

    /// <summary>
    /// Adds one line to a price, folding it into an existing line for the same coin.
    ///
    /// Merging rather than appending is what keeps the rest of the system simple: ShopService can
    /// spend the list straight through without checking whether two entries share a currency, and a
    /// price can never read "2 Gold + 3 Gold" on a button.
    /// </summary>
    static void Charge(List<CoinCost> into, CoinType currency, int amount)
    {
        if (amount <= 0) return;

        for (int i = 0; i < into.Count; i++)
        {
            if (into[i].currency != currency) continue;
            into[i] = new CoinCost(currency, into[i].amount + amount);
            return;
        }

        into.Add(new CoinCost(currency, amount));
    }

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
        // Dropped so the next read rebuilds it. Without this, editing a price in the inspector during
        // play mode would change nothing — the cached list is what every screen reads.
        price = null;

        if (string.IsNullOrEmpty(itemId)) itemId = name;

        if (prerequisite == this)
        {
            Debug.LogWarning($"[{name}] An item cannot be its own prerequisite — clearing it.", this);
            prerequisite = null;
        }
    }
#endif
}
