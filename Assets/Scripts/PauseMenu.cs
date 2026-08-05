using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// In-run pause. Freezes the run with timeScale and takes PlayerInputRouter out of the
/// loop while the panel is up, so a key pressed or a swipe made over the menu cannot be
/// spent the instant play resumes.
///
/// timeScale is the whole mechanism on purpose: PlayerController's slide, roll and coyote
/// windows are all Time.time deadlines, and the sequence coroutines wait on scaled
/// WaitForSeconds, so a scaled freeze stops every one of them consistently. Nothing
/// needs its own paused branch.
///
/// Pausing is offered while the run is live or between death and respawn, but not during
/// the intro or the finish shot — freezing a camera move would strand those coroutines
/// mid-blend with no way back.
/// </summary>
public class PauseMenu : MonoBehaviour
{
    [Header("UI")]
    [Tooltip("The top-right button. Hidden while the panel is up, and outside a live run.")]
    [SerializeField] GameObject pauseButton;
    [SerializeField] GameObject pausePanel;

    [Header("Refs")]
    [Tooltip("Disabled while paused so held keys and swipes do not queue up.")]
    [SerializeField] PlayerInputRouter inputRouter;

    [SerializeField] string menuSceneName = "Menu";

    public bool IsPaused { get; private set; }

    bool CanPause =>
        RunManager.Instance == null
        || RunManager.Instance.State == RunState.Running
        || RunManager.Instance.State == RunState.Dead;

    void Awake()
    {
        if (inputRouter == null) inputRouter = FindFirstObjectByType<PlayerInputRouter>();
    }

    void Start()
    {
        // Both of these outlive a scene load in the editor, so a run entered straight
        // after an Exit would otherwise open paused with the panel already up.
        if (pausePanel != null) pausePanel.SetActive(false);
        Time.timeScale = 1f;
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) Toggle();

        // Driven every frame rather than at the state changes, because the intro ending
        // and the finish starting are not events this component hears about.
        if (pauseButton != null) pauseButton.SetActive(!IsPaused && CanPause);
    }

    public void Toggle()
    {
        if (IsPaused) Resume();
        else Pause();
    }

    public void Pause()
    {
        if (IsPaused || !CanPause) return;
        IsPaused = true;
        Time.timeScale = 0f;
        if (inputRouter != null) inputRouter.enabled = false;
        if (pausePanel != null) pausePanel.SetActive(true);
    }

    public void Resume()
    {
        if (!IsPaused) return;
        IsPaused = false;
        Time.timeScale = 1f;
        if (inputRouter != null) inputRouter.enabled = true;
        if (pausePanel != null) pausePanel.SetActive(false);
    }

    /// <summary>Abandons the run and returns to the start menu.</summary>
    public void ExitToMenu()
    {
        IsPaused = false;
        Time.timeScale = 1f;   // the menu would otherwise load onto a stopped clock
        if (inputRouter != null) inputRouter.enabled = true;
        SceneManager.LoadScene(menuSceneName, LoadSceneMode.Single);
    }
}
