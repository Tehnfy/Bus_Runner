using System;
using UnityEngine;

/// <summary>
/// Single funnel for every way the player can jump or slide: keyboard, on-screen
/// buttons, and swipe gestures. Raises OnJump / OnSlide; PlayerController listens.
/// </summary>
public class PlayerInputRouter : MonoBehaviour
{
    [SerializeField] PlayerController player;
    [SerializeField] SwipeDetector swipeDetector;

    public event Action OnJump;
    public event Action OnSlide;

    // Set by HoldButton on the on-screen slide control. A UI Button only reports a click, so the
    // touch path cannot answer "still held" on its own.
    bool touchSlideHeld;

    /// <summary>
    /// Whether the player is still asking to slide, from any source that can express holding.
    /// A swipe cannot — it is a gesture, over the moment it is recognised — so a swiped slide runs
    /// its normal length and only key or button holds extend it.
    /// </summary>
    public bool SlideHeld => touchSlideHeld || InputBindings.IsHeld(GameAction.Slide);

    void Awake()
    {
        if (player == null) player = GetComponent<PlayerController>();
        if (swipeDetector == null) swipeDetector = GetComponent<SwipeDetector>();
    }

    void OnEnable()
    {
        OnJump += HandleJump;
        OnSlide += HandleSlide;
        if (swipeDetector != null)
        {
            swipeDetector.OnSwipeUp += RaiseJump;
            swipeDetector.OnSwipeDown += RaiseSlide;
        }
    }

    void OnDisable()
    {
        // The pause menu disables this component. A finger down at that moment would otherwise
        // leave the flag stuck true and extend the next slide forever.
        touchSlideHeld = false;

        OnJump -= HandleJump;
        OnSlide -= HandleSlide;
        if (swipeDetector != null)
        {
            swipeDetector.OnSwipeUp -= RaiseJump;
            swipeDetector.OnSwipeDown -= RaiseSlide;
        }
    }

    void Update()
    {
        // Which keys these are is InputBindings' business, not this component's — the Controls
        // screen rewrites them and saves to PlayerPrefs, so there is nothing to pass in here.
        if (InputBindings.WasPressedThisFrame(GameAction.Jump)) RaiseJump();
        if (InputBindings.WasPressedThisFrame(GameAction.Slide)) RaiseSlide();
    }

    // Public so the on-screen Buttons can call them from onClick.
    public void RaiseJump() => OnJump?.Invoke();
    public void RaiseSlide() => OnSlide?.Invoke();

    /// <summary>Called by HoldButton on the slide control as the finger goes down and comes up.</summary>
    public void SetTouchSlideHeld(bool held) => touchSlideHeld = held;

    void HandleJump()
    {
        if (player != null) player.Jump();
    }

    void HandleSlide()
    {
        if (player != null) player.Slide();
    }
}
