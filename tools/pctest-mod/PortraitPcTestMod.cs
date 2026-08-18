using System;
using System.IO;
using System.Reflection;
using System.Text;
using Godot;
using MegaCrit.Sts2.Core.Modding;

namespace Sts2PortraitPcTest;

// Entry point invoked by the game's ModManager. Applies the portrait patch
// group to the desktop game, then drives the screenshot schedule from the
// SceneTree.ProcessFrame event. Plain C# event handlers need no Godot script
// bridge, so this works for an assembly the engine never registered as a
// script assembly (custom Node subclasses from mod DLLs get no _Ready/_Process
// dispatch here).
[ModInitializer(nameof(Initialize))]
public static class PortraitPcTestMod
{
    private static readonly double[] CaptureAtSeconds = { 4.0, 8.0, 12.0, 16.0, 20.0, 24.0 };
    private const double QuitAtSeconds = 26.0;

    private static SceneTree _tree;
    private static Assembly _sts2Mobile;
    private static MethodInfo _portraitApply;
    private static ulong _startTicksMs;
    private static ulong _lastPortraitApplyMs;
    private static int _nextCapture;
    private static bool _quitRequested;

    public static void Initialize()
    {
        PcTestLog.Write("mod initializer entered");
        try
        {
            _sts2Mobile = LoadSts2Mobile();
            var patches = _sts2Mobile.GetType("STS2Mobile.Portrait.PortraitPatches");
            var apply = patches?.GetMethod(
                "Apply",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public
            );
            if (apply is null)
                throw new InvalidOperationException("PortraitPatches.Apply not found");

            // The game loads mod assemblies in their own AssemblyLoadContext, so
            // this mod's HarmonyLib.Harmony type and the one STS2Mobile binds to
            // can be different runtime types. Build the instance from the
            // parameter type Apply actually declares to stay in one context.
            var harmonyType = apply.GetParameters()[0].ParameterType;
            var harmony = Activator.CreateInstance(harmonyType, "sts2.portrait.pctest");
            apply.Invoke(null, new[] { harmony });
            PcTestLog.Write("portrait patch group applied");

            _portraitApply = _sts2Mobile
                .GetType("STS2Mobile.Portrait.PortraitDisplay")
                ?.GetMethod("Apply", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        }
        catch (Exception ex)
        {
            PcTestLog.Write($"portrait patch apply FAILED: {ex}");
        }

        if (Engine.GetMainLoop() is not SceneTree tree)
        {
            PcTestLog.Write("driver install failed: no SceneTree at mod init");
            return;
        }

        _tree = tree;
        _startTicksMs = Time.GetTicksMsec();
        tree.ProcessFrame += Tick;
        PcTestLog.Write("frame driver installed");
    }

    // STS2Mobile must land in the SAME AssemblyLoadContext as this mod so its
    // GodotSharp/0Harmony references resolve to the game's already-initialized
    // instances. A stray Assembly.Load/LoadFrom puts it in another context,
    // which drags in a second GodotSharp whose native function table is empty
    // and crashes with an access violation on first use.
    private static Assembly LoadSts2Mobile()
    {
        var self = typeof(PortraitPcTestMod).Assembly;
        var context = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(self)
            ?? System.Runtime.Loader.AssemblyLoadContext.Default;
        var local = Path.Combine(Path.GetDirectoryName(self.Location) ?? ".", "STS2Mobile.dll");
        return context.LoadFromAssemblyPath(local);
    }

    private static void Tick()
    {
        try
        {
            var elapsedMs = Time.GetTicksMsec() - _startTicksMs;
            var elapsed = elapsedMs / 1000.0;

            EnforceTestWindow();

            // PC belt for the viewport guard: the guard node's _Process does
            // not dispatch for unregistered assemblies, so re-assert the
            // portrait canvas from here once a second instead.
            if (elapsedMs - _lastPortraitApplyMs >= 1000)
            {
                _lastPortraitApplyMs = elapsedMs;
                _portraitApply?.Invoke(null, null);
            }

            if (_nextCapture < CaptureAtSeconds.Length && elapsed >= CaptureAtSeconds[_nextCapture])
            {
                Capture($"t{CaptureAtSeconds[_nextCapture]:00}");
                _nextCapture++;
            }

            if (!_quitRequested && elapsed >= QuitAtSeconds)
            {
                _quitRequested = true;
                PcTestLog.Write("driver run complete, quitting game");
                _tree.Quit();
            }
        }
        catch (Exception ex)
        {
            PcTestLog.Write($"tick FAILED: {ex.Message}");
        }
    }

    private static void EnforceTestWindow()
    {
        // The game re-applies its saved desktop display settings after boot,
        // which overrides the command-line resolution and re-centers the
        // window onto the visible desktop. Pin the phone-shaped window and the
        // off-screen position for the whole run so the rig never depends on
        // saved settings and never surfaces a window on the user's desktop.
        if (DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Windowed)
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        if (DisplayServer.WindowGetSize() != PcTestConfig.WindowSize)
            DisplayServer.WindowSetSize(PcTestConfig.WindowSize);
        if (DisplayServer.WindowGetPosition() != PcTestConfig.WindowPosition)
            DisplayServer.WindowSetPosition(PcTestConfig.WindowPosition);
    }

    private static void Capture(string label)
    {
        try
        {
            var image = _tree.Root.GetTexture().GetImage();
            Directory.CreateDirectory(PcTestLog.OutDir);
            image.SavePng(Path.Combine(PcTestLog.OutDir, $"{label}.png"));

            var window = (Vector2)DisplayServer.WindowGetSize();
            var canvas = _tree.Root.ContentScaleSize;
            var scene = _tree.CurrentScene;
            PcTestLog.Write(
                $"captured {label}: window={window.X:F0}x{window.Y:F0} canvas={canvas.X}x{canvas.Y} scene={(scene is null ? "(none)" : scene.Name.ToString())}"
            );
            DumpTree(label);
        }
        catch (Exception ex)
        {
            PcTestLog.Write($"capture {label} FAILED: {ex.Message}");
        }
    }

    // Shallow tree dump: enough to learn screen structure and pick node paths
    // for the next driver iteration, without megabyte logs.
    private static void DumpTree(string label)
    {
        try
        {
            var sb = new StringBuilder();
            Dump(_tree.Root, sb, 0, 4);
            File.WriteAllText(Path.Combine(PcTestLog.OutDir, $"{label}-tree.txt"), sb.ToString());
        }
        catch (Exception ex)
        {
            PcTestLog.Write($"tree dump {label} FAILED: {ex.Message}");
        }
    }

    private static void Dump(Node node, StringBuilder sb, int depth, int maxDepth)
    {
        sb.Append(new string(' ', depth * 2));
        sb.Append(node.Name).Append(" : ").Append(node.GetType().Name);
        if (node is Control control)
            sb.Append($"  pos={control.GlobalPosition.X:F0},{control.GlobalPosition.Y:F0} size={control.Size.X:F0}x{control.Size.Y:F0} visible={control.Visible}");
        sb.AppendLine();

        if (depth >= maxDepth)
            return;
        foreach (var child in node.GetChildren())
            Dump(child, sb, depth + 1, maxDepth);
    }
}

internal static class PcTestConfig
{
    internal static readonly Vector2I WindowSize = ReadVector("STS2_PCTEST_WINDOW", 'x', new Vector2I(1298, 2856));
    internal static readonly Vector2I WindowPosition = ReadVector("STS2_PCTEST_POSITION", ',', new Vector2I(10020, 60));

    private static Vector2I ReadVector(string env, char separator, Vector2I fallback)
    {
        var raw = System.Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;
        var parts = raw.Split(separator);
        return parts.Length == 2
            && int.TryParse(parts[0], out var x)
            && int.TryParse(parts[1], out var y)
            ? new Vector2I(x, y)
            : fallback;
    }
}

// File log the harness can tail; GD.Print also goes to the game's log.
internal static class PcTestLog
{
    internal static string OutDir =>
        System.Environment.GetEnvironmentVariable("STS2_PCTEST_OUT")
        ?? ProjectSettings.GlobalizePath("user://pctest-shots");

    internal static void Write(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        GD.Print($"[PcTest] {message}");
        try
        {
            Directory.CreateDirectory(OutDir);
            File.AppendAllText(Path.Combine(OutDir, "pctest-log.txt"), line + System.Environment.NewLine);
        }
        catch
        {
            // Logging must never take the game down.
        }
    }
}
