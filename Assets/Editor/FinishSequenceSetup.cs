using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One-shot wiring for the level-finish sequence. Menu: Bus Runner > Set Up Finish Sequence.
///
/// Builds and connects, in the open scene:
///   Lane/Finish_line         trigger box at the end of the lane
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
    // Past the last obstacle (a building at x=843), with room to keep running
    // through the pull-back. The ground runs to x=990.
    const float FinishLineX = 860f;

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
        if (runManager != null) SetRef(runManager, "finishSequence", finishSequence);

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[FinishSequenceSetup] Done. Save the scene to keep it.");
    }

    static BusRig SetUpBusRig(GameObject introStaging, Transform player)
    {
        var rig = introStaging.GetComponent<BusRig>();
        if (rig == null) rig = Undo.AddComponent<BusRig>(introStaging);
        SetRef(rig, "followTarget", player);
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

    static void SetUpFinishLine(GameObject lane)
    {
        var existing = Find("Finish_line");
        var go = existing != null ? existing : new GameObject("Finish_line");
        if (existing == null)
        {
            Undo.RegisterCreatedObjectUndo(go, "Create Finish_line");
            if (lane != null) Undo.SetTransformParent(go.transform, lane.transform, "Parent Finish_line");
        }

        Undo.RecordObject(go.transform, "Place Finish_line");
        go.transform.position = new Vector3(FinishLineX, 0f, 0f);
        go.transform.rotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        var box = go.GetComponent<BoxCollider>();
        if (box == null) box = Undo.AddComponent<BoxCollider>(go);
        Undo.RecordObject(box, "Configure Finish_line");
        box.isTrigger = true;
        // Tall enough to catch a player arriving along a rooftop, and thick enough
        // that 8 m/s cannot step over it in a single frame.
        box.center = new Vector3(0f, 10f, 0f);
        box.size = new Vector3(2f, 20f, 8f);

        if (go.GetComponent<FinishLine>() == null) Undo.AddComponent<FinishLine>(go);
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

        SetRef(seq, "gameCam", gameCam);
        SetRef(seq, "approachCam", approachCam);
        SetRef(seq, "outroCam", outroCam);
        SetRef(seq, "brain", brain);
        SetRef(seq, "player", player);
        SetRef(seq, "busRig", busRig);
        SetRef(seq, "touchUI", touchUI);
        return seq;
    }

    static GameObject Find(string name)
    {
        foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name == name) return root;
            var child = root.transform.Find(name);
            if (child != null) return child.gameObject;
        }
        return null;
    }

    /// <summary>Writes a private [SerializeField] object reference the way the inspector would.</summary>
    static void SetRef(Object target, string field, Object value)
    {
        var so = new SerializedObject(target);
        var prop = so.FindProperty(field);
        if (prop == null)
        {
            Debug.LogWarning($"[FinishSequenceSetup] {target.GetType().Name} has no serialized field '{field}'.");
            return;
        }
        prop.objectReferenceValue = value;
        so.ApplyModifiedProperties();
    }
}
