using UnityEngine;

/// <summary>
/// Writes the wallet to disk at the points where the app might not get another chance.
///
/// Collecting a coin only marks the wallet dirty — flushing there would be a whole-file PlayerPrefs
/// write per pickup. On mobile the process can be killed while backgrounded without OnApplicationQuit
/// ever running, so OnApplicationPause is the one that actually matters.
///
/// Creates itself, so no scene has to remember to carry it.
/// </summary>
[DisallowMultipleComponent]
public class CoinWalletFlusher : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Install()
    {
        var go = new GameObject("CoinWalletFlusher");
        go.AddComponent<CoinWalletFlusher>();
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideInHierarchy;
    }

    void OnApplicationPause(bool paused)
    {
        if (paused) CoinWallet.Flush();
    }

    void OnApplicationFocus(bool focused)
    {
        if (!focused) CoinWallet.Flush();
    }

    void OnApplicationQuit() => CoinWallet.Flush();
}
