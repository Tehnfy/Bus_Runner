using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Draws the catalogue: one row per item, with its price on the button and the reason it cannot be
/// bought underneath when that applies.
///
/// Rows are built at runtime rather than placed by hand, for the same reason MenuController builds
/// the level list that way — adding an item is then one asset dragged into the catalogue, and the
/// row count follows the catalogue for free.
///
/// Every label is asked to redraw whenever a balance moves or a purchase lands, so buying the last
/// affordable thing greys out everything else on the same frame rather than at the next visit.
/// </summary>
public class ShopScreen : MonoBehaviour
{
    [Tooltip("What is for sale. Empty and the screen says so rather than drawing nothing.")]
    [SerializeField] ShopCatalog catalog;

    [Tooltip("Container the rows are built into. Wants a VerticalLayoutGroup to space them.")]
    [SerializeField] RectTransform rows;

    [Tooltip("Shown in place of the rows when the catalogue is empty or missing.")]
    [SerializeField] TMP_Text emptyNotice;

    [SerializeField] TMP_FontAsset font;
    [SerializeField] int nameFontSize = 30;
    [SerializeField] int detailFontSize = 22;
    [SerializeField] float rowHeight = 104f;

    [Header("Section headings")]
    [SerializeField] int headerFontSize = 24;
    [SerializeField] float headerHeight = 48f;

    static readonly Color RowColor = new Color(0.16f, 0.18f, 0.23f, 0.92f);
    static readonly Color BuyColor = new Color(0.2f, 0.55f, 0.85f, 0.95f);
    static readonly Color OwnedColor = new Color(0.28f, 0.38f, 0.34f, 0.95f);

    // Equipped has to be tellable from owned at a glance — they are different states with the same
    // row, and the button label alone is a small target to read.
    static readonly Color EquippedColor = new Color(0.25f, 0.62f, 0.42f, 0.95f);
    static readonly Color EquipColor = new Color(0.42f, 0.4f, 0.62f, 0.95f);

    static readonly Color DetailColor = new Color(0.72f, 0.75f, 0.8f, 1f);
    static readonly Color RefusedColor = new Color(0.95f, 0.75f, 0.25f, 1f);
    static readonly Color HeaderColor = new Color(0.62f, 0.68f, 0.78f, 1f);

    ShopItem[] items;
    Button[] buyButtons;
    Image[] buyImages;
    TMP_Text[] buyLabels;
    TMP_Text[] nameLabels;
    TMP_Text[] detailLabels;
    bool built;

    // OnEnable, not Awake: this panel is inactive in the scene until the menu shows it, and an
    // inactive object gets neither Awake nor Start.
    void OnEnable()
    {
        // The rows are runtime UI. Building them in the editor would serialise generated objects into
        // the scene, and Destroy is not even legal there.
        if (!Application.isPlaying) return;

        if (!built) Build();
        Refresh();

        CoinWallet.BalanceChanged += HandleBalanceChanged;
        ShopService.Purchased += HandlePurchased;
        PowerLoadout.EquippedChanged += HandleEquippedChanged;
    }

    void OnDisable()
    {
        CoinWallet.BalanceChanged -= HandleBalanceChanged;
        ShopService.Purchased -= HandlePurchased;
        PowerLoadout.EquippedChanged -= HandleEquippedChanged;
    }

    void HandleBalanceChanged(CoinType type, int balance) => Refresh();
    void HandlePurchased(ShopItem item) => Refresh();

    /// <summary>
    /// Equipping one power can unequip another, so the whole list redraws rather than the row that
    /// was pressed. At one slot every equip displaces something, and a stale "Equipped" left on the
    /// row that just lost its slot is the obvious way to get this wrong.
    /// </summary>
    void HandleEquippedChanged(string itemId, bool equipped) => Refresh();

