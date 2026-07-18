using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace Sts2Portrait;

[ModInitializer(nameof(Init))]
public static class PortraitMod
{
    public const string HarmonyId = "shaggylorean.Sts2Portrait";

    public static void Init()
    {
        var mvid = Assembly.GetExecutingAssembly().ManifestModule.ModuleVersionId;
        Log($"initializing… build={mvid}");
        var harmony = new Harmony(HarmonyId);

        int ok = 0, fail = 0;
        var asm = Assembly.GetExecutingAssembly();
        foreach (var type in asm.GetTypes())
        {
            if (type.GetCustomAttributes(typeof(HarmonyPatch), true).Length == 0) continue;
            try
            {
                new PatchClassProcessor(harmony, type).Patch();
                ok++;
            }
            catch (System.Exception e)
            {
                fail++;
                Log($"WARN patch skipped [{type.Name}]: {e.InnerException?.Message ?? e.Message}");
            }
        }
        Log($"initialized — {ok} patch class(es) applied, {fail} skipped; " +
            $"{harmony.GetPatchedMethods().Count()} method(s) patched");
    }

    internal static void Log(string msg) => Godot.GD.Print($"[Sts2Portrait] {msg}");
}
