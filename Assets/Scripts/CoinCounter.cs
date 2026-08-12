using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Reads out coins, one row per type. Used twice, showing different things: the Shop screen reports
/// lifetime totals — the pile unlocks will spend from — while the in-run HUD reports what this run
/// has gathered, which is the number that actually moves while the player is looking at it.
///
/// Rows are built from the settings asset so their labels carry each type's own colour, and so adding
/// a fourth coin type later needs no work here.
/// </summary>
public class CoinCounter : MonoBehaviour
{
    public enum Readout
    {
        /// <summary>Everything ever banked. What the Shop spends from.</summary>
        Total,
        /// <summary>Gathered since the run began. Resets when a level loads, not when the player dies.</summary>
        Session,
    }

    [Tooltip("Total for the menu and shop, Session for the in-level HUD.")]
    [SerializeField] Readout readout = Readout.Total;

    [Tooltip("Container the rows are built into. Wants a layout group to space them.")]
    [SerializeField] RectTransform rows;

    [Tooltip("Supplies each type's colour, so the readout matches the coin the player picked up.")]
    [SerializeField] CoinSettings settings;

    [SerializeField] Font font;
    [SerializeField] int fontSize = 34;
    [SerializeField] float rowHeight = 44f;

    [Tooltip("Off for the compact in-run HUD, on for the Shop screen.")]
    [SerializeField] bool showTypeNames = true;

    Text[] values;

    void OnEnable()
    {
        Build();
        CoinWallet.BalanceChanged += HandleBalanceChanged;
    }

    void OnDisable()
    {
        CoinWallet.BalanceChanged -= HandleBalanceChanged;
    }

    /// <summary>
    /// The event carries the new lifetime total, which is only half of what this can display — so the
    /// value is re-read for whichever readout this instance is. A single Add moves both.
    /// </summary>
    void HandleBalanceChanged(CoinType type, int balance)
    {
        int index = (int)type;
        if (values != null && index < values.Length && values[index] != null)
            values[index].text = Format(type, Amount(type));
    }

    int Amount(CoinType type) =>
        readout == Readout.Session ? CoinWallet.SessionTotal(type) : CoinWallet.Balance(type);

    void Build()
    {
        if (rows == null)
        {
            Debug.LogError($"[CoinCounter] No rows container on '{name}' — run Bus Runner > Set Up Coins.");
            return;
        }

        for (int i = rows.childCount - 1; i >= 0; i--) Destroy(rows.GetChild(i).gameObject);

        var types = CoinWallet.Types;
        values = new Text[types.Length];

        foreach (var type in types)
        {
            var go = UiRect.Stretch(rows, type + "Row");
            go.AddComponent<LayoutElement>().preferredHeight = rowHeight;

            var text = go.AddComponent<Text>();
            text.font = UiRect.ResolveFont(font, "CoinCounter");
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = ColorFor(type);
            text.text = Format(type, Amount(type));

            values[(int)type] = text;
        }
    }

    /// <summary>
    /// A run tally is written with a leading + so it reads as "gained this run" rather than as a
    /// suspiciously small lifetime total.
    /// </summary>
    string Format(CoinType type, int amount)
    {
        string number = readout == Readout.Session ? "+" + amount : amount.ToString();
        return showTypeNames ? $"{Label(type)}  {number}" : number;
    }

    static string Label(CoinType type) => type switch
    {
        CoinType.Permanent => "Permanent",
        CoinType.Respawnable => "Coins",
        CoinType.Special => "Special",
        _ => type.ToString(),
    };

    Color ColorFor(CoinType type)
    {
        var style = settings != null ? settings.For(type) : null;
        return style != null ? style.baseColor : Color.white;
    }
}
