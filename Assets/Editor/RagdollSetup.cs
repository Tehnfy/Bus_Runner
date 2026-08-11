using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the runner's ragdoll: a rigid body and a collider on eleven bones, and a CharacterJoint
/// chaining each of them to its parent. Idempotent — running it again replaces what it made last
/// time, so the numbers below can be edited and the pass re-run.
///
/// Unity ships a Ragdoll Wizard for this, but it is a modal window that wants eighteen bones dragged
/// into it by hand and gives back generic limits. This does the same job from the humanoid bone map,
/// with limits measured off the project's own clips (see the joint table).
///
/// Every bone on this rig has its local +Y pointing down its own length — measured, dominance 0.999
/// or better on all eight sampled bones — so a capsule along local Y fits every segment and the same
/// axis serves as the joint's swing reference throughout.
/// </summary>
public static class RagdollSetup
{
    /// <summary>
    /// One rigid body: the bone it sits on, the bone the collider reaches toward, and how heavy it is.
    /// A <see cref="end"/> of LastBone means a sphere at the bone instead of a capsule along it.
    /// </summary>
    struct Part
    {
        public HumanBodyBones bone;
        public HumanBodyBones end;
        public float radius;
        public float mass;

        public Part(HumanBodyBones bone, HumanBodyBones end, float radius, float mass)
        {
            this.bone = bone; this.end = end; this.radius = radius; this.mass = mass;
        }
    }

    /// <summary>Which world axis a joint twists about. Both are read off the player root, not the bone.</summary>
    enum Twist { Lateral, Up }

    struct Link
    {
        public HumanBodyBones bone;
        public HumanBodyBones parent;
        public Twist twist;
        public float lowTwist, highTwist, swing1, swing2;

        public Link(HumanBodyBones bone, HumanBodyBones parent, Twist twist,
                    float lowTwist, float highTwist, float swing1, float swing2)
        {
            this.bone = bone; this.parent = parent; this.twist = twist;
            this.lowTwist = lowTwist; this.highTwist = highTwist;
            this.swing1 = swing1; this.swing2 = swing2;
        }
    }

    // Radii come from the mesh: 0.25 deep and 0.34 across at the chest, 0.21 across the thighs.
    // Masses total about 17kg, which is light for a 1.7m figure — deliberately, because gravity is
    // mass-independent and the lighter body settles faster, so the runner is on the floor and ready
    // to respawn sooner.
    static readonly Part[] Parts =
    {
        new Part(HumanBodyBones.Hips,          HumanBodyBones.Chest,         0.105f, 3.0f),
        new Part(HumanBodyBones.Chest,         HumanBodyBones.Neck,          0.115f, 4.0f),
        new Part(HumanBodyBones.Head,          HumanBodyBones.LastBone,      0.105f, 1.6f),
        new Part(HumanBodyBones.LeftUpperLeg,  HumanBodyBones.LeftLowerLeg,  0.075f, 2.2f),
        new Part(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, 0.075f, 2.2f),
        new Part(HumanBodyBones.LeftLowerLeg,  HumanBodyBones.LeftFoot,      0.060f, 1.1f),
        new Part(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot,     0.060f, 1.1f),
        new Part(HumanBodyBones.LeftUpperArm,  HumanBodyBones.LeftLowerArm,  0.050f, 0.6f),
        new Part(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, 0.050f, 0.6f),
        new Part(HumanBodyBones.LeftLowerArm,  HumanBodyBones.LeftHand,      0.042f, 0.4f),
        new Part(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,     0.042f, 0.4f),
    };

