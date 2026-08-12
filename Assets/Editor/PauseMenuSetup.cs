using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One-shot wiring for the in-run pause menu. Menu: Bus Runner &gt; Set Up Pause Menu.
///
/// Builds, in Level_1.unity:
///   PauseUI             its own canvas, sorting order above TouchUI
///   PauseUI/PauseButton top-right corner
///   PauseUI/PausePanel  full-screen dim, PAUSED, Resume, Exit
///
/// A separate canvas rather than a child of TouchUI: FinishSequence hides TouchUI at the
/// finish line, and the dim overlay has to be able to cover the jump and slide buttons,
/// which needs a higher sorting order than they have.
///
/// Safe to run more than once — it reuses whatever it already made.
/// </summary>
static class PauseMenuSetup
{
    const string ScenePath = "Assets/Scenes/Level_1.unity";
    const string CanvasName = "PauseUI";

    // Above TouchUI, which is left at the default 0.
    const int SortingOrder = 10;

    static readonly Color ActionColor = new Color(0.2f, 0.55f, 0.85f, 0.9f);
    static readonly Color ExitColor = new Color(0.7f, 0.28f, 0.25f, 0.9f);
    static readonly Color PauseButtonColor = new Color(0.12f, 0.13f, 0.15f, 0.6f);
    static readonly Color DimColor = new Color(0.03f, 0.04f, 0.06f, 0.78f);

    [MenuItem("Bus Runner/Set Up Pause Menu")]
    static void Run()
    {
        if (!UiBuild.OpenTargetScene(ScenePath, "PauseMenuSetup")) return;

        Undo.SetCurrentGroupName("Set Up Pause Menu");
        int group = Undo.GetCurrentGroup();

        var canvasGo = EnsureCanvas();
        var menu = canvasGo.GetComponent<PauseMenu>();
        if (menu == null) menu = Undo.AddComponent<PauseMenu>(canvasGo);

        var pauseButton = UiBuild.MakeButton(canvasGo.transform, "PauseButton", "II",
            new Vector2(0.915f, 0.855f), new Vector2(0.985f, 0.975f), PauseButtonColor, 44);
        UiBuild.Bind(pauseButton, menu.Pause);

        var panel = BuildPanel(canvasGo.transform, menu);

        UiBuild.SetRef(menu, "pauseButton", pauseButton.gameObject);
        UiBuild.SetRef(menu, "pausePanel", panel);
        UiBuild.SetRef(menu, "inputRouter", Object.FindFirstObjectByType<PlayerInputRouter>());

        // Down at edit time as well as at runtime, so the level view is not covered.
        panel.SetActive(false);

        if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            Debug.LogWarning("[PauseMenuSetup] No EventSystem in the scene — the buttons will not respond.");

        Undo.CollapseUndoOperations(group);
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[PauseMenuSetup] Done — PauseUI built and wired, scene saved.");
    }

    static GameObject BuildPanel(Transform canvas, PauseMenu menu)
    {
        var panel = UiBuild.Child(canvas, "PausePanel");
        UiBuild.Place(panel, Vector2.zero, Vector2.one);

        // The dim is also what swallows clicks aimed at the jump and slide buttons underneath,
        // so it has to be a raycast target covering the whole screen, not just a backdrop.
        var dim = panel.GetComponent<Image>();
        if (dim == null) dim = Undo.AddComponent<Image>(panel);
        Undo.RecordObject(dim, "Configure PausePanel");
        dim.color = DimColor;
        dim.raycastTarget = true;

        UiBuild.Label(panel.transform, "Header", "PAUSED",
            new Vector2(0.2f, 0.62f), new Vector2(0.8f, 0.78f), 84);

        var resume = UiBuild.MakeButton(panel.transform, "ResumeButton", "RESUME",
            new Vector2(0.375f, 0.44f), new Vector2(0.625f, 0.56f), ActionColor, 44);
        var exit = UiBuild.MakeButton(panel.transform, "ExitButton", "EXIT TO MENU",
            new Vector2(0.375f, 0.28f), new Vector2(0.625f, 0.40f), ExitColor, 40);

        UiBuild.Bind(resume, menu.Resume);
        UiBuild.Bind(exit, menu.ExitToMenu);
        return panel;
    }

    static GameObject EnsureCanvas()
    {
        var existing = UiBuild.FindRoot(CanvasName);
        var go = existing != null ? existing : new GameObject(CanvasName);
        if (existing == null) Undo.RegisterCreatedObjectUndo(go, "Create " + CanvasName);

        var canvas = go.GetComponent<Canvas>();
        if (canvas == null) canvas = Undo.AddComponent<Canvas>(go);
        Undo.RecordObject(canvas, "Configure " + CanvasName);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;

        var scaler = go.GetComponent<CanvasScaler>();
        if (scaler == null) scaler = Undo.AddComponent<CanvasScaler>(go);
        Undo.RecordObject(scaler, "Configure " + CanvasName + " scaler");
        // Matches TouchUI, so a button placed here lands where the same fractions land there.
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        if (go.GetComponent<GraphicRaycaster>() == null) Undo.AddComponent<GraphicRaycaster>(go);
        return go;
    }

}
