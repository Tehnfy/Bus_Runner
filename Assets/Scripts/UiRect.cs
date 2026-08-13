using TMPro;
using UnityEngine;

/// <summary>
/// The three things every runtime-built UI element in this project needs: a RectTransform stretched
/// to fill its parent, a font asset TextMeshPro will actually draw with, and the translation from
/// the TextAnchor the inspector fields are typed as into TMP's own alignment enum.
///
/// The editor side already has UiBuild, but that lives in Assets/Editor and calls Undo and
/// SerializedObject, so runtime code cannot reach it. This is the small runtime counterpart —
/// deliberately not a general UI toolkit.
///
/// Labels are TextMeshPro throughout. Legacy Text was what this project started on and it is a poor
/// fit: no control over spacing or outline from the inspector, and its default Wrap + Truncate makes
/// a label that does not fit render nothing at all rather than overflow visibly.
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
    /// The font asset to draw with: the caller's own if it was wired in the inspector, otherwise the
    /// project's TMP default. A TMP_Text with no font asset draws nothing at all, silently — the same
    /// trap legacy Text had with a null Font.
    /// </summary>
    public static TMP_FontAsset ResolveFont(TMP_FontAsset preferred = null, string owner = null)
    {
        if (preferred != null) return preferred;

        var font = TMP_Settings.defaultFontAsset;
        if (font == null && owner != null)
            Debug.LogWarning($"[{owner}] No font asset wired and no TMP default configured — labels " +
                             $"will be blank. Window > TextMeshPro > Project Settings sets the default.");
        return font;
    }

    /// <summary>
    /// TextAnchor to TMP's alignment.
    ///
    /// The serialized fields stay typed as TextAnchor on purpose. Both are stored as plain ints, so
    /// retyping them to TextAlignmentOptions would silently reinterpret every value already set in a
    /// scene — MiddleLeft (3) would come back as TopFlush. Mapping here costs one switch and keeps
    /// what the inspector already holds.
    /// </summary>
    public static TextAlignmentOptions Align(TextAnchor anchor)
    {
        switch (anchor)
        {
            case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
            case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
            case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
            case TextAnchor.MiddleLeft: return TextAlignmentOptions.MidlineLeft;
            case TextAnchor.MiddleCenter: return TextAlignmentOptions.Midline;
            case TextAnchor.MiddleRight: return TextAlignmentOptions.MidlineRight;
            case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
            case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
            case TextAnchor.LowerRight: return TextAlignmentOptions.BottomRight;
            default: return TextAlignmentOptions.Midline;
        }
    }
}
