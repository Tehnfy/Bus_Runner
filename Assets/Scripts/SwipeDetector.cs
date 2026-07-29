using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Turns a vertical flick on the touchscreen into OnSwipeUp / OnSwipeDown.
/// Kept separate from PlayerInputRouter because the thresholds need tuning on device.
/// </summary>
public class SwipeDetector : MonoBehaviour
{
    [Tooltip("Minimum vertical travel, as a fraction of screen height, to count as a swipe.")]
    [SerializeField] float minDistanceFraction = 0.06f;
    [Tooltip("A flick must finish within this long, or it is treated as a drag and ignored.")]
    [SerializeField] float maxDuration = 0.5f;
    [Tooltip("Vertical travel must exceed horizontal travel by this factor.")]
    [SerializeField] float verticalDominance = 1.2f;

    public event Action OnSwipeUp;
    public event Action OnSwipeDown;

    bool tracking;
    Vector2 startPosition;
    float startTime;

    void Update()
    {
        var touchscreen = Touchscreen.current;
        if (touchscreen == null) return;

        var touch = touchscreen.primaryTouch;

        if (touch.press.wasPressedThisFrame)
        {
            tracking = true;
            startPosition = touch.position.ReadValue();
            startTime = Time.time;
            return;
        }

        if (!tracking || !touch.press.wasReleasedThisFrame) return;
        tracking = false;

        if (Time.time - startTime > maxDuration) return;

        var delta = touch.position.ReadValue() - startPosition;
        float minDistance = Screen.height * minDistanceFraction;

        if (Mathf.Abs(delta.y) < minDistance) return;
        if (Mathf.Abs(delta.y) < Mathf.Abs(delta.x) * verticalDominance) return;

        if (delta.y > 0f) OnSwipeUp?.Invoke();
        else OnSwipeDown?.Invoke();
    }
}
