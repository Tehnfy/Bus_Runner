using UnityEngine;

/// <summary>
/// A canopy the runner bounces off rather than lands on — a trampoline, not a rooftop.
///
/// The decision lives here and the physics live in PlayerController: this component only answers
/// "should this contact launch them, and how high", so the launch itself stays with the one script
/// that owns velocity and gravity.
///
/// Only the top face bounces. A contact that is not facing up is the runner clipping an edge, and
/// that is deliberately left to the normal crash rules — a canopy is as solid from the front as a
/// building is.
/// </summary>
public class CanopyBooster : MonoBehaviour
{
    [Tooltip("Apex the launch reaches above the point of contact, in metres. Compare against the " +
             "player's own jumpHeight — the point of a canopy is to clear what a jump cannot.")]
    [SerializeField] float bounceHeight = 5f;

    [Range(0f, 1f)]
    [Tooltip("How far up a contact normal must point for the hit to count as the top face. " +
             "Anything flatter falls through to the usual crash handling.")]
    [SerializeField] float topNormalThreshold = 0.5f;

    [Tooltip("Repeat contacts inside this window are ignored. CharacterController reports a surface " +
             "several times across one touchdown, and without this each report would stack another " +
             "launch on top of the last.")]
    [SerializeField] float retriggerDelay = 0.25f;

    float nextBounceAt;

    public float BounceHeight => bounceHeight;

    /// <summary>
    /// True if this contact should launch the runner, and claims the bounce when it says so, so one
    /// touchdown produces exactly one launch however many hits the controller reports for it.
    /// </summary>
    public bool TryConsumeBounce(Vector3 contactNormal)
    {
        if (Time.time < nextBounceAt) return false;
        if (contactNormal.y < topNormalThreshold) return false;

        nextBounceAt = Time.time + retriggerDelay;
        return true;
    }
}
