using System;
using System.Globalization;
using System.IO;
using Godot;

namespace STS2Mobile.Launcher;

// The console panel is the only place a player (or a bug report) can see what
// state the launcher actually booted into. Left to the event stream alone it
// shows two or three lines and reads as broken, so the boot state is written
// into it explicitly: build, device, renderer, account, game files, storage.
// Everything here is read-only and failure-tolerant; a line that cannot be
// produced is skipped rather than taking the boot down.
internal static class LauncherBootReport
{
    // The signed-in account is already the launcher's headline, so it is not
    // repeated here; this report covers what the headline cannot show.
    internal static void Write(Action<string> append, string dataDir)
    {
        if (append is null)
            return;

        try
        {
            append("--- launcher ---");
            append($"Build {AppVersion()}  |  {DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}");
            append($"Device {OS.GetName()} {DeviceModel()}");
            append($"Screen {ScreenLine()}");
            append($"Renderer {RendererLine()}");
            foreach (var line in GameFileLines(dataDir))
                append(line);

            append($"Storage {FreeSpaceLine(dataDir)}");
            append("--- ready ---");
        }
        catch (Exception ex)
        {
            append($"Boot report unavailable: {ex.Message}");
        }
    }

    private static string AppVersion()
    {
        try
        {
            if (AndroidGodotAppBridge.TryGetInstance(out var app) && app is not null)
            {
                var name = app.Call("getVersionName").AsString();
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
        }
        catch
        {
            // The bridge is Android-only and optional; fall through.
        }

        return "local";
    }

    private static string DeviceModel()
    {
        var model = OS.GetModelName();
        return string.IsNullOrWhiteSpace(model) ? "unknown model" : model;
    }

    private static string ScreenLine()
    {
        var size = DisplayServer.WindowGetSize();
        var refresh = DisplayServer.ScreenGetRefreshRate();
        var dpi = DisplayServer.ScreenGetDpi();
        return $"{size.X}x{size.Y} @ {refresh:F0}Hz, {dpi} dpi";
    }

    private static string RendererLine()
    {
        var adapter = RenderingServer.GetVideoAdapterName();
        var driver = RenderingServer.GetCurrentRenderingDriverName();
        var method = RenderingServer.GetCurrentRenderingMethod();
        return $"{(string.IsNullOrWhiteSpace(adapter) ? "unknown" : adapter)} via {driver}/{method}";
    }

    // The two questions a player actually has: is the game installed, and which
    // build is it. The manifest id is what the update check compares against, so
    // showing it makes "up to date" verifiable instead of a claim.
    private static string[] GameFileLines(string dataDir)
    {
        try
        {
            var pck = Path.Combine(dataDir, "game", "SlayTheSpire2.pck");
            if (!File.Exists(pck))
                return new[] { "Game files not installed" };

            var info = new FileInfo(pck);
            var lines = new System.Collections.Generic.List<string>
            {
                $"Game files {Megabytes(info.Length)}, updated {info.LastWriteTime:yyyy-MM-dd HH:mm}",
            };

            // The depot writes "<depotId>.id" next to its manifest; that number
            // is exactly what the update check compares against, so showing it
            // makes "up to date" verifiable instead of a claim.
            var stateDir = Path.Combine(dataDir, "download_state");
            if (Directory.Exists(stateDir))
            {
                foreach (var idFile in Directory.GetFiles(stateDir, "*.id"))
                {
                    var manifest = ReadFirstLine(idFile);
                    if (!string.IsNullOrWhiteSpace(manifest))
                        lines.Add($"Depot {Path.GetFileNameWithoutExtension(idFile)} manifest {manifest.Trim()}");
                }
            }

            return lines.ToArray();
        }
        catch (Exception ex)
        {
            return new[] { $"Game files unreadable: {ex.Message}" };
        }
    }

    // DriveInfo reports 0 for an Android app directory; the Java side has the
    // only number that means anything here.
    private static string FreeSpaceLine(string dataDir)
    {
        try
        {
            if (AndroidGodotAppBridge.TryGetInstance(out var app) && app is not null)
            {
                var bytes = app.Call("getUsableSpaceBytes", dataDir).AsInt64();
                if (bytes > 0)
                    return $"{Megabytes(bytes)} free";
            }
        }
        catch
        {
            // Fall through to the desktop path.
        }

        try
        {
            var root = Path.GetPathRoot(dataDir);
            if (string.IsNullOrWhiteSpace(root))
                return "unknown";

            return $"{Megabytes(new DriveInfo(root).AvailableFreeSpace)} free";
        }
        catch
        {
            return "unknown";
        }
    }

    private static string ReadFirstLine(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            foreach (var line in File.ReadLines(path))
                return line;
        }
        catch
        {
            // Optional evidence only.
        }

        return null;
    }

    private static string Megabytes(long bytes)
        => bytes >= 1024L * 1024L * 1024L
            ? $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB"
            : $"{bytes / (1024.0 * 1024.0):F0} MB";
}
