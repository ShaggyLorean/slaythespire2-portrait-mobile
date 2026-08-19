using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using STS2Mobile.Launcher;

namespace STS2Mobile;

// Entry point for the mobile patcher. Bootstraps GodotSharp, applies all Harmony
// patches, and falls back to standalone launcher mode if game files aren't present.
public static class ModEntry
{
    private const int ApplyComplete = 2;
    private const int ApplyInProgress = 1;
    private const int ApplyNotStarted = 0;
    private const int BootstrapProbeCode = 1729;
    private const string HarmonyId = "com.sts2mobile";
    private const int HarmonyConstructorProbeCode = 1730;
    private const int ProbeFailure = -1;
    private const int ProbeSuccess = 0;
    private const int ProbeSuccessWithValue = 1;
    private const string BootstrapScenePath = "res://bootstrap.tscn";
    private const uint GodotPckMagic = 0x43504447;
    private const int MinimumPckHeaderLength = 96;
    private static string ManagedTempDirectory => Path.Combine(OS.GetDataDir(), "tmp");
    private static readonly string[] TempVariableNames =
    {
        "TMPDIR",
        "TMP",
        "TEMP",
    };
    private static int _applyState = ApplyNotStarted;
    private static int _exceptionHandlersInstalled;

    // Bootstraps GodotSharp by setting up DLL import resolver, native interop,
    // and managed callbacks. Called from gd_mono.cpp before Apply().
    [UnmanagedCallersOnly]
    public static int InitializeGodotSharp(
        IntPtr godotDllHandle,
        IntPtr outManagedCallbacks,
        IntPtr unmanagedCallbacks,
        int unmanagedCallbacksSize
    )
    {
        try
        {
            GodotSharpBootstrap.Initialize(
                godotDllHandle,
                outManagedCallbacks,
                unmanagedCallbacks,
                unmanagedCallbacksSize
            );
            return 1;
        }
        catch
        {
            return 0;
        }
    }

    [UnmanagedCallersOnly]
    public static void Apply()
    {
        ApplyInternal();
    }

    [UnmanagedCallersOnly]
    public static int ApplyFromGodot()
    {
        try
        {
            BootstrapTrace.Log("ApplyFromGodot entered");
            ApplyInternal();
            BootstrapTrace.Log("ApplyFromGodot completed");
            return ProbeSuccess;
        }
        catch (Exception ex)
        {
            BootstrapTrace.Log($"Unhandled bootstrap failure: {ex}");
            return ProbeFailure;
        }
    }

    [UnmanagedCallersOnly]
    public static int BootstrapProbe()
    {
        return BootstrapProbeCode;
    }

    [UnmanagedCallersOnly]
    public static int HarmonyConstructorProbe()
    {
        _ = new Harmony(HarmonyId);
        return HarmonyConstructorProbeCode;
    }

    [UnmanagedCallersOnly]
    public static int ShowLauncherOnly()
    {
        try
        {
            ScheduleStandaloneLauncher();
            return ProbeSuccessWithValue;
        }
        catch
        {
            return ProbeSuccess;
        }
    }

    private static void ApplyInternal()
    {
        BootstrapTrace.Log("ApplyInternal entered");

        // Device bisect switches (no rebuild needed once this ships):
        //   adb shell touch /data/local/tmp/sts2_no_patches   -> boot the game
        //     completely unpatched, to tell "our layer" from "the game/runtime"
        //   adb shell touch /data/local/tmp/sts2_core_only    -> core group only
        if (System.IO.File.Exists("/data/local/tmp/sts2_no_patches"))
        {
            BootstrapTrace.Log("ApplyInternal skipped by device flag sts2_no_patches");
            PatchHelper.Log("[bisect] all startup patches skipped by device flag");
            return;
        }


        InstallManagedExceptionHandlers();
        Portrait.PortraitFrameBudget.ApplyEarly();
        NeutralizeSentryEarly();
        if (!TryBeginApply())
        {
            BootstrapTrace.Log("ApplyInternal duplicate invocation skipped");
            PatchHelper.Log("Apply already running/completed; skipping duplicate invocation.");
            return;
        }

        try
        {
            ApplyStartupPatches();
        }
        finally
        {
            CompleteApply();
        }
    }


