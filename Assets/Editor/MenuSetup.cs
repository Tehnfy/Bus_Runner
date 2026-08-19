using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One-shot wiring for the start menu. Menu: Bus Runner &gt; Set Up Start Menu.
///
/// Builds, in Menu.unity, under the existing MenuUI canvas:
///   MainPanel         Play / Level Select / Options
///   LevelSelectPanel  header, LevelList (filled at runtime), Back
///   OptionsPanel      header, the planned settings as placeholder text, Back
///
/// and wires MenuController's panel references. The existing PlayButton is reused and
/// moved into MainPanel rather than replaced, so its label styling survives.
///
/// Safe to run more than once — it reuses whatever it already made.
/// </summary>
static class MenuSetup
{
    const string ScenePath = "Assets/Scenes/Menu.unity";
    const string CanvasName = "MenuUI";

    static readonly Color ActionColor = new Color(0.2f, 0.55f, 0.85f, 0.9f);
    static readonly Color BackColor = new Color(0.32f, 0.34f, 0.38f, 0.9f);
    static readonly Color MutedText = new Color(0.72f, 0.75f, 0.8f, 1f);
    static readonly Color WarnText = new Color(0.95f, 0.75f, 0.25f, 1f);
    // Set apart from the blue action buttons, so the shop reads as its own thing rather than
    // another way into a level.
    static readonly Color ShopColor = new Color(0.55f, 0.32f, 0.78f, 0.92f);

    // Dev tools read as tools, not as menu options — nothing here should look like something a player
    // is meant to press.
    static readonly Color DevColor = new Color(0.28f, 0.38f, 0.34f, 0.95f);
    static readonly Color DevWipeColor = new Color(0.52f, 0.24f, 0.22f, 0.95f);

    const string CoinSettingsPath = "Assets/Settings/CoinSettings.asset";
    const string CatalogPath = "Assets/Shop/ShopCatalog.asset";
    const string ExitIconPath = "Assets/Images/poower.png";

    // ControlsPanel overwrites the window colour as soon as a rebind starts; these are what the
    // scene shows at rest.
    static readonly Color PromptBackdrop = new Color(0.02f, 0.03f, 0.05f, 0.82f);
    static readonly Color PromptWindow = new Color(0.09f, 0.11f, 0.15f, 0.98f);

    // The Title already occupies y 0.60-0.85, so every panel's own content sits below it.
    static readonly Vector2 BackMin = new Vector2(0.40f, 0.08f);
    static readonly Vector2 BackMax = new Vector2(0.60f, 0.18f);
    static readonly Vector2 HeaderMin = new Vector2(0.15f, 0.50f);
    static readonly Vector2 HeaderMax = new Vector2(0.85f, 0.58f);

    // Same y band as the main panel's coin strip on the right, so the two top corners line up.
    static readonly Vector2 ExitMin = new Vector2(0.02f, 0.87f);
    static readonly Vector2 ExitMax = new Vector2(0.20f, 0.99f);

    // Fractions of the Exit button. At the size above this leaves the icon slot very close to
    // square on a 16:9 canvas, which is the shape an icon is usually drawn to.
    const float IconWidth = 0.30f;
    const float IconPad = 0.05f;
    const int ExitFontSize = 32;

