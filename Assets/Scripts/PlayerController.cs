using UnityEngine;

/// <summary>
/// Auto-runs the player along +X. Jump and slide are one-shot actions driven by
/// PlayerInputRouter. Movement is XY only — Z is pinned so the 2.5D lane holds.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Run")]
    [SerializeField] float runSpeed = 8f;
    [Tooltip("Yaw applied on Awake so the model faces down the +X lane.")]
    [SerializeField] float facingYaw = 90f;

    [Header("Jump")]
    [SerializeField] float jumpHeight = 2.2f;
    [SerializeField] float gravity = -30f;
    [Tooltip("Gravity multiplier while falling — a snappier arc than a symmetric parabola.")]
    [SerializeField] float fallGravityMultiplier = 1.8f;
    [Tooltip("Grace period after leaving the ground where a jump still registers.")]
    [SerializeField] float coyoteTime = 0.1f;

    [Header("Slide")]
    [SerializeField] float slideDuration = 0.9f;
    [Tooltip("Fraction of standing height kept while sliding.")]
    [SerializeField] float slideHeightFraction = 0.45f;
    [Range(0f, 1f)]
    [Tooltip("How far into the slide a jump may cancel out of it. 0.8 = the last fifth.")]
    [SerializeField] float slideJumpCancelAt = 0.8f;
    [Tooltip("While sliding, size the capsule from the animated pose each frame instead of holding " +
             "slideHeightFraction. Arms are ignored — a raised hand should not make the runner 'taller'.")]
    [SerializeField] bool slideCapsuleFollowsPose = true;
    [Tooltip("Clearance kept above the highest measured bone while pose-driven.")]
    [SerializeField] float slidePoseClearance = 0.06f;

    [Header("Landing roll")]
    [Tooltip("Airborne at least this long and the landing becomes a roll. A plain jump is only ~0.67s " +
             "in the air, so this fires on drops off rooftops rather than on every hop.")]
    [SerializeField] float rollAirTime = 0.8f;
    [Tooltip("Seconds after touchdown during which jump and slide are ignored. Deliberately short — " +
             "long enough that the interrupt reads as a cancel rather than a pop, short enough that " +
             "the runner can act as soon as they land. The roll animation keeps playing either way; " +
             "an action just cuts out of it. Clamped to the roll length.")]
    [SerializeField] float rollInputLockDuration = 0.12f;
    [Tooltip("Only used if the controller has no clip named 'Roll'. The real length is read from the clip.")]
    [SerializeField] float rollDurationFallback = 1.7f;
    [Tooltip("Cancel the roll clip's turn so the runner keeps facing down the lane. The clip is a " +
             "shoulder roll and swings the body off-lane on the way through; the lane is 2.5D, so " +
             "there is nowhere for that turn to go.")]
    [SerializeField] bool cancelRollYaw = true;
    [Range(0f, 0.5f)]
    [Tooltip("Share of the roll over which the yaw correction is eased back out, so handing back " +
             "to the run does not pop.")]
    [SerializeField] float rollYawReleaseShare = 0.2f;
    [Tooltip("Degrees per second the correction may change. Stops a bad frame snapping the model.")]
    [SerializeField] float rollYawMaxRate = 540f;

    [Header("Landing")]
    [Tooltip("A slide asked for in mid-air fires on touchdown instead of being lost to the landing " +
             "frame. Only airborne presses are held, and only this briefly. A press made while " +
             "already sliding is always dropped, which is what stops a double tap producing a " +
             "second slide when the first one ends.")]
    [SerializeField] float slideLandingGrace = 0.2f;

    [Header("Crashing")]
    [Tooltip("A surface counts as a wall when its normal points back down the lane at least this much. " +
             "-1 is dead-on frontal; roofs and floors point up, so they never qualify.")]
    [SerializeField] float wallNormalThreshold = -0.5f;
    [Tooltip("Extra slack above stepOffset before a ledge is treated as a wall instead of a step-up.")]
    [SerializeField] float ledgeTolerance = 0.05f;

    CharacterController cc;
    Animator animator;

    float verticalVelocity;
    float lastGroundedTime;
    float slideEndsAt = -1f;
    float lockedZ;

    float standHeight;
    Vector3 standCenter;

    // The one orientation the capsule is ever allowed to have.
    Quaternion facing;

    bool controlEnabled;

    // Set for the outro: legs keep moving, but jump and slide are ignored.
    bool actionsLocked;

    // Roll: when we left the ground (-1 while grounded), and how long the landing
    // roll owns the input. Separate from lastGroundedTime, which TryJump clears to
    // burn the coyote window and so cannot measure a fall.
    float leftGroundAt = -1f;
    float rollEndsAt = -1f;
    float rollInputLockUntil = -1f;
    float rollDuration;

    const string RollClipName = "Roll";

    // A mid-air slide press, held until touchdown or until it goes stale. Never set by a press
    // made while already sliding, and cleared the moment it is used.
    float airborneSlideAt = -999f;

    // Bones that define the runner's silhouette. Arms are deliberately absent.
    static readonly HumanBodyBones[] SilhouetteBoneIds =
    {
        HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest,
        HumanBodyBones.Neck,
        HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg,
        HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg,
        HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
    };
    Transform[] silhouetteBones;
    Transform headBone;

    // Skeleton root, used to take the turn back out of the roll.
    Transform hipsBone;
    // Correction currently applied, in degrees. Rate-limited towards its target rather than
    // written outright, and decays back to zero once the roll releases.
    float rollYawApplied;
    // How much mesh sits above the head bone (skull, hair). Calibrated from the standing pose,
    // because the head bone alone sits well below the top of the model.
    float headTopOffset;
    bool headOffsetCalibrated;

    public bool IsSliding => Time.time < slideEndsAt;
    public bool IsGrounded => cc.isGrounded || Time.time - lastGroundedTime < coyoteTime;

    /// <summary>True for the length of the roll clip after a long fall.</summary>
    public bool IsRolling => Time.time < rollEndsAt;

    /// <summary>Jump and slide are swallowed while this holds — the outro, or a landing roll's brief lock.</summary>
    bool ActionsBlocked => actionsLocked || Time.time < rollInputLockUntil;

    /// <summary>0 at the start of a slide, 1 at its end. Reads 1 when not sliding.</summary>
    public float SlideProgress =>
        IsSliding && slideDuration > 0f
            ? Mathf.Clamp01(1f - (slideEndsAt - Time.time) / slideDuration)
            : 1f;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();

        standHeight = cc.height;
        standCenter = cc.center;
        lockedZ = transform.position.z;

        facing = Quaternion.Euler(0f, facingYaw, 0f);
        transform.rotation = facing;

        CacheSilhouetteBones();
        CalibrateHeadOffset();   // bind pose is close enough to standing; LateUpdate refines if not
        ResolveRollDuration();

        // Intro holds control until RunManager.BeginRun().
        controlEnabled = false;
    }

    public void EnableControl(bool enabled)
    {
        controlEnabled = enabled;
        if (!enabled)
        {
            verticalVelocity = 0f;
            EndSlide();
            airborneSlideAt = -999f;
        }
        if (animator != null) animator.speed = enabled ? 1f : 0f;
    }

    /// <summary>
    /// Takes jump and slide away without stopping the run — used by FinishSequence so the
    /// runner keeps running through the pull-back but cannot act during the shot.
    /// </summary>
    public void LockActions(bool locked)
    {
        actionsLocked = locked;
        if (locked)
        {
            EndSlide();
            airborneSlideAt = -999f;
        }
    }

    /// <summary>
    /// Reads the roll length off the clip rather than trusting a number typed into the
    /// inspector, so retiming or replacing the animation cannot leave the input lock
    /// running long or short.
    /// </summary>
    void ResolveRollDuration()
    {
        rollDuration = rollDurationFallback;
        if (animator == null || animator.runtimeAnimatorController == null) return;

        foreach (var clip in animator.runtimeAnimatorController.animationClips)
        {
            if (clip == null || clip.name != RollClipName) continue;
            rollDuration = clip.length;
            return;
        }
    }

    /// <summary>
    /// Landing after a long fall. The runner keeps moving — a roll carries momentum — and jump
    /// and slide are swallowed only for rollInputLockDuration, a fraction of a second, so the
    /// roll never costs the player control of the landing. Nothing is remembered: a press inside
    /// that window is dropped, not queued.
    /// </summary>
    void BeginRoll()
    {
        EndSlide();
        rollYawApplied = 0f;
        rollEndsAt = Time.time + rollDuration;
        rollInputLockUntil = Time.time + Mathf.Clamp(rollInputLockDuration, 0f, rollDuration);
        if (animator != null) animator.SetTrigger("Roll");
    }

    void CacheSilhouetteBones()
    {
        if (animator == null || !animator.isHuman) return;
        headBone = animator.GetBoneTransform(HumanBodyBones.Head);
        hipsBone = animator.GetBoneTransform(HumanBodyBones.Hips);
        silhouetteBones = new Transform[SilhouetteBoneIds.Length];
        for (int i = 0; i < SilhouetteBoneIds.Length; i++)
            silhouetteBones[i] = animator.GetBoneTransform(SilhouetteBoneIds[i]);
    }

    /// <summary>
    /// How far the animated body is turned about the vertical, in degrees, as a swing-twist
    /// decomposition of the hips' rotation.
    ///
    /// This replaced a version that measured the heading of the hip line. That could not work:
    /// the roll inverts the body, and once inverted the hip line's flat projection both reverses
    /// and shrinks, so its heading is meaningless exactly across the middle of the roll where the
    /// turn actually happens. Reading the twist straight off the quaternion has no such blind
    /// spot, needs no reference pose, and cannot drift, because it is an absolute measure of the
    /// current pose rather than an integral of how it got there.
    ///
    /// Returns false only in the genuinely degenerate case: a half turn about a horizontal axis,
    /// where the vertical component is undefined.
    /// </summary>
    bool TryMeasureBodyYaw(out float twistDegrees)
    {
        twistDegrees = 0f;
        if (hipsBone == null) return false;

        var q = Quaternion.Inverse(transform.rotation) * hipsBone.rotation;
        if (q.w < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);   // canonical, so the sign is stable

        var proj = Vector3.Project(new Vector3(q.x, q.y, q.z), Vector3.up);
        if (proj.sqrMagnitude + q.w * q.w < 1e-8f) return false;

        var twist = new Quaternion(proj.x, proj.y, proj.z, q.w);
        twist.Normalize();

        twistDegrees = 2f * Mathf.Atan2(twist.y, twist.w) * Mathf.Rad2Deg;
        if (twistDegrees > 180f) twistDegrees -= 360f;
        else if (twistDegrees < -180f) twistDegrees += 360f;
        return true;
    }

    /// <summary>1 through the body of the roll, eased to 0 over its final share.</summary>
    float RollYawCorrectionWeight()
    {
        if (!cancelRollYaw || !IsRolling || rollDuration <= 0f) return 0f;
        float release = rollDuration * Mathf.Clamp(rollYawReleaseShare, 0f, 0.5f);
        if (release <= 0f) return 1f;
        return Mathf.Clamp01((rollEndsAt - Time.time) / release);
    }

    /// <summary>
    /// Turns the animated body back onto the lane. Applied to the skeleton root, so the whole
    /// pose swings with it, and pivoted on the capsule's own axis so the correction cannot shove
    /// the model sideways out of the lane.
    ///
    /// The applied angle is rate-limited so no single frame can snap the model, and eased back out
    /// at the end of the roll as the run pose takes over.
    /// </summary>
    void ApplyRollYawCorrection()
    {
        if (hipsBone == null) return;

        float weight = RollYawCorrectionWeight();
        float target = 0f;
        if (weight > 0f)
        {
            // Undo the whole measured twist, so the tumble stays in the lane plane. Peaks at
            // about 50 degrees mid-roll and settles near 18; both are removed.
            target = TryMeasureBodyYaw(out float twist) ? -twist * weight
                                                        : rollYawApplied;   // degenerate: coast
        }

        rollYawApplied = Mathf.MoveTowardsAngle(rollYawApplied, target, rollYawMaxRate * Time.deltaTime);
        if (Mathf.Abs(rollYawApplied) < 0.01f) return;

        var fix = Quaternion.AngleAxis(rollYawApplied, Vector3.up);
        hipsBone.rotation = fix * hipsBone.rotation;
        hipsBone.position = transform.position + fix * (hipsBone.position - transform.position);
    }

    /// <summary>
    /// Works out how much mesh sits above the head bone, so a pose measured from bones can be
    /// compared against the standing capsule height.
    /// </summary>
    void CalibrateHeadOffset()
    {
        if (headBone == null) return;
        float headHeight = headBone.position.y - transform.position.y;
        if (headHeight <= 0f) return;   // pose not ready yet; try again next frame
        headTopOffset = Mathf.Max(0f, standHeight - headHeight);
        headOffsetCalibrated = true;
    }

    /// <summary>
    /// Height of the animated pose above the capsule base, ignoring the arms.
    /// </summary>
    public float MeasurePoseHeight()
    {
        float feetY = transform.position.y;
        float top = 0f;

        if (headBone != null)
            top = headBone.position.y - feetY + headTopOffset;

        if (silhouetteBones != null)
            foreach (var b in silhouetteBones)
                if (b != null) top = Mathf.Max(top, b.position.y - feetY);

        return top;
    }

    /// <summary>
    /// Runs after the Animator has written this frame's pose, so the capsule can track it.
    /// </summary>
    void LateUpdate()
    {
        // The runner faces down the lane and never tips. Bones tumble freely — the roll
        // leans the body well over to one side — but the capsule's own orientation is
        // pinned here, after the Animator has written the pose, so no clip, no root
        // motion and no stray write can leave the character rotated on Z or X.
        if (transform.rotation != facing) transform.rotation = facing;

        if (!controlEnabled || animator == null || !animator.isHuman) return;

        if (!headOffsetCalibrated && !IsSliding && !IsRolling) CalibrateHeadOffset();

        ApplyRollYawCorrection();

        if (!slideCapsuleFollowsPose || !IsSliding) return;

        float target = Mathf.Clamp(MeasurePoseHeight() + slidePoseClearance, cc.radius * 2f, standHeight);
        cc.height = target;
        cc.center = new Vector3(standCenter.x, target * 0.5f, standCenter.z);
    }

    void Update()
    {
        if (!controlEnabled) return;

        if (cc.isGrounded) lastGroundedTime = Time.time;

        TrackAirborne();

        // Expire the slide
        if (slideEndsAt > 0f && Time.time >= slideEndsAt) EndSlide();

        // Fire a slide asked for in the air the moment the feet are down. Consumed before the
        // attempt, so a failure cannot leave it to retry on a later frame.
        if (cc.isGrounded && !ActionsBlocked && Time.time - airborneSlideAt <= slideLandingGrace)
        {
            airborneSlideAt = -999f;
            TrySlide();
        }

        // Gravity — heavier on the way down
        if (cc.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -1f;   // keep it pinned to the ground
        else
            verticalVelocity += gravity * (verticalVelocity < 0f ? fallGravityMultiplier : 1f) * Time.deltaTime;

        var motion = new Vector3(runSpeed, verticalVelocity, 0f) * Time.deltaTime;
        cc.Move(motion);

        // 2.5D: nothing should ever shift us off the lane plane
        var p = transform.position;
        if (!Mathf.Approximately(p.z, lockedZ))
            transform.position = new Vector3(p.x, p.y, lockedZ);

        // The Jump state leaves on this rather than on clip time. Air time depends on what the
        // runner lands on — hopping up onto a rooftop cuts it to a fraction of the clip — so a
        // fixed exit time left Jump playing while the runner was already up and running.
        //
        // Set after the move, so isGrounded is this frame's. Rising counts as airborne even on
        // the frame the jump starts, before the CharacterController reports leaving the floor,
        // or Jump would hand straight back to Run on the frame it was entered.
        if (animator != null) animator.SetBool("Grounded", cc.isGrounded && verticalVelocity <= 0f);
    }

    /// <summary>
    /// Measures how long we have been off the ground and turns a long enough fall into a
    /// roll on touchdown. A one-frame blip in CharacterController.isGrounded over a seam
    /// reads as a fraction of a second of air, well under the threshold.
    /// </summary>
    void TrackAirborne()
    {
        if (!cc.isGrounded)
        {
            if (leftGroundAt < 0f) leftGroundAt = Time.time;
            return;
        }

        if (leftGroundAt < 0f) return;   // been on the ground a while

        float airTime = Time.time - leftGroundAt;
        leftGroundAt = -1f;
        if (airTime >= rollAirTime) BeginRoll();
    }

    public void Jump()
    {
        // Not buffered: a jump pressed too early is dropped, so it can never
        // surprise the player with a hop a moment after they land.
        if (!controlEnabled || ActionsBlocked) return;
        TryJump();
    }

    public void Slide()
    {
        if (!controlEnabled || ActionsBlocked) return;
        if (TrySlide()) return;

        // Rejected. Exactly one kind of rejection is worth holding: an airborne press, which is
        // a player asking to slide the moment they land. Holding it covers the touchdown frame,
        // where cc.isGrounded can still read false because the input router and this script both
        // run in Update in no guaranteed order.
        //
        // A press made during a slide is dropped outright. Holding that was the old bug: it sat
        // in the buffer through the whole slide and then fired as a second, unasked-for duck the
        // instant the first expired.
        if (!IsSliding) airborneSlideAt = Time.time;
    }

    bool TryJump()
    {
        if (!IsGrounded) return false;

        // A slide may be jumped out of once it is far enough along. By then the
        // runner has travelled well past any gate it slid under, so standing back
        // up cannot leave the capsule inside a ceiling.
        if (IsSliding)
        {
            if (SlideProgress < slideJumpCancelAt) return false;
            EndSlide();
        }

        verticalVelocity = Mathf.Sqrt(2f * jumpHeight * -gravity);
        lastGroundedTime = -999f;
        if (animator != null) animator.SetTrigger("Jump");
        return true;
    }

    bool TrySlide()
    {
        // Real ground contact only. Coyote time is a jump forgiveness window; using it here
        // would let the runner duck in mid-air.
        //
        // Returning false while already sliding is what makes a second tap a no-op rather
        // than a queued second slide — the trigger is never set, so nothing can replay it.
        if (!cc.isGrounded || IsSliding) return false;
        slideEndsAt = Time.time + slideDuration;
        cc.height = standHeight * slideHeightFraction;
        cc.center = new Vector3(standCenter.x, cc.height * 0.5f, standCenter.z);
        if (animator != null) animator.SetTrigger("Slide");
        return true;
    }

    void EndSlide()
    {
        slideEndsAt = -1f;
        cc.height = standHeight;
        cc.center = standCenter;
    }

    /// <summary>
    /// Buildings are solid, not triggers, so the runner can land on and run along
    /// their roofs. Only a frontal impact ends the run: something whose face points
    /// back down the lane and whose top is too high to simply step onto.
    /// </summary>
    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (!controlEnabled) return;

        // Roofs, floors and ceilings all fail this test — only walls pass.
        if (hit.normal.x > wallNormalThreshold) return;

        // If we could step or already cleared it, it is not a crash.
        float feetY = transform.position.y;
        float surfaceTop = hit.collider.bounds.max.y;
        if (surfaceTop - feetY <= cc.stepOffset + ledgeTolerance) return;

        if (RunManager.Instance != null) RunManager.Instance.Kill();
    }

    /// <summary>Hard reposition used on respawn — CharacterController ignores transform writes while enabled.</summary>
    public void Teleport(Vector3 position)
    {
        EndSlide();
        airborneSlideAt = -999f;   // a press from before the death must not fire on respawn
        verticalVelocity = 0f;
        cc.enabled = false;
        transform.position = position;
        lockedZ = position.z;
        cc.enabled = true;
    }
}
