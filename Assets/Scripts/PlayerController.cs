using UnityEngine;

/// <summary>Which hand, if any, is planted on the ground during a slide.</summary>
public enum SlideHand
{
    None,
    RightHand,
    LeftHand,
    Both,
}

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
    // Optional. Without a built ragdoll the runner just freezes on death.
    PlayerRagdoll ragdoll;

    float verticalVelocity;
    float lastGroundedTime;
    PlayerInputRouter inputRouter;
    float slideEndsAt = -1f;
    // Elapsed slide time is measured from here, not back from the deadline — the deadline moves
    // while the slide is being extended.
    float slideStartedAt;
    // Slide animation actually played. Stops while the pose is frozen, so the recovery still gets
    // its full run after a long hold.
    float slideClipTime;
    // Latched, so a runner stuck past every allowance complains once rather than every frame.
    bool warnedSlideStuck;
    // Set when a long slide ends. Until it passes, the runner has to stay on their feet.
    float slideRecoveryUntil = -1f;
    // Whether the player's hold made this slide outlast its clip. Latched during the slide rather
    // than derived from its total length at the end: frame quantisation puts an ordinary tap a frame
    // over slideDuration, and comparing totals charged a plain 0.70s tap as a long one.
    bool slideProlongedByHold;
    // Set when a held long slide ends, cleared when an ordinary one does. While set the slide button
    // cannot extend anything, so two long slides never run back to back.
    bool slideHoldDisarmed;

    // Pitch applied to the body, in degrees, with its SmoothDamp velocity. Kept as state because
    // levelling out in mid-air decays from wherever the last slope left it rather than jumping to zero.
    float slidePitch;
    float slidePitchVelocity;

    // How far the body is currently sunk to meet the ground, and its SmoothDamp velocity.
    float slideHugDrop;
    float slideHugVelocity;

    // Arm chains for the planted hand. Bones never change length, so reach is measured once.
    Transform rightUpperArm, rightLowerArm, rightHand;
    Transform leftUpperArm, leftLowerArm, leftHand;
    float rightArmReach, leftArmReach;

    // Blend on the hand correction, eased so it arrives and leaves with the slide.
    float slideHandWeight;
    float slideHandWeightVelocity;
    // The last surface the hand was actually solved against, held so the ease-out has something real
    // to let go of after the probe stops finding ground.
    Vector3 handGroundPoint;
    Vector3 handGroundNormal = Vector3.up;
    bool handGroundValid;
    float lockedZ;

    float standHeight;
    Vector3 standCenter;

    // The one orientation the capsule is ever allowed to have.
    Quaternion facing;

    bool controlEnabled;

    // Set for the outro: legs keep moving, but jump and slide are ignored.
    bool actionsLocked;

    // Last value pushed into the Slide state's speed, so an unchanged write can be skipped.
    float lastSlideSpeed = 1f;

    // When we left the ground, -1 while grounded. Separate from lastGroundedTime, which TryJump
    // clears to burn the coyote window and so cannot measure a fall.
    float leftGroundAt = -1f;
    float rollEndsAt = -1f;
    float rollInputLockUntil = -1f;
    float rollDuration;

    // How far above the feet a self-excluded query starts, so the floor the runner is standing on is
    // never the thing it finds.
    const float GroundLift = 0.05f;

    const string RollClipName = "Roll";
    // Float the Roll state multiplies its own speed by. Added by Set Up Landing Roll.
    const string RollSpeedParameter = "RollSpeed";
    // Float the Slide state multiplies its own speed by. 0 freezes the pose for a stretched slide.
    const string SlideSpeedParameter = "SlideSpeed";
    // Bool that holds the Slide state open, replacing an unconditional clip-time exit.
    const string SlidingParameter = "Sliding";

    // Animator parameters addressed by hash. The string overloads hash the name natively on every
    // call, and Grounded is written every single frame.
    static readonly int JumpTrigger = Animator.StringToHash("Jump");
    static readonly int SlideTrigger = Animator.StringToHash("Slide");
    static readonly int RollTrigger = Animator.StringToHash("Roll");
    static readonly int GroundedBool = Animator.StringToHash("Grounded");
    static readonly int SlidingBool = Animator.StringToHash(SlidingParameter);
    static readonly int SlideSpeedFloat = Animator.StringToHash(SlideSpeedParameter);
    static readonly int RollSpeedFloat = Animator.StringToHash(RollSpeedParameter);

    // Which optional parameters the wired controller actually declares, resolved once. Reading
    // animator.parameters allocates a fresh array plus one object per parameter, and the old
    // existence check ran through SetSlideSpeed on every frame of every slide — the largest single
    // GC source in the project.
    bool hasSlideSpeedParameter;
    bool hasSlidingParameter;
    bool hasRollSpeedParameter;

    // Raw length of the roll clip, read once. Reaching it goes through
    // runtimeAnimatorController.animationClips, which allocates the whole controller's clip array —
    // and BeginRoll used to do that on the landing frame of every roll. Negative means "no such clip",
    // so the serialized fallback is used instead.
    float rollClipLength = -1f;

    // A mid-air slide press, held until touchdown or until it goes stale. Never set by a press made
    // while already sliding, and cleared the moment it is used.
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
    // Toes, for the ground-contact test only — the silhouette set answers "how tall is this pose"
    // and has no business consulting a toe. Hands and forearms were tried here on the theory that a
    // trailing arm could be the lowest thing; measured across the clip they never are.
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

    // Everything below is internal state. The external API is seven verbs: EnableControl,
    // LockActions, Die, Revive, Teleport, Jump, Slide.
    bool IsSliding => Time.time < slideEndsAt;
    bool IsGrounded => cc.isGrounded || Time.time - lastGroundedTime < coyoteTime;

    /// <summary>
    /// World velocity the runner is carrying — the same vector Update feeds to cc.Move. Handed to the
    /// ragdoll on death so the collapse continues the motion instead of dropping from a standstill.
    /// </summary>
    Vector3 CurrentVelocity => new Vector3(runSpeed, verticalVelocity, 0f);

    /// <summary>True for the length of the roll clip after a long fall.</summary>
    bool IsRolling => Time.time < rollEndsAt;

    /// <summary>True while the runner is serving out the mandatory stand after a long slide.</summary>
    bool IsSlideRecovering => Time.time < slideRecoveryUntil;

    /// <summary>Jump and slide are swallowed while this holds — the outro, or a landing roll's brief lock.</summary>
    bool ActionsBlocked => actionsLocked || Time.time < rollInputLockUntil;

    /// <summary>
    /// 0 at the start of a slide, 1 once a normal slide's worth of time has passed, and 1 when not
    /// sliding. Measured from the start rather than back from the deadline, which moves while a
    /// slide is held or blocked.
    /// </summary>
    float SlideProgress =>
        IsSliding && slideDuration > 0f
            ? Mathf.Clamp01((Time.time - slideStartedAt) / slideDuration)
            : 1f;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();
        // Asked whether the slide button is still down — the router is the only thing that sees both
        // the keyboard and the on-screen button.
        inputRouter = GetComponent<PlayerInputRouter>();
        ragdoll = GetComponent<PlayerRagdoll>();

        standHeight = cc.height;
        standCenter = cc.center;
        lockedZ = transform.position.z;

        facing = Quaternion.Euler(0f, facingYaw, 0f);
        transform.rotation = facing;

        CacheSilhouetteBones();
        CalibrateHeadOffset();   // bind pose is close enough to standing; LateUpdate refines if not
        InspectAnimatorController();
        ResolveRollDuration();

        // Intro holds control until RunManager.BeginRun(). Routed through EnableControl rather than
        // setting the flag directly, so the animator is stopped too — otherwise the run cycle plays
        // at full speed through the whole intro while the runner stands still.
        EnableControl(false);
    }

    public void EnableControl(bool enabled)
    {
        controlEnabled = enabled;
        if (!enabled)
        {
            verticalVelocity = 0f;
            EndSlide();
            // After EndSlide, which may have just charged them. Losing control is not a penalty the
            // player should still be paying when they get it back.
            ClearSlideDebt();
            ResetSlideCosmetics();
            ClearBufferedInput();
            CancelRoll();
        }
        if (animator != null) animator.speed = enabled ? 1f : 0f;
    }

    /// <summary>
    /// Zeroes the three eased slide corrections and their SmoothDamp velocities.
    ///
    /// Snapped rather than eased, because every caller is a discontinuity — control returning, a
    /// respawn. Easing from an old slope would show the runner righting themselves from a tilt they
    /// never earned.
    /// </summary>
    void ResetSlideCosmetics()
    {
        slidePitch = 0f;
        slidePitchVelocity = 0f;
        slideHugDrop = 0f;
        slideHugVelocity = 0f;
        slideHandWeight = 0f;
        slideHandWeightVelocity = 0f;
        handGroundValid = false;
    }

    /// <summary>
    /// Forgives what the last slide charged: the mandatory stand, and the disarmed button. Kept
    /// apart from ResetSlideCosmetics because losing control forgives the debt while an ordinary
    /// EndSlide is what levies it.
    /// </summary>
    void ClearSlideDebt()
    {
        slideRecoveryUntil = -1f;
        slideHoldDisarmed = false;
    }

    /// <summary>Drops both held airborne presses, so neither can fire after the thing that cleared them.</summary>
    void ClearBufferedInput()
    {
        airborneSlideAt = -999f;
        airborneJumpAt = -999f;
    }

    /// <summary>
    /// Ends any roll in progress outright.
    ///
    /// Both deadlines have to go, not just the visual one: rollEndsAt keeps LevelRollPose rotating
    /// the hips, and rollInputLockUntil keeps ActionsBlocked swallowing jump and slide. Dying mid-roll
    /// used to carry both across the respawn, so the runner could arrive at the checkpoint mid-tumble
    /// and unable to act. respawnDelay usually outlasts the roll and hid it, but pausing while dead
    /// freezes Time.time and the respawn coroutine together, which made it a timing accident rather
    /// than a guarantee.
    /// </summary>
    void CancelRoll()
    {
        rollEndsAt = -1f;
        rollInputLockUntil = -1f;
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
            ClearBufferedInput();
        }
    }

    /// <summary>
    /// Reads the roll length off the clip, so retiming or replacing the animation cannot leave the
    /// deadlines wrong.
    ///
    /// Pushing the playback speed into the animator and dividing the length by it have to happen
    /// together, or the levelling would still be correcting a pose the state machine had already
    /// handed back to Run. Re-run on every roll, so an inspector change takes effect on the next one.
    /// </summary>
    void ResolveRollDuration()
    {
        float speed = Mathf.Max(0.01f, rollPlaybackSpeed);

        rollDuration = (rollClipLength >= 0f ? rollClipLength : rollDurationFallback) / speed;
        if (animator != null && hasRollSpeedParameter) animator.SetFloat(RollSpeedFloat, speed);
    }

    /// <summary>
    /// One-time inspection of the wired controller: which optional parameters it declares, and how
    /// long the roll clip is. Both answers come from allocating APIs, and both are fixed for the
    /// lifetime of the controller, so they are taken once here rather than per frame or per roll.
    ///
    /// A controller that has not been through Set Up Landing Roll or Set Up Slide Hold simply lacks
    /// the parameter, and writing a missing one logs on every write.
    /// </summary>
    void InspectAnimatorController()
    {
        if (animator == null) return;

        foreach (var parameter in animator.parameters)
        {
            if (parameter.type == AnimatorControllerParameterType.Float)
            {
                if (parameter.name == SlideSpeedParameter) hasSlideSpeedParameter = true;
                else if (parameter.name == RollSpeedParameter) hasRollSpeedParameter = true;
            }
            else if (parameter.type == AnimatorControllerParameterType.Bool
                     && parameter.name == SlidingParameter)
            {
                hasSlidingParameter = true;
            }
        }

        if (animator.runtimeAnimatorController == null) return;
        foreach (var clip in animator.runtimeAnimatorController.animationClips)
        {
            if (clip == null || clip.name != RollClipName) continue;
            rollClipLength = clip.length;
            return;
        }
    }

    /// <summary>
    /// Landing after a long fall. The runner keeps moving — a roll carries momentum — and input is
    /// swallowed only for rollInputLockDuration. Nothing is remembered: a press inside that window
    /// is dropped, not queued.
    /// </summary>
    void BeginRoll()
    {
        EndSlide();
        // The slide corrections all write hipsBone, and so does LevelRollPose. Left decaying into the
        // roll they fight it for the same transform, so the roll takes the bone outright.
        ResetSlideCosmetics();
        ResolveRollDuration();   // picks up an inspector change to the playback speed
        rollEndsAt = Time.time + rollDuration;
        rollInputLockUntil = Time.time + Mathf.Clamp(rollInputLockDuration, 0f, rollDuration);
        if (animator != null) animator.SetTrigger(RollTrigger);
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

        // Eased at both ends, not just the exit: the clip's first frame already carries about 23
        // degrees of pelvis twist, so snapping on at entry pops as much as dropping at the handoff.
        return rollLevelStrength
               * Mathf.Min(Mathf.Clamp01(elapsed / ramp), Mathf.Clamp01(remaining / ramp));
    }

    /// <summary>
    /// Flattens the roll clip's corkscrew back into the plane the runner travels in.
    ///
    /// "Quick Roll To Run" is a shoulder roll: the somersault itself is correct, but the body also
    /// leans up to 46 degrees out of the travel plane, twice and in opposite directions, and comes up
    /// twisted. That lives entirely in the pose — applyRootMotion is off — so nothing done to the
    /// capsule can touch it.
    ///
    /// Two measured choices, both load-bearing:
    ///   Axis. Rotating about the travel axis spins the body along its own length without moving the
    ///   spine at all — measured, it drove the lean from 46 degrees to 73. It has to be the rotation
    ///   that maps the spine onto its target, which is what FromToRotation gives.
    ///   Pivot. The hips, not the root. The root sits at the feet, so pivoting there swings the body
    ///   off the lane and pushes feet through the floor.
    ///
    /// Stage one aligns the spine but leaves rotation about the spine free, and that leftover freedom
    /// is the spin — the pelvis swings about 140 degrees about the spine and back within a sixth of
    /// the clip. Stage two removes it, reading the twist off the pelvis rather than the shoulder line:
    /// the pelvis is rigid, and the ~25 degree disagreement between them at the ends is the natural
    /// shoulder counter-rotation of a run, which should be preserved.
    ///
    /// Measured at full strength against the raw clip: lean 46 degrees to 0, twist 167 to 0, worst
    /// foot clearance 0.07 to 0.06, lateral offset 0.79 to 0.72.
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

        // Rotating the hips carries every descendant about the hip joint; their own position is
        // left where the clip put it.
        hipsBone.rotation = Quaternion.Slerp(
            Quaternion.identity, Quaternion.FromToRotation(spine, flat), weight) * hipsBone.rotation;

        // Stage two: take the corkscrew out of the axis stage one left free. Re-read the spine
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
    /// no ground to lie along. Applied to the hips for the same reason as LevelRollPose; the capsule
    /// is left alone because LateUpdate pins its orientation to hold the 2.5D lane.
    ///
    /// The target comes from the surface tangent rather than the normal's angle, which makes the sign
    /// correct by construction — SignedAngle(forward, tangent) is by definition the rotation mapping
    /// travel onto the slope, so downhill is nose-down with no hand-picked sign to get wrong.
    ///
    /// Airborne the target is flat and the approach slower. That is the whole of "flying off a ramp
    /// evens out", and it works from any launch angle because it decays rather than resetting.
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
    /// Worked out from bone positions plus a constant offset (slideMeshBelowBone) — baking the
    /// skinned mesh every frame to find its true lowest vertex costs far too much for a few
    /// centimetres of polish.
    ///
    /// Clamped positive, so this only ever lowers the runner: a foot slightly in the ground reads as
    /// contact, a foot slightly above it reads as levitation. Runs after the pitch, which is what
    /// decides where the lowest point of the body actually is.
    /// </summary>
    void ApplySlideGroundHug(bool hasGround, Vector3 groundPoint, Vector3 groundNormal)
    {
        if (!slideHugsGround || hipsBone == null) return;

        float target = 0f;
        if (hasGround)
        {
            // Against the ground PLANE, not the ground height under the root. A prone body reaches
            // about half a unit along the lane, so on a 20 degree slope the surface under its far end
            // is 0.18 from the surface under its middle — comparing heights sank the runner 0.077
            // downhill and left them hovering 0.046 uphill. The probe already returns both.
            float clearance = LowestDistanceAbovePlane(groundPoint, groundNormal) - slideMeshBelowBone;

            // The gap is perpendicular to the slope but the body moves vertically, so it has to be
            // divided back out or a slope would only ever be partly closed.
            float lean = Mathf.Max(0.2f, Vector3.Dot(groundNormal, Vector3.up));
            target = Mathf.Clamp((clearance - slideGroundClearance) / lean, 0f, slideHugMaxDrop);
        }

        slideHugDrop = Mathf.SmoothDamp(slideHugDrop, target, ref slideHugVelocity,
                                       Mathf.Max(0.001f, slideHugSmoothTime));

        if (slideHugDrop <= 0.001f) return;
        // Moving the hips carries every bone with it — the same lever the pitch and levelling use.
        hipsBone.position -= Vector3.up * slideHugDrop;
    }

    /// <summary>
    /// Plants a hand on the ground for the length of the slide. Runs last of the three slide
    /// corrections: the pitch and the drop both move the shoulder, so solving earlier would put the
    /// hand where the shoulder used to be.
    ///
    /// Unity's humanoid IK pass is the wrong tool here — it runs inside the Animator update, before
    /// LateUpdate, so it cannot see the pitch or the drop.
    ///
    /// The correction fades rather than straining. Measured, the arm is 0.490 long while the shoulder
    /// passes 0.388 to 0.475 above the ground, so late in the clip a planted hand needs practically
    /// the whole arm locked straight, which reads as a mannequin.
    /// </summary>
    void ApplySlideHandContact(bool hasGround, Vector3 groundPoint, Vector3 groundNormal)
    {
        if (slideGroundHand == SlideHand.None) return;

        // Latch the real surface. The ease-out runs for several frames after hasGround has already
        // gone false, and LateUpdate's fallback plane — the runner's own feet, normal up — is not a
        // surface at all. Solving the arm onto it swung the hand at nothing for the first frames of
        // every run-out. Easing against the last surface actually probed is what reads as letting go.
        if (hasGround)
        {
            handGroundPoint = groundPoint;
            handGroundNormal = groundNormal;
            handGroundValid = true;
        }

        float target = hasGround ? 1f : 0f;
        slideHandWeight = Mathf.SmoothDamp(slideHandWeight, target, ref slideHandWeightVelocity,
                                          Mathf.Max(0.001f, slideHandSmoothTime));
        if (slideHandWeight <= 0.002f)
        {
            handGroundValid = false;
            return;
        }

        // Never had a surface to plant on, so there is nothing to ease away from either.
        if (!handGroundValid) return;

        if (slideGroundHand == SlideHand.RightHand || slideGroundHand == SlideHand.Both)
            PlantHand(rightUpperArm, rightLowerArm, rightHand, rightArmReach, handGroundPoint, handGroundNormal);

        if (slideGroundHand == SlideHand.LeftHand || slideGroundHand == SlideHand.Both)
            PlantHand(leftUpperArm, leftLowerArm, leftHand, leftArmReach, handGroundPoint, handGroundNormal);
    }

    void PlantHand(Transform upper, Transform lower, Transform hand, float reach,
                   Vector3 groundPoint, Vector3 groundNormal)
    {
        if (upper == null || lower == null || hand == null || reach <= 0f) return;

        float above = Vector3.Dot(hand.position - groundPoint, groundNormal);
        // Already touching, or through the surface. Pulling further would bury it; lifting is not
        // this method's job.
        if (above <= slideHandPalmOffset) return;

        float limit = reach * Mathf.Clamp(slideHandReachLimit, 0.1f, 1f);
        float shoulderAbove = Vector3.Dot(upper.position - groundPoint, groundNormal);

        // Fade on whether the ground is reachable AT ALL, not on whether one particular spot is.
        // Judging the spot made the hand let go the moment its own position went out of range, so it
        // planted for a third of the slide — the runner can always put the hand down closer in. What
        // genuinely cannot be fixed is a shoulder further up than the arm is long, which is exactly
        // the opening frames, where it rides 0.63 to 1.34 above the ground.
        float slack = limit - (shoulderAbove - slideHandPalmOffset);
        float fade = Mathf.Clamp01(slack / (reach * 0.08f));
        if (fade <= 0.002f) return;

        // Where the hand would like to go: straight down onto the surface, keeping its own position.
        var onPlane = groundNormal * slideHandPalmOffset;
        var shoulderFoot = upper.position - groundNormal * shoulderAbove + onPlane;
        var desired = hand.position - groundNormal * (above - slideHandPalmOffset);

        // Drawn in toward the shoulder until the arm can reach, so the target stays ON the surface.
        // The hand slides inward rather than lifting off.
        float maxAlongPlane = Mathf.Sqrt(Mathf.Max(0f, limit * limit -
                                                      (shoulderAbove - slideHandPalmOffset) *
                                                      (shoulderAbove - slideHandPalmOffset)));
        var alongPlane = desired - shoulderFoot;
        if (alongPlane.magnitude > maxAlongPlane)
            desired = shoulderFoot + alongPlane.normalized * maxAlongPlane;

        // The solve turns the forearm and the hand is its child, so the wrist would come along and
        // pitch the fingers into the ground — measured 0.149 below the surface without this. The
        // animated wrist is the authored one, so it is restored rather than recomputed.
        float weight = slideHandWeight * fade;
        var wrist = hand.rotation;
        SolveTwoBone(upper, lower, hand, desired, weight);
        hand.rotation = Quaternion.Slerp(hand.rotation, wrist, weight);
    }

    /// <summary>
    /// Two-bone IK. The elbow is solved analytically and each bone then simply aimed, which avoids
    /// the sign and handedness traps of working in angles. The bend plane comes from wherever the
    /// animation already had the elbow, so the correction does not decide which way the joint folds.
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
    /// How far the closest bone sits above the ground plane, measured perpendicular to it. Negative
    /// if something is already through the surface.
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
    /// Radius used by both self-excluded queries. Shaved off the controller's own so a shoulder
    /// brushing a wall does not register.
    /// </summary>
    float QueryRadius => Mathf.Max(0.01f, cc.radius * 0.9f);

    /// <summary>
    /// Takes the runner's own capsule out of the physics scene and flushes pending transform writes,
    /// so a query centred on the runner answers about the world rather than about themselves.
    ///
    /// Both are needed. Left enabled, the controller is the first thing every query hits. And this
    /// project runs with autoSyncTransforms off, so without the sync a query issued straight after a
    /// transform or collider change reads stale state — measured: CheckCapsule reported clear while
    /// OverlapCapsule found the ceiling in the same volume.
    ///
    /// Returns the enabled state to hand back to <see cref="EndSelfExcludedQuery"/>. Always pair them;
    /// returning early in between would leave the controller switched off.
    /// </summary>
    bool BeginSelfExcludedQuery()
    {
        bool wasEnabled = cc.enabled;
        cc.enabled = false;
        Physics.SyncTransforms();
        return wasEnabled;
    }

    void EndSelfExcludedQuery(bool wasEnabled) => cc.enabled = wasEnabled;

    /// <summary>
    /// Surface normal and height under the feet, shared by the pitch and the ground hug — LateUpdate
    /// casts once and hands the result to both.
    /// </summary>
    bool ProbeGround(out Vector3 normal, out Vector3 point)
    {
        normal = Vector3.up;
        point = transform.position;

        bool wasEnabled = BeginSelfExcludedQuery();

        float radius = QueryRadius;
        // Starting a radius up means the sphere begins clear of the floor, so a surface flush against
        // the feet registers as a hit rather than an initial overlap.
        var origin = transform.position + Vector3.up * (radius + GroundLift);
        bool hit = Physics.SphereCast(origin, radius, Vector3.down, out var info,
                                      radius + GroundLift + Mathf.Max(0.01f, slidePitchProbeDepth),
                                      ~0, QueryTriggerInteraction.Ignore);

        EndSelfExcludedQuery(wasEnabled);

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
    float MeasurePoseHeight()
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
        // Bones tumble freely — the roll leans the body well over — but the capsule's orientation is
        // pinned here, after the pose is written, so no clip, root motion or stray write can leave
        // the runner rotated off the lane.
        if (transform.rotation != facing) transform.rotation = facing;

        if (!controlEnabled || animator == null || !animator.isHuman) return;

        if (!headOffsetCalibrated && !IsSliding && !IsRolling) CalibrateHeadOffset();

        LevelRollPose();

        // One probe, shared by the pitch and the hug — casting twice a frame for the same answer
        // would be waste.
        var groundNormal = Vector3.up;
        var groundPoint = transform.position;
        bool hasGround = IsSliding && cc.isGrounded && ProbeGround(out groundNormal, out groundPoint);

        // Both before the capsule is sized from the pose, so what gets measured is the body as it
        // will be drawn: a nose-up slide is genuinely taller, a sunk body genuinely lower.
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
        // disabled CharacterController is an error per frame, so this backstops any path that hands
        // control back before the body.
        if (!cc.enabled) return;

        if (cc.isGrounded) lastGroundedTime = Time.time;

        TrackAirborne();

        MaintainSlide();

        // Fire an action asked for in the air the moment the feet are down, consuming it before the
        // attempt so a failure cannot retry on a later frame. Jump wins when both are held, and the
        // slide is dropped rather than queued — firing both would slide the instant they left the
        // ground.
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

        // The Jump state leaves on this rather than on clip time: air time depends on what the runner
        // lands on, so a fixed exit time left Jump playing while they were already up and running.
        //
        // Set after the move, so isGrounded is this frame's. Rising counts as airborne even before
        // the controller reports leaving the floor, or Jump would hand straight back to Run on the
        // frame it was entered.
        if (animator != null) animator.SetBool(GroundedBool, cc.isGrounded && verticalVelocity <= 0f);
    }

    /// <summary>
    /// Measures time off the ground and turns a long enough fall into a roll on touchdown. A
    /// one-frame isGrounded blip over a seam reads as a fraction of a second, well under the
    /// threshold.
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

        // Rejected. Hold one kind only: a press made in the air, which is a player asking to jump the
        // moment they land. That covers the touchdown frame, where isGrounded can still read false
        // because the router and this script both run in Update in no guaranteed order.
        //
        // A press rejected mid-slide is dropped, so it cannot resurface as an unasked-for hop when
        // the slide expires.
        if (!IsSliding) airborneJumpAt = Time.time;
    }

    public void Slide()
    {
        if (!controlEnabled || ActionsBlocked) return;
        if (TrySlide()) return;

        // Rejected. Same rule as Jump: hold an airborne press to cover the touchdown frame, drop
        // anything else. Holding a press made during a slide was the old bug — it sat in the buffer
        // through the whole slide, then fired as a second, unasked-for duck the instant it expired.
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
            // Same reason MaintainSlide refuses to end a blocked slide: standing up while something
            // is directly overhead drives the capsule into it.
            if (BlockedFromStanding()) return false;
            EndSlide();
        }

        verticalVelocity = Mathf.Sqrt(2f * jumpHeight * -gravity);
        lastGroundedTime = -999f;
        airborneJumpAt = -999f;   // spent, so leaving the ground cannot re-fire it on the way down
        if (animator != null) animator.SetTrigger(JumpTrigger);
        return true;
    }

    /// <summary>
    /// Ends the slide, or keeps it going. Two independent reasons to keep going:
    ///
    ///   Held. The player is still asking, up to slideHoldMaxMultiplier. Ignored entirely on the
    ///   slide after a held long one — see slideHoldDisarmed.
    ///
    ///   Blocked. Standing up would put the capsule inside something, up to
    ///   slideBlockedMaxMultiplier. Nothing to do with input — the alternative is expanding into
    ///   solid geometry.
    ///
    /// Past every allowance and still blocked, the slide continues anyway and logs once: a tunnel
    /// that long is a level-building mistake worth seeing rather than silently absorbing.
    /// </summary>
    void MaintainSlide()
    {
        if (slideEndsAt < 0f) return;   // not sliding

        float elapsed = Time.time - slideStartedAt;
        bool blocked = BlockedFromStanding();

        // The button is ignored outright for the slide after a held long one. Disarming the input
        // rather than shortening the allowance is what makes this cost nothing to reason about: with
        // held false there is no stretch and no freeze, so the slide behaves like an ordinary one
        // whatever the player does with the button.
        bool held = !slideHoldDisarmed && inputRouter != null && inputRouter.SlideHeld;

        float allowance = slideDuration;
        if (held)
            allowance = Mathf.Max(allowance, slideDuration * Mathf.Max(1f, slideHoldMaxMultiplier));
        if (blocked)
            allowance = Mathf.Max(allowance, slideDuration * Mathf.Max(1f, slideBlockedMaxMultiplier));

        // Blocked overrides the ceiling entirely, but says so once.
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

        // Freezing the pose is what does the prolonging — a frozen frame is a frame the clip does not
        // advance, so the slide outlives its own animation. The second term catches a slide still
        // being stretched past the point it would otherwise have ended.
        //
        // Gated on `held`: a slide prolonged purely by an overhang is the game keeping the runner
        // safe, not greed, and charging for it would punish level geometry instead of behaviour.
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
    /// its shape instead of running the recovery early — the clip is 0.42s long, so without this a
    /// held slide showed the runner up and running while the capsule was still crouched.
    /// </summary>
    void SetSlideSpeed(float speed)
    {
        if (animator == null || !hasSlideSpeedParameter) return;
        // Guarded on change: MaintainSlide calls this every frame of every slide, and the value is
        // the same 1 for almost all of them.
        if (Mathf.Approximately(speed, lastSlideSpeed)) return;
        lastSlideSpeed = speed;
        animator.SetFloat(SlideSpeedFloat, speed);
    }

    /// <summary>
    /// Holds the Slide state open. Guarded like the float parameters: a controller predating Set Up
    /// Slide Hold has no such bool, and writing a missing one logs on every slide.
    /// </summary>
    void SetSlidingFlag(bool sliding)
    {
        if (animator == null || !hasSlidingParameter) return;
        animator.SetBool(SlidingBool, sliding);
    }

    /// <summary>
    /// Whether expanding back to the standing capsule would intersect something.
    ///
    /// The probe capsule is the standing one, which overlaps the crouched capsule by definition —
    /// hence the self-excluded query. The base is lifted clear of the floor so the ground the runner
    /// is standing on is not mistaken for a ceiling.
    /// </summary>
    bool BlockedFromStanding()
    {
        if (standHeight - cc.height <= 0.001f) return false;

        bool wasEnabled = BeginSelfExcludedQuery();

        float radius = QueryRadius;
        float bottom = radius + GroundLift;
        float top = Mathf.Max(bottom, standHeight - radius);
        var p0 = transform.position + Vector3.up * bottom;
        var p1 = transform.position + Vector3.up * top;
        bool blocked = Physics.CheckCapsule(p0, p1, radius, ~0, QueryTriggerInteraction.Ignore);

        EndSelfExcludedQuery(wasEnabled);
        return blocked;
    }

    bool TrySlide()
    {
        // Real ground contact only. Coyote time is a jump forgiveness window; using it here would let
        // the runner duck in mid-air.
        //
        // Returning false while already sliding is what makes a second tap a no-op rather than a
        // queued second slide — the trigger is never set, so nothing can replay it.
        if (!cc.isGrounded || IsSliding) return false;

        // Standing out the recovery from the last long slide. Refused rather than queued, so holding
        // the button through it does not buy a slide the instant it expires.
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
        if (animator != null) animator.SetTrigger(SlideTrigger);
        return true;
    }

    /// <summary>
    /// Stands the runner back up and settles what the slide they are leaving costs them.
    ///
    /// A held long slide earns the recovery period and a disarmed button for whatever they slide
    /// next. An ordinary slide clears the disarm, which makes the pattern long, normal, long rather
    /// than long, long, long.
    /// </summary>
    void EndSlide()
    {
        // Guarded on actually having been sliding. EndSlide is also called when nothing is — control
        // being taken away, a roll starting, a respawn — and a leftover flag would charge a recovery
        // for nothing.
        if (slideEndsAt >= 0f)
        {
            if (slideProlongedByHold && slideRecoveryTime > 0f)
                slideRecoveryUntil = Time.time + slideRecoveryTime;

            // Only a held long slide disarms the button. An ordinary one — including a slide
            // prolonged purely by an overhang, which the player did not ask for — arms it again.
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

        // A canopy launches the runner off its top face, but only the booster decides whether this
        // contact counts as the top — a hit on its front edge falls through to the wall rules below
        // and kills, like any other obstacle. GetComponentInParent, so the collider may be a child.
        //
        // Gated on an upward-facing contact first. This callback fires for every wall, roof and road
        // segment touched, several times per frame in dense geometry, and GetComponentInParent walks
        // the whole parent chain each time. The booster's own topNormalThreshold is [Range(0, 1)], so
        // a downward normal can never bounce and the lookup would always have been wasted. It cannot
        // simply be moved below the wall test instead — a canopy top is not a wall, so that test
        // returns first and the bounce would never fire.
        if (hit.normal.y > 0f)
        {
            var canopy = hit.collider.GetComponentInParent<CanopyBooster>();
            if (canopy != null && canopy.TryConsumeBounce(hit.normal))
            {
                Bounce(canopy.BounceHeight);
                return;
            }
        }

        // Roofs, floors and ceilings all fail this test — only walls pass.
        if (hit.normal.x > wallNormalThreshold) return;

        // If we could step or already cleared it, it is not a crash.
        float feetY = transform.position.y;
        float surfaceTop = hit.collider.bounds.max.y;
        if (surfaceTop - feetY <= cc.stepOffset + ledgeTolerance) return;

        // The normal goes with it: it points out of the wall, which is the direction to throw the
        // body, and only this method knows it.
        if (RunManager.Instance != null) RunManager.Instance.Kill(hit.normal);
    }

    /// <summary>
    /// Ends the run and, if a ragdoll has been built, hands the body over to physics. Called by
    /// RunManager rather than reached directly, so run state and the visible collapse stay in step.
    /// </summary>
    public void Die(Vector3 impactNormal)
    {
        // Read before control is dropped — EnableControl(false) zeroes verticalVelocity, and the fall
        // the runner was in is half of what makes the collapse read right.
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
    /// Launches the runner off a canopy. Not a jump: it asks for no ground underfoot and ignores
    /// ActionsBlocked, because the canopy is doing the work. Height is an apex above the contact
    /// point, converted through the same gravity the jump uses so the two stay comparable.
    /// </summary>
    void Bounce(float height)
    {
        EndSlide();
        verticalVelocity = Mathf.Sqrt(2f * Mathf.Max(0f, height) * -gravity);

        // No coyote hop off the launch, and no buffered press cashing itself in at the top of it.
        lastGroundedTime = -999f;
        ClearBufferedInput();

        // The canopy absorbs the fall, so the air-time clock restarts here. Without this a long drop
        // onto a canopy would still be carrying that fall at the next touchdown and would fire a
        // landing roll the canopy has already cancelled.
        leftGroundAt = Time.time;

        if (animator != null) animator.SetTrigger(JumpTrigger);
    }

    /// <summary>Hard reposition used on respawn — CharacterController ignores transform writes while enabled.</summary>
    public void Teleport(Vector3 position)
    {
        EndSlide();
        ClearSlideDebt();        // a fresh start, not the tail of the slide they died in
        ResetSlideCosmetics();   // the checkpoint is flat ground, whatever they died on was not
        ClearBufferedInput();    // a press from before the death must not fire on respawn
        CancelRoll();            // and they must not arrive mid-tumble, unable to act
        // Dying in mid-air left leftGroundAt set, so TrackAirborne measured the fall as running from
        // before the death and fired a landing roll on the respawn's first grounded frame.
        leftGroundAt = -1f;
        verticalVelocity = 0f;
        cc.enabled = false;
        transform.position = position;
        lockedZ = position.z;
        cc.enabled = true;
    }
}