    void Build()
    {
        built = true;

        if (rows == null)
        {
            Debug.LogError($"[ShopScreen] No rows container on '{name}' — run Bus Runner > Set Up Start Menu.");
            return;
        }

        for (int i = rows.childCount - 1; i >= 0; i--) Destroy(rows.GetChild(i).gameObject);

        // Grouped up front rather than drawn straight down the catalogue, so a section whose items
        // are all missing can be skipped entirely — an empty "Powers" heading reads as a bug.
        var sections = new List<KeyValuePair<ShopCategory, List<ShopItem>>>();
        int count = 0;

        if (catalog != null)
        {
            foreach (var category in CurrencyRules.DisplayOrder)
            {
                var found = catalog.ItemsIn(category);
                if (found.Count == 0) continue;

                sections.Add(new KeyValuePair<ShopCategory, List<ShopItem>>(category, found));
                count += found.Count;
            }
        }

        if (emptyNotice != null) emptyNotice.gameObject.SetActive(count == 0);
        if (count == 0) return;

        items = new ShopItem[count];
        buyButtons = new Button[count];
        buyImages = new Image[count];
        buyLabels = new TMP_Text[count];
        nameLabels = new TMP_Text[count];
        detailLabels = new TMP_Text[count];

        var resolved = UiRect.ResolveFont(font, "ShopScreen");

        // Headings are plain children of the same layout group, not tracked in the arrays above —
        // nothing ever redraws them, so the per-item indexing stays one-to-one with `items`.
        int index = 0;
        foreach (var section in sections)
        {
            BuildHeader(section.Key, resolved);

            foreach (var item in section.Value)
            {
                items[index] = item;
                BuildRow(index, item, resolved);
                index++;
            }
        }
    }

    void BuildHeader(ShopCategory category, TMP_FontAsset resolved)
    {
        var go = UiRect.Stretch(rows, category + "Header");

        // Same height-on-both trap the item rows document: a stretched rect in a vertical layout
        // group comes out zero high without the LayoutElement as well as the sizeDelta.
        var rect = (RectTransform)go.transform;
        rect.sizeDelta = new Vector2(rect.sizeDelta.x, headerHeight);

        var element = go.AddComponent<LayoutElement>();
        element.preferredHeight = headerHeight;
        element.minHeight = headerHeight;

        var label = MakeLabel(go.transform, "Title", new Vector2(0.03f, 0f), new Vector2(0.97f, 1f),
            headerFontSize, HeaderColor, resolved);
        label.text = CurrencyRules.DisplayName(category).ToUpperInvariant();
    }

    void BuildRow(int index, ShopItem item, TMP_FontAsset resolved)
    {
        var row = UiRect.Stretch(rows, item.ItemId + "Row");

        // Height on the rect as well as the LayoutElement. A stretched rect has a zero sizeDelta and a
        // vertical layout group forces its children's anchors to a corner, so without this the row
        // comes out full width and zero high with nothing drawn — the exact failure the coin HUD had.
        var rect = (RectTransform)row.transform;
        rect.sizeDelta = new Vector2(rect.sizeDelta.x, rowHeight);

        var element = row.AddComponent<LayoutElement>();
        element.preferredHeight = rowHeight;
        element.minHeight = rowHeight;

        var background = row.AddComponent<Image>();
        background.color = RowColor;

        nameLabels[index] = MakeLabel(row.transform, "Name", new Vector2(0.03f, 0.48f),
            new Vector2(0.62f, 0.92f), nameFontSize, Color.white, resolved);
        detailLabels[index] = MakeLabel(row.transform, "Detail", new Vector2(0.03f, 0.08f),
            new Vector2(0.62f, 0.46f), detailFontSize, DetailColor, resolved);

        var buttonGo = UiRect.Stretch(row.transform, "BuyButton");
        var buttonRect = (RectTransform)buttonGo.transform;
        buttonRect.anchorMin = new Vector2(0.66f, 0.16f);
        buttonRect.anchorMax = new Vector2(0.97f, 0.84f);
        buttonRect.offsetMin = Vector2.zero;
        buttonRect.offsetMax = Vector2.zero;

        var buttonImage = buttonGo.AddComponent<Image>();
        buttonImage.color = BuyColor;

        var button = buttonGo.AddComponent<Button>();
        button.targetGraphic = buttonImage;

        // Same placeholder press flash the hand-built menu controls get. Added here because these
        // rows never pass through UiBuild — they are made at runtime from the catalogue.
        buttonGo.AddComponent<PressBorder>();

        // Captured per row. A shared loop variable would send every button at the last item — the same
        // trap BuildLevelList documents.
        var captured = item;
        button.onClick.AddListener(() => Press(captured));

        buyButtons[index] = button;
        buyImages[index] = buttonImage;
        buyLabels[index] = MakeLabel(buttonGo.transform, "Label", Vector2.zero, Vector2.one,
            detailFontSize + 2, Color.white, resolved);

        // The buy label is the one that has to hold an unknown amount of text: a single-currency
        // price is "2 Gold", but a two-currency one is "2 Gold + 10 Silver" in the same narrow
        // button. Auto-sizing shrinks it to fit rather than letting it run off the edge — the rest of
        // the labels keep a fixed size, because their boxes are wide enough not to need it.
        var buyLabel = buyLabels[index];
        buyLabel.enableAutoSizing = true;
        buyLabel.fontSizeMin = 14f;
        buyLabel.fontSizeMax = detailFontSize + 2;
    }

