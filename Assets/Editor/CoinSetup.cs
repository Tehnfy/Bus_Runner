using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// One-shot build of everything the coin system needs as assets. Menu: Bus Runner > Set Up Coins.
///
/// Makes, or reconfigures if it already made them:
///   a Pickup layer, so coins collide with nothing but the runner
///   three materials, one per coin type
///   CoinSettings, wired to those materials and holding the look and motion knobs
///   three prefab variants of Coin_pickup, one per type
///   a Bloom override on BusRunnerVolumeProfile, without which emission does not read as glow
///
/// The base Coin_pickup prefab is restructured on the way through — see RestructureBase.
///
/// Safe to run more than once.
/// </summary>
static class CoinSetup
{
    const string BasePrefab = "Assets/Prefabs/Coin_pickup.prefab";
    const string PrefabFolder = "Assets/Prefabs";
    const string MaterialFolder = "Assets/Materials";
    const string SettingsPath = "Assets/Settings/CoinSettings.asset";
    const string VolumeProfile = "Assets/Settings/BusRunnerVolumeProfile.asset";
    const string PickupLayer = "Pickup";
    const string VisualChild = "Visual";

    // World radius of the pickup trigger. Generous on purpose: the coin's own disc is 0.03 thick,
    // and the runner covers 0.13 per frame at 8 m/s, so a trigger matching the art would be stepped
    // straight over on a bad frame.
    const float TriggerRadius = 0.45f;

