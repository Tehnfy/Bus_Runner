using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// One-shot wiring for the landing roll. Menu: Bus Runner > Set Up Landing Roll.
///
/// The roll FBX arrives from Mixamo imported as Generic with no avatar, so its clip
/// cannot retarget onto the runner. This fixes the import to match the other three
/// animations (Humanoid, avatar copied from the character model), names the clip
/// "Roll" so PlayerController can measure its length at runtime, and adds the state
/// and transitions to PlayerAnimator.
///
/// Whether the clip needs trimming depends on the take. "Falling To Roll" opened with a descent
/// whose first frames put the feet 0.515 above the capsule base against ~0.10 for a grounded run,
/// and since BeginRoll fires on touchdown those frames rendered as a hover, so they were cut.
/// "Quick Roll To Run" starts on the ground, so it is used whole.
///
/// Safe to run more than once — it reuses whatever it already made.
/// </summary>
static class LandingRollSetup
{
    const string RollFbx = "Assets/Animations/Quick Roll To Run.fbx";

    // Frames kept from the take. Both negative means "use the whole clip", which is the case for
    // "Quick Roll To Run" — it starts on the ground, so there is no descent to cut. The trim branch
    // below is therefore unreachable today; it is kept for the next take, which may need it the way
    // "Falling To Roll" did.
    const float LandingFrame = -1f;
    const float LastFrame = -1f;
    const string CharacterFbx = "Assets/Models/Ch36_nonPBR.fbx";
    const string ControllerPath = "Assets/Animations/PlayerAnimator.controller";
    const string ClipName = "Roll";
    const string StateName = "Roll";
    const string TriggerName = "Roll";

    // The state's speed is multiplied by this parameter rather than being typed into the state, so
    // PlayerController owns the number — it needs the same value to derive the roll's deadlines from.
    const string SpeedParameterName = "RollSpeed";

    // The roll can be entered mid-jump or straight off a run, so it comes in from Any
    // State. Coming out, it mirrors the existing Jump/Slide cross-links: back to Run on
    // its own, or interrupted by an action once PlayerController's input lock has lifted.
    const float ExitToRunAt = 0.9f;
    const float ExitToRunDuration = 0.12f;
    const float InterruptDuration = 0.08f;

    [MenuItem("Bus Runner/Set Up Landing Roll")]
    static void Run()
    {
        var clip = FixImportSettings();
        if (clip == null) return;

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogError($"[LandingRollSetup] No AnimatorController at {ControllerPath}.");
            return;
        }

