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
    [Tooltip("Hidden once the intro is over — bus interior, kid, etc.")]
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

        if (introStaging != null) introStaging.SetActive(false);
        finished = true;
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
