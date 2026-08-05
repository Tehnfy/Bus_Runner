using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Full-screen black curtain, and the scene handoff that happens behind it.
///
/// Builds itself — no scene object, no wiring, no per-scene copy. That is the point:
/// the curtain has to outlive the scene it was raised in, because a fader that dies with
/// the level would leave the load itself uncovered, which is exactly the flash the fade
/// exists to hide. So it is a DontDestroyOnLoad singleton, and the transition coroutine
/// runs on it rather than on the caller.
///
/// Everything is timed on unscaled time. A fade is presentation, not gameplay, and pausing
/// mid-transition must not strand the screen black forever.
/// </summary>
public class ScreenFader : MonoBehaviour
{
    public static ScreenFader Instance { get; private set; }

    // Above every gameplay canvas, PauseUI at 10 included — the curtain covers all of it.
    const int SortingOrder = 1000;
    const string ObjectName = "ScreenFader";

    Image curtain;
    Coroutine running;

    /// <summary>The live fader, created on the spot if this is the first call.</summary>
    public static ScreenFader Get()
    {
        if (Instance != null) return Instance;
        var go = new GameObject(ObjectName);
        return go.AddComponent<ScreenFader>();   // Awake claims Instance and builds the curtain
    }

    void Awake()
    {
        // Two of these would mean two curtains, and the second would sit over the first
        // permanently — whichever got there first keeps the job.
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        Build();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Build()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;
        // No CanvasScaler: the curtain is anchored to all four corners, so it fills any
        // resolution without one.
        gameObject.AddComponent<GraphicRaycaster>();

        var go = new GameObject("Curtain", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        curtain = go.AddComponent<Image>();
        curtain.color = Color.black;
        // Swallows clicks while it is up, so a button under the curtain cannot be hit
        // during a fade. Disabling the Image when clear turns that off again.
        curtain.raycastTarget = true;
        SetAlpha(0f);
    }

    /// <summary>
    /// Fades to black, swaps scenes behind the curtain, then fades back in. The caller's own
    /// coroutine dies with its scene, which is why this one lives here instead.
    /// </summary>
    public void TransitionTo(string sceneName, float fadeOutDuration, float blackHold, float fadeInDuration)
    {
        Restart(LoadBehindCurtain(sceneName, fadeOutDuration, blackHold, fadeInDuration));
    }

    public void FadeOut(float duration) => Restart(FadeRoutine(1f, duration));

    public void FadeIn(float duration) => Restart(FadeRoutine(0f, duration));

    IEnumerator LoadBehindCurtain(string sceneName, float fadeOutDuration, float blackHold, float fadeInDuration)
    {
        yield return FadeRoutine(1f, fadeOutDuration);

        var load = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
        while (load != null && !load.isDone) yield return null;

        // A beat of black after the load, so the new scene's first frame — lights warming
        // up, a camera settling — is never the thing the fade-in reveals.
        if (blackHold > 0f) yield return new WaitForSecondsRealtime(blackHold);

        yield return FadeRoutine(0f, fadeInDuration);
    }

    IEnumerator FadeRoutine(float targetAlpha, float duration)
    {
        float start = curtain.color.a;
        if (duration <= 0f)
        {
            SetAlpha(targetAlpha);
            yield break;
        }

        for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
        {
            SetAlpha(Mathf.Lerp(start, targetAlpha, elapsed / duration));
            yield return null;
        }
        SetAlpha(targetAlpha);
    }

    void SetAlpha(float alpha)
    {
        var color = curtain.color;
        color.a = Mathf.Clamp01(alpha);
        curtain.color = color;
        // Off entirely when clear, so an invisible curtain is neither drawn nor in the way
        // of a raycast for the whole rest of the session.
        curtain.enabled = color.a > 0.001f;
    }

    /// <summary>Replaces whatever fade was in flight — two at once would fight over the alpha.</summary>
    void Restart(IEnumerator routine)
    {
        if (running != null) StopCoroutine(running);
        running = StartCoroutine(Track(routine));
    }

    IEnumerator Track(IEnumerator routine)
    {
        yield return routine;
        running = null;
    }
}
