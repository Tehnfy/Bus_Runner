using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Puts the Game view on a phone-shaped landscape viewport, so what is on screen in the editor is
/// the shape the game actually ships in. Menu: Bus Runner &gt; Set Phone Landscape Viewport.
///
/// 2340x1080 — 19.5:9, the aspect of essentially every phone sold since 2018 and the middle of the
/// range the player settings already allow (androidMinAspectRatio 1, androidMaxAspectRatio 2.4).
/// Held as a fixed resolution rather than a bare aspect ratio so the Game view also reports the
/// pixel count a phone really has, which is what makes a UI element sized in pixels look right or
/// wrong here for the same reason it will on a device.
///
/// The size is added to the Standalone, Android and iOS lists, because Unity keeps a separate list
/// per build target and switching platforms would otherwise drop straight back to Free Aspect.
///
/// **This reaches into internal editor API by reflection.** UnityEditor.GameViewSizes,
/// GameViewSize and GameView.selectedSizeIndex are all internal — there is no public way to add or
/// pick a Game view size, and there never has been. That makes this the one file in the project
/// that can break on a Unity upgrade without a compile error to say so, so every lookup below is
/// checked and reports exactly which member went missing. If that ever happens, the fallback is
/// three clicks: Game view > size dropdown > + > Fixed Resolution 2340x1080.
/// </summary>
static class PhoneViewport
{
    const string SizeName = "Phone Landscape (19.5:9)";
    const int Width = 2340;
    const int Height = 1080;

    [MenuItem("Bus Runner/Set Phone Landscape Viewport")]
    static void Run()
    {
        var editorAssembly = typeof(Editor).Assembly;

        var sizesType = editorAssembly.GetType("UnityEditor.GameViewSizes");
        var sizeType = editorAssembly.GetType("UnityEditor.GameViewSize");
        var sizeTypeEnum = editorAssembly.GetType("UnityEditor.GameViewSizeType");
        if (!Found(sizesType, "UnityEditor.GameViewSizes") ||
            !Found(sizeType, "UnityEditor.GameViewSize") ||
            !Found(sizeTypeEnum, "UnityEditor.GameViewSizeType")) return;

        // GameViewSizes is a ScriptableSingleton<GameViewSizes>; the list lives on its instance.
        var singletonType = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
        var instanceProperty = singletonType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static);
        if (!Found(instanceProperty, "ScriptableSingleton<GameViewSizes>.instance")) return;

        var sizes = instanceProperty.GetValue(null, null);

        var getGroup = sizesType.GetMethod("GetGroup", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (!Found(getGroup, "GameViewSizes.GetGroup")) return;

        // FixedResolution rather than AspectRatio — see the class note.
        object fixedResolution;
        try
        {
            fixedResolution = Enum.Parse(sizeTypeEnum, "FixedResolution");
        }
        catch (ArgumentException)
        {
            Debug.LogError("[PhoneViewport] GameViewSizeType has no 'FixedResolution' member any more.");
            return;
        }

        var constructor = sizeType.GetConstructor(new[] { sizeTypeEnum, typeof(int), typeof(int), typeof(string) });
        if (!Found(constructor, "GameViewSize(GameViewSizeType, int, int, string)")) return;

        int indexInCurrentGroup = -1;
        var currentGroupType = GetCurrentGroupType(sizesType, sizes);

        foreach (var groupType in new[] { GameViewSizeGroupType.Standalone,
                                          GameViewSizeGroupType.Android,
                                          GameViewSizeGroupType.iOS })
        {
            var group = getGroup.Invoke(sizes, new object[] { groupType });
            if (group == null) continue;

            int index = AddOrFind(group, constructor, fixedResolution);
            if (index < 0) return;

            if (groupType == currentGroupType) indexInCurrentGroup = index;
        }

        if (indexInCurrentGroup < 0)
        {
            Debug.LogWarning($"[PhoneViewport] '{SizeName}' was added, but the current build target " +
                             $"({currentGroupType}) is not one of Standalone, Android or iOS — pick the " +
                             $"size by hand from the Game view dropdown.");
            return;
        }

        Select(editorAssembly, indexInCurrentGroup);
    }