    // Sentry must never run on Android: its SDK init spins up native worker
    // threads that abort the process ("FORTIFY: pthread_mutex_lock called on
    // a destroyed mutex") within ~80 ms of NGame._EnterTree calling it.
    //
    // Harmony is useless here - device runs proved that merely patching
    // SentryService.Initialize, or even transpiling the NGame._EnterTree call
    // site, drags the Sentry assembly through the JIT and kills the process
    // exactly the same way. So use the game's own guard instead:
    // SentryService.Initialize() reads "sentry/config/dsn" from project
    // settings and returns early when it is empty, long before SentrySdk.Init.
    // Clearing that setting at bootstrap keeps every line of Sentry code cold.
    private static void NeutralizeSentryEarly()
    {
        try
        {
            const string dsnSetting = "sentry/config/dsn";
            var previous = Godot.ProjectSettings.GetSetting(dsnSetting, "").AsString();
            Godot.ProjectSettings.SetSetting(dsnSetting, "");
            BootstrapTrace.Log(
                $"Sentry guard: dsn cleared (was {(string.IsNullOrEmpty(previous) ? "empty" : "set")})"
            );
            ProbeRuntimeCodegen();
            ProbeRuntimeIdentity();
            SubscribeMonoModLog();
            InstallMonoModNativeShim();
            PrepareNativeTempDir();
            ProbeMemoryPermissions();
            ForceLinuxDetourPlatform();
            ProbeDetourPlatform();
            FixDetourPageSize();
        }
        catch (Exception ex)
        {
            BootstrapTrace.Log($"Sentry guard failed: {ex.GetBaseException().Message}");
        }
    }


    // Harmony needs runtime codegen. If the app ships an AOT/interpreter-only
    // runtime, every patch dies with NotImplementedException deep inside
    // MonoMod, which is exactly what the device trace shows. This probe says
    // in one line whether dynamic methods work at all here.
    private static void ProbeRuntimeCodegen()
    {
        try
        {
            var dynamic = new System.Reflection.Emit.DynamicMethod(
                "sts2_codegen_probe",
                typeof(int),
                Type.EmptyTypes
            );
            var il = dynamic.GetILGenerator();
            il.Emit(System.Reflection.Emit.OpCodes.Ldc_I4, 1729);
            il.Emit(System.Reflection.Emit.OpCodes.Ret);
            var result = (int)dynamic.Invoke(null, null);
            BootstrapTrace.Log(
                $"Codegen probe: dynamic method returned {result}; "
                + $"IsDynamicCodeSupported={System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported}, "
                + $"IsDynamicCodeCompiled={System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeCompiled}"
            );
        }
        catch (Exception ex)
        {
            BootstrapTrace.Log($"Codegen probe FAILED: {ex.GetType().Name}: {ex.Message}");
        }
    }


    // Harmony's detour engine (MonoMod.Core, merged into 0Harmony) refuses to
    // patch anything here with a bare NotImplementedException. Ask it directly
    // what it thinks this machine is: architecture, OS and runtime kind. The
    // answer says whether the arm64/Android/Mono combination is unsupported or
    // simply mis-detected.

    // MonoMod picks its detour backend from what the runtime says it is
    // running on. If this reports Android rather than Linux, the newer
    // MonoMod.Core has no matching system implementation and every patch
    // dies with NotImplementedException - which is exactly the symptom.
    private static void ProbeRuntimeIdentity()
    {
        try
        {
            var isAndroid = System.OperatingSystem.IsAndroid();
            var isLinux = System.OperatingSystem.IsLinux();
            BootstrapTrace.Log(
                "Runtime identity: "
                + $"OSDescription='{System.Runtime.InteropServices.RuntimeInformation.OSDescription}' "
                + $"RID='{System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier}' "
                + $"Framework='{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}' "
                + $"Arch={System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture} "
                + $"IsAndroid={isAndroid} IsLinux={isLinux}"
            );
        }
        catch (Exception ex)
        {
            BootstrapTrace.Log($"Runtime identity probe failed: {ex.GetBaseException().Message}");
        }
    }


    // THE fix for "no patch applies on device". MonoMod (inside 0Harmony)
    // detects OS=Android, and its platform layer only implements Windows,
    // Linux and macOS, so PlatformTriple.CreateCurrentSystem() throws
    // NotImplementedException and every single patch fails.
    //
    // Android *is* Linux: same /proc, same mmap/mprotect, same ELF layout,
    // and Mono already JITs here (verified by the codegen probe). So tell
    // MonoMod it is on Linux before anything asks for the platform triple.
    // Done by reflection because these fields are internal, and guarded so a
    // future MonoMod that supports Android natively is left alone.

