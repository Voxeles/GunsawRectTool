using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
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
        harmony.PatchAll();
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
    // The rect tool handles grid resizing on its own
    private static bool GetEditorKeyDown(KeyCode key, LevelEditor editor)
        => !(editor.mode == 3 && key is KeyCode.Equals or KeyCode.Minus) && Input.GetKeyDown(key);

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo original = AccessTools.Method(typeof(Input), nameof(Input.GetKeyDown), [typeof(KeyCode)]);
        MethodInfo replacement = AccessTools.Method(typeof(EditorUpdatePatch), nameof(GetEditorKeyDown));
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.Calls(original))
            {
                yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement;
            }
            yield return instruction;
        }
    }

    private static void Prefix(LevelEditor __instance)
    {
        var tool = __instance.GetComponent<RectTool>() ?? __instance.gameObject.AddComponent<RectTool>();
        tool.BeforeEditorUpdate(__instance);
    }
}

[HarmonyPatch(typeof(LevelEditor), nameof(LevelEditor.HandleSingleSelectInput))]
internal static class SingleInputPatch
{
    private static bool Prefix(LevelEditor __instance)
        => !(__instance.GetComponent<RectTool>()?.HandleInput() ?? false);
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
