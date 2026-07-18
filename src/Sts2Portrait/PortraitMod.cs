using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace Sts2Portrait;

[ModInitializer(nameof(Init))]
public static class PortraitMod
{
    public const string HarmonyId = "shaggylorean.Sts2Portrait";

    private static bool _done;

    // Called both by the PC mod loader (via ModInitializer) and, on Android, injected into the
    // launcher's managed entry. It must NEVER throw: on a fresh install the game assembly ('sts2')
    // isn't loaded yet, so our patch types can't resolve — we quietly apply nothing and the
    // launcher shows its install screen. Once the game is installed the app restarts and this
    // runs again with 'sts2' present, applying the patches for real.
    public static void Init()
    {
        if (_done) return;
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            Log($"initializing… build={asm.ManifestModule.ModuleVersionId}");
            var harmony = new Harmony(HarmonyId);

            System.Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t is not null).ToArray()!; }

            int ok = 0, fail = 0;
            foreach (var type in types)
            {
                try
                {
                    if (type is null || type.GetCustomAttributes(typeof(HarmonyPatch), true).Length == 0) continue;
                    new PatchClassProcessor(harmony, type).Patch();
                    ok++;
                }
                catch (System.Exception e)
                {
                    fail++;
                    Log($"WARN patch skipped [{type?.Name}]: {e.InnerException?.Message ?? e.Message}");
                }
            }
            if (ok > 0) _done = true;   // only latch once patches actually applied (game present)
            Log($"initialized — {ok} patch class(es) applied, {fail} skipped; " +
                $"{harmony.GetPatchedMethods().Count()} method(s) patched");
        }
        catch (System.Exception e)
        {
            Log($"init deferred (game not loaded yet?): {e.GetType().Name}: {e.Message}");
        }
    }

    internal static void Log(string msg) => Godot.GD.Print($"[Sts2Portrait] {msg}");
}