    // Second half of the Android detour fix. Once MonoMod believes it is on
    // Linux it P/Invokes glibc names bionic does not have - __errno_location
    // above all - and dies with EntryPointNotFoundException before any patch
    // lands. libmonomodshim.so exports those names and forwards to bionic;
    // this resolver points MonoMod's "libc"/"libdl" imports at it. Other
    // libraries keep their normal resolution.

    // Pipe MonoMod's own diagnostics into our trace file. Its detour work is
    // native and its exceptions surface as bare errno values ("Invalid
    // argument"), so without this the failing operation is invisible.
    private static void SubscribeMonoModLog()
    {
        try
        {
            var debugLog = HarmonyLib.AccessTools.TypeByName("MonoMod.Logs.DebugLog");
            var handlerType = HarmonyLib.AccessTools.TypeByName("MonoMod.Logs.DebugLog+OnLogMessage");
            var filterType = HarmonyLib.AccessTools.TypeByName("MonoMod.Logs.LogLevelFilter");
            if (debugLog is null || handlerType is null || filterType is null)
            {
                BootstrapTrace.Log("MonoMod log: types not found");
                return;
            }

            var sink = typeof(ModEntry).GetMethod(
                nameof(OnMonoModLog),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static
            );
            var handler = Delegate.CreateDelegate(handlerType, sink);
            var subscribe = debugLog.GetMethod(
                "Subscribe",
                new[] { filterType, handlerType }
            );
            subscribe?.Invoke(null, new[] { Enum.ToObject(filterType, -1), handler });
            BootstrapTrace.Log("MonoMod log: subscribed");
        }
        catch (Exception ex)
        {
            BootstrapTrace.Log($"MonoMod log subscribe failed: {ex.GetBaseException().Message}");
        }
    }

    private static void OnMonoModLog(string source, DateTime time, object level, string message)
        => BootstrapTrace.Log($"[monomod/{source}] {message}");

    private static void InstallMonoModNativeShim()
    {
        try
        {
            var harmonyAssembly = typeof(HarmonyLib.Harmony).Assembly;
            var shim = IntPtr.Zero;

            System.Runtime.InteropServices.NativeLibrary.SetDllImportResolver(
                harmonyAssembly,
                (name, assembly, searchPath) =>
                {
                    var wantsLibc =
                        name is "libc" or "libc.so" or "libc.so.6"
                        or "libdl" or "libdl.so" or "libdl.so.2";
                    if (!wantsLibc)
                        return IntPtr.Zero;

                    if (shim == IntPtr.Zero)
                    {
                        foreach (var candidate in new[]
                        {
                            "/data/local/tmp/libmonomodshim.so",
                            "libmonomodshim.so",
                        })
                        {
                            if (System.Runtime.InteropServices.NativeLibrary.TryLoad(candidate, out var handle))
                            {
                                shim = handle;
                                BootstrapTrace.Log($"MonoMod shim loaded from {candidate}");
                                break;
                            }
                        }
                    }

                    return shim;
                }
            );

            BootstrapTrace.Log("MonoMod shim resolver installed");
        }
        catch (Exception ex)
        {
            BootstrapTrace.Log($"MonoMod shim resolver failed: {ex.GetBaseException().Message}");
        }
    }




    [System.Runtime.InteropServices.DllImport("libc.so", EntryPoint = "sysconf")]
    private static extern long LibcSysconf(int name);

