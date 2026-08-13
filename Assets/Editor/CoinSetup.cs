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
/// The variants are the thing a level designer touches: drag in Coin_Permanent, Coin_Respawnable or
/// Coin_Special and it arrives already the right type, colour and rules. Because they are variants of
/// Coin_pickup rather than copies, a change to the base disc — mesh, collider, transform — still flows
/// through to all three. Switching an individual coin's Type dropdown afterwards re-materials it on the
/// spot, so a mistake costs one dropdown rather than a delete and a re-drag.
///
/// The base prefab's own transform, mesh and collider are left exactly as authored — see RestructureBase.
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

        // save: true — the _EMISSION keyword does not stick otherwise. See ApplyToMaterials.
        settings.ApplyToMaterials(save: true);
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
    /// Makes sure the base coin has a Coin script, a trigger collider and the right layer — and
    /// changes nothing else.
    ///
    /// An earlier version rebuilt the prefab: mesh moved to a child, root unscaled and unrotated,
    /// collider replaced with a sphere of TriggerRadius. That was wrong on both counts it was meant to
    /// fix. The spin never needed it — Coin.Update rotates about world up, so a rotated root turns the
    /// disc on the spot regardless. And the collider is hand-authored: unscaling a root that carries a
    /// (0.381, 0.033, 0.381) squash turns a box sized for that squash into a 1.5 x 2.0 x 2.3 slab.
    ///
    /// So this leaves the transform, the mesh and the collider shape alone. Whatever collider is there
    /// is forced to a trigger, which is the one property that actually matters: a solid coin registers
    /// as a frontal wall and ends the run.
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
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = layer;

            var colliders = root.GetComponentsInChildren<Collider>(true);
            if (colliders.Length == 0)
            {
                // Nothing authored, so fall back to a sphere big enough not to be stepped over: the
                // runner covers 0.16 per frame at 8 m/s.
                var sphere = root.AddComponent<SphereCollider>();
                sphere.radius = TriggerRadius;
                sphere.center = Vector3.zero;
                sphere.isTrigger = true;
                Debug.Log($"[CoinSetup] {BasePrefab} had no collider — added a sphere trigger of {TriggerRadius}.");
            }
            else
            {
                foreach (var collider in colliders) collider.isTrigger = true;
            }

            if (root.GetComponent<Coin>() == null) root.AddComponent<Coin>();

            PrefabUtility.SaveAsPrefabAsset(root, BasePrefab);
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
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
            }
            else
            {
                ConfigureCoin(instance, type, settings, style);
                // Instance of a prefab saved as a new asset becomes a variant of it, which is what keeps
                // a change to the base disc flowing through to all three types.
                PrefabUtility.SaveAsPrefabAsset(instance, path);
            }

            ClearAssetCoinId(path);
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    /// <summary>
    /// Blanks the id on a variant asset, after it has been written.
    ///
    /// It has to happen here rather than before the save. The instance being configured lives in the
    /// open scene — InstantiatePrefab puts it there — where a coin is entitled to an id, so Coin's
    /// OnValidate mints one; and blanking the field through SerializedObject fires OnValidate again,
    /// which mints another. Measured: three variants shipped with ids no matter how many times they were
    /// cleared beforehand.
    ///
    /// On the saved asset the loop breaks, because a persistent object fails OnValidate's first check.
    /// The id must be blank or every coin dragged from the variant shares it, and for the one-time types
    /// that means taking any of them hides all the rest.
    /// </summary>
    static void ClearAssetCoinId(string path)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var coin = asset != null ? asset.GetComponent<Coin>() : null;
        if (coin == null) return;

        var so = new SerializedObject(coin);
        var slot = so.FindProperty("coinId");
        if (string.IsNullOrEmpty(slot.stringValue)) return;

        slot.stringValue = string.Empty;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssetIfDirty(asset);
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
    /// Gives every coin in the open scenes an id that is its own, and leaves alone every coin whose id
    /// already was.
    ///
    /// This is the duplicate-a-coin fix. Unity's duplicate arrives with coinId already filled, so
    /// Coin's OnValidate hook — which only mints into a blank field — cannot tell it apart from the
    /// original. Two Permanent or Special coins sharing an id means taking either one hides both,
    /// because the collected mark is keyed on the id and not on the coin.
    ///
    /// The safe half of the pair: an untouched id keeps its collected mark valid, so a coin the player
    /// has already banked stays banked. Use Randomize One-Time Coin IDs when that is not what you want.
    /// </summary>
    [MenuItem("Bus Runner/Coins/Fix Duplicate Coin IDs")]
    static void FixDuplicateIds()
    {
        var coins = LoadedCoinsInOrder();

        Undo.SetCurrentGroupName("Fix Duplicate Coin IDs");
        int group = Undo.GetCurrentGroup();

        var live = new HashSet<string>();
        var freed = new List<(CoinType type, string scene, string id)>();
        int filled = 0, split = 0;

        foreach (var coin in coins)
        {
            string id = coin.CoinId;

            // First sighting of a good id: the coin keeps it, and so keeps its collected mark.
            if (!string.IsNullOrEmpty(id) && live.Add(id)) continue;

            if (string.IsNullOrEmpty(id)) filled++;
            else
            {
                split++;
                freed.Add((coin.Type, coin.gameObject.scene.name, id));
            }

            live.Add(Mint(coin));
            EditorSceneManager.MarkSceneDirty(coin.gameObject.scene);
        }

        Undo.CollapseUndoOperations(group);

        int orphaned = ForgetOrphanedMarks(freed, live);
        Debug.Log($"[CoinSetup] Coin ids across {UnityEngine.SceneManagement.SceneManager.loadedSceneCount} open scene(s): " +
                  $"{coins.Count} coins, {filled} blank filled, {split} duplicate(s) broken apart, " +
                  $"{orphaned} stale collected mark(s) cleaned up. Ctrl+Z undoes the id changes.");
    }

    /// <summary>
    /// Re-rolls the id of every Permanent and Special coin in the open scenes, whether it needed it or
    /// not — the "just give me fresh ids" pass for when a level has been built by duplicating coins and
    /// you would rather not care which ones collided.
    ///
    /// Every one-time coin in those scenes becomes collectable again on this save, because a fresh id
    /// has no collected mark against it. Respawnable coins are skipped: they never record a mark, so
    /// their id is unused and changing it would be noise in the diff.
    /// </summary>
    [MenuItem("Bus Runner/Coins/Randomize One-Time Coin IDs")]
    static void RandomizeOneTimeIds()
    {
        var coins = LoadedCoinsInOrder().FindAll(c => c.Type != CoinType.Respawnable);
        if (coins.Count == 0)
        {
            Debug.Log("[CoinSetup] No Permanent or Special coins in the open scenes — nothing to randomize.");
            return;
        }

        if (!EditorUtility.DisplayDialog(
                "Randomize one-time coin IDs",
                $"Gives all {coins.Count} Permanent and Special coin(s) in the open scenes a fresh id.\n\n" +
                "Every one of them becomes collectable again on this save. Ctrl+Z restores the ids, but " +
                "the collected marks cleared here stay cleared — they live in the save, not the scene.",
                "Randomize", "Cancel"))
            return;

        Undo.SetCurrentGroupName("Randomize One-Time Coin IDs");
        int group = Undo.GetCurrentGroup();

        var live = new HashSet<string>();
        var freed = new List<(CoinType type, string scene, string id)>();

        foreach (var coin in coins)
        {
            if (!string.IsNullOrEmpty(coin.CoinId))
                freed.Add((coin.Type, coin.gameObject.scene.name, coin.CoinId));

            live.Add(Mint(coin));
            EditorSceneManager.MarkSceneDirty(coin.gameObject.scene);
        }

        // Every Respawnable id stays where it is, and must not be read as orphaned.
        foreach (var coin in LoadedCoinsInOrder())
            if (coin.Type == CoinType.Respawnable && !string.IsNullOrEmpty(coin.CoinId))
                live.Add(coin.CoinId);

        Undo.CollapseUndoOperations(group);

        int orphaned = ForgetOrphanedMarks(freed, live);
        Debug.Log($"[CoinSetup] Randomized {coins.Count} one-time coin id(s) across " +
                  $"{UnityEngine.SceneManagement.SceneManager.loadedSceneCount} open scene(s); {orphaned} collected mark(s) cleared. " +
                  "Ctrl+Z undoes the id changes.");
    }

    /// <summary>
    /// Every coin in every loaded scene, in an order that does not change between runs or machines.
    ///
    /// FindObjectsByType's order is unspecified, and the id tools use position in this list to decide
    /// which of two colliding coins keeps its id — left unsorted, the same command could hand the id to
    /// a different coin for two people on the same scene. Sorting by scene and hierarchy path also has
    /// a useful side effect: Unity names a duplicate "Coin_Permanent (1)", which sorts after
    /// "Coin_Permanent", so the original keeps its id and the copy is the one re-rolled.
    /// </summary>
    static List<Coin> LoadedCoinsInOrder()
    {
        var coins = new List<Coin>(
            Object.FindObjectsByType<Coin>(FindObjectsInactive.Include, FindObjectsSortMode.None));

        coins.Sort((a, b) => string.Compare(SortKey(a), SortKey(b), System.StringComparison.Ordinal));
        return coins;
    }

    static string SortKey(Coin coin)
    {
        var path = coin.name;
        for (var parent = coin.transform.parent; parent != null; parent = parent.parent)
            path = parent.name + "/" + path;

        return coin.gameObject.scene.path + "/" + path;
    }

    /// <summary>
    /// Writes a fresh id and returns it. Goes through SerializedObject so the private field can be set,
    /// and through ApplyModifiedProperties rather than ApplyModifiedPropertiesWithoutUndo so a whole
    /// pass over a level's worth of coins is one Ctrl+Z.
    /// </summary>
    static string Mint(Coin coin)
    {
        var so = new SerializedObject(coin);
        var slot = so.FindProperty("coinId");
        slot.stringValue = System.Guid.NewGuid().ToString("N");
        so.ApplyModifiedProperties();
        return slot.stringValue;
    }

    /// <summary>
    /// Drops the collected marks of ids that no coin holds any more.
    ///
    /// Re-rolling an id leaves its mark in the wallet with nothing to point at. That mark is
    /// unreachable — no coin will ever ask about it again — but it still counts toward the taken tally
    /// the dev panel reports, so left alone the numbers drift up every time a level is reshuffled.
    ///
    /// Nothing becomes collectable that was not already: the coin moved to a new id the moment it was
    /// re-rolled, and a new id has no mark. This only clears the litter left behind.
    /// </summary>
    /// <param name="live">Every id still held by a coin — a duplicate's winner keeps its mark.</param>
    static int ForgetOrphanedMarks(List<(CoinType type, string scene, string id)> freed, HashSet<string> live)
    {
        int cleared = 0;
        foreach (var (type, scene, id) in freed)
        {
            if (live.Contains(id)) continue;                          // still in use by the coin that kept it
            if (!CoinWallet.IsCollected(type, scene, id)) continue;   // never taken, nothing to clear

            CoinWallet.ForgetCollected(type, scene, id);
            cleared++;
        }

        if (cleared > 0) CoinWallet.Flush();
        return cleared;
    }
}
