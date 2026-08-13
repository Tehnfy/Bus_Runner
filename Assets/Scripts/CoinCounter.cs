using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Reads out coins, one row per type. Used three times, showing different things: the Menu and Shop
/// screens report lifetime totals — the pile unlocks will spend from — while the in-run HUD reports
/// what this run has gathered, which is the number that actually moves while the player is looking
/// at it.
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

    [SerializeField] TMP_FontAsset font;
    [SerializeField] int fontSize = 34;
    [SerializeField] float rowHeight = 44f;

    [Tooltip("Off for the compact in-run HUD, on for the Shop screen.")]
    [SerializeField] bool showTypeNames = true;

    [Tooltip("Where the rows sit inside their own width. Left for a corner HUD, Center for a panel.")]
    [SerializeField] TextAnchor alignment = TextAnchor.MiddleLeft;

    [Header("Change feedback")]
    [Tooltip("Flash and swell a row when its number goes up, so a pickup registers even when the " +
             "player's eyes are on the road rather than the corner of the screen.")]
    [SerializeField] bool pulseOnGain = true;
    [Tooltip("How far the row swells at the peak of the flash. 1 is no swell.")]
    [SerializeField] float pulseScale = 1.35f;
    [Tooltip("Seconds from the peak back to rest.")]
    [SerializeField] float pulseTime = 0.22f;

    [Tooltip("Keep a row hidden until the player has actually earned one. Suits the run tally — the " +
             "HUD stays quiet, and a row arriving is itself the signal that something was picked up. " +
             "Wrong for the Shop, where a zero balance is information.")]
    [SerializeField] bool hideUntilEarned;

    TMP_Text[] values;
    RectTransform[] rects;

    // What each row currently displays, so a change can be told from a redraw. The event fires for
    // every type on BeginSession, and pulsing three rows at the start of every run would train the
    // player to ignore the flash.
    int[] shown;

    Coroutine[] pulses;

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
        if (values == null || index >= values.Length || values[index] == null) return;

        int amount = Amount(type);
        bool gained = amount > shown[index];

        shown[index] = amount;
        values[index].text = Format(type, amount);

        if (hideUntilEarned) values[index].gameObject.SetActive(amount > 0);
        if (gained && pulseOnGain && isActiveAndEnabled) Pulse(index, type);
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
        values = new TMP_Text[types.Length];
        rects = new RectTransform[types.Length];
        shown = new int[types.Length];
        pulses = new Coroutine[types.Length];

        foreach (var type in types)
        {
            var go = UiRect.Stretch(rows, type + "Row");

            // Height set on the rect itself, not left to the LayoutElement.
            //
            // A stretched rect has a zero sizeDelta, and a vertical layout group forces its children's
            // anchors to a corner — so the row's height becomes that zero. The LayoutElement does not
            // save it: a group only reads preferredHeight when childControlHeight is on, and the two
            // containers this is used in leave it off so a row keeps the height asked for here.
            // Measured before the fix: every row came out 537.6 x 0.0 with nothing drawn at all.
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, rowHeight);

            // Honoured if someone does turn childControlHeight on, so the row does not then collapse
            // to whatever the group decides a label wants.
            var element = go.AddComponent<LayoutElement>();
            element.preferredHeight = rowHeight;
            element.minHeight = rowHeight;

            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = UiRect.ResolveFont(font, "CoinCounter");
            text.fontSize = fontSize;
            text.alignment = UiRect.Align(alignment);
            text.color = ColorFor(type);

            // One line, drawn even if the row is a few pixels short. TMP's default Truncate is how a
            // row that does not quite fit ends up rendering nothing without saying so.
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;

            int amount = Amount(type);
            text.text = Format(type, amount);

            int index = (int)type;
            values[index] = text;
            rects[index] = (RectTransform)go.transform;
            shown[index] = amount;

            if (hideUntilEarned) go.SetActive(amount > 0);
        }
    }

    /// <summary>
    /// Swells the row and flashes it white, then eases both back. Runs on the row rather than the whole
    /// readout so two coins taken together each get their own flash.
    /// </summary>
    void Pulse(int index, CoinType type)
    {
        if (rects[index] == null) return;

        if (pulses[index] != null) StopCoroutine(pulses[index]);
        pulses[index] = StartCoroutine(PulseRow(index, type));
    }

    IEnumerator PulseRow(int index, CoinType type)
    {
        var rect = rects[index];
        var text = values[index];
        var resting = ColorFor(type);

        // Unscaled: a pickup during the finish sequence, or any future slow-motion, should still read
        // at normal speed. Nothing about this animation is part of the simulation.
        for (float t = 0f; t < pulseTime; t += Time.unscaledDeltaTime)
        {
            float k = 1f - t / pulseTime;                       // 1 at the peak, 0 at rest
            if (rect != null) rect.localScale = Vector3.one * Mathf.Lerp(1f, pulseScale, k);
            if (text != null) text.color = Color.Lerp(resting, Color.white, k);
            yield return null;
        }

        if (rect != null) rect.localScale = Vector3.one;
        if (text != null) text.color = resting;
        pulses[index] = null;
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
