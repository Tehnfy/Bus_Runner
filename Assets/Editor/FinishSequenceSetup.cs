using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One-shot wiring for the level-finish sequence. Menu: Bus Runner > Set Up Finish Sequence.
///
/// Builds and connects, in the open scene:
///   Lane/Finish_trigger      tall trigger box at the end of the lane
///   IntroStaging             gains a BusRig so the bus travels with the runner
///   IntroStaging/ApproachCam waypoint outside the bus window
///   IntroStaging/OutroCam    the shot at the bus window, beside the kid
///   FinishSequence           the orchestrator, wired to the cameras, brain and player
///   RunManager               gains its FinishSequence reference
///
/// Safe to run more than once — it reuses whatever it already made.
/// </summary>
static class FinishSequenceSetup
{
    // Only used when the level has no hand-placed Finish_line marker to read the
    // position off. Past the last obstacle (a building ending at x=846.7), with room
    // to keep running through the pull-back. The ground runs to x=990.
    const float FallbackFinishLineX = 950f;

    // The visual marker the level designer places on the road. Its own collider and
    // scale are left alone — it is read for its X and nothing else.
    const string MarkerName = "Finish_line";

    // The trigger is a separate object so it can be tall and wide without stretching
    // the marker's mesh, and so re-running this never disturbs hand placement.
    const string TriggerName = "Finish_trigger";

    // All camera positions are local to the BusRig, so they travel with the bus.
    //
    // The bus mockup's only opening is the window in the z=-17.6 wall: x 18.45-21.55,
    // y 0.8-1.9. ApproachCam sits on the lane side of that wall, at window height and
    // centred on the opening, so the leg that passes through it always threads the
    // hole no matter where the game camera happens to be at the finish.
    static readonly Vector3 ApproachCamLocalPosition = new Vector3(20f, 1.35f, -13f);
    static readonly Vector3 ApproachCamLocalEuler = new Vector3(1.5f, 0f, 0f);
    const float ApproachCamFov = 62f;   // matches GameCam, so stage one is a pure dolly

    // The seat beside the kid. Frames the window opening with the runner inside it
    // and the kid low and right in the foreground.
    static readonly Vector3 OutroCamLocalPosition = new Vector3(19f, 1.35f, -20.8f);
    static readonly Vector3 OutroCamLocalEuler = new Vector3(4f, 14f, 0f);
    const float OutroCamFov = 45f;   // matches IntroCam — the bus interior is tight

    [MenuItem("Bus Runner/Set Up Finish Sequence")]
    static void Run()
    {
        var scene = EditorSceneManager.GetActiveScene();

        var introStaging = Find("IntroStaging");
        var gameCamGo = Find("GameCam");
        var mainCamera = Find("Main Camera");
        var runManagerGo = Find("RunManager");
        var lane = Find("Lane");
        var touchUI = Find("TouchUI");
        var playerGo = GameObject.FindGameObjectWithTag("Player");

        if (introStaging == null || gameCamGo == null || mainCamera == null || runManagerGo == null)
        {
            Debug.LogError("[FinishSequenceSetup] Open Level_1 first — could not find IntroStaging, " +
                           "GameCam, Main Camera and RunManager in the active scene.");
            return;
        }
        if (playerGo == null)
        {
            Debug.LogError("[FinishSequenceSetup] No object tagged 'Player' in the scene.");
            return;
        }

        Undo.SetCurrentGroupName("Set Up Finish Sequence");
        int group = Undo.GetCurrentGroup();

        var busRig = SetUpBusRig(introStaging, playerGo.transform);
        var approachCam = SetUpShotCam(introStaging.transform, "ApproachCam",
            ApproachCamLocalPosition, ApproachCamLocalEuler, ApproachCamFov);
        var outroCam = SetUpShotCam(introStaging.transform, "OutroCam",
            OutroCamLocalPosition, OutroCamLocalEuler, OutroCamFov);
        SetUpFinishLine(lane);
        var finishSequence = SetUpFinishSequence(
            gameCamGo.GetComponent<CinemachineCamera>(),
            approachCam,
            outroCam,
            mainCamera.GetComponent<CinemachineBrain>(),
            playerGo.GetComponent<PlayerController>(),
            busRig,
            touchUI);

        var runManager = runManagerGo.GetComponent<RunManager>();
        if (runManager != null) UiBuild.SetRef(runManager, "finishSequence", finishSequence);

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[FinishSequenceSetup] Done. Save the scene to keep it.");
    }

    static BusRig SetUpBusRig(GameObject introStaging, Transform player)
    {
        var rig = introStaging.GetComponent<BusRig>();
        if (rig == null) rig = Undo.AddComponent<BusRig>(introStaging);
        UiBuild.SetRef(rig, "followTarget", player);
        return rig;
    }

