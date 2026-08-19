using UnityEngine;

/// <summary>
/// Something bought over and over and spent in a run: a head start, a revive, a coin magnet.
/// Bought with silver, because silver is the coin that comes back every run.
///
/// Never "owned" — buying a fifth revive is a legitimate purchase, so the base IsOwned of false is
/// exactly right and the only cap is <see cref="maxStock"/>.
///
/// Nothing here knows what the booster does. A run-time system asks
/// <see cref="PlayerInventory.TryConsume"/> for this item's id at the moment it wants the effect;
/// that keeps the catalogue free of gameplay and the gameplay free of prices.
/// </summary>
[CreateAssetMenu(menuName = "Bus Runner/Shop/Consumable", fileName = "Consumable_")]
public class ConsumableItem : ShopItem
{
    [Header("Consumable")]
    [Tooltip("How many are added per purchase. Above 1 for a bundle — 'five revives' is one buy.")]
    [Min(1)]
    [SerializeField] int quantityPerPurchase = 1;

    [Tooltip("Most the player may hold. 0 for no limit. A cap is what stops a player banking the " +
             "whole economy in one currency and never needing to play for it again.")]
    [Min(0)]
    [SerializeField] int maxStock;

    public override ShopCategory Category => ShopCategory.Consumable;

    public int QuantityPerPurchase => quantityPerPurchase;
    public int MaxStock => maxStock;

    /// <summary>How many the player is holding right now.</summary>
    public int Stock => PlayerInventory.Stock(ItemId);

    /// <summary>
    /// Whether another purchase would fit. Checked by ShopService before the coins are taken, so a
    /// player at the cap is refused rather than charged for something they cannot receive.
    /// </summary>
    public bool HasRoom => maxStock <= 0 || Stock + quantityPerPurchase <= maxStock;

    public override void Grant() => PlayerInventory.AddStock(ItemId, quantityPerPurchase);

    /// <summary>Removes one purchase worth, not the whole stock — the inverse of a single Grant.</summary>
    public override void Revoke() => PlayerInventory.AddStock(ItemId, -quantityPerPurchase);

    /// <summary>
    /// Spends one. The call gameplay makes when the effect actually fires, rather than at purchase:
    /// a revive bought in the menu is not used until the player dies, and charging it at the till
    /// would lose it on any run that never needed it.
    /// </summary>
    public bool TryUse(int amount = 1) => PlayerInventory.TryConsume(ItemId, amount);
}
