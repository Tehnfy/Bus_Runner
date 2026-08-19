using UnityEngine;

/// <summary>
/// A permanent ability — double jump, longer slide, a second life per run. Bought once with purple,
/// the coin the player cannot farm, so which power to buy first is a real decision.
///
/// The asset is the ability's identity, not its implementation. Gameplay asks
/// <c>PowerItem.IsUnlocked</c> (or PlayerInventory directly) for the id it cares about; this file
/// never learns what a double jump is. That is what lets a power be re-tuned, or its effect moved to
/// a different system, without the save or the shop noticing.
///
/// <see cref="upgradeOf"/> is the seam for tiers: Longer Slide II lists Longer Slide I, and
/// <see cref="Tier"/> counts the chain so a UI can show "II" without anyone typing it twice.
/// </summary>
[CreateAssetMenu(menuName = "Bus Runner/Shop/Power", fileName = "Power_")]
public class PowerItem : ShopItem
{
    [Header("Power")]
    [Tooltip("Optional. The tier below this one. Set it and this becomes that power's upgrade — it " +
             "is also used as the prerequisite if none is set explicitly.")]
    [SerializeField] PowerItem upgradeOf;

    [Tooltip("Free-form value the gameplay side reads — a multiplier, a duration, a count. Kept " +
             "loose on purpose: what a power means is the ability system's business, not the shop's.")]
    [SerializeField] float magnitude = 1f;

    public override ShopCategory Category => ShopCategory.Power;

    public PowerItem UpgradeOf => upgradeOf;
    public float Magnitude => magnitude;

    /// <summary>Owned means unlocked — a power has no stock and no charges.</summary>
    public override bool IsOwned => PlayerInventory.IsOwned(ItemId);

    /// <summary>Reads better at a gameplay call site than IsOwned, and says the same thing.</summary>
    public bool IsUnlocked => IsOwned;

    /// <summary>
    /// How far up its own chain this power sits, counting from 1. Walked rather than authored so a
    /// tier inserted in the middle renumbers everything above it for free.
    /// </summary>
    public int Tier
    {
        get
        {
            int tier = 1;
            // Bounded by a step count, not by trusting the data: a cycle authored by accident would
            // otherwise hang the editor the moment a shop row asked for a tier number.
            for (var below = upgradeOf; below != null && tier < 32; below = below.upgradeOf) tier++;
            return tier;
        }
    }

    /// <summary>
    /// The tier below doubles as the prerequisite when none is set by hand, so a chain only has to
    /// be described once.
    /// </summary>
    public ShopItem EffectivePrerequisite => Prerequisite != null ? Prerequisite : upgradeOf;

    public override void Grant() => PlayerInventory.MarkOwned(ItemId);
    public override void Revoke() => PlayerInventory.ForgetOwned(ItemId);
}
