using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Placeholder press feedback: a white border flashes around whatever the player activated.
///
/// Deliberately crude. It exists so a press is visibly acknowledged while the real feedback — a
/// sound, a scale punch, an authored sprite state — is still to be designed. Everything about how it
/// looks is a serialized field, so replacing it later means deleting this component rather than
/// unpicking it from the menu.
///
/// Four stretched strips rather than an outline sprite or UnityEngine.UI.Outline. Outline works by
/// offsetting copies of the graphic's own vertices, which fringes a filled Image rather than framing
/// it, and a 9-sliced border would need artwork that does not exist yet. Four white rectangles need
/// nothing and frame the rect exactly, whatever size it ends up.
///
/// The strips are built at runtime only. Building them in the editor would serialise four extra
/// objects per button into the scene, and every setup command would then have to know to leave them
/// alone.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class PressBorder : MonoBehaviour, IPointerDownHandler, ISubmitHandler
{
    [Tooltip("Border colour. White while this is a placeholder.")]
    [SerializeField] Color borderColor = Color.white;

    [Tooltip("Thickness in canvas units.")]
    [SerializeField] float thickness = 4f;

    [Tooltip("How long the border stays up after a press, in unscaled seconds. Unscaled because the " +
             "menu may be sitting on a paused clock and feedback should not stop with the game.")]
    [SerializeField] float holdTime = 0.25f;

    RectTransform frame;
    float hideAt;

    public void OnPointerDown(PointerEventData eventData) => Flash();

    /// <summary>Keyboard and gamepad reach a button through Submit, not a pointer.</summary>
    public void OnSubmit(BaseEventData eventData) => Flash();

    /// <summary>Shows the border and restarts its timer. Public so a press routed some other way can use it.</summary>
    public void Flash()
    {
        if (frame == null) Build();
        if (frame == null) return;

        frame.gameObject.SetActive(true);
        hideAt = Time.unscaledTime + Mathf.Max(0.01f, holdTime);
    }

    void OnDisable()
    {
        // A panel switched away mid-flash must not come back still lit.
        if (frame != null) frame.gameObject.SetActive(false);
    }

    void Update()
    {
        if (frame == null || !frame.gameObject.activeSelf) return;
        if (Time.unscaledTime < hideAt) return;

        frame.gameObject.SetActive(false);
    }

    void Build()
    {
        if (!Application.isPlaying) return;

        var go = new GameObject("PressBorder", typeof(RectTransform));
        go.transform.SetParent(transform, false);

        frame = (RectTransform)go.transform;
        frame.anchorMin = Vector2.zero;
        frame.anchorMax = Vector2.one;
        frame.offsetMin = Vector2.zero;
        frame.offsetMax = Vector2.zero;

        // Top and bottom span the full width; the sides span the full height and overlap them at the
        // corners, which is what closes the frame without any corner pieces.
        Strip("Top", new Vector2(0f, 1f), Vector2.one, new Vector2(0.5f, 1f), new Vector2(0f, thickness));
        Strip("Bottom", Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, thickness));
        Strip("Left", Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(thickness, 0f));
        Strip("Right", new Vector2(1f, 0f), Vector2.one, new Vector2(1f, 0.5f), new Vector2(thickness, 0f));

        frame.gameObject.SetActive(false);
    }

    void Strip(string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeDelta)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(frame, false);

        var rect = (RectTransform)go.transform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.sizeDelta = sizeDelta;
        rect.anchoredPosition = Vector2.zero;

        var image = go.AddComponent<Image>();
        image.color = borderColor;
        // The click belongs to the button underneath. A border that ate it would make the control
        // dead everywhere the frame covers.
        image.raycastTarget = false;
    }
}