    // glibc numbers _SC_PAGESIZE 30, bionic numbers it 39. MonoMod is compiled
    // against the glibc value, so on Android it asks for the wrong limit, gets
    // a nonsense page size back and rounds detour addresses to an unaligned
    // boundary. mprotect then rejects the call with EINVAL and every patch
    // fails. Correct the page size on the live objects before the first patch.
    private static void FixDetourPageSize()
    {
        try
        {
            const int glibcPageSizeName = 30;
            const int bionicPageSizeName = 39;
            var reported = LibcSysconf(glibcPageSizeName);
            var actual = (nint)LibcSysconf(bionicPageSizeName);
            BootstrapTrace.Log($"Page size: MonoMod would read {reported}, real is {actual}");
            if (actual <= 0 || reported == actual)
                return;

            var tripleType = HarmonyLib.AccessTools.TypeByName("MonoMod.Core.Platforms.PlatformTriple");
            var triple = tripleType?.GetProperty("Current")?.GetValue(null);
            var system = tripleType?.GetProperty("System")?.GetValue(triple);
            if (system is null)
            {
                BootstrapTrace.Log("Page size: platform system not reachable");
                return;
            }

            const System.Reflection.BindingFlags instanceFlags =
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Public;

            var fixedCount = 0;
            var systemPage = system.GetType().GetField("PageSize", instanceFlags);
            if (systemPage is not null)
            {
                systemPage.SetValue(system, actual);
                fixedCount++;
            }

            var allocator = system.GetType().GetField("allocator", instanceFlags)?.GetValue(system);
            for (var type = allocator?.GetType(); type is not null; type = type.BaseType)
            {
                var pageSize = type.GetField("pageSize", instanceFlags);
                if (pageSize is null)
                    continue;

                pageSize.SetValue(allocator, actual);
                type.GetField("pageSizeIsPow2", instanceFlags)?.SetValue(allocator, (actual & (actual - 1)) == 0);
                type.GetField("pageBaseMask", instanceFlags)?.SetValue(allocator, ~(actual - 1));
                fixedCount += 3;
                break;
            }

            BootstrapTrace.Log($"Page size: corrected to {actual} ({fixedCount} field(s))");
        }
        catch (Exception ex)
        {
            BootstrapTrace.Log($"Page size fix failed: {ex.GetBaseException().Message}");
        }
    }

    [System.Runtime.InteropServices.DllImport("libc.so", EntryPoint = "setenv")]
    private static extern int LibcSetEnv(string name, string value, int overwrite);

    [System.Runtime.InteropServices.DllImport("libc.so", EntryPoint = "getenv")]
    private static extern IntPtr LibcGetEnv(string name);

    [System.Runtime.InteropServices.DllImport("libc.so", EntryPoint = "mkstemp", SetLastError = true)]
    private static extern int LibcMkstemp(byte[] template);

    [System.Runtime.InteropServices.DllImport("libc.so", EntryPoint = "close")]
    private static extern int LibcClose(int fd);

    // MonoMod builds detour trampolines through a temp file so it never needs a
    // page that is writable and executable at the same time. Android has no
    // /tmp, so the default template cannot be created and the detour fails with
    // a bare errno. Point the native side at the app data directory first.
    private static void PrepareNativeTempDir()
    {
        try
        {
            var current = LibcGetEnv("TMPDIR");
            var currentText = current == IntPtr.Zero
                ? "<unset>"
                : System.Runtime.InteropServices.Marshal.PtrToStringAnsi(current);

            var tempDir = System.IO.Path.Combine(Godot.OS.GetUserDataDir(), "mmtmp");
            System.IO.Directory.CreateDirectory(tempDir);
            LibcSetEnv("TMPDIR", tempDir, 1);
            System.Environment.SetEnvironmentVariable("TMPDIR", tempDir);

            var template = System.Text.Encoding.UTF8.GetBytes(tempDir + "/mmXXXXXX ");
            var fd = LibcMkstemp(template);
            var errno = fd < 0
                ? System.Runtime.InteropServices.Marshal.GetLastWin32Error()
                : 0;
            if (fd >= 0)
            {
                LibcClose(fd);
                var created = System.Text.Encoding.UTF8.GetString(template, 0, template.Length - 1);
                try
                {
                    System.IO.File.Delete(created);
                }
                catch
                {
                    // The probe file is disposable; a failed delete is not worth reporting.
                }
            }

            BootstrapTrace.Log(
                $"Temp dir: was {currentText}, now {tempDir}, mkstemp={(fd >= 0 ? "ok" : "fail")}({errno})"
            );
        }
        catch (Exception ex)
        {
            BootstrapTrace.Log($"Temp dir setup failed: {ex.GetBaseException().Message}");
        }
    }

    [System.Runtime.InteropServices.DllImport("libmonomodshim", EntryPoint = "mm_probe")]
    private static extern int MmProbe(byte[] buffer, int length);

    // Detours need writable-then-executable memory. Android is stricter than
    // desktop Linux here, and MonoMod only surfaces the raw errno, so measure
    // the three operations it depends on directly.
    private static void ProbeMemoryPermissions()
    {
        try
        {
            var buffer = new byte[256];
            var written = MmProbe(buffer, buffer.Length);
            var text = System.Text.Encoding.ASCII.GetString(buffer, 0, Math.Max(0, Math.Min(written, buffer.Length)));
            BootstrapTrace.Log($"Memory probe: {text}");
        }
        catch (Exception ex)
        {
            BootstrapTrace.Log($"Memory probe failed: {ex.GetBaseException().Message}");
        }
    }

