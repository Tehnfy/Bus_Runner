using System;
using UnityEngine;

/// <summary>
/// One coin and how many of it. A price is a list of these, so a level unlock can ask for gold
/// <em>and</em> silver rather than being limited to the single currency its category implies.
///
/// A struct rather than a class: it is two values with no identity, it is copied freely between the
/// shop screen and the wallet, and a list of them serialises inline on the item asset instead of
/// spawning a sub-asset per line of the price.
///
/// The currency is authorable here, unlike on <see cref="ShopItem"/> itself. That is the whole point
/// of the type — the category still fixes the <em>primary</em> currency, which is what stops a power
/// being priced in silver, and these are the extras layered on top of it.
/// </summary>
[Serializable]
public struct CoinCost
{
    [Tooltip("Which coin pays this part of the price.")]
    public CoinType currency;

    [Min(0)]
    [Tooltip("How many. Zero entries are dropped rather than charged — a line left at zero is an " +
             "unfinished authoring step, and silently charging nothing for it would hide that.")]
    public int amount;

    public CoinCost(CoinType currency, int amount)
    {
        this.currency = currency;
        this.amount = amount;
    }

    /// <summary>"2 Gold". The one wording for a price fragment, so no screen invents its own.</summary>
    public override string ToString() => $"{amount} {CurrencyRules.DisplayName(currency)}";
}