    /// <summary>
    /// The index of the size in this group, adding it first if it is not already there. Re-runnable:
    /// pressing the menu item twice must not leave two identical entries in the dropdown.
    /// </summary>
    static int AddOrFind(object group, ConstructorInfo constructor, object fixedResolution)
    {
        var groupType = group.GetType();

        var getBuiltinCount = groupType.GetMethod("GetBuiltinCount");
        var getCustomCount = groupType.GetMethod("GetCustomCount");
        var getGameViewSize = groupType.GetMethod("GetGameViewSize");
        var addCustomSize = groupType.GetMethod("AddCustomSize");
        if (!Found(getBuiltinCount, "GameViewSizeGroup.GetBuiltinCount") ||
            !Found(getCustomCount, "GameViewSizeGroup.GetCustomCount") ||
            !Found(getGameViewSize, "GameViewSizeGroup.GetGameViewSize") ||
            !Found(addCustomSize, "GameViewSizeGroup.AddCustomSize")) return -1;

        int builtin = (int)getBuiltinCount.Invoke(group, null);
        int custom = (int)getCustomCount.Invoke(group, null);

        // Matched on the numbers, not on the name. Someone renaming their own 2340x1080 entry should
        // get that one selected rather than a second one added beside it.
        for (int i = 0; i < builtin + custom; i++)
        {
            var existing = getGameViewSize.Invoke(group, new object[] { i });
            if (existing == null) continue;

            var type = existing.GetType();
            int w = (int)type.GetProperty("width").GetValue(existing, null);
            int h = (int)type.GetProperty("height").GetValue(existing, null);
            if (w == Width && h == Height) return i;
        }

        var size = constructor.Invoke(new[] { fixedResolution, Width, Height, (object)SizeName });
        addCustomSize.Invoke(group, new[] { size });

        // Appended, so it is the last entry — built-ins come first in the dropdown and the custom
        // ones follow in the order they were added.
        return builtin + custom;
    }

    static GameViewSizeGroupType GetCurrentGroupType(Type sizesType, object sizes)
    {
        var property = sizesType.GetProperty("currentGroupType",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        // Falls back to the build target rather than failing: only the "which group did I just add
        // to" bookkeeping depends on this, and Standalone is the right guess in an editor that has
        // never been switched to a mobile target.
        if (property == null) return GameViewSizeGroupType.Standalone;

        return (GameViewSizeGroupType)(int)property.GetValue(sizes, null);
    }

    /// <summary>
    /// Points the open Game view at the size. Opens one if none is open — selecting a size on a
    /// window that does not exist would silently do nothing.
    /// </summary>
    static void Select(Assembly editorAssembly, int index)
    {
        var gameViewType = editorAssembly.GetType("UnityEditor.GameView");
        if (!Found(gameViewType, "UnityEditor.GameView")) return;

        var selectedSizeIndex = gameViewType.GetProperty("selectedSizeIndex",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (!Found(selectedSizeIndex, "GameView.selectedSizeIndex")) return;

        var window = EditorWindow.GetWindow(gameViewType, false, null, focus: false);
        selectedSizeIndex.SetValue(window, index, null);
        window.Repaint();

        Debug.Log($"[PhoneViewport] Game view set to '{SizeName}' — {Width}x{Height}, index {index}. " +
                  $"Added to the Standalone, Android and iOS size lists; run this again after a " +
                  $"platform switch if the view drops back to Free Aspect.");
    }

    static bool Found(object member, string name)
    {
        if (member != null) return true;

        Debug.LogError($"[PhoneViewport] {name} is not there any more — Unity's internal Game view API " +
                       $"has moved. Add the size by hand instead: Game view > size dropdown > + > " +
                       $"Fixed Resolution {Width}x{Height}.");
        return false;
    }
}
