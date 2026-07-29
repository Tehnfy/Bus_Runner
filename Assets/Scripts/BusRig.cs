using UnityEngine;

/// <summary>
/// Keeps the bus mockup (and the kid inside it) travelling alongside the runner
/// for the whole level instead of being left behind at the start line.
///
/// The rig sits well behind the game camera on Z, so nothing here is ever on
/// screen during play — the point is that the bus is always a couple of metres
/// away when the outro needs it, so OutroCam can pull back into the window
/// without a long dolly across the level.
///
/// The offset captured on Awake is whatever the level designer set up in the
/// intro, so the framing the intro shot was built around is preserved exactly.
///
/// Runs early in LateUpdate: the runner's position is final by then (it moves in
/// Update), and the Cinemachine brain blends afterwards, so OutroCam — a child of
/// this rig — is already in place for the frame it is drawn.
/// </summary>
[DefaultExecutionOrder(-500)]
public class BusRig : MonoBehaviour
{
    [Header("Refs")]
    [Tooltip("Usually the Player. Falls back to the tagged player on Awake.")]
    [SerializeField] Transform followTarget;

    [Header("Follow")]
    [Tooltip("Added to the offset captured on Awake. Positive puts the bus further down the lane.")]
    [SerializeField] float trailOffset = 0f;

    [Tooltip("Start following the moment the level loads rather than waiting for the intro to finish. " +
             "Off by default so the intro shot is not disturbed.")]
    [SerializeField] bool followFromStart = false;

    // Rig X minus target X at level load — the staging the intro was authored against.
    float baseOffsetX;
    bool following;

    public bool Following => following;

    void Awake()
    {
        if (followTarget == null)
        {
            var tagged = GameObject.FindGameObjectWithTag("Player");
            if (tagged != null) followTarget = tagged.transform;
        }

        baseOffsetX = followTarget != null ? transform.position.x - followTarget.position.x : 0f;
        following = followFromStart;
    }

    /// <summary>Called by IntroSequence once the camera has left the bus.</summary>
    public void BeginFollowing()
    {
        following = true;
    }

    /// <summary>Pins the bus where it stands — the runner then pulls away from it.</summary>
    public void StopFollowing()
    {
        following = false;
    }

    void LateUpdate()
    {
        if (!following || followTarget == null) return;

        // X only. Y and Z stay where the intro placed them, which is what keeps
        // the bus behind the game camera and off screen.
        var p = transform.position;
        p.x = followTarget.position.x + baseOffsetX + trailOffset;
        transform.position = p;
    }
}
