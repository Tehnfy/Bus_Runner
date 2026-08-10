using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Turns an on-screen button into something that can be held.
///
/// A UI Button reports a click — one event, on release — which is enough to start a slide and
/// useless for extending one. This reports the press and the release separately, so the touch path
/// can answer "still held" the way a key can.
///
/// Sits alongside the existing Button rather than replacing it: the Button still fires its onClick,
/// so the press that starts the slide keeps going through the one funnel it always did.
/// </summary>
public class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    [SerializeField] PlayerInputRouter router;

    void Awake()
    {
        if (router == null) router = FindFirstObjectByType<PlayerInputRouter>();
    }

    // Releasing outside the button still counts as a release — a finger dragged off the control
    // and lifted would otherwise leave the slide held down for good.
    void OnDisable() => Report(false);

    public void OnPointerDown(PointerEventData eventData) => Report(true);
    public void OnPointerUp(PointerEventData eventData) => Report(false);

    void Report(bool held)
    {
        if (router != null) router.SetTouchSlideHeld(held);
    }
}
