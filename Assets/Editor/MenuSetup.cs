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

    // ControlsPanel overwrites the window colour as soon as a rebind starts; these are what the
    // scene shows at rest.
    static readonly Color PromptBackdrop = new Color(0.02f, 0.03f, 0.05f, 0.82f);
    static readonly Color PromptWindow = new Color(0.09f, 0.11f, 0.15f, 0.98f);

    // The Title already occupies y 0.60-0.85, so every panel's own content sits below it.
    static readonly Vector2 BackMin = new Vector2(0.40f, 0.08f);
    static readonly Vector2 BackMax = new Vector2(0.60f, 0.18f);
    static readonly Vector2 HeaderMin = new Vector2(0.15f, 0.50f);
    static readonly Vector2 HeaderMax = new Vector2(0.85f, 0.58f);

    [MenuItem("Bus Runner/Set Up Start Menu")]
    static void Run()
    {
        if (!OpenTargetScene()) return;

        var canvas = FindRoot(CanvasName);
        var controllerGo = FindRoot("MenuController");
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
        var options = BuildOptionsPanel(canvas.transform, controller);
        var controls = BuildControlsPanel(canvas.transform, controller);

        UiBuild.SetRef(controller, "mainPanel", main);
        UiBuild.SetRef(controller, "levelSelectPanel", levelSelect);
        UiBuild.SetRef(controller, "optionsPanel", options);
        UiBuild.SetRef(controller, "controlsPanel", controls);
        UiBuild.SetRef(controller, "levelList", levelList);
        UiBuild.SetRef(controller, "levelButtonFont", UiBuild.BuiltinFont());

        // What the designer sees when the scene opens. MenuController.Start repeats this.
        main.SetActive(true);
        levelSelect.SetActive(false);
        options.SetActive(false);
        controls.SetActive(false);

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
                new Vector2(0.35f, 0.38f), new Vector2(0.65f, 0.50f), ActionColor, 56);

        var levelSelectButton = UiBuild.MakeButton(panel.transform, "LevelSelectButton", "LEVEL SELECT",
            new Vector2(0.35f, 0.24f), new Vector2(0.65f, 0.36f), ActionColor, 44);
        var optionsButton = UiBuild.MakeButton(panel.transform, "OptionsButton", "OPTIONS",
            new Vector2(0.35f, 0.10f), new Vector2(0.65f, 0.22f), ActionColor, 44);

        UiBuild.Bind(playButton, controller.PlayLevel);
        UiBuild.Bind(levelSelectButton, controller.ShowLevelSelect);
        UiBuild.Bind(optionsButton, controller.ShowOptions);
        return panel;
    }

    static GameObject BuildLevelSelectPanel(
        Transform canvas, MenuController controller, out RectTransform levelList)
    {
        var panel = UiBuild.Child(canvas, "LevelSelectPanel");
        UiBuild.Place(panel, Vector2.zero, Vector2.one);

        UiBuild.Label(panel.transform, "Header", "SELECT LEVEL", HeaderMin, HeaderMax, 48);

        var listGo = UiBuild.Child(panel.transform, "LevelList");
        levelList = UiBuild.Place(listGo, new Vector2(0.34f, 0.22f), new Vector2(0.66f, 0.48f));

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

    static GameObject BuildOptionsPanel(Transform canvas, MenuController controller)
    {
        var panel = UiBuild.Child(canvas, "OptionsPanel");
        UiBuild.Place(panel, Vector2.zero, Vector2.one);

        UiBuild.Label(panel.transform, "Header", "OPTIONS", HeaderMin, HeaderMax, 48);

        var controls = UiBuild.MakeButton(panel.transform, "ControlsButton", "CONTROLS",
            new Vector2(0.35f, 0.37f), new Vector2(0.65f, 0.47f), ActionColor, 40);
        UiBuild.Bind(controls, controller.ShowControls);

        // Controls has left this list — the rest is still placeholder.
        UiBuild.Label(panel.transform, "Planned",
            "Game Volume\nMusic Volume\nGraphics Preset",
            new Vector2(0.25f, 0.26f), new Vector2(0.75f, 0.35f), 32, MutedText);
        UiBuild.Label(panel.transform, "Status", "IN DEVELOPMENT",
            new Vector2(0.25f, 0.20f), new Vector2(0.75f, 0.25f), 36, WarnText);

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

        var rowsGo = UiBuild.Child(panel.transform, "Rows");
        var rows = UiBuild.Place(rowsGo, new Vector2(0.2f, 0.27f), new Vector2(0.8f, 0.48f));

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
        Transform panel, out Image window, out Text prompt, out Text hint)
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

        var windowGo = UiBuild.Child(listener.transform, "Window");
        UiBuild.Place(windowGo, new Vector2(0.22f, 0.38f), new Vector2(0.78f, 0.62f));
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
    static Button Configure(GameObject go, Vector2 anchorMin, Vector2 anchorMax)
    {
        UiBuild.Place(go, anchorMin, anchorMax);
        var button = go.GetComponent<Button>();
        if (button == null) button = Undo.AddComponent<Button>(go);
        return button;
    }

    /// <summary>
    /// Opens Menu.unity if it is not already the active scene. Refuses to do it over unsaved
    /// work — a silent discard here would cost whatever is open in the editor.
    /// </summary>
    static bool OpenTargetScene()
    {
        var active = EditorSceneManager.GetActiveScene();
        if (active.path == ScenePath) return true;

        if (active.isDirty)
        {
            Debug.LogError($"[MenuSetup] '{active.name}' has unsaved changes. Save it, then run this again " +
                           $"— this command needs to open {ScenePath}.");
            return false;
        }

        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        return EditorSceneManager.GetActiveScene().path == ScenePath;
    }

    static GameObject FindRoot(string name)
    {
        foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            if (root.name == name) return root;
        return null;
    }
}
