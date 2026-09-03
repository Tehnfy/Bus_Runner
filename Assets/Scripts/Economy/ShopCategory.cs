using System;

/// <summary>What a purchasable is for. One category per currency — see <see cref="CurrencyRules"/>.</summary>
public enum ShopCategory
{
    /// <summary>Spent and gone. Boosters, revives, head starts — anything with a stock count.</summary>
    Consumable,

    /// <summary>A permanent ability. Bought once, owned forever.</summary>
    Power,

    /// <summary>Access to content: a level, a world, a route.</summary>
    Progression,
}

/// <summary>
/// The single place that says which coin buys which kind of thing.
///
/// The design is one currency per category, so an item does not get to name its own currency — it
/// declares a category and the price follows from this table. That is deliberate: a purple price on
/// a consumable would be unauthorable rather than merely discouraged, and there is exactly one file
/// to edit if the economy is ever re-cut.
///
/// The colours are the ones in CoinSettings.asset, not decoration — they are how a player tells the
/// currencies apart, so the names here should keep matching the coins on screen.
/// </summary>
public static class CurrencyRules
{
    static readonly ShopCategory[] AllCategories = (ShopCategory[])Enum.GetValues(typeof(ShopCategory));

    // The order the shop shows its sections in, which is not the order the enum happens to declare
    // them. Progression first because a level is the thing a player is most likely to be saving for;
    // consumables last because they are the cheapest and the most repeatable.
    static readonly ShopCategory[] Order =
    {
        ShopCategory.Progression,
        ShopCategory.Power,
        ShopCategory.Consumable,
    };

    /// <summary>Every category, for menus that want to enumerate without depending on the enum shape.</summary>
    public static ShopCategory[] Categories => AllCategories;

    /// <summary>Categories in the order the shop draws them.</summary>
    public static ShopCategory[] DisplayOrder => Order;

    /// <summary>
    /// The section heading a player sees. Separate from the enum name on purpose — "Progression" is
    /// what the code calls the idea, "Levels" is what the thing being sold actually is.
    /// </summary>
    public static string DisplayName(ShopCategory category)
    {
        switch (category)
        {
            case ShopCategory.Progression: return "Levels";
            case ShopCategory.Power: return "Powers";
            case ShopCategory.Consumable: return "Consumables";
            default: return category.ToString();
        }
    }

    /// <summary>Which coin pays for this category.</summary>
    public static CoinType CurrencyFor(ShopCategory category)
    {
        switch (category)
        {
            // Silver. The one that comes back every run, so it is the one that funds things you
            // spend every run.
            case ShopCategory.Consumable: return CoinType.Respawnable;

            // Purple. One-time pickups buying one-time abilities: a power costs something the player
            // cannot farm, so the choice of which to buy first actually matters.
            case ShopCategory.Power: return CoinType.Permanent;

            // Gold. The rarest pickup gates the content, which is what CoinType.Special was always
            // described as being for.
            case ShopCategory.Progression: return CoinType.Special;

            default: throw new ArgumentOutOfRangeException(nameof(category), category, "No currency mapped.");
        }
    }

    /// <summary>
    /// The reverse lookup, for a shop screen that wants to show "what can I spend silver on".
    /// Returns false rather than throwing: a currency with no category is a legitimate state while
    /// the economy is being designed.
    /// </summary>
    public static bool CategoryFor(CoinType currency, out ShopCategory category)
    {
        foreach (var candidate in AllCategories)
        {
            if (CurrencyFor(candidate) != currency) continue;
            category = candidate;
            return true;
        }

        category = default;
        return false;
    }

    /// <summary>Player-facing name of the coin, so three screens do not each invent their own.</summary>
    public static string DisplayName(CoinType currency)
    {
        switch (currency)
        {
            case CoinType.Permanent: return "Purple";
            case CoinType.Respawnable: return "Silver";
            case CoinType.Special: return "Gold";
            default: return currency.ToString();
        }
    }
}
