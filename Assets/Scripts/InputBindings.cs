using System;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>The gameplay actions a key can be bound to.</summary>
public enum GameAction
{
    Jump,
    Slide,
}

/// <summary>
/// Keyboard bindings for the gameplay actions — two slots each, saved in PlayerPrefs.
///
/// Static rather than a component, because both ends need it in different scenes: the
/// Controls panel edits it in Menu, PlayerInputRouter reads it in a level. PlayerPrefs is
/// what carries it across the load, so there is nothing to keep alive in between.
///
/// A key belongs to at most one slot in the whole table. <see cref="Set"/> enforces that
/// itself rather than trusting the UI, so a bad call cannot leave two actions fighting over
/// one key. There is no unbound state: every slot always holds a real key, which is why
/// the panel needs no "clear" affordance and nothing downstream has to handle a hole.
/// </summary>
public static class InputBindings
{
    public const int SlotCount = 2;

    static readonly GameAction[] AllActions = { GameAction.Jump, GameAction.Slide };

    /// <summary>
    /// Slot 0 keeps what the game shipped with. Slot 1 adds the arrow keys, which read as
    /// up-to-jump and down-to-slide and so match the swipe gestures they sit beside.
    /// </summary>
    static readonly Key[,] Defaults =
    {
        { Key.Space, Key.UpArrow },        // Jump
        { Key.LeftCtrl, Key.DownArrow },   // Slide
    };

    static Key[,] bindings;

    public static GameAction[] Actions => AllActions;

    public static Key Get(GameAction action, int slot)
    {
        EnsureLoaded();
        return InRange(slot) ? bindings[(int)action, slot] : Key.None;
    }

    /// <summary>
    /// Binds a key, or refuses. Fails on a key that cannot be bound at all and on one already
    /// held elsewhere in the table — rebinding a slot to the key it already has is a no-op success.
    /// </summary>
    public static bool Set(GameAction action, int slot, Key key)
    {
        EnsureLoaded();
        if (!InRange(slot) || !IsBindable(key)) return false;
        if (bindings[(int)action, slot] == key) return true;
        if (TryFindConflict(key, action, slot, out _)) return false;

        bindings[(int)action, slot] = key;
        PlayerPrefs.SetInt(PrefKey(action, slot), (int)key);
        PlayerPrefs.Save();
        return true;
    }

    /// <summary>
    /// Finds the action already holding this key, ignoring the slot being edited. The other slot
    /// of the same action counts as a conflict too: one key, one job, even within an action.
    /// </summary>
    public static bool TryFindConflict(Key key, GameAction targetAction, int targetSlot, out GameAction owner)
    {
        EnsureLoaded();
        owner = targetAction;
        if (key == Key.None) return false;

        foreach (var action in AllActions)
            for (int slot = 0; slot < SlotCount; slot++)
            {
                if (action == targetAction && slot == targetSlot) continue;
                if (bindings[(int)action, slot] != key) continue;
                owner = action;
                return true;
            }
        return false;
    }

    public static void ResetToDefaults()
    {
        EnsureLoaded();
        foreach (var action in AllActions)
            for (int slot = 0; slot < SlotCount; slot++)
            {
                bindings[(int)action, slot] = Defaults[(int)action, slot];
                PlayerPrefs.SetInt(PrefKey(action, slot), (int)Defaults[(int)action, slot]);
            }
        PlayerPrefs.Save();
    }

    /// <summary>
    /// True if either slot for this action went down this frame. Deliberately one bool for both
    /// slots — pressing both at once is one press of the action, not two.
    /// </summary>
    public static bool WasPressedThisFrame(GameAction action)
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return false;

        EnsureLoaded();
        for (int slot = 0; slot < SlotCount; slot++)
        {
            var key = bindings[(int)action, slot];
            if (key == Key.None) continue;
            var control = keyboard[key];
            if (control != null && control.wasPressedThisFrame) return true;
        }
        return false;
    }

    /// <summary>
    /// True while either slot for this action is down. The press-this-frame version answers "did
    /// they ask for it"; this answers "are they still asking", which is what a held slide needs.
    /// </summary>
    public static bool IsHeld(GameAction action)
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return false;

        EnsureLoaded();
        for (int slot = 0; slot < SlotCount; slot++)
        {
            var key = bindings[(int)action, slot];
            if (key == Key.None) continue;
            var control = keyboard[key];
            if (control != null && control.isPressed) return true;
        }
        return false;
    }

    /// <summary>
    /// Escape is excluded because the rebind prompt uses it to cancel — binding it would leave
    /// no way out of that window. None and any value not in the enum are rejected as junk,
    /// which is also what makes indexing Keyboard by a stored value safe.
    /// </summary>
    public static bool IsBindable(Key key) =>
        key != Key.None && key != Key.Escape && Enum.IsDefined(typeof(Key), key);

    /// <summary>Readable name for a key: LeftCtrl reads "LEFT CTRL", Digit1 reads "1".</summary>
    public static string DisplayName(Key key)
    {
        if (key == Key.None) return "—";

        var name = key.ToString();
        if (name.StartsWith("Digit", StringComparison.Ordinal)) name = name.Substring(5);
        else if (name.StartsWith("Numpad", StringComparison.Ordinal)) name = "Num" + name.Substring(6);

        // Split the enum's camel case, so the label reads as words rather than as an identifier.
        var builder = new StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1])) builder.Append(' ');
            builder.Append(name[i]);
        }
        return builder.ToString().ToUpperInvariant();
    }

    public static string DisplayName(GameAction action) => action.ToString().ToUpperInvariant();

    static void EnsureLoaded()
    {
        if (bindings != null) return;

        bindings = new Key[AllActions.Length, SlotCount];
        foreach (var action in AllActions)
            for (int slot = 0; slot < SlotCount; slot++)
            {
                var fallback = Defaults[(int)action, slot];
                var stored = (Key)PlayerPrefs.GetInt(PrefKey(action, slot), (int)fallback);
                // A saved value that is no longer bindable — a stale enum from an older build,
                // say — falls back rather than being carried into a Keyboard lookup.
                bindings[(int)action, slot] = IsBindable(stored) ? stored : fallback;
            }
    }

    static bool InRange(int slot) => slot >= 0 && slot < SlotCount;

    static string PrefKey(GameAction action, int slot) => $"bind.{action}.{slot}";
}
