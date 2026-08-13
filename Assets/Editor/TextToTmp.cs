using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Replaces every legacy UnityEngine.UI.Text with a TextMeshProUGUI carrying the same string, size,
/// colour and alignment. Two entry points, because scenes and prefabs are edited through completely
/// different APIs:
///
///   Bus Runner &gt; Convert Legacy Text To TMP            the open scene
///   Bus Runner &gt; Convert Legacy Text To TMP (Prefabs)   every prefab under Assets
///
/// Written for the one-off migration off legacy Text, but safe to leave in and safe to re-run:
/// with nothing left to convert it reports so and changes nothing.
///
/// Two details make this more than an AddComponent loop:
///
/// Unity allows only one Graphic per GameObject, so the Text has to be destroyed before the
/// TextMeshProUGUI can be added — which means every serialized reference pointing at it is recorded
/// first and re-pointed afterwards. Without that pass, every Button.targetGraphic aimed at a caption
/// would come back null and the screen would look intact in the hierarchy while doing nothing.
///
/// The font cannot carry across: a legacy Font and a TMP_FontAsset share no data, so each converted
/// label is given the project's TMP default. Anything needing a specific face gets it back from its
/// own setup command, or by hand.
/// </summary>
static class TextToTmp
{
    /// <summary>What is worth carrying from the old component to the new one.</summary>
    struct Captured
    {
        public GameObject Owner;
        public string Content;
        public int FontSize;
        public Color Color;
        public TextAnchor Alignment;
        public bool RaycastTarget;
        public bool Enabled;
        public bool Wrap;
    }

    /// <summary>One serialized field that points at a Text being replaced.</summary>
    struct Referrer
    {
        public Component Owner;
        public string PropertyPath;
        public int TargetIndex;
    }

