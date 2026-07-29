using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Opening beat: hold on the kid at the bus window, then let Cinemachine blend
/// out to the game camera and hand control to the player.
///
/// Runs once on scene load. Respawning never touches the camera, so the intro
/// cannot replay.
/// </summary>
public class IntroSequence : MonoBehaviour
{
    [Header("Cameras")]
    [SerializeField] CinemachineCamera introCam;
    [SerializeField] CinemachineCamera gameCam;

    [Header("Timing")]
    [Tooltip("How long to sit on the kid before blending out.")]
    [SerializeField] float holdDuration = 2f;
    [Tooltip("Must match the CinemachineBrain default blend time.")]
    [SerializeField] float blendDuration = 1.5f;

    [Header("Staging")]
    [Tooltip("Bus interior, kid, etc. If it carries a BusRig it is kept alive and set travelling with " +
             "the runner so the outro can pull back into it; otherwise it is hidden as dead weight.")]
    [SerializeField] GameObject introStaging;

    [Header("Skip")]
    [SerializeField] bool skippable = true;

    bool finished;

    void Start()
    {
        // IntroCam wins at load; GameCam takes over when we drop the priority.
        if (introCam != null) introCam.Priority.Value = 100;
        if (gameCam != null) gameCam.Priority.Value = 10;
        StartCoroutine(Play());
    }

    IEnumerator Play()
    {
        float elapsed = 0f;
        while (elapsed < holdDuration)
        {
            if (skippable && SkipPressed()) break;
            elapsed += Time.deltaTime;
            yield return null;
        }

        // Hand the shot to the game camera; Cinemachine blends between them.
        if (introCam != null) introCam.Priority.Value = 0;

        // Start running partway through the blend so the runner is already
        // moving as the camera arrives, rather than popping into motion.
        yield return new WaitForSeconds(blendDuration * 0.5f);

        if (RunManager.Instance != null) RunManager.Instance.BeginRun();

        yield return new WaitForSeconds(blendDuration * 0.5f);

        HandOffStaging();
        finished = true;
    }

    /// <summary>
    /// The bus used to be switched off here, since nothing looked at it again. It is now
    /// the outro's set, so when a BusRig is present the staging stays live and starts
    /// travelling with the runner instead. It sits behind the game camera either way, so
    /// keeping it on costs nothing on screen.
    /// </summary>
    void HandOffStaging()
    {
        if (introStaging == null) return;

        var rig = introStaging.GetComponent<BusRig>();
        if (rig != null) rig.BeginFollowing();
        else introStaging.SetActive(false);
    }

    static bool SkipPressed()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null && (keyboard.spaceKey.wasPressedThisFrame || keyboard.escapeKey.wasPressedThisFrame))
            return true;
        var touchscreen = Touchscreen.current;
        return touchscreen != null && touchscreen.primaryTouch.press.wasPressedThisFrame;
    }

    public bool Finished => finished;
}