    // Limits bracket what the project's own clips actually do, measured by sampling Run, Jump and
    // Slide at twenty points each and taking the signed angle of each segment against its parent:
    //
    //   knee    left 5..176, right 8..164 about the lane-lateral axis — the same sign on both sides,
    //           because knees bend in the sagittal plane and share that axis. Hence one entry, no mirror.
    //   hip     -108..31 about the same axis, negative being the knee coming up.
    //   elbow   left 37..170, right -175..-6 about the character's up axis — mirrored, so these two
    //           entries are not the same.
    //
    // Twist is about `axis`, swing1 about `swingAxis` (set to the bone's own length, so it is the
    // segment rotating about itself) and swing2 about the third axis. Hinges therefore get a wide
    // twist and both swings pinned near zero; balls get the reverse.
    static readonly Link[] Links =
    {
        new Link(HumanBodyBones.Chest,         HumanBodyBones.Hips,           Twist.Lateral,  -25f,  25f, 25f, 20f),
        new Link(HumanBodyBones.Head,          HumanBodyBones.Chest,          Twist.Lateral,  -30f,  25f, 30f, 25f),

        new Link(HumanBodyBones.LeftUpperLeg,  HumanBodyBones.Hips,           Twist.Lateral, -110f,  35f, 20f, 25f),
        new Link(HumanBodyBones.RightUpperLeg, HumanBodyBones.Hips,           Twist.Lateral, -110f,  35f, 20f, 25f),
        // Knees get almost no negative range at all. A real knee hyperextends about 5 degrees, and
        // this is the joint the eye is least forgiving about — a shin past straight reads as a broken
        // model instantly, where a slightly stiff bend reads as nothing.
        new Link(HumanBodyBones.LeftLowerLeg,  HumanBodyBones.LeftUpperLeg,   Twist.Lateral,   -5f, 155f,  6f,  6f),
        new Link(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightUpperLeg,  Twist.Lateral,   -5f, 155f,  6f,  6f),

        new Link(HumanBodyBones.LeftUpperArm,  HumanBodyBones.Chest,          Twist.Up,       -85f,  85f, 70f, 60f),
        new Link(HumanBodyBones.RightUpperArm, HumanBodyBones.Chest,          Twist.Up,       -85f,  85f, 70f, 60f),
        // Elbows, same idea as the knees but mirrored — the arms lie along the lateral axis in the
        // bind pose, so left and right flex in opposite senses about the shared up axis.
        new Link(HumanBodyBones.LeftLowerArm,  HumanBodyBones.LeftUpperArm,   Twist.Up,       -10f, 160f, 25f, 10f),
        new Link(HumanBodyBones.RightLowerArm, HumanBodyBones.RightUpperArm,  Twist.Up,      -160f,  10f, 25f, 10f),
    };

    const float HeadOffset = 0.05f;   // the head bone sits below the middle of the skull

    [MenuItem("Bus Runner/Set Up Ragdoll")]
    static void RunOnSelectionOrScene()
    {
        var target = Selection.activeGameObject != null
            ? Selection.activeGameObject.GetComponentInParent<PlayerController>()
            : Object.FindFirstObjectByType<PlayerController>();

        if (target == null)
        {
            Debug.LogError("[RagdollSetup] No PlayerController selected or in the open scene.");
            return;
        }

        Debug.Log(Build(target.gameObject));
    }

