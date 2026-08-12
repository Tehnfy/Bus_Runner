using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Adds the in-run coin readout to the open level. Menu: Bus Runner > Set Up Coin HUD.
///
/// Goes into the existing TouchUI canvas rather than a new one, so it inherits the CanvasScaler that
/// is already matching the jump and slide buttons to the screen — and so FinishSequence keeps hiding
/// it along with the rest of the run's UI when the outro starts.
///
/// Safe to run more than once.
/// </summary>
static class CoinHudSetup
{
    const string CanvasName = "TouchUI";
    const string HudName = "CoinHud";
    const string SettingsPath = "Assets/Settings/CoinSettings.asset";

    // Top-left, clear of the touch controls, which sit along the bottom.
    static readonly Vector2 HudMin = new Vector2(0.02f, 0.78f);
    static readonly Vector2 HudMax = new Vector2(0.30f, 0.98f);

    [MenuItem("Bus Runner/Set Up Coin HUD")]
    static void Run()
    {
        var canvas = UiBuild.FindRoot(CanvasName);
        if (canvas == null)
        {
            Debug.LogError($"[CoinHudSetup] No '{CanvasName}' in the open scene — open a level first.");
            return;
        }

        Undo.SetCurrentGroupName("Set Up Coin HUD");
        int group = Undo.GetCurrentGroup();

        var hud = UiBuild.Child(canvas.transform, HudName);
        UiBuild.Place(hud, HudMin, HudMax);

        var layout = hud.GetComponent<VerticalLayoutGroup>();
        if (layout == null) layout = Undo.AddComponent<VerticalLayoutGroup>(hud);
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.spacing = 2f;

        var counter = hud.GetComponent<CoinCounter>();
        if (counter == null) counter = Undo.AddComponent<CoinCounter>(hud);

        UiBuild.SetRef(counter, "rows", hud.GetComponent<RectTransform>());
        UiBuild.SetRef(counter, "settings", AssetDatabase.LoadAssetAtPath<CoinSettings>(SettingsPath));
        UiBuild.SetRef(counter, "font", UiBuild.BuiltinFont());
        // The run tally, not the lifetime pile — this is the number the player watches move.
        UiBuild.SetEnum(counter, "readout", (int)CoinCounter.Readout.Session);

        Undo.CollapseUndoOperations(group);
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"[CoinHudSetup] CoinHud added under {CanvasName}. Save the scene to keep it.");
    }
}
