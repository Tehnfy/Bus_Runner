using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Shared helpers for the Bus Runner setup commands — scene lookup, serialized-field wiring, and
/// UI construction. Everything here is find-or-create, so re-running a setup reconfigures what it
/// made last time instead of stacking duplicates.
///
/// Image and Button are UnityEngine.UI; captions are TextMeshPro. Legacy Text is what this project
/// started on and it was replaced wholesale: it offers almost nothing to tune from the inspector,
/// and its default Wrap + Truncate means a caption that does not fit draws nothing at all rather
/// than overflowing where you can see it.
/// </summary>
static class UiBuild
{
    /// <summary>
    /// Finds a GameObject in the active scene by name. Roots only by default; with
    /// <paramref name="includeChildren"/> it also checks each root's immediate children, which is
    /// what the finish-sequence wiring needs to reach objects parented under Lane and IntroStaging.
    /// </summary>
    public static GameObject FindRoot(string name, bool includeChildren = false)
    {
        foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name == name) return root;
            if (!includeChildren) continue;

            var child = root.transform.Find(name);
            if (child != null) return child.gameObject;
        }
        return null;
    }

    /// <summary>
    /// Makes <paramref name="scenePath"/> the active scene, opening it if it is not already.
    /// Refuses to do that over unsaved work — a silent discard here would cost whatever is open in
    /// the editor. Returns false if the caller should abort.
    /// </summary>
    public static bool OpenTargetScene(string scenePath, string logPrefix)
    {
        var active = EditorSceneManager.GetActiveScene();
        if (active.path == scenePath) return true;

        if (active.isDirty)
        {
            Debug.LogError($"[{logPrefix}] '{active.name}' has unsaved changes. Save it, then run this " +
                           $"again — this command needs to open {scenePath}.");
            return false;
        }

        EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        return EditorSceneManager.GetActiveScene().path == scenePath;
    }

    /// <summary>
    /// The font asset TMP needs to draw anything at all. Shares UiRect's resolver so the editor and
    /// runtime paths can never disagree about which font a generated label gets.
    /// </summary>
    public static TMP_FontAsset BuiltinFont() => UiRect.ResolveFont(null, "UiBuild");

    /// <summary>Find-or-create a child by name, always carrying a RectTransform.</summary>
    public static GameObject Child(Transform parent, string name) => Child(parent, name, out _);

    /// <summary>
    /// Find-or-create a child, reporting which it was.
    ///
    /// <paramref name="created"/> is what lets a re-run tell "I am building this" from "this was
    /// already here" — and therefore lets it configure a control without moving one somebody has
    /// since positioned by hand. See <see cref="PlaceNew"/>.
    /// </summary>
    public static GameObject Child(Transform parent, string name, out bool created)
    {
        var existing = parent.Find(name);
        if (existing != null)
        {
            created = false;
            return existing.gameObject;
        }

        var go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        Undo.SetTransformParent(go.transform, parent, "Parent " + name);
        go.transform.localScale = Vector3.one;
        go.transform.localRotation = Quaternion.identity;
        created = true;
        return go;
    }

    /// <summary>
    /// The layout in this file is the *initial* layout, not an enforced one.
    ///
    /// Places <paramref name="go"/> only when it was just created; an object that already existed
    /// keeps whatever position it has. Anything else makes re-running a setup command hostile — the
    /// commands are advertised as safe to re-run, and they were quietly dragging every control back
    /// to the constants here, discarding hand-placement each time. Play was the obvious casualty
    /// because it is the one control that predates this file.
    ///
    /// Returns the rect either way, so a caller that needs it does not have to care which happened.
    /// </summary>
    public static RectTransform PlaceNew(GameObject go, bool created, Vector2 anchorMin, Vector2 anchorMax)
    {
        return created ? Place(go, anchorMin, anchorMax) : go.GetComponent<RectTransform>();
    }

    /// <summary>
    /// Anchors a rect to a fraction of its parent with no offsets. Unconditional — callers that
    /// should respect hand-placement want <see cref="PlaceNew"/> instead. Still the right call for a
    /// full-bleed panel, which has to fill its canvas to work at all and is never positioned by hand.
    /// </summary>
    public static RectTransform Place(GameObject go, Vector2 anchorMin, Vector2 anchorMax)
    {
        var rect = go.GetComponent<RectTransform>();
        Undo.RecordObject(rect, "Place " + go.name);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = Vector2.zero;
        rect.anchoredPosition3D = Vector3.zero;   // also flattens z, which a stray drag can leave set
        return rect;
    }

    /// <summary>
    /// Find-or-create a TextMeshProUGUI caption, configured whole.
    ///
    /// A legacy Text already on the object is removed first. Unity allows only one Graphic per
    /// GameObject, so AddComponent would otherwise return null and the next line would throw — which
    /// is exactly how this command broke once the menu's captions were converted by hand. Removing
    /// rather than refusing is what lets a setup command be re-run over a half-migrated scene.
    /// </summary>
    public static TMP_Text Label(
        Transform parent, string name, string content, Vector2 anchorMin, Vector2 anchorMax,
        int fontSize, Color? color = null, TextAnchor anchor = TextAnchor.MiddleCenter)
    {
        var go = Child(parent, name, out bool created);
        PlaceNew(go, created, anchorMin, anchorMax);

        var legacy = go.GetComponent<Text>();
        if (legacy != null) Undo.DestroyObjectImmediate(legacy);

        var text = go.GetComponent<TextMeshProUGUI>();
        if (text == null) text = Undo.AddComponent<TextMeshProUGUI>(go);
        Undo.RecordObject(text, "Configure " + name);
        text.font = BuiltinFont();
        text.fontSize = fontSize;
        text.alignment = UiRect.Align(anchor);
        text.color = color ?? Color.white;
        // Overflow rather than TMP's default Truncate: a caption that outgrows its box should be
        // visibly wrong, not silently absent.
        text.overflowMode = TextOverflowModes.Overflow;
        text.text = content;
        return text;
    }

    public static Button MakeButton(
        Transform parent, string name, string label, Vector2 anchorMin, Vector2 anchorMax,
        Color color, int fontSize)
    {
        var go = Child(parent, name, out bool created);
        PlaceNew(go, created, anchorMin, anchorMax);

        var image = go.GetComponent<Image>();
        if (image == null) image = Undo.AddComponent<Image>(go);
        Undo.RecordObject(image, "Configure " + name);
        image.color = color;

        var button = go.GetComponent<Button>();
        if (button == null) button = Undo.AddComponent<Button>(go);
        Undo.RecordObject(button, "Configure " + name);
        button.targetGraphic = image;

        AddPressFeedback(go);

        Label(go.transform, "Label", label, Vector2.zero, Vector2.one, fontSize);
        return button;
    }

    /// <summary>
    /// Gives a control the placeholder press flash, if it has not got one already.
    ///
    /// Added by the builder rather than by hand so every control in every menu behaves the same, and
    /// so deleting the feedback later is one line here plus one script — not a hunt through the
    /// scene for the buttons that happened to get it.
    /// </summary>
    public static void AddPressFeedback(GameObject go)
    {
        if (go.GetComponent<PressBorder>() == null) Undo.AddComponent<PressBorder>(go);
    }

    /// <summary>
    /// Points a button's inspector-visible onClick at exactly one call. Existing persistent
    /// listeners are cleared first, so re-running a setup does not fire the same handler twice.
    /// </summary>
    public static void Bind(Button button, UnityAction call)
    {
        Undo.RecordObject(button, "Bind " + button.name);
        for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
            UnityEventTools.RemovePersistentListener(button.onClick, i);
        UnityEventTools.AddVoidPersistentListener(button.onClick, call);
    }

    /// <summary>Writes a private [SerializeField] enum the way the inspector would.</summary>
    public static void SetEnum(Object target, string field, int value)
    {
        var so = new SerializedObject(target);
        var prop = so.FindProperty(field);
        if (prop == null)
        {
            Debug.LogWarning($"[UiBuild] {target.GetType().Name} has no serialized field '{field}'.");
            return;
        }
        prop.enumValueIndex = value;
        so.ApplyModifiedProperties();
    }

    /// <summary>Writes a private [SerializeField] object reference the way the inspector would.</summary>
    public static void SetRef(Object target, string field, Object value)
    {
        var so = new SerializedObject(target);
        var prop = so.FindProperty(field);
        if (prop == null)
        {
            Debug.LogWarning($"[UiBuild] {target.GetType().Name} has no serialized field '{field}'.");
            return;
        }
        prop.objectReferenceValue = value;
        so.ApplyModifiedProperties();
    }

    /// <summary>
    /// Fills a private [SerializeField] array of object references, replacing whatever was there.
    /// Rebuilt rather than appended to, so re-running a setup does not grow the array a duplicate
    /// entry every time.
    /// </summary>
    public static void SetRefArray(Object target, string field, params Object[] values)
    {
        var so = new SerializedObject(target);
        var prop = so.FindProperty(field);
        if (prop == null || !prop.isArray)
        {
            Debug.LogWarning($"[UiBuild] {target.GetType().Name} has no serialized array '{field}'.");
            return;
        }

        prop.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        so.ApplyModifiedProperties();
    }

    public static void SetInt(Object target, string field, int value) =>
        Set(target, field, prop => prop.intValue = value);

    public static void SetFloat(Object target, string field, float value) =>
        Set(target, field, prop => prop.floatValue = value);

    public static void SetBool(Object target, string field, bool value) =>
        Set(target, field, prop => prop.boolValue = value);

    static void Set(Object target, string field, System.Action<SerializedProperty> write)
    {
        var so = new SerializedObject(target);
        var prop = so.FindProperty(field);
        if (prop == null)
        {
            Debug.LogWarning($"[UiBuild] {target.GetType().Name} has no serialized field '{field}'.");
            return;
        }
        write(prop);
        so.ApplyModifiedProperties();
    }

    /// <summary>Depth-first search by name, including inactive objects.</summary>
    public static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var hit = FindDeep(root.GetChild(i), name);
            if (hit != null) return hit;
        }
        return null;
    }
}
