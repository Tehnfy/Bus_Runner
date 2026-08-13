using UnityEditor;
using UnityEngine;

/// <summary>
/// The same coin resets CoinDevPanel offers in the menu, available without entering play mode.
/// Menu: Bus Runner > Coins.
///
/// Not a duplicate of the runtime panel — this one can do something the panel cannot. Collected marks
/// live in PlayerPrefs, which is not enumerable, so the wallet keeps its own index of what it has
/// written; a mark made before that index existed is unreachable through it. From the editor the open
/// scene is available, so every placed coin can be asked for its own id and that exact key deleted.
///
/// Anything cleared here is shared with the runtime wallet — same PlayerPrefs, so the editor and a
/// build on this machine see one save.
/// </summary>
static class CoinSaveTools
{
    [MenuItem("Bus Runner/Coins/Reset Permanent Coins")]
    static void ResetPermanent() => Reset(CoinType.Permanent);

    [MenuItem("Bus Runner/Coins/Reset Special Coins")]
    static void ResetSpecial() => Reset(CoinType.Special);

    [MenuItem("Bus Runner/Coins/Reset All Coin Progress")]
    static void ResetAll()
    {
        if (!Confirm("every coin type")) return;

        int cleared = CoinWallet.ResetAll();
        cleared += SweepOpenScene(null);
        CoinWallet.Flush();
        Report("all coin types", cleared);
    }

    [MenuItem("Bus Runner/Coins/Report Coin Save")]
    static void ReportSave()
    {
        var text = new System.Text.StringBuilder("[CoinSaveTools] Coin save:\n");
        foreach (var type in CoinWallet.Types)
        {
            text.Append($"  {type}: balance {CoinWallet.Balance(type)}");
            if (type != CoinType.Respawnable) text.Append($", {CoinWallet.CollectedCount(type)} taken");
            text.Append('\n');
        }
        Debug.Log(text.ToString().TrimEnd());
    }

    static void Reset(CoinType type)
    {
        if (!Confirm(type.ToString())) return;

        int cleared = CoinWallet.ResetType(type);
        cleared += SweepOpenScene(type);
        CoinWallet.Flush();
        Report(type.ToString(), cleared);
    }

    /// <summary>
    /// Clears the mark on every coin placed in the open scene, whatever shape its key is in. Catches
    /// what the index cannot: marks written before the index existed, and any coin whose id was
    /// re-rolled by Repair Coin IDs after it was already taken.
    /// </summary>
    /// <param name="type">One type, or null for all of them.</param>
    static int SweepOpenScene(CoinType? type)
    {
        int cleared = 0;
        var coins = Object.FindObjectsByType<Coin>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (var coin in coins)
        {
            if (type.HasValue && coin.Type != type.Value) continue;
            if (string.IsNullOrEmpty(coin.CoinId)) continue;
            if (!CoinWallet.IsCollected(coin.Type, coin.gameObject.scene.name, coin.CoinId)) continue;

            CoinWallet.ForgetCollected(coin.Type, coin.gameObject.scene.name, coin.CoinId);
            cleared++;
        }
        return cleared;
    }

    static bool Confirm(string what) => EditorUtility.DisplayDialog(
        "Reset coin progress",
        $"This clears the collected marks for {what} and zeroes the matching balance on this machine.\n\n" +
        "Coins already taken become collectable again. Cannot be undone.",
        "Reset", "Cancel");

    static void Report(string what, int cleared) =>
        Debug.Log($"[CoinSaveTools] Reset {what} — {cleared} collected mark(s) cleared, balance zeroed. " +
                  "Reopen or reload the level to see the coins back.");
}
