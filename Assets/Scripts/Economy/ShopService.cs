using System;

/// <summary>
/// The one place a purchase happens. Everything else asks it a question or listens to what it did.
///
/// The order of the checks is the whole point: every reason to refuse is established before a single
/// coin moves, so there is no path where the player is charged and then the grant fails. That is why
/// TrySpend is the last thing before Grant and nothing but Grant follows it.
///
/// Static, like CoinWallet and PlayerInventory, and for the same reason — a purchase can be made
/// from the menu, the pause screen or a debug key, and none of them should have to find an object
/// first.
/// </summary>
public static class ShopService
{
    /// <summary>Raised after a successful purchase, once the coins are gone and the item is granted.</summary>
    public static event Action<ShopItem> Purchased;

    /// <summary>
    /// Whether this could be bought right now, and why not if it could not. The exact test TryBuy
    /// runs, exposed so a shop row can grey itself out and show the reason without attempting a
    /// purchase to find out.
    /// </summary>
    public static PurchaseResult CanBuy(ShopItem item)
    {
        if (item == null || string.IsNullOrEmpty(item.ItemId)) return PurchaseResult.Invalid;
        if (!item.Available) return PurchaseResult.Unavailable;

        // Progression is allowed to be non-purchasable — a level the game hands over rather than
        // sells still belongs in the catalogue so that ownership can be recorded against it.
        if (item is LevelUnlockItem level && !level.Purchasable) return PurchaseResult.Unavailable;

        if (item.IsOwned) return PurchaseResult.AlreadyOwned;

        var required = Prerequisite(item);
        if (required != null && !required.IsOwned) return PurchaseResult.Locked;

        // Before affordability on purpose: a player at the cap should be told they are full, not
        // told to go and earn coins they would not be allowed to spend.
        if (item is ConsumableItem consumable && !consumable.HasRoom) return PurchaseResult.AtCap;

        if (!CoinWallet.CanAfford(item.Currency, item.Cost)) return PurchaseResult.CannotAfford;

        return PurchaseResult.Purchased;
    }

    /// <summary>
    /// Buys it, or explains why not. Nothing is spent unless the item is also granted.
    /// </summary>
    public static PurchaseResult TryBuy(ShopItem item)
    {
        var verdict = CanBuy(item);
        if (verdict != PurchaseResult.Purchased) return verdict;

        // Re-checked through TrySpend rather than trusted from CanAfford above: the two are separated
        // by nothing today, but a balance that moved between them must lose the item, not the coins.
        if (!CoinWallet.TrySpend(item.Currency, item.Cost)) return PurchaseResult.CannotAfford;

        item.Grant();

        // Both saves written together. A crash between them would otherwise leave coins spent with
        // nothing to show for it, which is the one failure a player will always notice.
        CoinWallet.Flush();
        PlayerInventory.Flush();

        Purchased?.Invoke(item);
        return PurchaseResult.Purchased;
    }

    /// <summary>
    /// Hands the item over without charging. For rewards, story grants and the dev panel — anything
    /// where the player gets something they did not buy.
    /// </summary>
    public static void Grant(ShopItem item)
    {
        if (item == null || string.IsNullOrEmpty(item.ItemId)) return;

        item.Grant();
        PlayerInventory.Flush();
        Purchased?.Invoke(item);
    }

    /// <summary>
    /// What has to be owned first. A PowerItem falls back to the tier below it, so a chain only has
    /// to be described once — see PowerItem.EffectivePrerequisite.
    /// </summary>
    public static ShopItem Prerequisite(ShopItem item)
    {
        if (item is PowerItem power) return power.EffectivePrerequisite;
        return item == null ? null : item.Prerequisite;
    }

    /// <summary>
    /// A line a shop row can show without composing one itself, so the same refusal is worded the
    /// same way everywhere it appears.
    /// </summary>
    public static string Explain(ShopItem item, PurchaseResult result)
    {
        switch (result)
        {
            case PurchaseResult.Purchased:
                return string.Empty;
            case PurchaseResult.AlreadyOwned:
                return "Owned";
            case PurchaseResult.AtCap:
                return "Full";
            case PurchaseResult.Unavailable:
                return "Not available";
            case PurchaseResult.Locked:
                var required = Prerequisite(item);
                return required == null ? "Locked" : $"Requires {required.DisplayName}";
            case PurchaseResult.CannotAfford:
                if (item == null) return "Cannot afford";
                int shortfall = item.Cost - CoinWallet.Balance(item.Currency);
                return $"Need {shortfall} more {CurrencyRules.DisplayName(item.Currency)}";
            default:
                return "Unavailable";
        }
    }
}
