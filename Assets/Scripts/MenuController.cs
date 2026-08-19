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
    const int LevelButtonFontSize = 44;
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
    /// is then one string in the inspector, and the row count follows UnlockedCount for
    /// free once unlock logic exists.
    /// </summary>
    void BuildLevelList()
    {
        if (levelList == null) return;

        for (int i = levelList.childCount - 1; i >= 0; i--)
            Destroy(levelList.GetChild(i).gameObject);

        var font = BuiltinFont();
        int count = Mathf.Clamp(UnlockedCount, 0, levelScenes.Length);

        for (int i = 0; i < count; i++)
        {
            int index = i;   // captured per row — a shared loop variable would send every button to the last level

            var go = new GameObject(levelScenes[index] + "Button", typeof(RectTransform));
            go.transform.SetParent(levelList, false);

            var image = go.AddComponent<Image>();
            image.color = LevelButtonColor;

            // The parent's VerticalLayoutGroup drives width; height has to come from here.
            go.AddComponent<LayoutElement>().preferredHeight = LevelButtonHeight;

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => LoadLevel(index));

            var labelGo = UiRect.Stretch(go.transform, "Label");

            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.fontSize = LevelButtonFontSize;
            label.alignment = TextAlignmentOptions.Midline;
            label.color = Color.white;
            // Overflow rather than TMP's default Truncate: a level name too long for its row should
            // be visibly wrong instead of vanishing.
            label.overflowMode = TextOverflowModes.Overflow;
            label.text = levelScenes[index].Replace('_', ' ').ToUpperInvariant();
        }
    }

    TMP_FontAsset BuiltinFont() => UiRect.ResolveFont(levelButtonFont, "MenuController");
}
