using System;
using System.Linq;
using System.Reflection;

namespace STS2Mobile.Launcher;

// A failed type initializer only reports "NullReferenceException" and the name
// of the type, which is not enough to tell whether the game state behind it is
// missing or merely different on device. These probes report the state the
// failing initializers depend on, so a device round produces an answer instead
// of another guess.
internal static class LauncherStartupDiagnostics
{
    internal static void ReportStartupFailure(Exception failure)
    {
        try
        {
            ReportTypeInitializerChain(failure);
            ReportLocalizationState();
            ReportAssemblyIdentity();
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Diag] Startup diagnostics failed: {ex.GetBaseException().Message}");
        }
    }

    private static void ReportTypeInitializerChain(Exception failure)
    {
        for (var current = failure; current != null; current = current.InnerException)
        {
            var typeName = current is TypeInitializationException typeInit
                ? typeInit.TypeName
                : current.GetType().Name;
            PatchHelper.Log($"[Diag] failure chain: {typeName}: {current.Message}");
        }
    }

    private static void ReportLocalizationState()
    {
        var locManagerType = Type.GetType(
            "MegaCrit.Sts2.Core.Localization.LocManager, sts2",
            throwOnError: false
        );
        if (locManagerType == null)
        {
            PatchHelper.Log("[Diag] LocManager type not found");
            return;
        }

        var instance = locManagerType
            .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)
            ?.GetValue(null);
        if (instance == null)
        {
            PatchHelper.Log("[Diag] LocManager.Instance is null");
            return;
        }

        var tables = locManagerType
            .GetField("_tables", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.GetValue(instance) as System.Collections.IDictionary;
        if (tables == null)
        {
            PatchHelper.Log("[Diag] LocManager tables field unavailable");
            return;
        }

        var names = tables.Keys.Cast<object>().Select(key => key?.ToString()).ToArray();
        PatchHelper.Log($"[Diag] Loc tables ({names.Length}): {string.Join(", ", names)}");

        if (!tables.Contains("main_menu_ui"))
            return;

        var table = tables["main_menu_ui"];
        var translations = table
            ?.GetType()
            .GetField("_translations", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.GetValue(table) as System.Collections.IDictionary;
        PatchHelper.Log(
            translations == null
                ? "[Diag] main_menu_ui has no translations dictionary"
                : $"[Diag] main_menu_ui entries={translations.Count}, has DATE_FORMAT={translations.Contains("DAILY_RUN_MENU.DATE_FORMAT")}"
        );
    }

    private static void ReportAssemblyIdentity()
    {
        var loaded = AppDomain.CurrentDomain
            .GetAssemblies()
            .Where(assembly => assembly.GetName().Name == "sts2")
            .ToArray();
        PatchHelper.Log($"[Diag] sts2 assemblies loaded: {loaded.Length}");
    }
}
