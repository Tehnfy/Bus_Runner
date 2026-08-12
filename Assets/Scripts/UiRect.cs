using UnityEngine;

/// <summary>
/// The two things every runtime-built UI element in this project needs: a RectTransform stretched
/// to fill its parent, and a font that legacy Text will actually draw with.
///
/// The editor side already has UiBuild, but that lives in Assets/Editor and calls Undo and
/// SerializedObject, so runtime code cannot reach it. This is the small runtime counterpart —
/// deliberately not a general UI toolkit.
/// </summary>
public static class UiRect
{
    /// <summary>
    /// A new child filling its parent edge to edge. Every runtime-built element here is a full-bleed
    /// panel, label or curtain, so the stretched anchors are the only configuration needed.
    /// </summary>
    public static GameObject Stretch(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return go;
    }

    /// <summary>
    /// The font to draw with: the caller's own if it was wired in the inspector, otherwise the
    /// built-in one. Unity renamed Arial.ttf to LegacyRuntime.ttf, so both names are tried — a
    /// legacy Text with no font draws nothing at all, silently.
    /// </summary>
    public static Font ResolveFont(Font preferred = null, string owner = null)
    {
        if (preferred != null) return preferred;

        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                   ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (font == null && owner != null)
            Debug.LogWarning($"[{owner}] No font wired and no built-in fallback — labels will be blank.");
        return font;
    }
}