    TMP_Text MakeLabel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
                       int size, Color color, TMP_FontAsset resolved)
    {
        var go = UiRect.Stretch(parent, name);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var label = go.AddComponent<TextMeshProUGUI>();
        label.font = resolved;
        label.fontSize = size;
        label.color = color;
        label.alignment = name == "Label" ? TextAlignmentOptions.Midline : TextAlignmentOptions.MidlineLeft;
        // Overflow rather than TMP's default Truncate: a name too long for its box should look wrong
        // rather than vanish.
        label.overflowMode = TextOverflowModes.Overflow;
        label.raycastTarget = false;
        return label;
    }

    /// <summary>
    /// One button, two jobs. A power the player already owns cannot be bought again, so its button
    /// stops being a till and becomes the equip toggle — which is what makes owning and equipping
    /// one row instead of a separate equipment screen.
    /// </summary>
    void Press(ShopItem item)
    {
        if (item is PowerItem power && power.IsOwned)
        {
            ToggleEquip(power);
            return;
        }

        var verdict = ShopService.TryBuy(item);

        // Refresh happens through the Purchased event on success. A refusal raises nothing, so the
        // reason is written here — otherwise a failed press would look like the button did nothing.
        if (verdict != PurchaseResult.Purchased) Refresh(item, verdict);
    }

    void ToggleEquip(PowerItem power)
    {
        if (PowerLoadout.IsEquipped(power.ItemId))
            PowerLoadout.Unequip(power.ItemId);
        else
            PowerLoadout.Equip(power.ItemId, catalog != null ? catalog.PowerSlots : 1);

        // Written straight away rather than left to the app-quit flush. Equipping is the last thing a
        // player does before pressing Play, so the run that follows must not be able to start with
        // the old loadout still on disk.
        PowerLoadout.Flush();
    }

    /// <summary>Redraws every row against the current balances, inventory and loadout.</summary>
    public void Refresh()
    {
        if (items == null) return;

        // A power revoked since it was equipped — by the dev wipe, or a save edited by hand — would
        // otherwise keep granting its ability. Dropped here, where the shop already has the catalogue
        // needed to answer whether an id is still owned.
        PowerLoadout.DropUnowned(id =>
        {
            var found = catalog != null ? catalog.Find(id) : null;
            // An id the catalogue does not know is left alone: an item pulled from the shop is not
            // evidence that the save is wrong.
            return found == null || found.IsOwned;
        });

        for (int i = 0; i < items.Length; i++)
            if (items[i] != null) Draw(i, ShopService.CanBuy(items[i]));
    }

    /// <summary>Redraws one row with a verdict already in hand, so a refusal reports its own reason.</summary>
    void Refresh(ShopItem item, PurchaseResult verdict)
    {
        if (items == null) return;
        for (int i = 0; i < items.Length; i++)
            if (items[i] == item) { Draw(i, verdict); return; }
    }

    void Draw(int index, PurchaseResult verdict)
    {
        var item = items[index];
        bool buyable = verdict == PurchaseResult.Purchased;

        // An owned power is the one row that is neither buyable nor finished with — it still has a
        // decision left on it, which is what the rest of this method branches on.
        bool ownedPower = item is PowerItem && item.IsOwned;
        bool equipped = ownedPower && PowerLoadout.IsEquipped(item.ItemId);

        // A consumable's stock belongs next to its name — it is the number that changes as you buy,
        // and the only way to tell a fifth revive from a first.
        string suffix = item is ConsumableItem consumable && consumable.Stock > 0
            ? $"   x{consumable.Stock}"
            : string.Empty;
        nameLabels[index].text = item.DisplayName + suffix;

        if (ownedPower)
        {
            // The description has done its job by the time it is bought; what the player needs now is
            // which state the power is in and that the button will change it.
            detailLabels[index].text = equipped ? "Equipped — tap to remove" : "Owned — tap to equip";
            detailLabels[index].color = DetailColor;

            buyLabels[index].text = equipped ? "Equipped" : "Equip";
            buyButtons[index].interactable = true;
            buyImages[index].color = equipped ? EquippedColor : EquipColor;
            return;
        }

        // The description while it can be bought, the reason once it cannot. A row that is refused is
        // asking a question the description does not answer.
        detailLabels[index].text = buyable ? item.Description : ShopService.Explain(item, verdict);
        detailLabels[index].color = buyable ? DetailColor : RefusedColor;

        buyLabels[index].text = ShopService.PriceText(item);
        buyButtons[index].interactable = buyable;
        buyImages[index].color = verdict == PurchaseResult.AlreadyOwned ? OwnedColor : BuyColor;
    }
}
