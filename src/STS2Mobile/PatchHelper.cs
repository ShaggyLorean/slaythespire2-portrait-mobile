using System;
using System.Reflection;
using HarmonyLib;

namespace STS2Mobile;

// Shared utilities for applying Harmony patches with consistent error handling and logging.
internal static class PatchHelper
{
    private const BindingFlags AllFlags =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    internal static void Patch(
        Harmony harmony,
        Type targetType,
        string methodName,
        MethodInfo prefix = null,
        MethodInfo postfix = null,
        MethodInfo transpiler = null,
        BindingFlags flags = AllFlags
    )
    {
        PatchSafely(
            $"{targetType.Name}.{methodName}",
            () =>
            {
                if (!TryGetPatchMethod(targetType, methodName, flags, out var target))
                    return false;

                PatchMethod(harmony, target, prefix, postfix, transpiler);
                return true;
            }
        );
    }

    internal static void PatchGetter(
        Harmony harmony,
        Type targetType,
        string propertyName,
        MethodInfo prefix
    )
    {
        PatchSafely(
            $"{targetType.Name}.{propertyName} getter",
            () =>
            {
                if (!TryGetPatchGetter(targetType, propertyName, AllFlags, out var getter))
                    return false;

                harmony.Patch(getter, ToHarmonyMethod(prefix));
                return true;
            }
        );
    }

    // Like Patch(), but throws on failure for security-critical patches.
    internal static void PatchCritical(
        Harmony harmony,
        Type targetType,
        string methodName,
        MethodInfo prefix = null,
        MethodInfo postfix = null,
        BindingFlags flags = AllFlags
    )
    {
        var target = GetCriticalPatchMethod(targetType, methodName, flags);
        PatchMethod(harmony, target, prefix, postfix);
        Log($"Patched {targetType.Name}.{methodName} (critical)");
    }

    internal static MethodInfo Method(Type type, string name) =>
        type.GetMethod(name, AllFlags);

    internal static event Action<string> LogEmitted;

    internal static void Log(string msg)
    {
        BootstrapTrace.Log(msg);
        LogEmitted?.Invoke(msg);
    }

    private static bool _loggedFirstPatchFailureDetail;

    private static void PatchSafely(string label, Func<bool> patch)
    {
        try
        {
            if (patch())
                Log($"Patched {label}");
        }
        catch (Exception ex)
        {
            Log($"FAILED {label}: {ex.Message}");
            if (!_loggedFirstPatchFailureDetail)
            {
                // One full stack trace is worth a hundred one-line failures:
                // it names the layer that cannot patch on this platform.
                _loggedFirstPatchFailureDetail = true;
                Log($"FIRST FAILURE DETAIL: {ex}");
            }
        }
    }

    private static bool TryGetPatchMethod(
        Type targetType,
        string methodName,
        BindingFlags flags,
        out MethodInfo method
    )
    {
        method = targetType.GetMethod(methodName, flags);
        if (method != null)
            return true;

        Log($"FAILED {targetType.Name}.{methodName}: method not found");
        return false;
    }

    private static MethodInfo GetCriticalPatchMethod(
        Type targetType,
        string methodName,
        BindingFlags flags
    )
    {
        return targetType.GetMethod(methodName, flags)
            ?? throw new InvalidOperationException(
                $"Critical patch failed: {targetType.Name}.{methodName} not found"
            );
    }

    private static bool TryGetPatchGetter(
        Type targetType,
        string propertyName,
        BindingFlags flags,
        out MethodInfo getter
    )
    {
        getter = null;

        var prop = targetType.GetProperty(propertyName, flags);
        if (prop == null)
        {
            Log($"FAILED {targetType.Name}.{propertyName} getter: property not found");
            return false;
        }

        getter = prop.GetGetMethod(true);
        if (getter != null)
            return true;

        Log($"FAILED {targetType.Name}.{propertyName} getter: no getter");
        return false;
    }


    // Mono on Android aborts the process inside mono_class_from_mono_type_internal
    // ("implement me 0x00", class.c:2324) when Harmony copies a method whose
    // signature carries a nullable game struct. The abort is native, so no
    // try/catch can contain it and the only defence is to leave the method alone.
    internal static bool IsUnpatchableOnDevice(MethodBase target, out string reason)
    {
        reason = null;
        if (target == null || !OperatingSystem.IsAndroid())
            return false;

        if (ContainsNullable((target as MethodInfo)?.ReturnType))
        {
            reason = "nullable return value aborts Mono on Android";
            return true;
        }

        foreach (var parameter in target.GetParameters())
        {
            if (!ContainsNullable(parameter.ParameterType))
                continue;

            reason = $"nullable parameter '{parameter.Name}' aborts Mono on Android";
            return true;
        }

        return false;
    }

    private static bool ContainsNullable(Type type)
    {
        if (type == null || !type.IsGenericType)
            return false;

        if (type.GetGenericTypeDefinition() == typeof(Nullable<>))
            return true;

        foreach (var argument in type.GetGenericArguments())
        {
            if (ContainsNullable(argument))
                return true;
        }

        return false;
    }

    private static void PatchMethod(
        Harmony harmony,
        MethodInfo target,
        MethodInfo prefix = null,
        MethodInfo postfix = null,
        MethodInfo transpiler = null
    )
    {
        if (IsUnpatchableOnDevice(target, out var reason))
        {
            Log($"SKIPPED {target.DeclaringType?.Name}.{target.Name}: {reason}");
            return;
        }

        harmony.Patch(
            target,
            prefix: ToHarmonyMethod(prefix),
            postfix: ToHarmonyMethod(postfix),
            transpiler: ToHarmonyMethod(transpiler)
        );
    }

    private static HarmonyMethod ToHarmonyMethod(MethodInfo method) =>
        method != null ? new HarmonyMethod(method) : null;
}
