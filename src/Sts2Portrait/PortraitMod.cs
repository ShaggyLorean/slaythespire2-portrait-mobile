using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace Sts2Portrait;

/// <summary>
/// Mod girişi. ModManager, ModInitializer attribute'unu bulup Init()'i çağırır.
/// </summary>
[ModInitializer(nameof(Init))]
public static class PortraitMod
{
    public const string HarmonyId = "shaggylorean.Sts2Portrait";

    public static void Init()
    {
        var mvid = Assembly.GetExecutingAssembly().ManifestModule.ModuleVersionId;
        Log($"initializing… build={mvid}");
        var harmony = new Harmony(HarmonyId);
        harmony.PatchAll(Assembly.GetExecutingAssembly());
        Log($"initialized — {harmony.GetPatchedMethods().Count()} method(s) patched");
        // Teşhis/köprü node'ları SceneContainerDumpPatch içinde (mod yüklendikten sonra) enjekte edilir.
    }

    internal static void Log(string msg) => Godot.GD.Print($"[Sts2Portrait] {msg}");
}
