using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Takes the No_Light layer out of every light's culling mask, so nothing on that layer is lit by a
/// realtime light. Menu: Bus Runner &gt; Exclude No_Light From Lights.
///
/// The layer exists for geometry that should look self-lit — the lamp bulbs, which should read as
/// the source of the light rather than as a surface catching it from somewhere else.
///
/// Re-runnable, and worth re-running whenever a light is added: a light's culling mask defaults to
/// Everything, so a new one lights the layer again and nothing warns about it.
///
/// **A culling mask only governs realtime light.** Two things ignore it entirely, and neither can be
/// fixed from here:
///
///   Baked light. The lightmapper decides by the ContributeGI flag and the renderer's Receive GI
///   setting, not by layer, so the baked half of a Mixed light still reaches the layer.
///
///   Ambient and environment light. RenderSettings.ambientMode applies to everything rendered with
///   a lit shader, with no layer filter of any kind.
///
/// For geometry that must ignore *all* illumination, the complete answer is an Unlit shader — that
/// is what "no light touches this" actually means in a renderer. This command is the layer half of
/// it, and the Report item below says how far it got.
/// </summary>
static class NoLightSetup
{
    const string LayerName = "No_Light";

    [MenuItem("Bus Runner/Exclude No_Light From Lights")]
    static void Run()
    {
        int layer = LayerMask.NameToLayer(LayerName);
        if (layer < 0)
        {
            Debug.LogError($"[NoLightSetup] No layer called '{LayerName}'. Add it in " +
                           $"Project Settings > Tags and Layers, then run this again.");
            return;
        }

        int mask = 1 << layer;
        int sceneLights = 0, prefabLights = 0, prefabsTouched = 0;

        var scene = EditorSceneManager.GetActiveScene();
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var light in root.GetComponentsInChildren<Light>(true))
            {
                if ((light.cullingMask & mask) == 0) continue;

                Undo.RecordObject(light, "Exclude No_Light");
                light.cullingMask &= ~mask;
                EditorUtility.SetDirty(light);
                sceneLights++;
            }
        }

        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);

            // Cheap check on the loaded asset first — opening prefab contents spins up a hidden
            // preview scene per prefab, far too expensive to do for every prefab in the project just
            // to discover most have no lights.
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) continue;

            bool needed = false;
            foreach (var light in asset.GetComponentsInChildren<Light>(true))
                if ((light.cullingMask & mask) != 0) { needed = true; break; }
            if (!needed) continue;

            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                int changed = 0;
                foreach (var light in contents.GetComponentsInChildren<Light>(true))
                {
                    if ((light.cullingMask & mask) == 0) continue;
                    light.cullingMask &= ~mask;
                    changed++;
                }

                if (changed == 0) continue;

                PrefabUtility.SaveAsPrefabAsset(contents, path);
                prefabLights += changed;
                prefabsTouched++;
                Debug.Log($"[NoLightSetup] {path}: {changed} light(s) updated.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        if (sceneLights > 0) EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();

        // The scene is left unsaved on purpose: this is usually run against a level someone is in
        // the middle of editing, and saving on their behalf would fold this into whatever else they
        // have open.
        Debug.Log($"[NoLightSetup] '{LayerName}' (layer {layer}) removed from {sceneLights} light(s) in " +
                  $"'{scene.name}' and {prefabLights} in {prefabsTouched} prefab(s). Prefabs saved; the " +
                  $"scene is left dirty for you to save.\n" +
                  $"Realtime light now skips the layer. Baked light and ambient do not respect culling " +
                  $"masks — run Bus Runner > Report No_Light Coverage to see what still reaches it.");
    }

    /// <summary>
    /// Read-only audit: which lights still reach the layer, and what else is lighting it that a
    /// culling mask cannot stop. Separate from the fix so it can be run any time without changing
    /// anything.
    /// </summary>
    [MenuItem("Bus Runner/Report No_Light Coverage")]
    static void Report()
    {
        int layer = LayerMask.NameToLayer(LayerName);
        if (layer < 0) { Debug.LogError($"[NoLightSetup] No layer called '{LayerName}'."); return; }

        int mask = 1 << layer;
        var scene = EditorSceneManager.GetActiveScene();

        int leaking = 0, baked = 0, total = 0, onLayer = 0, litShaders = 0;

        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var light in root.GetComponentsInChildren<Light>(true))
            {
                total++;
                if ((light.cullingMask & mask) != 0) leaking++;

                // Baked and Mixed both write a lightmap contribution the culling mask never sees.
                if (light.lightmapBakeType != LightmapBakeType.Realtime) baked++;
            }

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.gameObject.layer != layer) continue;
                onLayer++;

                var renderer = t.GetComponent<Renderer>();
                var material = renderer != null ? renderer.sharedMaterial : null;
                if (material != null && !material.shader.name.Contains("Unlit")) litShaders++;
            }
        }

        Debug.Log($"[NoLightSetup] '{LayerName}' coverage in '{scene.name}':\n" +
                  $"  lights total {total}, still reaching the layer {leaking}\n" +
                  $"  lights that also bake (Baked or Mixed) {baked} — a culling mask does not apply " +
                  $"to their baked contribution\n" +
                  $"  objects on the layer {onLayer}, of which {litShaders} use a lit shader and so " +
                  $"still receive ambient and baked light\n" +
                  $"  ambient is {RenderSettings.ambientMode} at intensity {RenderSettings.ambientIntensity} " +
                  $"and reaches every lit shader regardless of layer");
    }
}
