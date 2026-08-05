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

    void HandleJump()
    {
        if (player != null) player.Jump();
    }

    void HandleSlide()
    {
        if (player != null) player.Slide();
    }
}
