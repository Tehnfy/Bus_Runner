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
    [Range(1f, 6f)]
    [Tooltip("Longest a held slide may run, as a multiple of slideDuration. 1 turns holding off. " +
             "Only keys and the on-screen button can hold — a swipe is over the moment it lands.")]
    [SerializeField] float slideHoldMaxMultiplier = 2f;
    [Range(1f, 6f)]
    [Tooltip("How long a slide may be stretched purely because standing up would put the capsule " +
             "inside something, as a multiple of slideDuration. Independent of holding: the runner " +
             "keeps ducking under a long overhang whether or not they are still pressing.")]
    [SerializeField] float slideBlockedMaxMultiplier = 3f;
    [Range(0.1f, 0.95f)]
    [Tooltip("How far through the slide clip the pose freezes while the slide is being stretched. " +
             "Whatever is left plays out after the stretch ends, and the capsule stays low for it — " +
             "so this is what decides how long the runner keeps sliding after the way up is clear. " +
             "Set late on purpose: the clip barely rises (pose 0.515 to 0.633 across the whole take, " +
             "never above 0.39 of standing height), so freezing near the end still holds a low pose " +
             "and leaves almost no tail to sit through.")]
    [SerializeField] float slideHoldPoint = 0.85f;
    [Tooltip("After a slide that ran longer than slideDuration, the runner must be back on their feet " +
             "for this long before another slide will start. Without it, spamming the button chains " +
             "one extended slide straight into the next and the runner never stands up at all — the " +
             "extension is driven by the button being down at any point during the slide, so a rapid " +
             "tap stretches it just as a hold does. Only long slides pay this; a single quick tap " +
             "runs its normal length and is followed by nothing.")]
    [SerializeField] float slideRecoveryTime = 0.2f;
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
    [Range(0.25f, 4f)]
    [Tooltip("Playback multiplier for the roll clip. 2 plays it twice as fast. Pushed into the " +
             "animator's RollSpeed parameter, and every roll deadline is the clip length divided by " +
             "it, so the state machine and this controller cannot drift apart.")]
    [SerializeField] float rollPlaybackSpeed = 2f;
    [Range(0f, 1f)]
    [Tooltip("How much of the roll clip's out-of-plane lean is cancelled. 1 flattens the tumble into " +
             "the plane the runner travels in; 0 leaves the clip untouched. The clip is a shoulder " +
             "roll, and the lane is 2.5D, so there is nowhere for that lean to go.")]
    [SerializeField] float rollLevelStrength = 1f;
    [Range(0f, 0.5f)]
    [Tooltip("Share of the roll over which the correction is eased back out, so handing back to the " +
             "run does not snap.")]
    [SerializeField] float rollLevelReleaseShare = 0.2f;

    [Header("Landing")]
    [Tooltip("A slide asked for in mid-air fires on touchdown instead of being lost to the landing " +
             "frame. Only airborne presses are held, and only this briefly. A press made while " +
             "already sliding is always dropped, which is what stops a double tap producing a " +
             "second slide when the first one ends.")]
    [SerializeField] float slideLandingGrace = 0.2f;
    [Tooltip("Same idea for jump. Without it a jump pressed on the touchdown frame is dropped " +
             "outright, which is the main way a press appears to do nothing at all.")]
    [SerializeField] float jumpLandingGrace = 0.15f;

    [Header("Crashing")]
    [Tooltip("A surface counts as a wall when its normal points back down the lane at least this much. " +
             "-1 is dead-on frontal; roofs and floors point up, so they never qualify.")]
    [SerializeField] float wallNormalThreshold = -0.5f;
    [Tooltip("Extra slack above stepOffset before a ledge is treated as a wall instead of a step-up.")]
    [SerializeField] float ledgeTolerance = 0.05f;

    CharacterController cc;
    Animator animator;
    // Optional. A player without a built ragdoll simply freezes on death, as it always did.
    PlayerRagdoll ragdoll;

    float verticalVelocity;
    float lastGroundedTime;
    PlayerInputRouter inputRouter;
    float slideEndsAt = -1f;
    // When the current slide began. Elapsed time is measured from here rather than from the
    // deadline, because the deadline moves while the slide is being extended.
    float slideStartedAt;
    // How much of the slide animation has actually played. Stops advancing while the pose is frozen,
    // which is what lets the recovery still get its full run after a long hold.
    float slideClipTime;
    // Latched so a runner stuck under geometry past every allowance complains once, not every frame.
    bool warnedSlideStuck;
    // Set when a long slide ends. Until this passes, the runner has to stay on their feet.
    float slideRecoveryUntil = -1f;
    // Whether the slide in progress was actually made to outlast its clip, by a hold or by geometry.
    // Latched during the slide rather than worked out from its total length at the end: an ordinary
    // slide measures a frame or two over slideDuration purely from frame quantisation, and comparing
    // totals charged a plain 0.70s tap as if it had been a long one.
    bool slideWasProlonged;
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
    // Float parameter the Roll state multiplies its own speed by. Added by Set Up Landing Roll.
    const string RollSpeedParameter = "RollSpeed";
    // Float the Slide state multiplies its own speed by. 0 holds the pose for a stretched slide.
    const string SlideSpeedParameter = "SlideSpeed";
    // Bool that keeps the Slide state alive; its exit used to be an unconditional clip-time one.
    const string SlidingParameter = "Sliding";

    // A mid-air slide press, held until touchdown or until it goes stale. Never set by a press
    // made while already sliding, and cleared the moment it is used.
    float airborneSlideAt = -999f;
    // Same, for jump.
    float airborneJumpAt = -999f;


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

    // Pivot for the roll levelling: rotating this carries the whole skeleton with it.
    Transform hipsBone;
    // How much mesh sits above the head bone (skull, hair). Calibrated from the standing pose,
    // because the head bone alone sits well below the top of the model.
    float headTopOffset;
    bool headOffsetCalibrated;

    public bool IsSliding => Time.time < slideEndsAt;
    public bool IsGrounded => cc.isGrounded || Time.time - lastGroundedTime < coyoteTime;

    /// <summary>
    /// World velocity the runner is carrying. Read by the ragdoll on death so the collapse continues
    /// the motion instead of dropping from a standstill — the same vector Update feeds to cc.Move.
    /// </summary>
    public Vector3 CurrentVelocity => new Vector3(runSpeed, verticalVelocity, 0f);

    /// <summary>True for the length of the roll clip after a long fall.</summary>
    public bool IsRolling => Time.time < rollEndsAt;

    /// <summary>
    /// True while the runner is serving out the mandatory stand after a long slide. Nothing else
    /// reads it — it exists so the state is inspectable from a test or a debug readout, because
    /// "the slide did not start" is otherwise indistinguishable from a dropped input.
    /// </summary>
    public bool IsSlideRecovering => Time.time < slideRecoveryUntil;

    /// <summary>Jump and slide are swallowed while this holds — the outro, or a landing roll's brief lock.</summary>
    bool ActionsBlocked => actionsLocked || Time.time < rollInputLockUntil;

    /// <summary>
    /// 0 at the start of a slide, 1 once a normal slide's worth of time has passed. Reads 1 when not
    /// sliding, and stays at 1 through an extension — measured from the start rather than back from
    /// the deadline, because the deadline moves while a slide is held or blocked.
    /// </summary>
    public float SlideProgress =>
        IsSliding && slideDuration > 0f
            ? Mathf.Clamp01((Time.time - slideStartedAt) / slideDuration)
            : 1f;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();
        // Asked whether the slide button is still down. The router is the only thing that knows,
        // because it is the only thing that sees both the keyboard and the on-screen button.
        inputRouter = GetComponent<PlayerInputRouter>();
        ragdoll = GetComponent<PlayerRagdoll>();

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
            // Cleared after EndSlide, which may have just charged one. Losing control is not a
            // penalty the player should still be paying when they get it back.
            slideRecoveryUntil = -1f;
            airborneSlideAt = -999f;
            airborneJumpAt = -999f;
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
            airborneJumpAt = -999f;
        }
    }

    /// <summary>
    /// Reads the roll length off the clip rather than trusting a number typed into the inspector,
    /// so retiming or replacing the animation cannot leave the input lock running long or short.
    ///
    /// Also pushes the playback speed into the animator, and divides the length by it. Those two
    /// have to happen together: the Roll state's speed and the deadlines derived from it are the
    /// same number, and if only one of them changed the levelling would still be correcting a pose
    /// the state machine had already handed back to Run.
    ///
    /// Re-run on every roll, so dialling the speed in the inspector takes effect on the next one.
    /// </summary>
    void ResolveRollDuration()
    {
        float speed = Mathf.Max(0.01f, rollPlaybackSpeed);

        rollDuration = rollDurationFallback / speed;
        if (animator == null || animator.runtimeAnimatorController == null) return;

        if (HasFloatParameter(RollSpeedParameter)) animator.SetFloat(RollSpeedParameter, speed);

        foreach (var clip in animator.runtimeAnimatorController.animationClips)
        {
            if (clip == null || clip.name != RollClipName) continue;
            rollDuration = clip.length / speed;
            return;
        }
    }

    /// <summary>
    /// Guards the SetFloat above. A controller that has not been through Set Up Landing Roll has no
    /// RollSpeed parameter, and writing a missing one logs on every roll.
    /// </summary>
    bool HasFloatParameter(string name)
    {
        foreach (var parameter in animator.parameters)
            if (parameter.type == AnimatorControllerParameterType.Float && parameter.name == name)
                return true;
        return false;
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
        ResolveRollDuration();   // picks up an inspector change to the playback speed
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

    /// <summary>Full strength through the body of the roll, eased in and out at its ends.</summary>
    float RollLevelWeight()
    {
        if (rollLevelStrength <= 0f || !IsRolling || rollDuration <= 0f) return 0f;

        float ramp = rollDuration * Mathf.Clamp(rollLevelReleaseShare, 0f, 0.5f);
        if (ramp <= 0f) return rollLevelStrength;

        float remaining = rollEndsAt - Time.time;
        float elapsed = rollDuration - remaining;

        // Eased at both ends, not just the exit. The clip's very first frame already carries about
        // 23 degrees of pelvis twist, so snapping the correction on at entry pops exactly as much
        // as dropping it at the handoff to Run.
        return rollLevelStrength
               * Mathf.Min(Mathf.Clamp01(elapsed / ramp), Mathf.Clamp01(remaining / ramp));
    }

    /// <summary>
    /// Flattens the roll clip's corkscrew back into the plane the runner travels in.
    ///
    /// "Quick Roll To Run" is a shoulder roll. Its pitch sweeps a clean 360 degrees in the travel
    /// plane — the somersault itself is correct — but the body also leans up to 46 degrees out of
    /// that plane, twice and in opposite directions, and comes up twisted. That corkscrew is what
    /// reads as rolling sideways. It lives entirely in the pose: applyRootMotion is off, so no root
    /// curve reaches the character, and nothing done to the capsule's own orientation can touch it.
    ///
    /// The fix rotates the spine onto its own projection into the travel plane, pivoting on the
    /// hips. Both of those choices were arrived at by measurement, and both matter:
    ///
    ///   Axis. Rotating about the travel axis cannot work. Mid-roll the spine points forward, and a
    ///   rotation about forward then spins the body along its own length without moving the spine at
    ///   all — measured, it drove the lean from 46 degrees up to 73. The rotation has to be the one
    ///   that maps the spine onto its target, which is what FromToRotation gives.
    ///
    ///   Pivot. The hips, not the root. The root sits at the feet, so pivoting there swings the
    ///   whole body off the lane and pushes feet through the floor. The hips are the mass centre,
    ///   so the correction untwists rather than translates.
    ///
    /// It takes two stages, because aligning the spine only fixes where the spine points and leaves
    /// rotation about the spine completely free — measurement confirmed the twist was bit-identical
    /// before and after stage one. That leftover freedom is the spin: the pelvis swings about 140
    /// degrees about the spine and back within a sixth of the clip, with dot(hips.right, lateral)
    /// running 0.92 to -0.46 and back to 1.00. Stage two removes it.
    ///
    /// The twist is read off the pelvis rather than the shoulder line. The pelvis is rigid, so a
    /// mid-roll tuck cannot drag its axis around, and both references agreed on the shape while
    /// disagreeing by about 25 degrees at the ends — that gap is the natural shoulder counter-
    /// rotation of a run, which should be preserved, not flattened.
    ///
    /// Measured at full strength, both stages, against the raw clip: lean 46 degrees to 0, twist
    /// 167 degrees to 0, worst foot clearance 0.07 to 0.06, and lateral offset 0.79 to 0.72. The
    /// body ends up more centred than the untouched clip, which is why this is on by default.
    /// </summary>
    void LevelRollPose()
    {
        float weight = RollLevelWeight();
        if (weight <= 0f || hipsBone == null || headBone == null) return;

        var lateral = transform.right;

        // Stage one: swing the spine into the plane the runner travels in.
        var spine = headBone.position - hipsBone.position;
        if (spine.sqrMagnitude < 1e-6f) return;
        spine.Normalize();

        var flat = spine - lateral * Vector3.Dot(spine, lateral);
        // A spine lying along the lateral axis has no in-plane direction to aim at. Leave the pose
        // alone rather than pick one arbitrarily and snap the model.
        if (flat.sqrMagnitude < 1e-6f) return;
        flat.Normalize();

        // Rotating the hips carries every descendant with it about the hip joint, which is the
        // pivot we want; the hips' own position is left where the clip put it.
        hipsBone.rotation = Quaternion.Slerp(
            Quaternion.identity, Quaternion.FromToRotation(spine, flat), weight) * hipsBone.rotation;

        // Stage two: take the corkscrew out of the one axis stage one left free. Re-read the spine
        // first — stage one just moved it.
        var axis = (headBone.position - hipsBone.position).normalized;
        var pelvis = hipsBone.right;
        var pelvisPerp = pelvis - axis * Vector3.Dot(pelvis, axis);
        var lateralPerp = lateral - axis * Vector3.Dot(lateral, axis);
        if (pelvisPerp.sqrMagnitude < 1e-6f || lateralPerp.sqrMagnitude < 1e-6f) return;

        float twist = Vector3.SignedAngle(pelvisPerp.normalized, lateralPerp.normalized, axis);
        hipsBone.rotation = Quaternion.AngleAxis(twist * weight, axis) * hipsBone.rotation;
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

        LevelRollPose();

        if (!slideCapsuleFollowsPose || !IsSliding) return;

        float target = Mathf.Clamp(MeasurePoseHeight() + slidePoseClearance, cc.radius * 2f, standHeight);
        cc.height = target;
        cc.center = new Vector3(standCenter.x, target * 0.5f, standCenter.z);
    }

    void Update()
    {
        if (!controlEnabled) return;

        // The ragdoll switches the controller off and owns the body until the respawn. Moving a
        // disabled CharacterController is an error per frame, not a silent no-op, so this is the
        // backstop for any path that hands control back before the body is handed back.
        if (!cc.enabled) return;

        if (cc.isGrounded) lastGroundedTime = Time.time;

        TrackAirborne();

        MaintainSlide();

        // Fire an action asked for in the air the moment the feet are down. Each is consumed before
        // its attempt, so a failure cannot leave it to retry on a later frame.
        //
        // Jump wins when both are held, and the slide is dropped rather than queued behind it —
        // firing both would put the runner into a slide the instant they left the ground.
        if (cc.isGrounded && !ActionsBlocked && Time.time - airborneJumpAt <= jumpLandingGrace)
        {
            airborneJumpAt = -999f;
            airborneSlideAt = -999f;
            TryJump();
        }
        else if (cc.isGrounded && !ActionsBlocked && Time.time - airborneSlideAt <= slideLandingGrace)
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

        PinLane();

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

    /// <summary>2.5D: nothing may shift the runner off the lane plane.</summary>
    void PinLane()
    {
        var p = transform.position;
        if (!Mathf.Approximately(p.z, lockedZ))
            transform.position = new Vector3(p.x, p.y, lockedZ);
    }

    public void Jump()
    {
        if (!controlEnabled || ActionsBlocked) return;
        if (TryJump()) return;

        // Rejected. Hold exactly one kind, the same kind the slide holds: a press made in the air,
        // which is a player asking to jump the moment they land. That covers the touchdown frame,
        // where cc.isGrounded can still read false because the input router and this script both
        // run in Update in no guaranteed order — so whether the press lands depends on which ran
        // first, and that is what makes it feel intermittent rather than broken.
        //
        // A press rejected mid-slide is dropped outright, so it cannot resurface as an unasked-for
        // hop when the slide expires.
        if (!IsSliding) airborneJumpAt = Time.time;
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
            // Same reason MaintainSlide refuses to end a blocked slide: standing up to jump while
            // something is directly overhead drives the capsule into it.
            if (BlockedFromStanding()) return false;
            EndSlide();
        }

        verticalVelocity = Mathf.Sqrt(2f * jumpHeight * -gravity);
        lastGroundedTime = -999f;
        airborneJumpAt = -999f;   // spent, so leaving the ground cannot re-fire it on the way down
        if (animator != null) animator.SetTrigger("Jump");
        return true;
    }

    /// <summary>
    /// Ends the slide, or keeps it going. Two independent reasons to keep going:
    ///
    ///   Held. The player is still asking for it, up to slideHoldMaxMultiplier. This is the one
    ///   players drive, and it is what makes long ducking stretches designable.
    ///
    ///   Blocked. Standing up would put the capsule inside something, up to
    ///   slideBlockedMaxMultiplier. Nothing to do with input — the runner keeps ducking under a long
    ///   overhang whether or not they are still pressing, because the alternative is expanding into
    ///   solid geometry.
    ///
    /// Past every allowance and still blocked, the slide continues anyway and logs once. Forcing the
    /// stand there would jam the capsule inside the obstacle, and a tunnel longer than the configured
    /// allowance is a level-building mistake worth seeing in the console rather than silently
    /// absorbing.
    /// </summary>
    void MaintainSlide()
    {
        if (slideEndsAt < 0f) return;   // not sliding

        float elapsed = Time.time - slideStartedAt;
        bool blocked = BlockedFromStanding();
        bool held = inputRouter != null && inputRouter.SlideHeld;

        float allowance = slideDuration;
        if (held)
            allowance = Mathf.Max(allowance, slideDuration * Mathf.Max(1f, slideHoldMaxMultiplier));
        if (blocked)
            allowance = Mathf.Max(allowance, slideDuration * Mathf.Max(1f, slideBlockedMaxMultiplier));

        // Blocked overrides the ceiling entirely — expanding into geometry is never the answer — but
        // it says so once, because a tunnel that long is a level-building mistake.
        bool overAllowance = elapsed >= allowance;
        bool stretch = (held || blocked) && (!overAllowance || blocked);

        if (blocked && overAllowance && !warnedSlideStuck)
        {
            warnedSlideStuck = true;
            Debug.LogWarning($"[PlayerController] Still cannot stand {elapsed:F2}s into a slide, past the " +
                             $"{slideBlockedMaxMultiplier:F0}x allowance. Staying down rather than expanding " +
                             $"into geometry — the overhang near x={transform.position.x:F1} is longer than " +
                             $"the allowance permits.");
        }

        // Clip time only advances when the pose is not frozen, so it is the honest measure of how
        // much of the animation has actually played.
        bool freeze = stretch && slideClipTime >= slideDuration * Mathf.Clamp01(slideHoldPoint);
        if (!freeze) slideClipTime += Time.deltaTime;
        SetSlideSpeed(freeze ? 0f : 1f);

        // Two ways a slide gets prolonged, and both count. Freezing the pose is the one that does the
        // work — every frame held there is a frame the clip does not advance, so the slide outlives its
        // own animation. The second catches a slide still being stretched past the point it would
        // otherwise have ended. Either way the player leaned on it, and owes the recovery.
        if (freeze || (stretch && elapsed >= slideDuration)) slideWasProlonged = true;

        if (stretch)
        {
            // Hold the deadline a frame ahead, so IsSliding stays true and the decision is re-made
            // next frame against fresh geometry and fresh input.
            slideEndsAt = Time.time + Mathf.Max(Time.deltaTime, 0.001f);
            return;
        }

        // Not stretching any more. The capsule stays low until the clip has played its recovery,
        // otherwise the runner would stand up while the animation was still on the floor.
        if (slideClipTime < slideDuration)
        {
            slideEndsAt = Time.time + Mathf.Max(Time.deltaTime, 0.001f);
            return;
        }

        EndSlide();
    }

    /// <summary>
    /// Scales the Slide state's playback. 0 freezes the pose, which is how a stretched slide holds
    /// its shape instead of running the recovery early: the clip is 0.42s long and its exit used to
    /// be unconditional, so without this a held slide showed the runner up and running while the
    /// capsule was still crouched.
    /// </summary>
    void SetSlideSpeed(float speed)
    {
        if (animator == null || !HasFloatParameter(SlideSpeedParameter)) return;
        animator.SetFloat(SlideSpeedParameter, speed);
    }

    /// <summary>
    /// Holds the Slide state open. Guarded like the float parameters: a controller that predates
    /// Set Up Slide Hold has no such bool, and writing a missing one logs on every slide.
    /// </summary>
    void SetSlidingFlag(bool sliding)
    {
        if (animator == null) return;
        foreach (var parameter in animator.parameters)
            if (parameter.type == AnimatorControllerParameterType.Bool && parameter.name == SlidingParameter)
            {
                animator.SetBool(SlidingParameter, sliding);
                return;
            }
    }

    /// <summary>
    /// Whether expanding back to the standing capsule would intersect something.
    ///
    /// The controller's own collider is switched off for the test — the probe capsule is the standing
    /// one, which overlaps the crouched capsule by definition, so leaving it on would report blocked
    /// every time. Teleport already uses the same disable-and-restore trick. The base is lifted clear
    /// of the floor and the radius shaved, so the ground the runner is standing on is not mistaken
    /// for a ceiling and a shoulder brushing a wall is not either.
    /// </summary>
    bool BlockedFromStanding()
    {
        if (standHeight - cc.height <= 0.001f) return false;

        bool wasEnabled = cc.enabled;
        cc.enabled = false;

        // This project runs with Physics.autoSyncTransforms off, so a query issued straight after a
        // transform or collider change can read stale physics state — measured: the same volume
        // reported clear from CheckCapsule while OverlapCapsule found the ceiling in it. Syncing
        // first is what makes the answer trustworthy on the frame it is asked.
        Physics.SyncTransforms();

        float radius = Mathf.Max(0.01f, cc.radius * 0.9f);
        float bottom = radius + 0.05f;
        float top = Mathf.Max(bottom, standHeight - radius);
        var p0 = transform.position + Vector3.up * bottom;
        var p1 = transform.position + Vector3.up * top;
        bool blocked = Physics.CheckCapsule(p0, p1, radius, ~0, QueryTriggerInteraction.Ignore);

        cc.enabled = wasEnabled;
        return blocked;
    }

    bool TrySlide()
    {
        // Real ground contact only. Coyote time is a jump forgiveness window; using it here
        // would let the runner duck in mid-air.
        //
        // Returning false while already sliding is what makes a second tap a no-op rather
        // than a queued second slide — the trigger is never set, so nothing can replay it.
        if (!cc.isGrounded || IsSliding) return false;

        // Standing out the recovery from the last long slide. Refused rather than queued, so holding
        // the button through the recovery does not buy a slide the instant it expires.
        if (IsSlideRecovering) return false;

        slideStartedAt = Time.time;
        slideClipTime = 0f;
        slideWasProlonged = false;
        warnedSlideStuck = false;
        SetSlideSpeed(1f);
        SetSlidingFlag(true);
        slideEndsAt = Time.time + slideDuration;
        cc.height = standHeight * slideHeightFraction;
        cc.center = new Vector3(standCenter.x, cc.height * 0.5f, standCenter.z);
        if (animator != null) animator.SetTrigger("Slide");
        return true;
    }

    /// <summary>
    /// Stands the runner back up, and charges a recovery period if the slide they are leaving had been
    /// prolonged. Holding and blocked-under-geometry both count: either way the runner spent longer
    /// down than one slide is worth, and the point of the recovery is that they have to be seen back
    /// on their feet before they may go down again.
    /// </summary>
    void EndSlide()
    {
        // Guarded on actually having been sliding. EndSlide is also called when nothing is sliding at
        // all — control being taken away, a roll starting, a respawn — and a flag left over from an
        // earlier slide would otherwise charge a recovery for nothing.
        if (slideEndsAt >= 0f && slideWasProlonged && slideRecoveryTime > 0f)
            slideRecoveryUntil = Time.time + slideRecoveryTime;

        slideWasProlonged = false;
        slideEndsAt = -1f;
        slideClipTime = 0f;
        SetSlideSpeed(1f);   // never leave the state frozen for the next slide to inherit
        SetSlidingFlag(false);
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

        // A canopy launches the runner off its top face. Tested first, but only the booster itself
        // decides whether this contact counts as the top — a hit on its front edge falls straight
        // through to the wall rules below and kills, exactly like any other obstacle.
        // GetComponentInParent, so the collider is free to be a child of the prefab root.
        var canopy = hit.collider.GetComponentInParent<CanopyBooster>();
        if (canopy != null && canopy.TryConsumeBounce(hit.normal))
        {
            Bounce(canopy.BounceHeight);
            return;
        }

        // Roofs, floors and ceilings all fail this test — only walls pass.
        if (hit.normal.x > wallNormalThreshold) return;

        // If we could step or already cleared it, it is not a crash.
        float feetY = transform.position.y;
        float surfaceTop = hit.collider.bounds.max.y;
        if (surfaceTop - feetY <= cc.stepOffset + ledgeTolerance) return;

        // The normal goes with it: it points out of the wall, which is the direction the runner
        // should be thrown, and only this method knows it.
        if (RunManager.Instance != null) RunManager.Instance.Kill(hit.normal);
    }

    /// <summary>
    /// Ends the run and, if a ragdoll has been built, hands the body over to physics. Called by
    /// RunManager rather than reached directly, so run state and the visible collapse stay in step.
    /// </summary>
    public void Die(Vector3 impactNormal)
    {
        // Read before control is dropped — EnableControl(false) zeroes verticalVelocity, and the
        // fall the runner was in the middle of is half of what makes the collapse read right.
        var velocity = CurrentVelocity;

        EnableControl(false);
        if (ragdoll != null) ragdoll.Activate(velocity, impactNormal);
    }

    /// <summary>
    /// Takes the body back off physics, ready to be teleported to the checkpoint. Separate from
    /// Teleport because the order matters: the skeleton has to be back in its rest pose before the
    /// capsule is moved, or the ragdoll's last sprawl is what gets carried to the spawn point.
    /// </summary>
    public void Revive()
    {
        if (ragdoll != null) ragdoll.Deactivate();
    }

    /// <summary>
    /// Launches the runner off a canopy. Not a jump: it asks for no ground under the feet and does
    /// not consult ActionsBlocked, because the canopy is doing the work, not the player. Height is
    /// an apex above the point of contact, converted through the same gravity the jump uses so the
    /// two stay comparable.
    /// </summary>
    public void Bounce(float height)
    {
        EndSlide();
        verticalVelocity = Mathf.Sqrt(2f * Mathf.Max(0f, height) * -gravity);

        // No coyote hop off the launch, and no buffered press cashing itself in at the top of it.
        lastGroundedTime = -999f;
        airborneJumpAt = -999f;
        airborneSlideAt = -999f;

        // The canopy absorbs the fall, so the air-time clock restarts here. Without this, a long
        // drop onto a canopy would still be carrying that fall when the runner next touches down
        // and would fire a landing roll the canopy has already cancelled.
        leftGroundAt = Time.time;

        if (animator != null) animator.SetTrigger("Jump");
    }

    /// <summary>Hard reposition used on respawn — CharacterController ignores transform writes while enabled.</summary>
    public void Teleport(Vector3 position)
    {
        EndSlide();
        slideRecoveryUntil = -1f;   // a fresh start, not the tail of the slide they died in
        airborneSlideAt = -999f;   // a press from before the death must not fire on respawn
        airborneJumpAt = -999f;
        // Dying in mid-air used to leave leftGroundAt set, so TrackAirborne measured the fall as
        // running from before the death and fired a landing roll on the respawn's first grounded
        // frame. The respawn is a fresh start, not the end of a fall.
        leftGroundAt = -1f;
        verticalVelocity = 0f;
        cc.enabled = false;
        transform.position = position;
        lockedZ = position.z;
        cc.enabled = true;
    }
}
