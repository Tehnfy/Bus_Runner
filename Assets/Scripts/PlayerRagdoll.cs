using UnityEngine;

/// <summary>
/// Hands the runner's skeleton over to physics when they die, and takes it back on respawn.
///
/// The bodies, colliders and joints are authored once by "Bus Runner/Set Up Ragdoll" and live on the
/// bone transforms permanently. This component owns the switch between the two states:
///
///   Alive — every bone body kinematic and every bone collider disabled. Disabled rather than merely
///   kinematic, because PlayerController probes the world with a mask of ~0: BlockedFromStanding
///   would find the runner's own thighs above their own head and refuse to ever stand up.
///
///   Dead — Animator and CharacterController off, bodies dynamic, colliders on, the run's momentum
///   carried into every body so the collapse continues the motion instead of dropping from a stop.
///
/// Nothing here reparents anything. The bone bodies stay children of the player root, which stops
/// moving the moment control is taken away, so the ragdoll tumbles in place and the camera keeps
/// looking at the spot where the runner died.
/// </summary>
public class PlayerRagdoll : MonoBehaviour
{
    [Header("Impact")]
    [Tooltip("Velocity change applied to the chest at the moment of death, along the surface normal " +
             "of whatever was hit. Applied off-centre, high on the body, so the runner topples over " +
             "the obstacle rather than sliding away from it flat.")]
    [SerializeField] float impactPush = 3.5f;
    [Tooltip("Upward part of that same kick. A little lift reads as being knocked off their feet; " +
             "too much and they fly.")]
    [SerializeField] float impactLift = 2f;
    [Tooltip("How far above the chest the kick is applied. Bigger values topple harder.")]
    [SerializeField] float impactLeverage = 0.25f;

    [Header("Stability")]
    [Tooltip("Caps how fast physics may push two overlapping colliders apart. Death happens against " +
             "a wall the capsule is already touching, so without a cap the first frame can fire the " +
             "ragdoll across the level.")]
    [SerializeField] float maxDepenetrationSpeed = 3f;
    [Tooltip("Solver iterations per bone body. Ragdolls need more than the project default or the " +
             "joints visibly stretch.")]
    [SerializeField] int solverIterations = 12;

    Animator animator;
    CharacterController cc;

    Rigidbody[] bodies;
    Collider[][] colliders;
    Rigidbody chest;

    // The pose to hand back to the Animator on respawn. Without it the crumpled pose shows for a
    // frame before the first animated one is written.
    Transform[] boneTransforms;
    Vector3[] restPositions;
    Quaternion[] restRotations;

    public bool Active { get; private set; }

    /// <summary>True once the setup pass has actually put bodies on the bones.</summary>
    public bool IsBuilt => bodies != null && bodies.Length > 0;

    void Awake()
    {
        animator = GetComponent<Animator>();
        cc = GetComponent<CharacterController>();

        // The player root carries no Rigidbody, so everything found here is a bone body.
        bodies = GetComponentsInChildren<Rigidbody>(true);
        colliders = new Collider[bodies.Length][];
        boneTransforms = new Transform[bodies.Length];
        restPositions = new Vector3[bodies.Length];
        restRotations = new Quaternion[bodies.Length];

        for (int i = 0; i < bodies.Length; i++)
        {
            // Per body rather than one GetComponentsInChildren<Collider> sweep: that would also
            // return the CharacterController, which is a Collider and is not ours to switch off here.
            colliders[i] = bodies[i].GetComponents<Collider>();
            boneTransforms[i] = bodies[i].transform;
            restPositions[i] = boneTransforms[i].localPosition;
            restRotations[i] = boneTransforms[i].localRotation;

            bodies[i].maxDepenetrationVelocity = maxDepenetrationSpeed;
            bodies[i].solverIterations = solverIterations;
            bodies[i].interpolation = RigidbodyInterpolation.Interpolate;
        }

        chest = FindBody(HumanBodyBones.Chest) ?? FindBody(HumanBodyBones.Hips);

        // Authoring leaves the colliders on so they can be seen and tuned in the scene view. Play
        // starts from the alive state regardless of how the scene was left.
        GoLimp(false);
    }

    Rigidbody FindBody(HumanBodyBones bone)
    {
        if (animator == null || !animator.isHuman) return null;
        var t = animator.GetBoneTransform(bone);
        return t != null ? t.GetComponent<Rigidbody>() : null;
    }

    /// <summary>
    /// Collapses the runner. <paramref name="velocity"/> is their world velocity at the moment of
    /// death, and <paramref name="impactNormal"/> points out of the surface they hit — away from the
    /// obstacle, so it is the direction they should be thrown.
    /// </summary>
    public void Activate(Vector3 velocity, Vector3 impactNormal)
    {
        if (Active || !IsBuilt) return;
        Active = true;

        // Order matters. The Animator has to stop writing bone transforms before physics is allowed
        // to own them, and the CharacterController's capsule has to go before the bone colliders
        // arrive, or the two overlap and shove each other apart.
        if (animator != null) animator.enabled = false;
        if (cc != null) cc.enabled = false;

        GoLimp(true);

        // This project runs with Physics.autoSyncTransforms off, so the bone transforms the Animator
        // wrote this frame have not reached PhysX yet. Pushing them across before the bodies go
        // dynamic is what makes the ragdoll start from the pose on screen rather than a stale one.
        Physics.SyncTransforms();

        for (int i = 0; i < bodies.Length; i++)
        {
            bodies[i].isKinematic = false;
            bodies[i].linearVelocity = velocity;
            bodies[i].angularVelocity = Vector3.zero;
        }

        if (chest == null) return;

        var push = impactNormal.sqrMagnitude > 1e-4f ? impactNormal.normalized : -transform.forward;
        var kick = push * impactPush + Vector3.up * impactLift;

        // Off-centre on purpose: a force through the centre of mass only translates, and the runner
        // needs to go over the obstacle. VelocityChange so the tuning numbers read as speeds and do
        // not have to be re-found every time a bone mass changes.
        chest.AddForceAtPosition(kick, chest.worldCenterOfMass + Vector3.up * impactLeverage,
                                 ForceMode.VelocityChange);
    }

    /// <summary>Takes the skeleton back off physics and returns it to the Animator, in its rest pose.</summary>
    public void Deactivate()
    {
        if (!IsBuilt) return;
        Active = false;

        GoLimp(false);

        for (int i = 0; i < bodies.Length; i++)
        {
            boneTransforms[i].localPosition = restPositions[i];
            boneTransforms[i].localRotation = restRotations[i];
        }

        // The bones have just been moved a long way back; let PhysX see that before it is asked to
        // simulate anything else, for the same autoSyncTransforms reason as above.
        Physics.SyncTransforms();

        if (animator != null) animator.enabled = true;
        // PlayerController.Teleport toggles the controller itself, so leave it to that — enabling it
        // here would place the capsule at the death spot for a frame first.
    }

    void GoLimp(bool limp)
    {
        for (int i = 0; i < bodies.Length; i++)
        {
            // Cleared while the body is still dynamic — a kinematic body will not accept a velocity
            // write, so a leftover one would be waiting the next time it is let go.
            if (!limp && !bodies[i].isKinematic)
            {
                bodies[i].linearVelocity = Vector3.zero;
                bodies[i].angularVelocity = Vector3.zero;
            }

            bodies[i].isKinematic = !limp;

            foreach (var c in colliders[i])
                if (c != null) c.enabled = limp;
        }
    }
}
