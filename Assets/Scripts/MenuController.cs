using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Start menu. Three panels — main, level select, options — swapped in place, plus the
/// scene loads behind them. Menu is build index 0, so without this there is no way
/// into a level at all.
///
/// Level unlocking is not built yet. <see cref="UnlockedCount"/> is the single seam
/// where it will go: today every entry in levelScenes counts as unlocked, so Play
/// starts the last one and the level list shows them all.
/// </summary>
public class MenuController : MonoBehaviour
{
    [Header("Levels")]
    [Tooltip("In play order. Play starts the furthest unlocked entry.")]
    [SerializeField] string[] levelScenes = { "Level_1" };

    [Header("Panels")]
    [SerializeField] GameObject mainPanel;
    [SerializeField] GameObject levelSelectPanel;
    [SerializeField] GameObject optionsPanel;
    [Tooltip("Reached from Options, and its Back returns there rather than to the main menu.")]
    [SerializeField] GameObject controlsPanel;
    [Tooltip("Unlocks, once they exist. Carries an in-development notice and the coin balances for now.")]
    [SerializeField] GameObject shopPanel;
    [Tooltip("Coin save tools for testing. Hides itself, and the button that opens it, when its own " +
             "dev-controls switch is off.")]
    [SerializeField] GameObject devPanel;

    [Tooltip("One button per unlocked level is built into this at startup.")]
    [SerializeField] RectTransform levelList;

    [Tooltip("Font asset for the generated level buttons. Wired to the same one the hand-placed " +
             "captions use; falls back to the project's TMP default if left empty.")]
    [SerializeField] TMP_FontAsset levelButtonFont;

    // Matches the hand-placed buttons, so a generated row does not look bolted on.
    static readonly Color LevelButtonColor = new Color(0.2f, 0.55f, 0.85f, 0.9f);

    // Drained of the blue rather than merely dimmed. A locked row has to read as a different kind of
    // thing at a glance, not as the same button rendered badly.
    static readonly Color LockedButtonColor = new Color(0.22f, 0.24f, 0.28f, 0.9f);
    static readonly Color LockedLabelColor = new Color(0.66f, 0.68f, 0.72f, 1f);

    const int LevelButtonFontSize = 44;
    const int LockedPriceFontSize = 26;
    const float LevelButtonHeight = 96f;

    [Tooltip("Everything for sale, and what the player owns of it. Leave empty and every level is " +
             "open — which is what a project with no unlocks authored yet wants.")]
    [SerializeField] ShopCatalog catalog;

    /// <summary>
    /// How far along levelScenes the player may start.
    ///
    /// A prefix count rather than a set, because that is what both callers want: Play starts the
    /// furthest entry, and the level list shows the first this many. A level bought out of order
    /// therefore does not open the ones before it — buy the gate, not the destination.
    ///
    /// A scene the catalogue does not gate counts as open. That default is what keeps a project
    /// with no unlocks authored yet behaving exactly as it did before any of this existed.
    /// </summary>
    public int UnlockedCount
    {
        get
        {
            if (catalog == null) return levelScenes.Length;

            int open = 0;
            foreach (var scene in levelScenes)
            {
                if (!IsUnlocked(scene)) break;
                open++;
            }

            // Never zero. A player who owns nothing still has to be able to start the game, and the
            // alternative is a menu with no way into it — see the first entry being ungated.
            return Mathf.Max(1, open);
        }
    }

    /// <summary>Whether this level may be started: either not gated at all, or gated and bought.</summary>
    public bool IsUnlocked(string sceneName)
    {
        if (catalog == null || !catalog.IsGated(sceneName)) return true;
        return catalog.UnlockedScenes().Contains(sceneName);
    }

    // Subscribed for the whole life of the menu, not just while the level select is showing: the
    // purchase happens on the shop panel, which means the level list is switched off at the moment
    // the thing that changes it lands.
    //
    // OwnedChanged rather than ShopService.Purchased, and only that one. A level unlock grants
    // itself through PlayerInventory.MarkOwned, so this fires for a purchase — and it also fires for
    // the dev panel's wipe, which is a revoke and raises no Purchased event at all. Listening to
    // both would rebuild the list twice for every purchase and still miss nothing extra.
    void OnEnable() => PlayerInventory.OwnedChanged += HandleOwnedChanged;
    void OnDisable() => PlayerInventory.OwnedChanged -= HandleOwnedChanged;