    private static void ForceLinuxDetourPlatform()
    {
        try
        {
            var detection = HarmonyLib.AccessTools.TypeByName("MonoMod.Utils.PlatformDetection");
            var osKind = HarmonyLib.AccessTools.TypeByName("MonoMod.Utils.OSKind");
            if (detection is null || osKind is null)
            {
                BootstrapTrace.Log("Platform override: MonoMod detection types not found");
                return;
            }

            var osProperty = HarmonyLib.AccessTools.Property(detection, "OS");
            var detected = osProperty?.GetValue(null)?.ToString();
            if (detected is null || !detected.Contains("Android", StringComparison.OrdinalIgnoreCase))
            {
                BootstrapTrace.Log($"Platform override: not needed (OS={detected})");
                return;
            }

            var linux = Enum.Parse(osKind, "Linux");
            var patchedFields = 0;
            foreach (var field in detection.GetFields(
                System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Public))
            {
                if (field.FieldType != osKind)
                    continue;
                field.SetValue(null, linux);
                patchedFields++;
            }

            var after = osProperty.GetValue(null)?.ToString();
            BootstrapTrace.Log(
                $"Platform override: {detected} -> {after} ({patchedFields} field(s) rewritten)"
            );
        }
        catch (Exception ex)
        {
            BootstrapTrace.Log($"Platform override failed: {ex.GetBaseException().Message}");
        }
    }

    private static void ProbeDetourPlatform()
    {
        try
        {
            var tripleType = HarmonyLib.AccessTools.TypeByName("MonoMod.Core.Platforms.PlatformTriple");
            if (tripleType is null)
            {
                BootstrapTrace.Log("Detour probe: PlatformTriple type not found (old MonoMod?)");
                ProbeLegacyDetourPlatform();
                return;
            }

            var detection = HarmonyLib.AccessTools.TypeByName("MonoMod.Utils.PlatformDetection");
            if (detection is not null)
            {
                string Detected(string name)
                {
                    try
                    {
                        return HarmonyLib.AccessTools.Property(detection, name)?.GetValue(null)?.ToString() ?? "<null>";
                    }
                    catch (Exception ex)
                    {
                        return $"<{ex.GetBaseException().GetType().Name}>";
                    }
                }

                BootstrapTrace.Log(
                    $"MonoMod detection: OS={Detected("OS")} Arch={Detected("Architecture")} Runtime={Detected("Runtime")}"
                );
            }

            var current = HarmonyLib.AccessTools.Property(tripleType, "Current")?.GetValue(null);
            if (current is null)
            {
                BootstrapTrace.Log("Detour probe: PlatformTriple.Current was null");
                return;
            }

            string Read(string name)
            {
                try
                {
                    return HarmonyLib.AccessTools.Property(tripleType, name)?.GetValue(current)?.ToString()
                        ?? "<null>";
                }
                catch (Exception ex)
                {
                    return $"<{ex.GetBaseException().GetType().Name}>";
                }
            }

            BootstrapTrace.Log(
                $"Detour probe: Architecture={Read("Architecture")} System={Read("System")} Runtime={Read("Runtime")}"
            );
        }
        catch (Exception ex)
        {
            // The stack trace names the missing piece (system, architecture
            // or runtime), which is the difference between "patch Harmony" and
            // "give up on Harmony".
            BootstrapTrace.Log($"Detour probe FAILED: {ex}");
        }
    }

    private static void ProbeLegacyDetourPlatform()
    {
        try
        {
            var platformHelper = HarmonyLib.AccessTools.TypeByName("MonoMod.Utils.PlatformHelper");
            var value = HarmonyLib.AccessTools.Property(platformHelper, "Current")?.GetValue(null);
            BootstrapTrace.Log($"Detour probe (legacy): PlatformHelper.Current={value}");
        }
        catch (Exception ex)
        {
            BootstrapTrace.Log($"Detour probe (legacy) FAILED: {ex.GetBaseException().Message}");
        }
    }

