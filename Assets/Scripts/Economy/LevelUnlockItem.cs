using UnityEngine;

/// <summary>
/// Access to a level. Bought once with gold — the rarest coin gating the content, which is what
/// CoinType.Special was described as being for from the start.
///
/// <see cref="sceneName"/> is the link to MenuController.levelScenes. It is a string rather than a
/// build index because indices renumber the moment a scene is added to the build list, and a save
/// that says "owns level 3" would then unlock a different level than it did yesterday.
/// </summary>
[CreateAssetMenu(menuName = "Bus Runner/Shop/Level Unlock", fileName = "Unlock_")]
public class LevelUnlockItem : ShopItem
{
    [Header("Level")]
    [Tooltip("Scene name, matching an entry in MenuController's levelScenes. A name rather than a " +
             "build index: indices shift whenever the build list changes, and a saved index would " +
             "then point somewhere else.")]
    [SerializeField] string sceneName;

    [Tooltip("Off for a level the player is given rather than sold — the first one, or anything a " +
             "story beat hands over. It still appears in the catalogue and still records ownership; " +
             "it just cannot be bought.")]
    [SerializeField] bool purchasable = true;

    public override ShopCategory Category => ShopCategory.Progression;

    public string SceneName => sceneName;
    public bool Purchasable => purchasable;

    public override bool IsOwned => PlayerInventory.IsOwned(ItemId);

    /// <summary>Reads better where the question is about access rather than about a purchase.</summary>
    public bool IsUnlocked => IsOwned;

    public override void Grant() => PlayerInventory.MarkOwned(ItemId);
    public override void Revoke() => PlayerInventory.ForgetOwned(ItemId);
}
