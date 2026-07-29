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

    [Header("Input buffering")]
    [Tooltip("A slide pressed while airborne is remembered this long and fires on touchdown, instead of " +
             "being swallowed. Jumps are never buffered — a late jump press is simply dropped.")]
    [SerializeField] float slideBufferTime = 1f;

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

    bool controlEnabled;

    // A slide press that could not run yet, kept until it lands or goes stale.
    float bufferedSlideAt = -999f;

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
    // How much mesh sits above the head bone (skull, hair). Calibrated from the standing pose,
    // because the head bone alone sits well below the top of the model.
    float headTopOffset;
    bool headOffsetCalibrated;

    public bool IsSliding => Time.time < slideEndsAt;
    public bool IsGrounded => cc.isGrounded || Time.time - lastGroundedTime < coyoteTime;

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

        transform.rotation = Quaternion.Euler(0f, facingYaw, 0f);

        CacheSilhouetteBones();
        CalibrateHeadOffset();   // bind pose is close enough to standing; LateUpdate refines if not

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
            ClearBufferedSlide();
        }
        if (animator != null) animator.speed = enabled ? 1f : 0f;
    }

    void ClearBufferedSlide()
    {
        bufferedSlideAt = -999f;
    }

    void CacheSilhouetteBones()
    {
        if (animator == null || !animator.isHuman) return;
        headBone = animator.GetBoneTransform(HumanBodyBones.Head);
        silhouetteBones = new Transform[SilhouetteBoneIds.Length];
        for (int i = 0; i < SilhouetteBoneIds.Length; i++)
            silhouetteBones[i] = animator.GetBoneTransform(SilhouetteBoneIds[i]);
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
        if (!controlEnabled || animator == null || !animator.isHuman) return;

        if (!headOffsetCalibrated && !IsSliding) CalibrateHeadOffset();

        if (!slideCapsuleFollowsPose || !IsSliding) return;

        float target = Mathf.Clamp(MeasurePoseHeight() + slidePoseClearance, cc.radius * 2f, standHeight);
        cc.height = target;
        cc.center = new Vector3(standCenter.x, target * 0.5f, standCenter.z);
    }

    void Update()
    {
        if (!controlEnabled) return;

        if (cc.isGrounded) lastGroundedTime = Time.time;

        // Expire the slide
        if (slideEndsAt > 0f && Time.time >= slideEndsAt) EndSlide();

        // A slide pressed in mid-air gets its chance the moment we touch down.
        if (cc.isGrounded && Time.time - bufferedSlideAt <= slideBufferTime) TrySlide();

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
    }

    public void Jump()
    {
        // Not buffered: a jump pressed too early is dropped, so it can never
        // surprise the player with a hop a moment after they land.
        if (!controlEnabled) return;
        TryJump();
    }

    public void Slide()
    {
        if (!controlEnabled) return;
        if (!TrySlide()) bufferedSlideAt = Time.time;   // hold it for touchdown
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
        ClearBufferedSlide();
        return true;
    }

    bool TrySlide()
    {
        // Real ground contact only. Coyote time is a jump forgiveness window; using it
        // here would allow a mid-air duck and would stop the buffer from ever engaging.
        if (!cc.isGrounded || IsSliding) return false;
        slideEndsAt = Time.time + slideDuration;
        cc.height = standHeight * slideHeightFraction;
        cc.center = new Vector3(standCenter.x, cc.height * 0.5f, standCenter.z);
        if (animator != null) animator.SetTrigger("Slide");
        ClearBufferedSlide();
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
        ClearBufferedSlide();   // a press from before the death must not fire on respawn
        verticalVelocity = 0f;
        cc.enabled = false;
        transform.position = position;
        lockedZ = position.z;
        cc.enabled = true;
    }
}