    void HandleOwnedChanged(string itemId, bool owned) => BuildLevelList();

    void Start()
    {
        // A run left through the pause menu already restores this, but arriving here
        // any other way must not leave the menu sitting on a frozen clock.
        Time.timeScale = 1f;

        ApplyDevAvailability();
        BuildLevelList();
        ShowMain();
    }

    /// <summary>
    /// Lets the dev panel decide whether it exists in this build, and forgets it if not — so Show can
    /// never bring back a panel that just switched itself off.
    /// </summary>
    void ApplyDevAvailability()
    {
        if (devPanel == null) return;

        var dev = devPanel.GetComponent<CoinDevPanel>();
        if (dev != null && !dev.ApplyAvailability()) devPanel = null;
    }

    public void ShowMain() => Show(mainPanel);
    public void ShowLevelSelect() => Show(levelSelectPanel);
    public void ShowOptions() => Show(optionsPanel);
    public void ShowControls() => Show(controlsPanel);
    public void ShowShop() => Show(shopPanel);
    /// <summary>Guarded: with the controls switched off devPanel is null, and Show(null) would leave
    /// the menu on no panel at all.</summary>
    public void ShowDev()
    {
        if (devPanel != null) Show(devPanel);
    }

    /// <summary>Starts the furthest level the player has unlocked.</summary>
    public void PlayLevel()
    {
        if (levelScenes.Length == 0)
        {
            Debug.LogError("[MenuController] No level scenes configured — nothing to play.");
            return;
        }
        LoadLevel(Mathf.Clamp(UnlockedCount, 1, levelScenes.Length) - 1);
    }

    public void LoadLevel(int index)
    {
        if (index < 0 || index >= levelScenes.Length)
        {
            Debug.LogError($"[MenuController] Level index {index} is outside levelScenes (length {levelScenes.Length}).");
            return;
        }
        // Checked here rather than only where the buttons are built. This is public and reachable
        // from a button someone wires by hand, and a locked level loading anyway would make the gate
        // decorative.
        if (!IsUnlocked(levelScenes[index]))
        {
            Debug.LogWarning($"[MenuController] '{levelScenes[index]}' is locked — not loading it.");
            return;
        }

        Time.timeScale = 1f;
        SceneManager.LoadScene(levelScenes[index], LoadSceneMode.Single);
    }

