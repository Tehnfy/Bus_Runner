using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Closing beat, and a deliberate mirror of IntroSequence.
///
/// The runner keeps running — they are never stopped mid-shot — while the camera
/// pulls back off their shoulder and then glides in through the bus window to
/// settle beside the kid. Because BusRig keeps the bus travelling with the runner,
/// the runner stays framed in the window opening for the whole move instead of
/// sliding out of shot.
///
/// Two stages, not one. A Cinemachine blend moves in a straight line between the
/// two camera positions, and a straight line from the game camera to the window
/// seat can clip the window frame — the game camera's height at the finish depends
/// on where the PositionComposer's damping has left it. ApproachCam is a fixed
/// waypoint on the lane side of the window, at window height and lined up with the
/// opening, so the leg that actually passes through the wall starts from a known
/// place and always threads the hole.
///
/// Runs once. Triggered by RunManager.Finish(), which FinishLine calls.
/// </summary>
public class FinishSequence : MonoBehaviour
{
    [Header("Cameras")]
    [SerializeField] CinemachineCamera gameCam;
    [Tooltip("Waypoint on the lane side of the bus window, at window height. Must be a child of the BusRig.")]
    [SerializeField] CinemachineCamera approachCam;
    [Tooltip("The seat beside the kid, inside the bus. Must be a child of the BusRig.")]
    [SerializeField] CinemachineCamera outroCam;
    [SerializeField] CinemachineBrain brain;

    [Header("Refs")]
    [SerializeField] PlayerController player;
    [Tooltip("Kept moving through the whole shot so the runner stays inside the window opening.")]
    [SerializeField] BusRig busRig;
    [Tooltip("Jump/slide buttons — hidden once the run is over.")]
    [SerializeField] GameObject touchUI;

    [Header("Timing")]
    [Tooltip("Stage one: off the runner's shoulder, back to the outside of the bus window.")]
    [SerializeField] float pullBackDuration = 2.5f;
    [Tooltip("Stage two: in through the window to the seat beside the kid.")]
    [SerializeField] float moveInDuration = 2f;
    [Tooltip("How long to sit on the kid and the running player before the runner is stopped.")]
    [SerializeField] float holdDuration = 3f;

    bool played;

    public bool Finished { get; private set; }

    void Awake()
    {
        if (brain == null) brain = FindFirstObjectByType<CinemachineBrain>();
        if (player == null) player = FindFirstObjectByType<PlayerController>();
        if (busRig == null) busRig = FindFirstObjectByType<BusRig>();
    }

    /// <summary>Called by RunManager when the finish line is crossed.</summary>
    public void Play()
    {
        if (played) return;
        played = true;
        StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        // The run is over, so take away the actions but leave the legs moving —
        // a jump or slide landing mid-shot would read as the player still being
        // in control of something.
        if (player != null) player.LockActions(true);
        if (touchUI != null) touchUI.SetActive(false);

        // Stage one: pull back to the window.
        SetBlendTime(pullBackDuration);
        if (gameCam != null) gameCam.Priority.Value = 10;
        if (approachCam != null) approachCam.Priority.Value = 100;
        yield return new WaitForSeconds(pullBackDuration);

        // Stage two: in through the opening, ending beside the kid.
        SetBlendTime(moveInDuration);
        if (outroCam != null) outroCam.Priority.Value = 200;
        yield return new WaitForSeconds(moveInDuration + holdDuration);

        // Only now does the runner stop, well after the camera has landed.
        if (player != null) player.EnableControl(false);
        if (busRig != null) busRig.StopFollowing();

        Finished = true;
        Debug.Log($"[FinishSequence] Level complete — run time {RunManager.Instance?.RunTime:F1}s");
    }

    /// <summary>
    /// The intro's 1.5s brain default is the wrong length for both outro legs, and each
    /// leg wants its own. Nothing blends after this shot, so retiming the default is
    /// cheaper than carrying a CinemachineBlenderSettings asset for two transitions.
    /// </summary>
    void SetBlendTime(float seconds)
    {
        if (brain == null) return;
        brain.DefaultBlend = new CinemachineBlendDefinition(
            CinemachineBlendDefinition.Styles.EaseInOut, seconds);
    }
}