    [MenuItem("Bus Runner/Set Up Start Menu")]
    static void Run()
    {
        if (!UiBuild.OpenTargetScene(ScenePath, "MenuSetup")) return;

        var canvas = UiBuild.FindRoot(CanvasName);
        var controllerGo = UiBuild.FindRoot("MenuController");
        if (canvas == null || controllerGo == null)
        {
            Debug.LogError($"[MenuSetup] {ScenePath} is missing '{CanvasName}' or 'MenuController'.");
            return;
        }

        var controller = controllerGo.GetComponent<MenuController>();
        if (controller == null)
        {
            Debug.LogError("[MenuSetup] The MenuController object has no MenuController component.");
            return;
        }

        Undo.SetCurrentGroupName("Set Up Start Menu");
        int group = Undo.GetCurrentGroup();

        var main = BuildMainPanel(canvas.transform, controller);
        var levelSelect = BuildLevelSelectPanel(canvas.transform, controller, out var levelList);
        var options = BuildOptionsPanel(canvas.transform, controller, out var devButton);
        var controls = BuildControlsPanel(canvas.transform, controller);
        var shop = BuildShopPanel(canvas.transform, controller);
        var dev = BuildDevPanel(canvas.transform, controller, devButton);

        UiBuild.SetRef(controller, "mainPanel", main);
        UiBuild.SetRef(controller, "levelSelectPanel", levelSelect);
        UiBuild.SetRef(controller, "optionsPanel", options);
        UiBuild.SetRef(controller, "controlsPanel", controls);
        UiBuild.SetRef(controller, "shopPanel", shop);
        UiBuild.SetRef(controller, "devPanel", dev);
        UiBuild.SetRef(controller, "levelList", levelList);
        UiBuild.SetRef(controller, "levelButtonFont", UiBuild.BuiltinFont());
        // Re-wired every run so a re-setup cannot silently drop the gate and leave every level open.
        UiBuild.SetRef(controller, "catalog", LoadCatalog());

        // What the designer sees when the scene opens. MenuController.Start repeats this.
        main.SetActive(true);
        levelSelect.SetActive(false);
        options.SetActive(false);
        controls.SetActive(false);
        shop.SetActive(false);
        dev.SetActive(false);

        WarnUnclickable(canvas.transform);

        Undo.CollapseUndoOperations(group);
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[MenuSetup] Done — MainPanel, LevelSelectPanel and OptionsPanel wired, scene saved.");
    }

    static GameObject BuildMainPanel(Transform canvas, MenuController controller)
    {
        var panel = UiBuild.Child(canvas, "MainPanel");
        UiBuild.Place(panel, Vector2.zero, Vector2.one);

        // The scene already has a PlayButton with its own label styling; move it in rather
        // than building a second one beside it.
        var play = UiBuild.FindDeep(canvas, "PlayButton");
        if (play != null && play.parent != panel.transform)
            Undo.SetTransformParent(play, panel.transform, "Move PlayButton into MainPanel");

        var playButton = play != null
            ? Configure(play.gameObject, new Vector2(0.35f, 0.38f), new Vector2(0.65f, 0.50f))
            : UiBuild.MakeButton(panel.transform, "PlayButton", "PLAY",
                new Vector2(0.35f, 0.42f), new Vector2(0.65f, 0.54f), ActionColor, 56);

        var shopButton = UiBuild.MakeButton(panel.transform, "ShopButton", "SHOP",
            new Vector2(0.35f, 0.30f), new Vector2(0.65f, 0.40f), ShopColor, 44);
        var levelSelectButton = UiBuild.MakeButton(panel.transform, "LevelSelectButton", "LEVEL SELECT",
            new Vector2(0.35f, 0.18f), new Vector2(0.65f, 0.28f), ActionColor, 44);
        var optionsButton = UiBuild.MakeButton(panel.transform, "OptionsButton", "OPTIONS",
            new Vector2(0.35f, 0.06f), new Vector2(0.65f, 0.16f), ActionColor, 44);

        BuildExitButton(panel.transform, controller);

        UiBuild.Bind(playButton, controller.PlayLevel);
        UiBuild.Bind(shopButton, controller.ShowShop);
        UiBuild.Bind(levelSelectButton, controller.ShowLevelSelect);
        UiBuild.Bind(optionsButton, controller.ShowOptions);

        // What the player has, on the first screen they see — the Shop is one tap further in, and a
        // currency nobody can see until they go looking for it is not a currency they will play for.
        BuildBalances(panel.transform, "CoinStrip",
            new Vector2(0.62f, 0.87f), new Vector2(0.98f, 0.99f),
            CoinCounter.Readout.Total, TextAnchor.MiddleRight, fontSize: 26, rowHeight: 30f, spacing: 2f);

        return panel;
    }