    [MenuItem("Bus Runner/Convert Legacy Text To TMP")]
    static void RunOnScene()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid())
        {
            Debug.LogError("[TextToTmp] No scene open.");
            return;
        }

        Undo.SetCurrentGroupName("Convert Legacy Text To TMP");
        int group = Undo.GetCurrentGroup();

        int converted = Convert(scene.GetRootGameObjects(), useUndo: true, out int repointed);

        Undo.CollapseUndoOperations(group);

        if (converted == 0)
        {
            Debug.Log($"[TextToTmp] '{scene.name}' has no legacy Text left — nothing to do.");
            return;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        // Deliberately not saved. This rewrites every caption in the scene at once, so it is left
        // dirty for a look at the result and one Ctrl+Z if it is wrong.
        Debug.Log($"[TextToTmp] '{scene.name}': converted {converted} label(s), re-pointed {repointed} " +
                  $"reference(s). Scene left unsaved on purpose — check it, then save.");
    }

    /// <summary>
    /// The same conversion over prefab assets. Packages are skipped: their prefabs are not ours to
    /// rewrite, and the URP debug widgets alone account for well over a hundred legacy labels that
    /// nothing in this game ever shows.
    /// </summary>
    [MenuItem("Bus Runner/Convert Legacy Text To TMP (Prefabs)")]
    static void RunOnPrefabs()
    {
        int prefabs = 0, converted = 0, repointed = 0;

        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);

            // Cheap check on the loaded asset first. Opening prefab contents spins up a hidden
            // preview scene per prefab, which is far too expensive to do for every prefab in the
            // project just to find out most have no labels at all.
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null || asset.GetComponentsInChildren<Text>(true).Length == 0) continue;

            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                // No Undo inside prefab contents — it is a throwaway scene, and the edit is committed
                // by saving rather than by the undo stack.
                int n = Convert(new[] { contents }, useUndo: false, out int r);
                if (n == 0) continue;

                PrefabUtility.SaveAsPrefabAsset(contents, path);
                prefabs++;
                converted += n;
                repointed += r;
                Debug.Log($"[TextToTmp] {path}: converted {n} label(s), re-pointed {r} reference(s).");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        if (prefabs == 0)
        {
            Debug.Log("[TextToTmp] No prefab under Assets has a legacy Text — nothing to do.");
            return;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[TextToTmp] {prefabs} prefab(s) rewritten: {converted} label(s), {repointed} reference(s). " +
                  $"Prefab edits are not undoable — revert with git if this is wrong.");
    }

    /// <summary>
    /// The conversion itself, over whatever roots it is handed. Returns how many labels were
    /// replaced, and reports how many references had to be re-aimed at the new components.
    /// </summary>
    static int Convert(IList<GameObject> roots, bool useUndo, out int repointed)
    {
        repointed = 0;

        var texts = new List<Text>();
        foreach (var root in roots)
            texts.AddRange(root.GetComponentsInChildren<Text>(true));

        if (texts.Count == 0) return 0;

        var index = new Dictionary<Text, int>();
        for (int i = 0; i < texts.Count; i++) index[texts[i]] = i;

        var referrers = CollectReferrers(roots, index);

        // Captured in full before anything is destroyed — reading a property off a dead component
        // returns a default, and the labels would all come back blank and black.
        var captured = new Captured[texts.Count];
        for (int i = 0; i < texts.Count; i++)
        {
            var t = texts[i];
            captured[i] = new Captured
            {
                Owner = t.gameObject,
                Content = t.text,
                FontSize = t.fontSize,
                Color = t.color,
                Alignment = t.alignment,
                RaycastTarget = t.raycastTarget,
                Enabled = t.enabled,
                Wrap = t.horizontalOverflow == HorizontalWrapMode.Wrap,
            };
        }

        var font = UiRect.ResolveFont(null, "TextToTmp");
        var replacements = new TMP_Text[texts.Count];

        for (int i = 0; i < texts.Count; i++)
        {
            if (useUndo) Undo.DestroyObjectImmediate(texts[i]);
            else Object.DestroyImmediate(texts[i]);

            var owner = captured[i].Owner;
            var tmp = useUndo
                ? Undo.AddComponent<TextMeshProUGUI>(owner)
                : owner.AddComponent<TextMeshProUGUI>();

            tmp.font = font;
            tmp.text = captured[i].Content;
            tmp.fontSize = captured[i].FontSize;
            tmp.color = captured[i].Color;
            tmp.alignment = UiRect.Align(captured[i].Alignment);
            tmp.raycastTarget = captured[i].RaycastTarget;
            tmp.enabled = captured[i].Enabled;
            tmp.textWrappingMode = captured[i].Wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            // Overflow whatever the original did. Legacy Text defaulted to Truncate and so does TMP,
            // and that pairing is how a caption that does not quite fit renders nothing at all.
            tmp.overflowMode = TextOverflowModes.Overflow;

            replacements[i] = tmp;
        }

        foreach (var r in referrers)
        {
            if (r.Owner == null) continue;

            var so = new SerializedObject(r.Owner);
            var prop = so.FindProperty(r.PropertyPath);
            if (prop == null) continue;

            prop.objectReferenceValue = replacements[r.TargetIndex];
            so.ApplyModifiedProperties();
            repointed++;
        }

        return texts.Count;
    }

    /// <summary>
    /// Every serialized object-reference field under these roots that points at one of the Texts
    /// about to be replaced. Walked once, before any destruction, because the reference is what
    /// identifies the field and it is gone the moment the component is.
    /// </summary>
    static List<Referrer> CollectReferrers(IList<GameObject> roots, Dictionary<Text, int> index)
    {
        var found = new List<Referrer>();

        foreach (var root in roots)
        {
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                // Null where a script is missing. Iterating one throws rather than reporting.
                if (component == null) continue;

                var so = new SerializedObject(component);
                var it = so.GetIterator();
                while (it.NextVisible(true))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference) continue;

                    var target = it.objectReferenceValue as Text;
                    if (target == null || !index.TryGetValue(target, out int i)) continue;

                    found.Add(new Referrer
                    {
                        Owner = component,
                        PropertyPath = it.propertyPath,
                        TargetIndex = i,
                    });
                }
            }
        }
        return found;
    }
}