    [MenuItem("Bus Runner/Set Up Coins")]
    static void Run()
    {
        int layer = EnsurePickupLayer();
        var settings = EnsureSettings();
        if (settings == null) return;

        if (!RestructureBase(layer)) return;

        foreach (var type in CoinWallet.Types)
            EnsureVariant(type, settings);

        settings.ApplyToMaterials();
        EditorUtility.SetDirty(settings);

        EnsureBloom();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[CoinSetup] Done. Drag Coin_Permanent / Coin_Respawnable / Coin_Special under " +
                  "Pickups in a level, then dial the look on " + SettingsPath + ".");
    }

    /// <summary>
    /// Adds a Pickup layer if the project has none, and returns its index.
    ///
    /// What actually stops a coin killing the runner is that its collider is a trigger — the
    /// CharacterController ignores triggers when it moves, so OnControllerColliderHit never sees one
    /// and the wall test never runs on it. On Default with a solid collider, a coin at chest height
    /// reads as exactly the frontal impact that ends a run.
    ///
    /// The layer is separate insurance, and the seam for narrowing the collision matrix to Pickup
    /// against Player only. It does not narrow it by itself: a new layer collides with everything
    /// until someone unticks the boxes in Project Settings > Physics.
    /// </summary>
    static int EnsurePickupLayer()
    {
        int existing = LayerMask.NameToLayer(PickupLayer);
        if (existing >= 0) return existing;

        var tagManager = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tagManager.FindProperty("layers");

        // 0-7 are Unity's own. 8-10 are already Ground, Obstacle and Player.
        for (int i = 8; i < layers.arraySize; i++)
        {
            var slot = layers.GetArrayElementAtIndex(i);
            if (!string.IsNullOrEmpty(slot.stringValue)) continue;

            slot.stringValue = PickupLayer;
            tagManager.ApplyModifiedProperties();
            Debug.Log($"[CoinSetup] Added layer '{PickupLayer}' at index {i}.");
            return i;
        }

        Debug.LogWarning("[CoinSetup] No free layer slot — coins will stay on Default. Free one and " +
                         "re-run, or the crash test will treat a coin as a wall.");
        return 0;
    }

    /// <summary>
    /// Splits the coin into a plain root carrying the trigger and the script, with the artwork moved
    /// to a child.
    ///
    /// The prefab arrives as a single object whose transform is a squashed, rotated disc — scale
    /// (0.38, 0.03, 0.38). Any collider on it inherits that squash, so the trigger would be 3cm tall
    /// and easy to miss entirely at running speed, and spinning the root would tumble the disc end
    /// over end rather than turning it on the spot. An unscaled root fixes both.
    /// </summary>
    static bool RestructureBase(int layer)
    {
        var root = PrefabUtility.LoadPrefabContents(BasePrefab);
        if (root == null)
        {
            Debug.LogError($"[CoinSetup] No prefab at {BasePrefab}.");
            return false;
        }

        try
        {
            var visual = root.transform.Find(VisualChild);
            if (visual == null)
            {
                // First run: the mesh is still on the root, so move it to a child that keeps the
                // authored rotation and squash.
                var child = new GameObject(VisualChild);
                child.transform.SetParent(root.transform, false);
                child.transform.localPosition = Vector3.zero;
                child.transform.localRotation = root.transform.localRotation;
                child.transform.localScale = root.transform.localScale;

                Move<MeshFilter>(root, child);
                Move<MeshRenderer>(root, child);
                visual = child.transform;
            }

            // The root is now pure logic: no scale, no rotation, so the spin turns the coin on the
            // spot and the trigger is a true sphere.
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
            root.layer = layer;
            visual.gameObject.layer = layer;

            foreach (var capsule in root.GetComponents<CapsuleCollider>())
                Object.DestroyImmediate(capsule);

            var trigger = root.GetComponent<SphereCollider>();
            if (trigger == null) trigger = root.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = TriggerRadius;
            trigger.center = Vector3.zero;

            if (root.GetComponent<Coin>() == null) root.AddComponent<Coin>();

            PrefabUtility.SaveAsPrefabAsset(root, BasePrefab);
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static void Move<T>(GameObject from, GameObject to) where T : Component
    {
        var source = from.GetComponent<T>();
        if (source == null) return;

        if (source is MeshFilter filter)
            to.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
        else if (source is MeshRenderer renderer)
            to.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;

        Object.DestroyImmediate(source);
    }

    /// <summary>A variant per type, so a level designer drags in the coin they mean.</summary>
    static void EnsureVariant(CoinType type, CoinSettings settings)
    {
        string path = $"{PrefabFolder}/Coin_{type}.prefab";
        var style = settings.For(type);

        var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BasePrefab);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);

        try
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                // Reconfigure in place, so hand placements in scenes keep pointing at it.
                ConfigureCoin(existing, type, settings, style);
                EditorUtility.SetDirty(existing);
                return;
            }

            ConfigureCoin(instance, type, settings, style);
            // Instance of a prefab saved as a new asset becomes a variant of it, which is what keeps
            // a change to the base disc flowing through to all three types.
            PrefabUtility.SaveAsPrefabAsset(instance, path);
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    static void ConfigureCoin(GameObject go, CoinType type, CoinSettings settings, CoinSettings.Style style)
    {
        var coin = go.GetComponent<Coin>();
        if (coin == null) coin = go.AddComponent<Coin>();

        var so = new SerializedObject(coin);
        so.FindProperty("type").enumValueIndex = (int)type;
        so.FindProperty("settings").objectReferenceValue = settings;
        // Deliberately blank on the asset: an id baked into the prefab would be shared by every coin
        // dragged out of it, and taking one would hide the rest.
        so.FindProperty("coinId").stringValue = string.Empty;
        so.ApplyModifiedPropertiesWithoutUndo();

        if (style?.material == null) return;

        var renderer = go.GetComponentInChildren<MeshRenderer>(true);
        if (renderer != null) renderer.sharedMaterial = style.material;
    }

    static CoinSettings EnsureSettings()
    {
        var settings = AssetDatabase.LoadAssetAtPath<CoinSettings>(SettingsPath);
        if (settings == null)
        {
            settings = ScriptableObject.CreateInstance<CoinSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
        }

        var so = new SerializedObject(settings);
        var styles = so.FindProperty("styles");
        for (int i = 0; i < styles.arraySize; i++)
        {
            var style = styles.GetArrayElementAtIndex(i);
            var type = (CoinType)style.FindPropertyRelative("type").enumValueIndex;
            var slot = style.FindPropertyRelative("material");
            if (slot.objectReferenceValue == null)
                slot.objectReferenceValue = EnsureMaterial(type);
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        return settings;
    }

    static Material EnsureMaterial(CoinType type)
    {
        string path = $"{MaterialFolder}/M_Coin{type}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            Debug.LogError("[CoinSetup] URP Lit shader not found — cannot build coin materials.");
            return null;
        }

        material = new Material(shader) { name = $"M_Coin{type}" };
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    /// <summary>
    /// Adds Bloom to the shared volume profile. Emission alone only makes a coin bright; the halo
    /// that reads as a glow comes from bloom picking up the parts of the image above its threshold,
    /// which is why the emission intensities in CoinSettings are above 1.
    ///
    /// Conservative settings — this is a mobile target, and bloom is a full-screen effect that
    /// changes how the whole level looks, not just the coins.
    /// </summary>
    static void EnsureBloom()
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfile);
        if (profile == null)
        {
            Debug.LogWarning($"[CoinSetup] No volume profile at {VolumeProfile} — coins will glow " +
                             "only as flat bright discs.");
            return;
        }

        if (!profile.TryGet<Bloom>(out var bloom))
            bloom = profile.Add<Bloom>(true);

        bloom.active = true;
        bloom.threshold.overrideState = true;
        bloom.threshold.value = 1.0f;      // only genuinely HDR pixels bloom, so the road does not
        bloom.intensity.overrideState = true;
        bloom.intensity.value = 0.5f;
        bloom.scatter.overrideState = true;
        bloom.scatter.value = 0.6f;
        bloom.highQualityFiltering.overrideState = true;
        bloom.highQualityFiltering.value = false;   // off for mobile

        EditorUtility.SetDirty(profile);
    }

    /// <summary>
    /// Gives every coin in the open scene a unique id, and reports any it had to break apart.
    /// Duplicating a placed coin copies its id, and two coins sharing one means taking either hides
    /// both — the OnValidate hook cannot see that, because a duplicate arrives already filled in.
    /// </summary>
    [MenuItem("Bus Runner/Repair Coin IDs")]
    static void RepairIds()
    {
        var seen = new HashSet<string>();
        int assigned = 0, freed = 0;

        foreach (var coin in Object.FindObjectsByType<Coin>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            string id = coin.CoinId;
            if (string.IsNullOrEmpty(id)) assigned++;
            else if (seen.Add(id)) continue;   // first sighting of a good id, nothing to do
            else freed++;

            var so = new SerializedObject(coin);
            var slot = so.FindProperty("coinId");
            slot.stringValue = System.Guid.NewGuid().ToString("N");
            so.ApplyModifiedPropertiesWithoutUndo();
            seen.Add(slot.stringValue);
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"[CoinSetup] Coin ids — {assigned} blank filled, {freed} duplicates broken apart.");
    }
}