        WireController(controller, clip);
        AssetDatabase.SaveAssets();
        Debug.Log($"[LandingRollSetup] Done. '{ClipName}' is {clip.length:F2}s. The input lock after " +
                  $"touchdown is set separately on the player (rollInputLockDuration) and is not " +
                  $"derived from this length.");
    }

    /// <summary>
    /// Retargets the FBX onto the character's avatar and names its clip. Returns the
    /// imported clip, or null if something is missing.
    /// </summary>
    static AnimationClip FixImportSettings()
    {
        var importer = AssetImporter.GetAtPath(RollFbx) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError($"[LandingRollSetup] No model importer at {RollFbx}.");
            return null;
        }

        var avatar = AssetDatabase.LoadAllAssetsAtPath(CharacterFbx).OfType<Avatar>().FirstOrDefault();
        if (avatar == null)
        {
            Debug.LogError($"[LandingRollSetup] No Avatar on {CharacterFbx} to copy from.");
            return null;
        }

        // Humanoid first, and reimport before reading the clip list — the defaults differ
        // between Generic and Humanoid, so reading them early gives the wrong take.
        if (importer.animationType != ModelImporterAnimationType.Human
            || importer.sourceAvatar != avatar)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            importer.sourceAvatar = avatar;
            importer.SaveAndReimport();
        }

        // PlayerController finds the clip by name to read its length, so the Mixamo take
        // name will not do — and the descent has to come off the front.
        var existing = importer.clipAnimations;
        bool trimWanted = LandingFrame >= 0f && LastFrame >= 0f;
        bool needsFixing = existing.Length == 0
                           || existing[0].name != ClipName
                           || (trimWanted && (!Mathf.Approximately(existing[0].firstFrame, LandingFrame)
                                              || !Mathf.Approximately(existing[0].lastFrame, LastFrame)));

        if (needsFixing)
        {
            var clips = existing.Length > 0 ? existing : importer.defaultClipAnimations;
            if (clips.Length == 0)
            {
                Debug.LogError($"[LandingRollSetup] {RollFbx} contains no animation take.");
                return null;
            }
            clips[0].name = ClipName;
            clips[0].loopTime = false;
            if (trimWanted)
            {
                clips[0].firstFrame = LandingFrame;
                clips[0].lastFrame = LastFrame;
            }
            importer.clipAnimations = new[] { clips[0] };
            importer.SaveAndReimport();
        }

        var clip = AssetDatabase.LoadAllAssetsAtPath(RollFbx)
            .OfType<AnimationClip>()
            .FirstOrDefault(c => c.name == ClipName);

        if (clip == null) Debug.LogError($"[LandingRollSetup] No clip named '{ClipName}' after reimport.");
        return clip;
    }

    static void WireController(AnimatorController controller, AnimationClip clip)
    {
        if (!controller.parameters.Any(p => p.name == TriggerName))
            controller.AddParameter(TriggerName, AnimatorControllerParameterType.Trigger);

        // Defaults to 1, not 0: a controller whose parameter never gets written should play the roll
        // at normal speed, not freeze on its first frame.
        if (!controller.parameters.Any(p => p.name == SpeedParameterName))
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = SpeedParameterName,
                type = AnimatorControllerParameterType.Float,
                defaultFloat = 1f,
            });

        var machine = controller.layers[0].stateMachine;
        var run = FindState(machine, "Run");
        var jump = FindState(machine, "Jump");
        var slide = FindState(machine, "Slide");

        var roll = FindState(machine, StateName);
        if (roll == null) roll = machine.AddState(StateName, new Vector3(560f, 210f, 0f));
        roll.motion = clip;
        roll.speed = 1f;
        roll.speedParameterActive = true;
        roll.speedParameter = SpeedParameterName;

        // In: from anywhere, on the trigger. No exit time — the landing decides the timing.
        if (!machine.anyStateTransitions.Any(t => t.destinationState == roll))
        {
            var enter = machine.AddAnyStateTransition(roll);
            enter.AddCondition(AnimatorConditionMode.If, 0f, TriggerName);
            enter.hasExitTime = false;
            enter.hasFixedDuration = true;
            enter.duration = InterruptDuration;
            enter.canTransitionToSelf = false;
        }

        // Out: back to running when the roll plays out.
        if (run != null && !roll.transitions.Any(t => t.destinationState == run))
        {
            var back = roll.AddTransition(run);
            back.hasExitTime = true;
            back.exitTime = ExitToRunAt;
            back.hasFixedDuration = true;
            back.duration = ExitToRunDuration;
        }

        // Out: interrupted by an action. PlayerController blocks these for only a fraction of a
        // second after touchdown, so the roll can be cancelled almost immediately on landing.
        AddInterrupt(roll, jump, "Jump");
        AddInterrupt(roll, slide, "Slide");

        EditorUtility.SetDirty(controller);
    }

    static void AddInterrupt(AnimatorState from, AnimatorState to, string trigger)
    {
        if (to == null || from.transitions.Any(t => t.destinationState == to)) return;
        var t2 = from.AddTransition(to);
        t2.AddCondition(AnimatorConditionMode.If, 0f, trigger);
        t2.hasExitTime = false;
        t2.hasFixedDuration = true;
        t2.duration = InterruptDuration;
    }

    static AnimatorState FindState(AnimatorStateMachine machine, string name)
    {
        foreach (var child in machine.states)
            if (child.state.name == name) return child.state;
        return null;
    }
}
