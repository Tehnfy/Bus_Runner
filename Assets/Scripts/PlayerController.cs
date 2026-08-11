using UnityEngine;

/// <summary>
/// Auto-runs the player along +X. Jump and slide are one-shot actions driven by
/// PlayerInputRouter. Movement is XY only — Z is pinned so the 2.5D lane holds.
/// </summary>
/// <summary>Which hand, if any, is planted on the ground during a slide.</summary>
public enum SlideHand
{
    None,
    RightHand,
    LeftHand,
    Both,
}

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
    [Tooltip("After a slide the player held long, the runner must be back on their feet for this long " +
             "before another slide will start. Only held long slides pay it — a quick tap, and a slide " +
             "prolonged only by an overhang, cost nothing. The slide following a held long one also " +
             "ignores the button entirely, so two long slides can never run back to back.")]
    [SerializeField] float slideRecoveryTime = 0.2f;
    [Tooltip("While sliding, size the capsule from the animated pose each frame instead of holding " +
             "slideHeightFraction. Arms are ignored — a raised hand should not make the runner 'taller'.")]
    [SerializeField] bool slideCapsuleFollowsPose = true;
    [Tooltip("Clearance kept above the highest measured bone while pose-driven.")]
    [SerializeField] float slidePoseClearance = 0.06f;

    [Header("Slide pitch")]
    [Tooltip("While sliding, tilt the body to match the slope underfoot — nose-down going downhill, " +
             "nose-up going uphill. Applied to the bones, not the capsule: the capsule's orientation is " +
             "pinned every LateUpdate so the 2.5D lane holds, and tipping it would fight that.")]
    [SerializeField] bool slidePitchFollowsGround = true;
    [Tooltip("Largest tilt allowed either way, in degrees. A cap rather than a scale, so ordinary " +
             "slopes read at their true angle and only absurd ones are reined in.")]
    [Range(0f, 80f)]
    [SerializeField] float slidePitchMaxAngle = 35f;
    [Tooltip("Approach time onto a new slope. Short — cresting a ramp should look like the body " +
             "following the ground, not lagging behind it.")]
    [SerializeField] float slidePitchSmoothTime = 0.07f;
    [Tooltip("Time to level back out once there is no ground to follow. This is what makes flying off " +
             "a ramp settle into a flat slide whatever angle it was launched at, and it is deliberately " +
             "slower than the approach: coming off a lip should ease out, not snap.")]
    [SerializeField] float slidePitchRecoverTime = 0.25f;
    [Tooltip("How far below the feet to look for the surface. Only needs to reach through the small " +
             "gap a CharacterController keeps between itself and the floor.")]
    [SerializeField] float slidePitchProbeDepth = 0.6f;

    [Header("Slide ground contact")]
    [Tooltip("Sink the body during a slide until it actually touches the surface. Measured: standing, " +
             "the lowest vertex of the mesh sits 0.005 below the road — contact. Sliding, the clip's " +
             "authored pose leaves it 0.024 to 0.040 above it, which on a 1.69-tall model reads as the " +
             "runner hovering. Nothing to do with the capsule; the capsule rests where it should.")]
    [SerializeField] bool slideHugsGround = true;
    [Tooltip("Gap deliberately left between the body and the surface. 0 puts it in contact.")]
    [SerializeField] float slideGroundClearance = 0f;
    [Tooltip("How far the mesh surface hangs below the lowest bone. The drop is worked out from bone " +
             "positions, because baking 15,882 skinned vertices every frame to find the true lowest one " +
             "is far too expensive for a few centimetres of polish.\n\n" +
             "Calibrated by doing exactly that bake once and comparing: through the body of the slide the " +
             "ankle is the lowest bone and the skin sits 0.029 to 0.043 below it, so 0.035 lands the " +
             "contact within 0.008 all the way through. Earlier in the clip the lowest bone is a toe, " +
             "where the skin is only 0.004 to 0.019 below — but those frames are the stand-to-prone " +
             "transition and the clamp leaves them alone, because subtracting 0.035 there puts the " +
             "estimate under the floor and the drop clamps to zero.")]
    [SerializeField] float slideMeshBelowBone = 0.035f;
    [Tooltip("Most the body may ever be sunk, as a backstop. A bad probe should make the runner hover, " +
             "never bury them.")]
    [SerializeField] float slideHugMaxDrop = 0.12f;
    [Tooltip("Approach time for the drop, so entering and leaving the slide eases rather than pops.")]
    [SerializeField] float slideHugSmoothTime = 0.05f;

    [Header("Slide hand contact")]
    [Tooltip("Which hand is planted on the ground during a slide. Solved with a two-bone IK on the arm " +
             "after the body has been pitched and dropped, so it lands on the surface the runner is " +
             "actually lying on.")]
    [SerializeField] SlideHand slideGroundHand = SlideHand.RightHand;
    [Tooltip("How far the palm surface sits below the hand bone. Same idea as slideMeshBelowBone: the " +
             "bone is what can be positioned, the skin is what is seen. Calibrated off the 1,910 vertices " +
             "the right hand chain actually owns — through the contact frames the palm sits 0.032 to " +
             "0.050 below the bone, so this sits in the middle of that.")]
    [SerializeField] float slideHandPalmOffset = 0.04f;
    [Range(0.5f, 1f)]
    [Tooltip("Fraction of the arm's length the reach may use before the correction fades out. Has to be " +
             "near 1: measured, the right arm is 0.490 long while the shoulder rides 0.388 to 0.475 above " +
             "the ground, so simply touching the surface needs 90 to 100 percent extension. An earlier " +
             "0.92 here, with a wide fade band, cut the correction to nothing and the hand never moved " +
             "at all. The fade now covers only the last 8 percent of the arm, which is enough to drop it " +
             "cleanly when the shoulder is genuinely too high to reach — as it is during the first few " +
             "frames, where the shoulder is 0.63 to 1.34 up and no arm would do.")]
    [SerializeField] float slideHandReachLimit = 0.99f;
    [Tooltip("Approach time for the hand correction, so it eases in and out with the slide.")]
    [SerializeField] float slideHandSmoothTime = 0.09f;

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
    // Whether the slide in progress was made to outlast its clip by the player holding the button.
    // Latched during the slide rather than worked out from its total length at the end: an ordinary
    // slide measures a frame or two over slideDuration purely from frame quantisation, and comparing
    // totals charged a plain 0.70s tap as if it had been a long one.
    bool slideProlongedByHold;
    // Set when a held long slide ends, cleared when an ordinary one does. While set, the slide button
    // cannot extend anything — so two long slides can never run back to back.
    bool slideHoldDisarmed;

    // Pitch currently applied to the body, in degrees, and the SmoothDamp velocity carrying it. Kept
    // as state rather than recomputed each frame because levelling out in mid-air is a decay from
    // wherever the last slope left it, not a jump to zero.
    float slidePitch;
    float slidePitchVelocity;

    // How far the body is currently sunk to meet the ground, and its SmoothDamp velocity.
    float slideHugDrop;
    float slideHugVelocity;

    // Arm chains for the planted hand, cached with their bone lengths. Bones do not change length, so
    // the reach only has to be measured once.
    Transform rightUpperArm, rightLowerArm, rightHand;
    Transform leftUpperArm, leftLowerArm, leftHand;
    float rightArmReach, leftArmReach;

    // Blend on the hand correction, eased so it arrives and leaves with the slide.
    float slideHandWeight;
    float slideHandWeightVelocity;
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
    // Toes, for the ground-contact test only. Absent from the silhouette set, which answers "how tall
    // is this pose" and has no business consulting a toe.
    //
    // Hands and forearms were in here on the theory that a trailing arm could be the lowest thing.
    // Measured across the whole slide clip: they never are, and including them changed the answer by
    // exactly nothing, so they are gone.
    static readonly HumanBodyBones[] ExtremityBoneIds =
    {
        HumanBodyBones.LeftToes, HumanBodyBones.RightToes,
    };
    Transform[] toeBones;
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

    /// <summary>
    /// True while the slide button cannot extend anything, because the last slide was a held long one.
    /// Cleared by serving an ordinary slide. Exposed for the same reason as IsSlideRecovering — a hold
    /// that quietly does nothing is otherwise indistinguishable from a dropped input.
    /// </summary>
    public bool IsSlideHoldDisarmed => slideHoldDisarmed;

    /// <summary>Tilt currently applied to the body by the slide pitch, in degrees. Negative is nose-down.</summary>
    public float SlidePitch => slidePitch;

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
            // Cleared after EndSlide, which may have just charged them. Losing control is not a
            // penalty the player should still be paying when they get it back.
            slideRecoveryUntil = -1f;
            slideHoldDisarmed = false;
            // Snapped rather than eased: control coming back is a fresh start, and easing from an old
            // slope would show the runner righting themselves from a tilt they never earned.
            slidePitch = 0f;
            slidePitchVelocity = 0f;
            slideHugDrop = 0f;
            slideHugVelocity = 0f;
            slideHandWeight = 0f;
            slideHandWeightVelocity = 0f;
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

        toeBones = new Transform[ExtremityBoneIds.Length];
        for (int i = 0; i < ExtremityBoneIds.Length; i++)
            toeBones[i] = animator.GetBoneTransform(ExtremityBoneIds[i]);

        rightUpperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        rightLowerArm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        leftUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        leftLowerArm = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);

        rightArmReach = ArmReach(rightUpperArm, rightLowerArm, rightHand);
        leftArmReach = ArmReach(leftUpperArm, leftLowerArm, leftHand);
    }

    static float ArmReach(Transform upper, Transform lower, Transform hand)
    {
        if (upper == null || lower == null || hand == null) return 0f;
        return Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, hand.position);
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
    /// Tilts the body to lie along the ground while sliding, and eases it back to flat when there is
    /// no ground to lie along.
    ///
    /// The tilt goes onto the hips, which carries the whole skeleton rigidly about the hip joint — the
    /// same pivot and the same reason as LevelRollPose. The capsule is left alone: its orientation is
    /// pinned every LateUpdate to hold the 2.5D lane, so a tilt applied there would be overwritten on
    /// the spot.
    ///
    /// The target angle is derived from the surface tangent rather than from the normal's angle, which
    /// makes the sign correct by construction: rotating by SignedAngle(forward, tangent) about the
    /// lateral axis is by definition the rotation that maps the travel direction onto the slope, so
    /// downhill comes out nose-down and uphill nose-up without a hand-picked sign to get wrong.
    ///
    /// Airborne, the target is flat and the approach is slower — that is the whole of the "flying off a
    /// ramp evens out" behaviour, and it works from any launch angle because it decays from wherever
    /// the slope left the body rather than resetting.
    /// </summary>
    void ApplySlidePitch(bool hasGround, Vector3 normal)
    {
        if (!slidePitchFollowsGround || hipsBone == null) return;

        float target = hasGround
            ? Mathf.Clamp(SlopePitch(normal), -slidePitchMaxAngle, slidePitchMaxAngle)
            : 0f;

        float smoothTime = hasGround ? slidePitchSmoothTime : slidePitchRecoverTime;
        slidePitch = Mathf.SmoothDamp(slidePitch, target, ref slidePitchVelocity, Mathf.Max(0.001f, smoothTime));

        if (Mathf.Abs(slidePitch) < 0.05f) return;   // nothing worth writing
        hipsBone.rotation = Quaternion.AngleAxis(slidePitch, transform.right) * hipsBone.rotation;
    }

    /// <summary>
    /// Pitch of the surface along the direction of travel: the angle from forward to the surface
    /// tangent, measured about the lateral axis. Zero on the flat.
    /// </summary>
    float SlopePitch(Vector3 normal)
    {
        var forward = transform.forward;
        var tangent = forward - normal * Vector3.Dot(forward, normal);
        if (tangent.sqrMagnitude < 1e-6f) return 0f;   // travelling straight into the surface
        return Vector3.SignedAngle(forward, tangent.normalized, transform.right);
    }

    /// <summary>
    /// Sinks the body until it meets the surface, so a slide reads as contact rather than a hover.
    ///
    /// The drop is worked out from bone positions rather than from the skinned mesh: baking the mesh
    /// every frame to find its true lowest vertex is far too expensive for what amounts to a few
    /// centimetres of polish, and the offset between the two is stable enough to fold into a constant
    /// (slideMeshBelowBone, measured at about 0.02 across the clip).
    ///
    /// Clamped to positive values, so this only ever lowers the runner. Lifting them would be the wrong
    /// correction in the one case it could fire — the standing pose already dips 0.005 through the road
    /// and nobody has ever noticed, because a foot slightly in the ground reads as contact while a foot
    /// slightly above it reads as levitation.
    ///
    /// Runs after the pitch, which is what decides where the lowest point of the body actually is.
    /// </summary>
    void ApplySlideGroundHug(bool hasGround, Vector3 groundPoint, Vector3 groundNormal)
    {
        if (!slideHugsGround || hipsBone == null) return;

        float target = 0f;
        if (hasGround)
        {
            // Measured against the ground PLANE, not against the ground height under the root. A prone
            // body reaches about half a unit along the lane, so on a 20 degree slope the surface under
            // its far end is 0.18 away from the surface under its middle — comparing heights sank the
            // runner 0.077 downhill and left them hovering 0.046 uphill. The plane costs nothing extra:
            // the probe already returns a point and a normal.
            float clearance = LowestDistanceAbovePlane(groundPoint, groundNormal) - slideMeshBelowBone;

            // The gap is perpendicular to the slope but the body is moved vertically, so it has to be
            // divided back out or a slope would only ever be partly closed.
            float lean = Mathf.Max(0.2f, Vector3.Dot(groundNormal, Vector3.up));
            target = Mathf.Clamp((clearance - slideGroundClearance) / lean, 0f, slideHugMaxDrop);
        }

        slideHugDrop = Mathf.SmoothDamp(slideHugDrop, target, ref slideHugVelocity,
                                       Mathf.Max(0.001f, slideHugSmoothTime));

        if (slideHugDrop <= 0.001f) return;
        // Moving the hips carries every bone with it, the same lever the pitch and the roll levelling use.
        hipsBone.position -= Vector3.up * slideHugDrop;
    }

    /// <summary>
    /// Plants a hand on the ground for the length of the slide.
    ///
    /// Runs last of the three slide corrections, because it has to aim at the surface the body has
    /// actually ended up on — the pitch and the drop both move the shoulder, and solving before them
    /// would put the hand where the shoulder used to be.
    ///
    /// Unity's humanoid IK pass would be the obvious tool and is the wrong one here: it runs inside the
    /// Animator update, before LateUpdate, so it cannot see the pitch or the drop. Solving the two bones
    /// directly keeps everything in one place and in the right order.
    ///
    /// The correction fades rather than straining. Measured, the right arm is 0.490 long while the
    /// shoulder passes 0.388 to 0.475 above the ground through a slide — so late in the clip a planted
    /// hand needs practically the whole arm locked straight down, which reads as a mannequin. Past
    /// slideHandReachLimit the weight drops away and the animation is left to speak for itself.
    /// </summary>
    void ApplySlideHandContact(bool hasGround, Vector3 groundPoint, Vector3 groundNormal)
    {
        if (slideGroundHand == SlideHand.None) return;

        float target = hasGround ? 1f : 0f;
        slideHandWeight = Mathf.SmoothDamp(slideHandWeight, target, ref slideHandWeightVelocity,
                                          Mathf.Max(0.001f, slideHandSmoothTime));
        if (slideHandWeight <= 0.002f) return;

        if (slideGroundHand == SlideHand.RightHand || slideGroundHand == SlideHand.Both)
            PlantHand(rightUpperArm, rightLowerArm, rightHand, rightArmReach, groundPoint, groundNormal);

        if (slideGroundHand == SlideHand.LeftHand || slideGroundHand == SlideHand.Both)
            PlantHand(leftUpperArm, leftLowerArm, leftHand, leftArmReach, groundPoint, groundNormal);
    }

    void PlantHand(Transform upper, Transform lower, Transform hand, float reach,
                   Vector3 groundPoint, Vector3 groundNormal)
    {
        if (upper == null || lower == null || hand == null || reach <= 0f) return;

        float above = Vector3.Dot(hand.position - groundPoint, groundNormal);
        // Already touching, or through the surface. Pulling further would bury it; lifting is not this
        // method's job.
        if (above <= slideHandPalmOffset) return;

        float limit = reach * Mathf.Clamp(slideHandReachLimit, 0.1f, 1f);
        float shoulderAbove = Vector3.Dot(upper.position - groundPoint, groundNormal);

        // Fade on whether the ground is reachable AT ALL, not on whether one particular spot on it is.
        // Judging the specific spot made the hand give up the moment its own position went out of range,
        // so it planted for a third of the slide and then let go; the runner can always just put the hand
        // down closer in. What genuinely cannot be fixed is a shoulder further above the ground than the
        // arm is long — which is exactly the opening frames, where it rides 0.63 to 1.34 up.
        float slack = limit - (shoulderAbove - slideHandPalmOffset);
        float fade = Mathf.Clamp01(slack / (reach * 0.08f));
        if (fade <= 0.002f) return;

        // Where the hand would like to go: straight down onto the surface, keeping its own position.
        var onPlane = groundNormal * slideHandPalmOffset;
        var shoulderFoot = upper.position - groundNormal * shoulderAbove + onPlane;
        var desired = hand.position - groundNormal * (above - slideHandPalmOffset);

        // Drawn in toward the shoulder until the arm can actually get there, so the target stays ON the
        // surface instead of hanging above it. The hand slides inward rather than lifting off.
        float maxAlongPlane = Mathf.Sqrt(Mathf.Max(0f, limit * limit -
                                                      (shoulderAbove - slideHandPalmOffset) *
                                                      (shoulderAbove - slideHandPalmOffset)));
        var alongPlane = desired - shoulderFoot;
        if (alongPlane.magnitude > maxAlongPlane)
            desired = shoulderFoot + alongPlane.normalized * maxAlongPlane;

        // The solve turns the forearm, and the hand is its child, so the wrist would come along for the
        // ride and pitch the fingers into the ground — measured at 0.149 below the surface before this
        // was put back. The animated wrist orientation is the one the pose was authored with, so it is
        // restored rather than recomputed.
        float weight = slideHandWeight * fade;
        var wrist = hand.rotation;
        SolveTwoBone(upper, lower, hand, desired, weight);
        hand.rotation = Quaternion.Slerp(hand.rotation, wrist, weight);
    }

    /// <summary>
    /// Two-bone IK. The elbow position is solved analytically and then each bone is simply aimed, which
    /// avoids the sign and handedness traps of working in angles — the ragdoll's twist limits were a long
    /// enough lesson in those.
    ///
    /// The bend plane is taken from wherever the animation already had the elbow, so the correction
    /// lowers the hand without deciding for itself which way the joint folds.
    /// </summary>
    static void SolveTwoBone(Transform root, Transform mid, Transform tip, Vector3 target, float weight)
    {
        if (weight <= 0.002f) return;

        var pRoot = root.position;
        float upperLength = Vector3.Distance(pRoot, mid.position);
        float lowerLength = Vector3.Distance(mid.position, tip.position);
        if (upperLength < 1e-4f || lowerLength < 1e-4f) return;

        var toTarget = target - pRoot;
        float distance = toTarget.magnitude;
        if (distance < 1e-4f) return;

        var direction = toTarget / distance;
        // Clamped inside what the two bones can span, so the triangle below always has a solution.
        distance = Mathf.Clamp(distance,
                               Mathf.Abs(upperLength - lowerLength) + 1e-3f,
                               upperLength + lowerLength - 1e-3f);
        var reachable = pRoot + direction * distance;

        // Keep the animated bend plane: the elbow's current sideways offset from the new aim line.
        var toElbow = mid.position - pRoot;
        var bend = toElbow - direction * Vector3.Dot(toElbow, direction);
        if (bend.sqrMagnitude < 1e-8f)
        {
            bend = Vector3.Cross(direction, Vector3.up);
            if (bend.sqrMagnitude < 1e-8f) bend = Vector3.Cross(direction, Vector3.forward);
        }
        bend.Normalize();

        // Where the elbow has to sit for both bones to keep their length.
        float along = (upperLength * upperLength - lowerLength * lowerLength + distance * distance) / (2f * distance);
        float across = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
        var elbow = pRoot + direction * along + bend * across;

        var rootFix = Quaternion.FromToRotation(mid.position - pRoot, elbow - pRoot);
        root.rotation = Quaternion.Slerp(Quaternion.identity, rootFix, weight) * root.rotation;

        var midFix = Quaternion.FromToRotation(tip.position - mid.position, reachable - mid.position);
        mid.rotation = Quaternion.Slerp(Quaternion.identity, midFix, weight) * mid.rotation;
    }

    /// <summary>
    /// How far the closest bone sits above the ground plane, measured perpendicular to it. Negative if
    /// something is already through the surface.
    /// </summary>
    float LowestDistanceAbovePlane(Vector3 groundPoint, Vector3 groundNormal)
    {
        float lowest = Vector3.Dot(hipsBone.position - groundPoint, groundNormal);

        if (silhouetteBones != null)
            foreach (var b in silhouetteBones)
                if (b != null) lowest = Mathf.Min(lowest, Vector3.Dot(b.position - groundPoint, groundNormal));

        if (toeBones != null)
            foreach (var b in toeBones)
                if (b != null) lowest = Mathf.Min(lowest, Vector3.Dot(b.position - groundPoint, groundNormal));

        return lowest;
    }

    /// <summary>
    /// Surface normal and height under the feet. The controller is switched off for the cast for the
    /// same reason BlockedFromStanding does it — otherwise the runner's own capsule is the first thing
    /// hit — and the transforms are synced first because this project runs with autoSyncTransforms off.
    ///
    /// One probe serves both the pitch and the ground hug; LateUpdate calls it once and hands the
    /// result to both.
    /// </summary>
    bool ProbeGround(out Vector3 normal, out Vector3 point)
    {
        normal = Vector3.up;
        point = transform.position;

        bool wasEnabled = cc.enabled;
        cc.enabled = false;
        Physics.SyncTransforms();

        float radius = Mathf.Max(0.01f, cc.radius * 0.9f);
        // Starting a sphere cast from a radius up means the sphere begins clear of the floor, so a
        // surface flush against the feet still registers as a hit rather than an initial overlap.
        var origin = transform.position + Vector3.up * (radius + 0.05f);
        bool hit = Physics.SphereCast(origin, radius, Vector3.down, out var info,
                                      radius + 0.05f + Mathf.Max(0.01f, slidePitchProbeDepth),
                                      ~0, QueryTriggerInteraction.Ignore);

        cc.enabled = wasEnabled;

        if (!hit) return false;
        normal = info.normal;
        point = info.point;
        return true;
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

        // One ground probe, shared. Both the pitch and the ground hug want the surface underfoot, and
        // casting twice a frame for the same answer would be waste.
        var groundNormal = Vector3.up;
        var groundPoint = transform.position;
        bool hasGround = IsSliding && cc.isGrounded && ProbeGround(out groundNormal, out groundPoint);

        // Both before the capsule is sized from the pose, so what gets measured is the body as it will
        // actually be drawn. A nose-up slide is genuinely taller than a flat one, and a body sunk to meet
        // the ground is genuinely lower.
        ApplySlidePitch(hasGround, groundNormal);
        ApplySlideGroundHug(hasGround, groundPoint, groundNormal);

        // Last, so it aims at the surface the body has actually settled onto.
        ApplySlideHandContact(hasGround, groundPoint, groundNormal);

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
    ///   players drive, and it is what makes long ducking stretches designable. Ignored entirely on the
    ///   slide after a held long one — see slideHoldDisarmed.
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

        // The button is ignored outright for the slide that follows a held long one. Disarming the
        // input rather than shortening the allowance is what makes this cost nothing to reason about:
        // with held false there is no stretch and no freeze, so this slide behaves exactly like an
        // ordinary one whatever the player does with the button.
        bool held = !slideHoldDisarmed && inputRouter != null && inputRouter.SlideHeld;

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

        // Freezing the pose is what does the prolonging — every frozen frame is a frame the clip does
        // not advance, so the slide outlives its own animation. The second term catches a slide still
        // being stretched past the point it would otherwise have ended.
        //
        // Gated on `held`, so only a slide the player leaned on counts. A slide prolonged purely
        // because the runner is under an overhang is the game keeping them safe, not greed, and
        // charging for it would punish level geometry instead of behaviour.
        if (held && (freeze || (stretch && elapsed >= slideDuration))) slideProlongedByHold = true;

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
        slideProlongedByHold = false;
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
    /// Stands the runner back up and settles what the slide they are leaving costs them.
    ///
    /// A slide the player held long earns two things: the recovery period, and a disarmed button for
    /// whatever they slide next. An ordinary slide clears the disarm, which is what makes the pattern
    /// long, normal, long rather than long, long, long — a long slide has to be paid for with a plain
    /// one before another is available.
    /// </summary>
    void EndSlide()
    {
        // Guarded on actually having been sliding. EndSlide is also called when nothing is sliding at
        // all — control being taken away, a roll starting, a respawn — and a flag left over from an
        // earlier slide would otherwise charge a recovery for nothing.
        if (slideEndsAt >= 0f)
        {
            if (slideProlongedByHold && slideRecoveryTime > 0f)
                slideRecoveryUntil = Time.time + slideRecoveryTime;

            // Only a held long slide disarms the button. An ordinary slide — including one prolonged
            // purely by an overhang, which the player did not ask for — arms it again.
            slideHoldDisarmed = slideProlongedByHold;
        }

        slideProlongedByHold = false;
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
        slideHoldDisarmed = false;
        slidePitch = 0f;           // the checkpoint is flat ground, whatever they died on was not
        slidePitchVelocity = 0f;
        slideHugDrop = 0f;
        slideHugVelocity = 0f;
        slideHandWeight = 0f;
        slideHandWeightVelocity = 0f;
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
