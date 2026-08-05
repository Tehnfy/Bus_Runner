using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Shared builders for the menu setup commands. Everything here is find-or-create, so
/// re-running a setup reconfigures what it made last time instead of stacking duplicates.
///
/// Legacy UI (UnityEngine.UI.Text / Image / Button) on purpose — that is what the
/// hand-placed menu and touch buttons already use, and mixing in TextMeshPro would mean
/// two font pipelines for four screens of UI.
/// </summary>
static class UiBuild
{
    /// <summary>
    /// The font legacy Text needs to draw anything at all. Unity renamed Arial.ttf to
    /// LegacyRuntime.ttf, so both names are tried.
    /// </summary>
    public static Font BuiltinFont() =>
        Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
        ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

    /// <summary>Find-or-create a child by name, always carrying a RectTransform.</summary>
    public static GameObject Child(Transform parent, string name)
    {
        var existing = parent.Find(name);
        if (existing != null) return existing.gameObject;

        var go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        Undo.SetTransformParent(go.transform, parent, "Parent " + name);
        go.transform.localScale = Vector3.one;
        go.transform.localRotation = Quaternion.identity;
        return go;
    }

    /// <summary>Anchors a rect to a fraction of its parent with no offsets.</summary>
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

    public static Text Label(
        Transform parent, string name, string content, Vector2 anchorMin, Vector2 anchorMax,
        int fontSize, Color? color = null, TextAnchor anchor = TextAnchor.MiddleCenter)
    {
        var go = Child(parent, name);
        Place(go, anchorMin, anchorMax);

        var text = go.GetComponent<Text>();
        if (text == null) text = Undo.AddComponent<Text>(go);
        Undo.RecordObject(text, "Configure " + name);
        text.font = BuiltinFont();
        text.fontSize = fontSize;
        text.alignment = anchor;
        text.color = color ?? Color.white;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.text = content;
        return text;
    }

    public static Button MakeButton(
        Transform parent, string name, string label, Vector2 anchorMin, Vector2 anchorMax,
        Color color, int fontSize)
    {
        var go = Child(parent, name);
        Place(go, anchorMin, anchorMax);

        var image = go.GetComponent<Image>();
        if (image == null) image = Undo.AddComponent<Image>(go);
        Undo.RecordObject(image, "Configure " + name);
        image.color = color;

        var button = go.GetComponent<Button>();
        if (button == null) button = Undo.AddComponent<Button>(go);
        Undo.RecordObject(button, "Configure " + name);
        button.targetGraphic = image;

        Label(go.transform, "Label", label, Vector2.zero, Vector2.one, fontSize);
        return button;
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