    /// <summary>
    /// Closes the game.
    ///
    /// Application.Quit does nothing in the editor and nothing in a play-mode test, so play mode is
    /// stopped explicitly instead — otherwise the button reads as broken every time it is tried from
    /// the editor, which is the only place it gets tried during development.
    ///
    /// Nothing is flushed here on purpose: CoinWalletFlusher writes the wallet from
    /// OnApplicationQuit, and that fires for both branches below. Saving again from here would
    /// duplicate the write and put the knowledge of what needs persisting in two places.
    /// </summary>
    public void QuitGame()
    {
        // Logged before either branch runs. In the editor the next line ends play mode, and in a
        // build Application.Quit tears the process down — so anything logged afterwards would be
        // written during shutdown, when it may never reach the console at all.
        Debug.Log("[MenuController] Exit pressed — quitting.");

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void Show(GameObject panel)
    {
        // Every panel is addressed, not just the incoming one — otherwise whichever was
        // last left on stays stacked underneath.
        if (mainPanel != null) mainPanel.SetActive(panel == mainPanel);
        if (levelSelectPanel != null) levelSelectPanel.SetActive(panel == levelSelectPanel);
        if (optionsPanel != null) optionsPanel.SetActive(panel == optionsPanel);
        if (controlsPanel != null) controlsPanel.SetActive(panel == controlsPanel);
        if (shopPanel != null) shopPanel.SetActive(panel == shopPanel);
        if (devPanel != null) devPanel.SetActive(panel == devPanel);
    }

    /// <summary>
    /// Builds the level list in code rather than from hand-placed buttons: adding a level
    /// is then one string in the inspector, and the row count follows the level list for free.
    ///
    /// Every level gets a row, locked ones included. Hiding a locked level was what this did while
    /// nothing could be unlocked, and it is the wrong answer now something can: a player cannot want
    /// to buy a level they have never been shown. A locked row is drained of colour, says what it
    /// costs, and opens the shop instead of the level.
    /// </summary>
    void BuildLevelList()
    {
        if (levelList == null) return;

        for (int i = levelList.childCount - 1; i >= 0; i--)
        {
            var stale = levelList.GetChild(i);
            // Unparented before it is destroyed. Destroy only takes effect at the end of the frame,
            // so on a rebuild the old rows would otherwise still be in the layout group alongside the
            // new ones for a frame — a visible double list every time a level is bought.
            stale.SetParent(null, false);
            Destroy(stale.gameObject);
        }

        var font = BuiltinFont();
        int open = Mathf.Clamp(UnlockedCount, 0, levelScenes.Length);

        for (int i = 0; i < levelScenes.Length; i++)
        {
            int index = i;   // captured per row — a shared loop variable would send every button to the last level

            // The prefix rule, not IsUnlocked on its own: buying level 3 while 2 is still locked
            // must not make 3 startable, or the gate on 2 stops meaning anything. UnlockedCount is
            // where that rule lives, and this is the same count the Play button obeys.
            bool unlocked = index < open;

            var go = new GameObject(levelScenes[index] + "Button", typeof(RectTransform));
            go.transform.SetParent(levelList, false);

            var image = go.AddComponent<Image>();
            image.color = unlocked ? LevelButtonColor : LockedButtonColor;

            // The parent's VerticalLayoutGroup drives width; height has to come from here.
            go.AddComponent<LayoutElement>().preferredHeight = LevelButtonHeight;

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;

            // Left interactable while locked, on purpose. A dead button tells the player nothing
            // about how to open the level; this one takes them to the thing that sells it.
            if (unlocked) button.onClick.AddListener(() => LoadLevel(index));
            else button.onClick.AddListener(ShowShop);

            var label = MakeLevelLabel(go.transform, "Label", font, LevelButtonFontSize,
                                       unlocked ? Color.white : LockedLabelColor);
            label.text = levelScenes[index].Replace('_', ' ').ToUpperInvariant();

            if (unlocked) continue;

            // The name lifts to make room for the price underneath, rather than the two labels
            // sharing a centre line and overlapping.
            var labelRect = (RectTransform)label.transform;
            labelRect.anchorMin = new Vector2(0f, 0.34f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            var price = MakeLevelLabel(go.transform, "Price", font, LockedPriceFontSize, LockedLabelColor);
            var priceRect = (RectTransform)price.transform;
            priceRect.anchorMin = new Vector2(0f, 0.04f);
            priceRect.anchorMax = new Vector2(1f, 0.34f);
            priceRect.offsetMin = Vector2.zero;
            priceRect.offsetMax = Vector2.zero;
            price.text = LockedPriceText(levelScenes[index]);
        }
    }

    /// <summary>
    /// "LOCKED — 2 GOLD + 10 SILVER", or plain "LOCKED" when nothing in the catalogue prices this
    /// scene. The price comes from ShopService so the level select and the shop never disagree about
    /// what something costs.
    /// </summary>
    string LockedPriceText(string sceneName)
    {
        var unlock = catalog == null ? null : catalog.UnlockFor(sceneName);
        if (unlock == null || !unlock.Purchasable) return "LOCKED";

        return ("LOCKED  —  " + ShopService.PriceText(unlock)).ToUpperInvariant();
    }

    TMP_Text MakeLevelLabel(Transform parent, string name, TMP_FontAsset font, int size, Color color)
    {
        var go = UiRect.Stretch(parent, name);

        var label = go.AddComponent<TextMeshProUGUI>();
        label.font = font;
        label.fontSize = size;
        label.alignment = TextAlignmentOptions.Midline;
        label.color = color;
        // Overflow rather than TMP's default Truncate: a level name too long for its row should
        // be visibly wrong instead of vanishing.
        label.overflowMode = TextOverflowModes.Overflow;
        // Nothing here is clickable in its own right — the row's button is. A label that swallowed
        // the raycast would leave a dead patch across the middle of the button.
        label.raycastTarget = false;
        return label;
    }

    TMP_FontAsset BuiltinFont() => UiRect.ResolveFont(levelButtonFont, "MenuController");
}