    private static void ApplyStartupPatches()
    {
        BootstrapTrace.Log("Initializing STS2Mobile");
        PatchHelper.Log("Initializing STS2Mobile...");
        try
        {
            ConfigureWritableTempDirectory();
            var harmony = new Harmony(HarmonyId);
            BootstrapTrace.Log("Starting startup patch orchestration");
            var patchResult = StartupPatchOrchestrator.Apply(harmony);
            BootstrapTrace.Log("Finished startup patch orchestration");

            if (patchResult.CriticalFailed)
            {
                PatchHelper.Log("Critical startup patches failed; scheduling standalone launcher fallback.");
                ScheduleStandaloneLauncher();
                return;
            }

            if (patchResult.HasFailures)
            {
                PatchHelper.Log(
                    $"Startup completed with {patchResult.FailedPatchCount} non-critical patch failures."
                );
            }

            foreach (var failure in patchResult.FailureMessages().Take(10))
            {
                PatchHelper.Log($"[startup] {failure}");
            }

            PatchHelper.Log("Startup patch orchestration complete.");
            if (IsStandaloneLauncherRequired())
            {
                ScheduleStandaloneLauncher();
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"Unexpected startup error: {ex.Message}");
            ScheduleStandaloneLauncher();
        }
    }

    private static bool IsStandaloneLauncherRequired()
    {
        // Android deliberately boots the small bootstrap PCK until PLAY requests
        // a one-shot restart with the downloaded game PCK. The downloaded file can
        // already be valid in launcher-only mode, so file readiness alone cannot
        // identify which pack is currently mounted.
        if (IsBootstrapPackLoaded())
        {
            PatchHelper.Log("Bootstrap PCK detected; standalone launcher required");
            return true;
        }

        return !IsGamePckStructurallyReady(
            Path.Combine(
                OS.GetDataDir(),
                LauncherStorageNames.GameDirectory,
                LauncherStorageNames.GamePck
            )
        );
    }

    private static bool IsBootstrapPackLoaded()
    {
        try
        {
            return ResourceLoader.Exists(BootstrapScenePath);
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"Could not inspect mounted PCK: {ex.Message}");
            return false;
        }
    }

    private static void InstallManagedExceptionHandlers()
    {
        if (Interlocked.Exchange(ref _exceptionHandlersInstalled, 1) == 1)
            return;

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            try
            {
                BootstrapTrace.Log($"Unhandled managed exception: {args.ExceptionObject}");
            }
            catch (Exception ex)
            {
                BootstrapTrace.Log($"Managed exception handler logging failed: {ex.Message}");
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            try
            {
                BootstrapTrace.Log($"Unobserved task exception: {args.Exception}");
            }
            catch (Exception ex)
            {
                BootstrapTrace.Log($"Managed exception handler logging failed: {ex.Message}");
            }
        };

        BootstrapTrace.Log("Managed exception handlers installed");
    }

    private static void ConfigureWritableTempDirectory()
    {
        Directory.CreateDirectory(ManagedTempDirectory);

        foreach (var variable in TempVariableNames)
            System.Environment.SetEnvironmentVariable(variable, ManagedTempDirectory);

        PatchHelper.Log($"Using writable temp directory: {ManagedTempDirectory}");
    }

    private static bool IsGamePckStructurallyReady(string path)
    {
        try
        {
            if (!File.Exists(path))
                return false;

            using var fs = File.OpenRead(path);
            using var reader = new BinaryReader(fs);
            if (!TryReadPckDirectoryBase(reader, fs.Length, out var dirBase))
                return false;

            fs.Position = dirBase;
            return reader.ReadUInt32() > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadPckDirectoryBase(BinaryReader reader, long fileLength, out long dirBase)
    {
        dirBase = 0;
        if (fileLength < MinimumPckHeaderLength)
            return false;

        if (reader.ReadUInt32() != GodotPckMagic)
            return false;

        reader.ReadUInt32();
        reader.ReadUInt32();
        reader.ReadUInt32();
        reader.ReadUInt32();
        reader.ReadUInt32();
        reader.ReadInt64();
        dirBase = reader.ReadInt64();
        return dirBase > 0 && dirBase + 4 <= fileLength;
    }

    private static void ScheduleStandaloneLauncher()
    {
        PatchHelper.Log("Scheduling standalone launcher...");
        Callable.From(CreateStandaloneLauncher).CallDeferred();
    }

    private static void CreateStandaloneLauncher()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
        {
            Callable.From(CreateStandaloneLauncher).CallDeferred();
            return;
        }

        var launcher = new LauncherUI();
        tree.Root.AddChild(launcher);
        launcher.Initialize();
        PatchHelper.Log("Standalone launcher displayed");
    }

    private static bool TryBeginApply()
    {
        return Interlocked.CompareExchange(ref _applyState, ApplyInProgress, ApplyNotStarted) == ApplyNotStarted;
    }

    private static void CompleteApply()
    {
        _applyState = ApplyComplete;
    }
}