    /// <summary>
    /// Puts the ragdoll on <paramref name="playerRoot"/> and returns a report of what it made.
    /// Safe to call twice: anything it added last time is removed first.
    /// </summary>
    public static string Build(GameObject playerRoot)
    {
        var animator = playerRoot.GetComponent<Animator>();
        if (animator == null || !animator.isHuman)
            return $"[RagdollSetup] '{playerRoot.name}' has no humanoid Animator — nothing to build from.";

        var report = new StringBuilder($"[RagdollSetup] '{playerRoot.name}':\n");
        report.AppendLine("  " + ResetToBindPose(playerRoot));

        // Clear first, all of it, before anything is added. A joint whose connected body is about to
        // be destroyed would otherwise be left dangling half way through.
        for (int i = 0; i < Parts.Length; i++)
        {
            var bone = animator.GetBoneTransform(Parts[i].bone);
            if (bone != null) Strip(bone.gameObject);
        }

        float totalMass = 0f;
        int built = 0;

        foreach (var part in Parts)
        {
            var bone = animator.GetBoneTransform(part.bone);
            if (bone == null)
            {
                report.AppendLine($"  {part.bone}: MISSING from the avatar, skipped");
                continue;
            }

            // Bone colliders have to answer to the same collision matrix row as the capsule they
            // stand in for, or the ragdoll falls through the floor the runner was just standing on.
            if (bone.gameObject.layer != playerRoot.layer)
            {
                Undo.RecordObject(bone.gameObject, "Ragdoll layer");
                bone.gameObject.layer = playerRoot.layer;
            }

            var body = Undo.AddComponent<Rigidbody>(bone.gameObject);
            body.mass = part.mass;
            body.linearDamping = 0.05f;
            body.angularDamping = 0.15f;   // a little, or the limbs windmill forever
            body.useGravity = true;
            body.isKinematic = true;       // PlayerRagdoll lets go of them on death
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.solverIterations = 12;
            body.solverVelocityIterations = 4;
            body.maxDepenetrationVelocity = 3f;

            string shape;
            if (part.end == HumanBodyBones.LastBone)
            {
                var sphere = Undo.AddComponent<SphereCollider>(bone.gameObject);
                sphere.radius = part.radius;
                sphere.center = Vector3.up * HeadOffset;
                shape = $"sphere r={part.radius:F3}";
            }
            else
            {
                var endBone = animator.GetBoneTransform(part.end);
                if (endBone == null)
                {
                    report.AppendLine($"  {part.bone}: end bone {part.end} MISSING, skipped");
                    continue;
                }

                var localEnd = bone.InverseTransformPoint(endBone.position);
                float length = localEnd.magnitude;

                var capsule = Undo.AddComponent<CapsuleCollider>(bone.gameObject);
                capsule.direction = DominantAxis(localEnd);
                capsule.radius = part.radius;
                capsule.height = length + part.radius * 2f;   // Unity's height includes both caps
                capsule.center = localEnd * 0.5f;
                shape = $"capsule r={part.radius:F3} len={length:F3} axis={"XYZ"[capsule.direction]}";
            }

            totalMass += part.mass;
            built++;
            report.AppendLine($"  {part.bone} on '{bone.name}': {shape} mass={part.mass:F1}");
        }

        var lateral = playerRoot.transform.right;
        var up = playerRoot.transform.up;
        int jointed = 0;

        foreach (var link in Links)
        {
            var bone = animator.GetBoneTransform(link.bone);
            var parent = animator.GetBoneTransform(link.parent);
            if (bone == null || parent == null) continue;

            var parentBody = parent.GetComponent<Rigidbody>();
            if (parentBody == null)
            {
                report.AppendLine($"  {link.bone}: parent {link.parent} has no body, unjointed");
                continue;
            }

            var joint = Undo.AddComponent<CharacterJoint>(bone.gameObject);
            joint.connectedBody = parentBody;

            // The bone's own pivot is the joint. Everything below is expressed in this bone's local
            // space, which is why the world axes have to be converted rather than used directly.
            joint.anchor = Vector3.zero;
            joint.autoConfigureConnectedAnchor = true;

            var worldTwist = link.twist == Twist.Lateral ? lateral : up;
            var axis = bone.InverseTransformDirection(worldTwist).normalized;

            // Local +Y is down the bone on this rig, so this makes swing1 the segment turning about
            // its own length. Orthogonalised against the twist axis, because PhysX builds its frame
            // from the pair and a swing axis leaning into the twist axis skews both limits.
            var swing = Vector3.up - axis * Vector3.Dot(Vector3.up, axis);
            if (swing.sqrMagnitude < 1e-6f) swing = Vector3.forward;

            joint.axis = axis;
            joint.swingAxis = swing.normalized;

            // Negated on the way in, because PhysX measures twist in the opposite sense to the axis
            // it is given. Measured: with lowTwist -10 and highTwist 160 written straight through,
            // a dead runner's knee settled at exactly -160 degrees — clamped at the high magnitude,
            // on the wrong side. That is 160 degrees of hyperextension and 10 of actual bend, which
            // is a knee folding forwards. The table above is written in anatomical terms (positive
            // is flexion, as measured off the clips) and the flip happens here, once.
            joint.lowTwistLimit = new SoftJointLimit { limit = -link.highTwist };
            joint.highTwistLimit = new SoftJointLimit { limit = -link.lowTwist };
            joint.swing1Limit = new SoftJointLimit { limit = link.swing1 };
            joint.swing2Limit = new SoftJointLimit { limit = link.swing2 };

            // Adjacent bones overlap by design — the capsules are sized to fill the body, not to
            // avoid each other — so the pair a joint connects must not collide.
            joint.enableCollision = false;

            // Projection pulls a joint that has been forced apart back together. Death happens
            // against a wall the capsule is already inside, which is exactly the case that stretches
            // one, and a stretched ragdoll reads as a broken model rather than a dead runner.
            joint.enableProjection = true;
            joint.projectionDistance = 0.1f;
            joint.projectionAngle = 30f;

            // Preprocessing is what turns a badly-conditioned joint into an explosion; off is the
            // standard advice for ragdolls and costs nothing here.
            joint.enablePreprocessing = false;

            jointed++;
        }

        if (playerRoot.GetComponent<PlayerRagdoll>() == null)
        {
            Undo.AddComponent<PlayerRagdoll>(playerRoot);
            report.AppendLine("  added PlayerRagdoll");
        }

        report.Append($"  {built} bodies, {jointed} joints, total mass {totalMass:F1}kg");
        return report.ToString();
    }

