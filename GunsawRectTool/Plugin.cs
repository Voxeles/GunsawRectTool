using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace GunsawRectTool;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal new static ManualLogSource Logger = null!;
    private Harmony? harmony;

    private void Awake()
    {
        Logger = base.Logger;
        harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        harmony.PatchAll(typeof(Plugin).Assembly);
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_NAME} is loaded!");
    }

    private void OnDestroy()
    {
        harmony?.UnpatchSelf();
        foreach (var tool in FindObjectsOfType<RectTool>())
            Destroy(tool);
    }
}

[HarmonyPatch(typeof(LevelEditor), nameof(LevelEditor.Update))]
internal static class EditorUpdatePatch
{
    private static void Prefix(LevelEditor __instance)
    {
        var tool = __instance.GetComponent<RectTool>() ?? __instance.gameObject.AddComponent<RectTool>();
        tool.BeforeEditorUpdate(__instance);
    }
}

[HarmonyPatch(typeof(LevelEditor), nameof(LevelEditor.HandleSingleSelectInput))]
internal static class SingleInputPatch
{
    private static bool Prefix(LevelEditor __instance) =>
        !(__instance.GetComponent<RectTool>()?.HandleInput() ?? false);
}

[HarmonyPatch(typeof(LevelEditor), nameof(LevelEditor.HandleMultiSelectInput))]
internal static class MultiInputPatch
{
    private static bool Prefix(LevelEditor __instance)
    {
        var tool = __instance.GetComponent<RectTool>();
        return tool == null || !tool.IsDragging || !tool.HandleInput();
    }
}
