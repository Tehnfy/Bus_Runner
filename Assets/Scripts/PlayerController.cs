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
    // Float parameter the Roll state multiplies its own speed by. Added by Set Up Landing Roll.
    const string RollSpeedParameter = "RollSpeed";

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

    // Pivot for the roll levelling: rotating this carries the whole skeleton with it.
    Transform hipsBone;
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