    /// <summary>
    /// Puts the skeleton back into its bind pose before anything is measured or created, and leaves
    /// it there.
    ///
    /// This is not tidiness — the pose is an input to the build twice over:
    ///
    ///   Axes. A joint's axis is stored in the bone's local space, and is derived here by converting
    ///   a world direction into it. In a bind pose the arms lie along the lane-lateral axis, so the
    ///   character's up axis crosses them and makes a usable elbow hinge. Built from a running pose
    ///   instead, the left forearm's axis came out as (-0.13, -0.98, -0.15) — local -Y, which on this
    ///   rig is straight down the bone. That elbow would have twisted rather than bent.
    ///
    ///   Zero. PhysX makes the two halves of a joint's frame coincide at the moment the joint is
    ///   created, which is when the scene loads, in whatever pose the scene was saved in. So the
    ///   saved pose is what "no rotation" means to every limit below. A bind pose puts that zero at
    ///   a straight limb, which is the reference the measured limits were taken against.
    ///
    /// The bind poses come off the SkinnedMeshRenderer, which stores each bone's inverse rest matrix
    /// relative to the renderer's own transform. All scales on this rig are 1, so position and
    /// rotation are enough to restore it.
    /// </summary>
    static string ResetToBindPose(GameObject playerRoot)
    {
        var smr = playerRoot.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (smr == null || smr.sharedMesh == null) return "no skinned mesh, pose left as found";

        var bones = smr.bones;
        var binds = smr.sharedMesh.bindposes;
        if (bones.Length != binds.Length) return $"bone/bindpose mismatch ({bones.Length} vs {binds.Length}), pose left as found";

        var rendererToWorld = smr.transform.localToWorldMatrix;
        var targets = new Matrix4x4[bones.Length];
        for (int i = 0; i < bones.Length; i++) targets[i] = rendererToWorld * binds[i].inverse;

        // Parents first. A world pose written to a parent drags its children with it, so a child
        // placed before its parent would be moved straight back out of position.
        var order = new int[bones.Length];
        var depths = new int[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            order[i] = i;
            depths[i] = Depth(bones[i]);
        }
        System.Array.Sort(depths, order);

        foreach (int i in order)
        {
            if (bones[i] == null) continue;
            Undo.RecordObject(bones[i], "Ragdoll bind pose");
            bones[i].SetPositionAndRotation(targets[i].GetColumn(3), targets[i].rotation);
        }

        return $"reset {bones.Length} bones to the bind pose";
    }

    static int Depth(Transform t)
    {
        int d = 0;
        while (t != null) { d++; t = t.parent; }
        return d;
    }

    /// <summary>Removes only what this pass adds, so a rebuild cannot stack two ragdolls.</summary>
    static void Strip(GameObject go)
    {
        foreach (var joint in go.GetComponents<CharacterJoint>()) Undo.DestroyObjectImmediate(joint);
        foreach (var capsule in go.GetComponents<CapsuleCollider>()) Undo.DestroyObjectImmediate(capsule);
        foreach (var sphere in go.GetComponents<SphereCollider>()) Undo.DestroyObjectImmediate(sphere);
        foreach (var body in go.GetComponents<Rigidbody>()) Undo.DestroyObjectImmediate(body);
    }

    static int DominantAxis(Vector3 v)
    {
        var a = new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
        if (a.x >= a.y && a.x >= a.z) return 0;
        return a.y >= a.z ? 1 : 2;
    }
}