    /// <summary>
    /// Adds just the Exit button, without rebuilding the rest of the menu. Menu:
    /// Bus Runner &gt; Add Exit Button.
    ///
    /// Kept separate from Set Up Start Menu so the button can be added or repositioned without
    /// re-placing every other control on the screen — that command rewrites each panel back to the
    /// anchors in this file, which is more than anyone wants when only one corner is in question.
    /// </summary>
    [MenuItem("Bus Runner/Add Exit Button")]
    static void AddExitButton()
    {
        // Refused rather than merged into: this command saves when it is done, and Menu may be
        // holding edits from a half-finished Set Up Start Menu. Saving those would bake a partial
        // rebuild into the file, and deciding to discard them is the caller's call, not this one's.
        var active = EditorSceneManager.GetActiveScene();
        if (active.path == ScenePath && active.isDirty)
        {
            Debug.LogError("[MenuSetup] Menu.unity has unsaved changes. Reopen it without saving — or " +
                           "save them deliberately — then run this again.");
            return;
        }

        if (!UiBuild.OpenTargetScene(ScenePath, "MenuSetup")) return;

        var canvas = UiBuild.FindRoot(CanvasName);
        var controllerGo = UiBuild.FindRoot("MenuController");
        if (canvas == null || controllerGo == null)
        {
            Debug.LogError($"[MenuSetup] {ScenePath} is missing '{CanvasName}' or 'MenuController'.");
            return;
        }

        var controller = controllerGo.GetComponent<MenuController>();
        var panel = canvas.transform.Find("MainPanel");
        if (controller == null || panel == null)
        {
            Debug.LogError("[MenuSetup] Need a MenuController component and a MainPanel under the canvas.");
            return;
        }

        Undo.SetCurrentGroupName("Add Exit Button");
        int group = Undo.GetCurrentGroup();
        BuildExitButton(panel, controller);
        Undo.CollapseUndoOperations(group);

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[MenuSetup] ExitButton added to MainPanel, bound to MenuController.QuitGame. Drop the " +
                  "artwork on its Icon child when it exists — the Image is deliberately spriteless.");
    }

    /// <summary>
    /// The main menu's Exit control: a box carrying an icon on the left with its caption beside it,
    /// in the top-left corner opposite the coin strip.
    ///
    /// The Icon child's Image is created with no sprite on purpose. It draws as a plain white square
    /// until the artwork is dropped on it, so the button can be placed, sized and wired before the
    /// art exists.
    /// </summary>
    static Button BuildExitButton(Transform panel, MenuController controller)
    {
        var go = UiBuild.Child(panel, "ExitButton", out bool newButton);
        UiBuild.PlaceNew(go, newButton, ExitMin, ExitMax);

        var image = go.GetComponent<Image>();
        if (image == null) image = Undo.AddComponent<Image>(go);
        Undo.RecordObject(image, "Configure ExitButton");
        image.color = BackColor;

        var button = go.GetComponent<Button>();
        if (button == null) button = Undo.AddComponent<Button>(go);
        Undo.RecordObject(button, "Configure ExitButton");
        button.targetGraphic = image;
        UiBuild.AddPressFeedback(go);

        var iconGo = UiBuild.Child(go.transform, "Icon", out bool newIcon);
        UiBuild.PlaceNew(iconGo, newIcon, new Vector2(IconPad, 0.14f), new Vector2(IconPad + IconWidth, 0.86f));
        var icon = iconGo.GetComponent<Image>();
        if (icon == null) icon = Undo.AddComponent<Image>(iconGo);
        Undo.RecordObject(icon, "Configure ExitButton Icon");
        // Set now rather than when the sprite arrives: an Image stretches its sprite to the rect by
        // default, and this slot is not the shape of the icon that will land in it.
        icon.preserveAspect = true;
        // The click belongs to the box underneath. An icon that swallowed it would leave a dead spot
        // in the middle of the control.
        icon.raycastTarget = false;

        // Only assigned when the artwork is actually there. A missing file leaves the spriteless
        // white box the button shipped with rather than failing the whole setup, and an icon already
        // dropped in by hand is left alone.
        var art = AssetDatabase.LoadAssetAtPath<Sprite>(ExitIconPath);
        if (art != null)
        {
            icon.sprite = art;
            // White: an Image tints its sprite by this, and the placeholder colour would stain it.
            icon.color = Color.white;
        }

        // Left-aligned, because the caption starts where the icon ends. Centring it in the leftover
        // space would leave the gap between icon and text changing width with the wording.
        var label = UiBuild.Label(go.transform, "Label", "Exit",
            new Vector2(IconPad * 2f + IconWidth, 0.1f), new Vector2(1f - IconPad, 0.9f),
            ExitFontSize, anchor: TextAnchor.MiddleLeft);
        label.raycastTarget = false;

        UiBuild.Bind(button, controller.QuitGame);
        return button;
    }

    /// <summary>
    /// A CoinCounter and the layout group that stacks its rows. Shared by the main menu's corner strip
    /// and the Shop's balance block: same component the in-run HUD uses, so a fourth coin type appears
    /// in all three without touching any of them.
    /// </summary>
    static CoinCounter BuildBalances(
        Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
        CoinCounter.Readout readout, TextAnchor alignment, int fontSize, float rowHeight, float spacing)
    {
        var go = UiBuild.Child(parent, name, out bool newRow);
        UiBuild.PlaceNew(go, newRow, anchorMin, anchorMax);

        var layout = go.GetComponent<VerticalLayoutGroup>();
        if (layout == null) layout = Undo.AddComponent<VerticalLayoutGroup>(go);
        Undo.RecordObject(layout, "Configure " + name);
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.spacing = spacing;

        var counter = go.GetComponent<CoinCounter>();
        if (counter == null) counter = Undo.AddComponent<CoinCounter>(go);

        UiBuild.SetRef(counter, "rows", go.GetComponent<RectTransform>());
        UiBuild.SetRef(counter, "settings", AssetDatabase.LoadAssetAtPath<CoinSettings>(CoinSettingsPath));
        UiBuild.SetRef(counter, "font", UiBuild.BuiltinFont());
        UiBuild.SetEnum(counter, "readout", (int)readout);
        UiBuild.SetEnum(counter, "alignment", (int)alignment);
        UiBuild.SetInt(counter, "fontSize", fontSize);
        UiBuild.SetFloat(counter, "rowHeight", rowHeight);
        // A zero balance is information on a menu — it says the currency exists and you have none of
        // it. Only the in-run HUD hides an untouched row.
        UiBuild.SetBool(counter, "hideUntilEarned", false);
        return counter;
    }

    /// <summary>
    /// The coin save tools. Reached from Options and hidden entirely when CoinDevPanel's own switch is
    /// off, which also takes the button in Options with it.
    ///
    /// Deliberately three separate buttons rather than one wipe. Permanent and Special are the two
    /// types a second pass through a level cannot test, and they are usually wanted back one at a time.
    /// </summary>
    static GameObject BuildDevPanel(Transform canvas, MenuController controller, GameObject launcher)
    {
        var panel = UiBuild.Child(canvas, "DevPanel");
        UiBuild.Place(panel, Vector2.zero, Vector2.one);

        UiBuild.Label(panel.transform, "Header", "COIN DEV TOOLS", HeaderMin, HeaderMax, 48);
        UiBuild.Label(panel.transform, "Warning",
            "Wipes saved progress on this device. Each button asks twice.",
            new Vector2(0.12f, 0.45f), new Vector2(0.88f, 0.50f), 26, WarnText);

        var status = UiBuild.Label(panel.transform, "Status", "",
            new Vector2(0.20f, 0.31f), new Vector2(0.80f, 0.44f), 26, MutedText);

        var permanent = UiBuild.MakeButton(panel.transform, "ResetPermanentButton", "RESET PERMANENT",
            new Vector2(0.28f, 0.245f), new Vector2(0.72f, 0.30f), DevColor, 30);
        var special = UiBuild.MakeButton(panel.transform, "ResetSpecialButton", "RESET SPECIAL",
            new Vector2(0.28f, 0.185f), new Vector2(0.72f, 0.24f), DevColor, 30);
        // Purchases only, and pointedly not the coins — see CoinDevPanel.Request for why that is the
        // useful shape when testing a shop.
        var unlocks = UiBuild.MakeButton(panel.transform, "ResetUnlocksButton", "RESET UNLOCKS",
            new Vector2(0.28f, 0.125f), new Vector2(0.72f, 0.18f), DevColor, 30);
        var everything = UiBuild.MakeButton(panel.transform, "ResetAllButton", "RESET ALL PROGRESS",
            new Vector2(0.28f, 0.065f), new Vector2(0.72f, 0.12f), DevWipeColor, 30);

        var devPanel = panel.GetComponent<CoinDevPanel>();
        if (devPanel == null) devPanel = Undo.AddComponent<CoinDevPanel>(panel);

        UiBuild.Bind(permanent, devPanel.ResetPermanent);
        UiBuild.Bind(special, devPanel.ResetSpecial);
        UiBuild.Bind(unlocks, devPanel.ResetUnlocks);
        UiBuild.Bind(everything, devPanel.ResetEverything);

        UiBuild.SetRef(devPanel, "status", status);
        UiBuild.SetRefArray(devPanel, "launchers", launcher);

        // Back to Options, where the button that opens this lives.
        var back = UiBuild.MakeButton(panel.transform, "BackButton", "BACK",
            new Vector2(0.40f, 0.005f), new Vector2(0.60f, 0.06f), BackColor, 30);
        UiBuild.Bind(back, controller.ShowOptions);
        return panel;
    }

    /// <summary>
    /// The shop itself: the lifetime balances, and one row per catalogue entry built at runtime by
    /// ShopScreen.
    ///
    /// The rows are not placed here on purpose. What is for sale is a catalogue asset, so the row
    /// count has to follow it — placing them by hand would mean this command needed re-running every
    /// time an item was added, and a stale scene would quietly stop showing something for sale.
    ///
    /// The balances are a CoinCounter, the same component the in-run HUD uses, sitting top-right in
    /// the same band as the main menu's strip so the two screens agree about where money lives.
    /// </summary>
    static GameObject BuildShopPanel(Transform canvas, MenuController controller)
    {
        var panel = UiBuild.Child(canvas, "ShopPanel");
        UiBuild.Place(panel, Vector2.zero, Vector2.one);

        UiBuild.Label(panel.transform, "Header", "SHOP", HeaderMin, HeaderMax, 56);

        // The placeholder screen's copy. Every builder here is find-or-create, which never removes
        // what it has stopped making — so a scene built before the shop existed keeps an "IN
        // DEVELOPMENT" banner sitting across the new rows until something deletes it by name.
        Remove(panel.transform, "Notice");
        Remove(panel.transform, "Blurb");

        BuildBalances(panel.transform, "Balances",
            new Vector2(0.64f, 0.86f), new Vector2(0.98f, 0.99f),
            CoinCounter.Readout.Total, TextAnchor.MiddleRight, fontSize: 26, rowHeight: 30f, spacing: 2f);

        var listGo = UiBuild.Child(panel.transform, "ItemList", out bool newList);
        var itemList = UiBuild.PlaceNew(listGo, newList, new Vector2(0.08f, 0.20f), new Vector2(0.92f, 0.48f));

        var layout = listGo.GetComponent<VerticalLayoutGroup>();
        if (layout == null) layout = Undo.AddComponent<VerticalLayoutGroup>(listGo);
        Undo.RecordObject(layout, "Configure ItemList");
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        // On, so the LayoutElement each row carries is what decides its height. Off, a stretched row
        // would collapse to the zero sizeDelta stretching leaves behind.
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        // Sits in the same band as the list and is switched off by ShopScreen the moment there is
        // anything to show, so an empty catalogue reads as empty rather than as broken.
        var empty = UiBuild.Label(panel.transform, "EmptyNotice",
            "Nothing for sale yet. Add items to the shop catalogue.",
            new Vector2(0.12f, 0.30f), new Vector2(0.88f, 0.38f), 28, MutedText);

        var screen = panel.GetComponent<ShopScreen>();
        if (screen == null) screen = Undo.AddComponent<ShopScreen>(panel);

        UiBuild.SetRef(screen, "catalog", LoadCatalog());
        UiBuild.SetRef(screen, "rows", itemList);
        UiBuild.SetRef(screen, "emptyNotice", empty);
        UiBuild.SetRef(screen, "font", UiBuild.BuiltinFont());

        var back = UiBuild.MakeButton(panel.transform, "BackButton", "BACK", BackMin, BackMax, BackColor, 40);
        UiBuild.Bind(back, controller.ShowMain);
        return panel;
    }

    /// <summary>
    /// Reports any button a player could never actually press.
    ///
    /// A Button is only clickable through a Graphic the raycaster can hit, and a Graphic that is
    /// disabled, missing, or has raycastTarget off is hit by nothing. None of that shows in the
    /// hierarchy, none of it errors, and the onClick wiring still looks perfect in the inspector —
    /// invoking it from code even works. The Exit button sat dead in the editor and in a build
    /// because its Image had been unticked, presumably to hide the box behind the icon.
    ///
    /// An alpha of zero is fine and not reported: raycastTarget is independent of colour, so an
    /// invisible Image is still a full-rect hit area. That is the way to have a button with no
    /// visible background.
    /// </summary>
    static void WarnUnclickable(Transform canvas)
    {
        foreach (var button in canvas.GetComponentsInChildren<Button>(true))
        {
            var graphic = button.targetGraphic;

            string fault =
                graphic == null ? "no Target Graphic" :
                !graphic.enabled ? $"its Target Graphic ({graphic.GetType().Name}) is disabled" :
                !graphic.raycastTarget ? "its Target Graphic has Raycast Target off" :
                null;

            if (fault == null) continue;

            Debug.LogWarning($"[MenuSetup] '{button.name}' cannot be clicked — {fault}. The onClick " +
                             $"wiring is fine; nothing can reach it. For a button with no visible " +
                             $"background, keep the Image enabled and set its colour alpha to 0.",
                             button);
        }
    }

    /// <summary>Deletes a child by name if it is still there. Undo-registered, like everything else here.</summary>
    static void Remove(Transform parent, string name)
    {
        var child = parent.Find(name);
        if (child != null) Undo.DestroyObjectImmediate(child.gameObject);
    }

    /// <summary>
    /// The catalogue asset, or null with a warning. Not an error: a project that has not authored one
    /// yet is a legitimate state, and both MenuController and ShopScreen handle a null by behaving as
    /// though nothing is gated and nothing is for sale.
    /// </summary>
    static ShopCatalog LoadCatalog()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<ShopCatalog>(CatalogPath);
        if (catalog == null)
            Debug.LogWarning($"[MenuSetup] No shop catalogue at {CatalogPath}. The shop will show its " +
                             $"empty notice and every level will count as unlocked.");
        return catalog;
    }

    static GameObject BuildLevelSelectPanel(
        Transform canvas, MenuController controller, out RectTransform levelList)
    {
        var panel = UiBuild.Child(canvas, "LevelSelectPanel");
        UiBuild.Place(panel, Vector2.zero, Vector2.one);

        UiBuild.Label(panel.transform, "Header", "SELECT LEVEL", HeaderMin, HeaderMax, 48);

        var listGo = UiBuild.Child(panel.transform, "LevelList", out bool newList);
        levelList = UiBuild.PlaceNew(listGo, newList, new Vector2(0.34f, 0.22f), new Vector2(0.66f, 0.48f));

        // MenuController spawns one button per unlocked level into this at runtime, so the
        // layout group is what gives them a size and a stacking order.
        var layout = listGo.GetComponent<VerticalLayoutGroup>();
        if (layout == null) layout = Undo.AddComponent<VerticalLayoutGroup>(listGo);
        Undo.RecordObject(layout, "Configure LevelList");
        layout.spacing = 16f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var back = UiBuild.MakeButton(panel.transform, "BackButton", "BACK",
            BackMin, BackMax, BackColor, 40);
        UiBuild.Bind(back, controller.ShowMain);
        return panel;
    }

    static GameObject BuildOptionsPanel(
        Transform canvas, MenuController controller, out GameObject devButton)
    {
        var panel = UiBuild.Child(canvas, "OptionsPanel");
        UiBuild.Place(panel, Vector2.zero, Vector2.one);

        UiBuild.Label(panel.transform, "Header", "OPTIONS", HeaderMin, HeaderMax, 48);

        var controls = UiBuild.MakeButton(panel.transform, "ControlsButton", "CONTROLS",
            new Vector2(0.35f, 0.40f), new Vector2(0.65f, 0.49f), ActionColor, 40);
        UiBuild.Bind(controls, controller.ShowControls);

        // Handed back so CoinDevPanel can hide it along with itself.
        var dev = UiBuild.MakeButton(panel.transform, "DevToolsButton", "COIN DEV TOOLS",
            new Vector2(0.35f, 0.31f), new Vector2(0.65f, 0.38f), DevColor, 30);
        UiBuild.Bind(dev, controller.ShowDev);
        devButton = dev.gameObject;

        // Controls has left this list — the rest is still placeholder.
        UiBuild.Label(panel.transform, "Planned",
            "Game Volume\nMusic Volume\nGraphics Preset",
            new Vector2(0.25f, 0.22f), new Vector2(0.75f, 0.30f), 32, MutedText);
        UiBuild.Label(panel.transform, "Status", "IN DEVELOPMENT",
            new Vector2(0.25f, 0.185f), new Vector2(0.75f, 0.22f), 30, WarnText);

        var back = UiBuild.MakeButton(panel.transform, "BackButton", "BACK",
            BackMin, BackMax, BackColor, 40);
        UiBuild.Bind(back, controller.ShowMain);
        return panel;
    }

    /// <summary>
    /// The Controls screen. Only the frame is built here — ControlsPanel generates the action
    /// rows at runtime from InputBindings, so a new action needs no scene work.
    /// </summary>
    static GameObject BuildControlsPanel(Transform canvas, MenuController controller)
    {
        var panel = UiBuild.Child(canvas, "ControlsPanel");
        UiBuild.Place(panel, Vector2.zero, Vector2.one);

        var controls = panel.GetComponent<ControlsPanel>();
        if (controls == null) controls = Undo.AddComponent<ControlsPanel>(panel);

        UiBuild.Label(panel.transform, "Header", "CONTROLS", HeaderMin, HeaderMax, 48);

        var rowsGo = UiBuild.Child(panel.transform, "Rows", out bool newRows);
        var rows = UiBuild.PlaceNew(rowsGo, newRows, new Vector2(0.2f, 0.27f), new Vector2(0.8f, 0.48f));

        var layout = rowsGo.GetComponent<VerticalLayoutGroup>();
        if (layout == null) layout = Undo.AddComponent<VerticalLayoutGroup>(rowsGo);
        Undo.RecordObject(layout, "Configure Rows");
        layout.spacing = 14f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var reset = UiBuild.MakeButton(panel.transform, "ResetButton", "RESET TO DEFAULTS",
            new Vector2(0.34f, 0.19f), new Vector2(0.66f, 0.25f), BackColor, 30);
        UiBuild.Bind(reset, controls.ResetToDefaults);

        // Back goes to Options, not to the main menu — Controls is reached from there.
        var back = UiBuild.MakeButton(panel.transform, "BackButton", "BACK",
            BackMin, BackMax, BackColor, 40);
        UiBuild.Bind(back, controller.ShowOptions);

        var listener = BuildRebindPrompt(panel.transform, out var window, out var prompt, out var hint);

        UiBuild.SetRef(controls, "rows", rows);
        UiBuild.SetRef(controls, "listener", listener);
        UiBuild.SetRef(controls, "listenerWindow", window);
        UiBuild.SetRef(controls, "listenerPrompt", prompt);
        UiBuild.SetRef(controls, "listenerHint", hint);
        UiBuild.SetRef(controls, "font", UiBuild.BuiltinFont());
        return panel;
    }

    /// <summary>
    /// The "press a key" modal: a dim backdrop that swallows clicks, and the window inside it
    /// that ControlsPanel recolours when the key is already taken.
    /// </summary>
    static GameObject BuildRebindPrompt(
        Transform panel, out Image window, out TMP_Text prompt, out TMP_Text hint)
    {
        var listener = UiBuild.Child(panel, "RebindPrompt");
        UiBuild.Place(listener, Vector2.zero, Vector2.one);

        // Covers the slot buttons underneath, so a second click cannot start another rebind
        // while this one is still listening.
        var backdrop = listener.GetComponent<Image>();
        if (backdrop == null) backdrop = Undo.AddComponent<Image>(listener);
        Undo.RecordObject(backdrop, "Configure RebindPrompt");
        backdrop.color = PromptBackdrop;
        backdrop.raycastTarget = true;

        var windowGo = UiBuild.Child(listener.transform, "Window", out bool newWindow);
        UiBuild.PlaceNew(windowGo, newWindow, new Vector2(0.22f, 0.38f), new Vector2(0.78f, 0.62f));
        window = windowGo.GetComponent<Image>();
        if (window == null) window = Undo.AddComponent<Image>(windowGo);
        Undo.RecordObject(window, "Configure Window");
        window.color = PromptWindow;

        prompt = UiBuild.Label(windowGo.transform, "Prompt", "PRESS A KEY",
            new Vector2(0.05f, 0.42f), new Vector2(0.95f, 0.9f), 40);
        hint = UiBuild.Label(windowGo.transform, "Hint", "ESC TO CANCEL",
            new Vector2(0.05f, 0.12f), new Vector2(0.95f, 0.38f), 26, MutedText);

        listener.SetActive(false);
        return listener;
    }

    /// <summary>Re-anchors an already-built button without touching its colour or label.</summary>
    /// <summary>
    /// Makes sure a control that was already in the scene has a Button, and leaves it exactly where
    /// it is.
    ///
    /// This only ever runs against the hand-placed PlayButton, which by definition someone
    /// positioned deliberately. It used to re-anchor it on every run, so each pass of Set Up Start
    /// Menu dragged Play back to the constants in this file and threw the placement away.
    ///
    /// The anchors are still taken so the call sites read the same as the ones that do place, and so
    /// there is somewhere obvious to put them back if this ever has to build the button itself.
    /// </summary>
    static Button Configure(GameObject go, Vector2 anchorMin, Vector2 anchorMax)
    {
        var button = go.GetComponent<Button>();
        if (button == null) button = Undo.AddComponent<Button>(go);
        UiBuild.AddPressFeedback(go);
        return button;
    }

}
