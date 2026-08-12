using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// The Controls screen: one row per action, two rebindable slots each, and the modal that
/// listens for the replacement key.
///
/// Rows are generated from <see cref="InputBindings.Actions"/> rather than hand-placed, so
/// adding a third action is one enum entry and one defaults row — no scene work.
///
/// A conflicting key does not close the modal. It turns the window red, names the action
/// already holding that key, and keeps listening, so the player can try another key or back
/// out with Escape instead of being dropped back to the list to work out what went wrong.
/// </summary>
public class ControlsPanel : MonoBehaviour
{
    [SerializeField] RectTransform rows;

    [Header("Rebind Prompt")]
    [Tooltip("The whole modal, dim backdrop included. Hidden unless a rebind is in progress.")]
    [SerializeField] GameObject listener;
    [Tooltip("The box that turns red on a conflict.")]
    [SerializeField] Image listenerWindow;
    [SerializeField] Text listenerPrompt;
    [SerializeField] Text listenerHint;

    [SerializeField] Font font;

    static readonly Color SlotColor = new Color(0.22f, 0.25f, 0.31f, 0.95f);
    static readonly Color WindowColor = new Color(0.09f, 0.11f, 0.15f, 0.98f);
    static readonly Color ConflictColor = new Color(0.5f, 0.1f, 0.1f, 0.98f);

    const float RowHeight = 84f;
    const float NameWidth = 240f;
    const int RowFontSize = 34;

    Text[,] slotLabels;
    bool built;

    bool listening;
    GameAction pendingAction;
    int pendingSlot;
    int listenStartFrame;

    // OnEnable, not Awake: the panel is inactive when the scene loads, and an inactive object
    // gets neither Awake nor Start until something switches it on.
    void OnEnable()
    {
        // The rows are runtime UI. Building them in the editor would serialise generated objects
        // into the scene, and Destroy is not even legal there.
        if (!Application.isPlaying) return;

        if (!built) BuildRows();
        RefreshLabels();
        CloseListener();
    }

    void OnDisable() => CloseListener();

    void Update()
    {
        if (!listening) return;

        // The click that opened the modal and a key held down on the same frame would both land
        // here; skipping the opening frame keeps a held key from binding itself instantly.
        if (Time.frameCount <= listenStartFrame) return;

        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        foreach (var control in keyboard.allKeys)
        {
            if (!control.wasPressedThisFrame) continue;
            HandleKeyPress(control.keyCode);
            return;
        }
    }

    /// <summary>Wired to the RESET TO DEFAULTS button.</summary>
    public void ResetToDefaults()
    {
        InputBindings.ResetToDefaults();
        RefreshLabels();
        CloseListener();
    }

    void BeginRebind(GameAction action, int slot)
    {
        listening = true;
        listenStartFrame = Time.frameCount;
        pendingAction = action;
        pendingSlot = slot;

        if (listener != null) listener.SetActive(true);
        if (listenerWindow != null) listenerWindow.color = WindowColor;
        if (listenerPrompt != null)
            listenerPrompt.text = $"PRESS A KEY FOR {InputBindings.DisplayName(action)}";
        if (listenerHint != null) listenerHint.text = "ESC TO CANCEL";
    }

    /// <summary>
    /// Decides what one pressed key means: cancel, accept, or reject with a reason. Separate from
    /// the polling in Update, and public, so the decision can be exercised without a device —
    /// the editor cannot deliver a synthetic press to a paused frame reliably enough to test it.
    /// Ignored unless a rebind is actually in progress.
    /// </summary>
    public void HandleKeyPress(Key key)
    {
        if (!listening) return;

        if (key == Key.Escape)
        {
            CloseListener();
            return;
        }

        // Re-picking the key the slot already holds is what the player asked for, so treat it as
        // done rather than as a conflict with itself.
        if (key == InputBindings.Get(pendingAction, pendingSlot))
        {
            CloseListener();
            return;
        }

        if (!InputBindings.IsBindable(key))
        {
            Reject($"{InputBindings.DisplayName(key)} CANNOT BE BOUND");
            return;
        }

        if (InputBindings.TryFindConflict(key, pendingAction, pendingSlot, out var owner))
        {
            Reject($"BUTTON ALREADY USED AS {InputBindings.DisplayName(owner)}");
            return;
        }

        InputBindings.Set(pendingAction, pendingSlot, key);
        RefreshLabels();
        CloseListener();
    }

    /// <summary>Red window, reason, still listening.</summary>
    void Reject(string message)
    {
        if (listenerWindow != null) listenerWindow.color = ConflictColor;
        if (listenerPrompt != null) listenerPrompt.text = message;
        if (listenerHint != null) listenerHint.text = "TRY ANOTHER KEY, OR ESC TO CANCEL";
    }

    void CloseListener()
    {
        listening = false;
        if (listener != null) listener.SetActive(false);
    }

    void BuildRows()
    {
        // Set only once the build can actually happen. Latching it first meant a misconfigured panel
        // logged once and then reported itself built forever, so wiring `rows` up afterwards never
        // took effect.
        if (rows == null)
        {
            Debug.LogError("[ControlsPanel] No rows container — run Bus Runner > Set Up Start Menu.");
            return;
        }
        built = true;

        for (int i = rows.childCount - 1; i >= 0; i--) Destroy(rows.GetChild(i).gameObject);

        var actions = InputBindings.Actions;
        slotLabels = new Text[actions.Length, InputBindings.SlotCount];

        foreach (var action in actions)
        {
            var row = new GameObject("Row_" + action, typeof(RectTransform));
            row.transform.SetParent(rows, false);
            row.AddComponent<LayoutElement>().preferredHeight = RowHeight;

            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            // Off, so the widths below decide the split instead of every child expanding equally.
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            var name = MakeLabel(row.transform, "Name", InputBindings.DisplayName(action), TextAnchor.MiddleLeft);
            var nameElement = name.gameObject.AddComponent<LayoutElement>();
            nameElement.preferredWidth = NameWidth;
            nameElement.flexibleWidth = 0f;

            for (int slot = 0; slot < InputBindings.SlotCount; slot++)
            {
                // Captured per slot — a shared loop variable would point every button at the last one.
                var boundAction = action;
                int boundSlot = slot;

                var go = new GameObject($"Slot{slot}", typeof(RectTransform));
                go.transform.SetParent(row.transform, false);

                var image = go.AddComponent<Image>();
                image.color = SlotColor;

                var element = go.AddComponent<LayoutElement>();
                element.flexibleWidth = 1f;   // the two slots share whatever the name label leaves

                var button = go.AddComponent<Button>();
                button.targetGraphic = image;
                button.onClick.AddListener(() => BeginRebind(boundAction, boundSlot));

                slotLabels[(int)action, slot] = MakeLabel(go.transform, "Label", "", TextAnchor.MiddleCenter);
            }
        }
    }

    void RefreshLabels()
    {
        if (slotLabels == null) return;
        foreach (var action in InputBindings.Actions)
            for (int slot = 0; slot < InputBindings.SlotCount; slot++)
            {
                var label = slotLabels[(int)action, slot];
                if (label != null) label.text = InputBindings.DisplayName(InputBindings.Get(action, slot));
            }
    }

    Text MakeLabel(Transform parent, string name, string content, TextAnchor anchor)
    {
        var go = UiRect.Stretch(parent, name);

        var text = go.AddComponent<Text>();
        text.font = UiRect.ResolveFont(font, "ControlsPanel");
        text.fontSize = RowFontSize;
        text.alignment = anchor;
        text.color = Color.white;
        text.text = content;
        return text;
    }
}