    static CinemachineCamera SetUpShotCam(
        Transform busRig, string name, Vector3 localPosition, Vector3 localEuler, float fov)
    {
        var existing = busRig.Find(name);
        var go = existing != null ? existing.gameObject : new GameObject(name);
        if (existing == null)
        {
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            Undo.SetTransformParent(go.transform, busRig, "Parent " + name);
        }

        Undo.RecordObject(go.transform, "Place " + name);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = Quaternion.Euler(localEuler);
        go.transform.localScale = Vector3.one;

        var cam = go.GetComponent<CinemachineCamera>();
        if (cam == null) cam = Undo.AddComponent<CinemachineCamera>(go);

        Undo.RecordObject(cam, "Configure " + name);
        cam.Priority.Value = 0;              // FinishSequence raises this to take the shot
        var lens = cam.Lens;
        lens.FieldOfView = fov;
        lens.NearClipPlane = 0.1f;
        lens.FarClipPlane = 5000f;
        cam.Lens = lens;
        return cam;
    }

    /// <summary>
    /// Puts the finish trigger where the level's Finish_line marker already stands, or at
    /// the fallback X when there is no marker. The marker itself is never touched — it is
    /// a scaled, hand-placed mesh, and rewriting its transform or its collider to trigger
    /// dimensions would deform the visual.
    /// </summary>
    static void SetUpFinishLine(GameObject lane)
    {
        var marker = Find(MarkerName);
        float x = marker != null ? marker.transform.position.x : FallbackFinishLineX;
        if (marker == null)
            Debug.LogWarning($"[FinishSequenceSetup] No '{MarkerName}' in the scene — " +
                             $"putting the finish trigger at the fallback x={FallbackFinishLineX}.");

        var existing = Find(TriggerName);
        var go = existing != null ? existing : new GameObject(TriggerName);
        if (existing == null)
        {
            Undo.RegisterCreatedObjectUndo(go, "Create " + TriggerName);
            if (lane != null) Undo.SetTransformParent(go.transform, lane.transform, "Parent " + TriggerName);
        }

        Undo.RecordObject(go.transform, "Place " + TriggerName);
        go.transform.position = new Vector3(x, 0f, 0f);
        go.transform.rotation = Quaternion.identity;
        // Scale one, so the box dimensions below are world units regardless of the
        // scaling on the marker or on Lane.
        go.transform.localScale = Vector3.one;

        var box = go.GetComponent<BoxCollider>();
        if (box == null) box = Undo.AddComponent<BoxCollider>(go);
        Undo.RecordObject(box, "Configure " + TriggerName);
        box.isTrigger = true;
        // Tall enough to catch a player arriving along a rooftop, and thick enough
        // that 8 m/s cannot step over it in a single frame.
        box.center = new Vector3(0f, 10f, 0f);
        box.size = new Vector3(2f, 20f, 8f);

        if (go.GetComponent<FinishLine>() == null) Undo.AddComponent<FinishLine>(go);

        Debug.Log($"[FinishSequenceSetup] Finish trigger at x={x:F1}"
                  + (marker != null ? $" (read off '{MarkerName}')." : " (fallback)."));
    }

    static FinishSequence SetUpFinishSequence(
        CinemachineCamera gameCam, CinemachineCamera approachCam, CinemachineCamera outroCam,
        CinemachineBrain brain, PlayerController player, BusRig busRig, GameObject touchUI)
    {
        var existing = Find("FinishSequence");
        var go = existing != null ? existing : new GameObject("FinishSequence");
        if (existing == null) Undo.RegisterCreatedObjectUndo(go, "Create FinishSequence");

        var seq = go.GetComponent<FinishSequence>();
        if (seq == null) seq = Undo.AddComponent<FinishSequence>(go);

        UiBuild.SetRef(seq, "gameCam", gameCam);
        UiBuild.SetRef(seq, "approachCam", approachCam);
        UiBuild.SetRef(seq, "outroCam", outroCam);
        UiBuild.SetRef(seq, "brain", brain);
        UiBuild.SetRef(seq, "player", player);
        UiBuild.SetRef(seq, "busRig", busRig);
        UiBuild.SetRef(seq, "touchUI", touchUI);
        return seq;
    }

    /// <summary>
    /// Scene lookup for this command. Always searches one level of children as well — the trigger
    /// and its marker live under Lane, and the shot cameras under IntroStaging.
    /// </summary>
    static GameObject Find(string name) => UiBuild.FindRoot(name, includeChildren: true);
}
