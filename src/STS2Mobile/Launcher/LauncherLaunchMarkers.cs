using System;
using System.IO;
using Godot;
using STS2Mobile.Patches;

namespace STS2Mobile.Launcher;

internal static class LauncherLaunchMarkers
{
    internal static string StartupMarkerPath =>
        Path.Combine(OS.GetDataDir(), LauncherStorageNames.StartupMarker);

    internal static string ManualSafeLaunchPath =>
        Path.Combine(OS.GetDataDir(), LauncherStorageNames.ManualSafeLaunch);

    internal static string OfflineLaunchPath =>
        Path.Combine(OS.GetDataDir(), LauncherStorageNames.OfflineLaunch);

    internal static string PendingModeResumePath =>
        Path.Combine(OS.GetDataDir(), LauncherStorageNames.PendingModeResume);

    internal static bool PreviousGameLaunchIncomplete(out string phase)
    {
        phase = null;
        try
        {
            if (!File.Exists(StartupMarkerPath))
                return false;

            var lines = File.ReadAllLines(StartupMarkerPath);
            phase = lines.Length >= 2 ? lines[1].Trim() : null;
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static void SaveManualSafeLaunchMarker()
    {
        try
        {
            File.WriteAllText(ManualSafeLaunchPath, $"{DateTime.UtcNow:O}\n");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Launcher] Failed to write manual safe launch marker: {ex.Message}");
        }
    }

    internal static bool ConsumeManualSafeLaunchMarker()
    {
        try
        {
            if (!File.Exists(ManualSafeLaunchPath))
                return false;

            File.Delete(ManualSafeLaunchPath);
            PatchHelper.Log("Manual safe launch marker consumed");
            return true;
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"Failed to consume manual safe launch marker: {ex.Message}");
            return true;
        }
    }

    internal static void SaveOfflineLaunchMarker()
    {
        try
        {
            File.WriteAllText(OfflineLaunchPath, $"{DateTime.UtcNow:O}\n");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Launcher] Failed to write offline launch marker: {ex.Message}");
        }
    }

    internal static bool ConsumeOfflineLaunchMarker()
    {
        try
        {
            if (!File.Exists(OfflineLaunchPath))
                return false;

            File.Delete(OfflineLaunchPath);
            PatchHelper.Log("Offline launch marker consumed");
            return true;
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"Failed to consume offline launch marker: {ex.Message}");
            return true;
        }
    }

    internal static void SavePendingModeResume(LauncherMode mode)
    {
        if (mode == LauncherMode.None)
            return;

        try
        {
            File.WriteAllText(PendingModeResumePath, mode.ToString());
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Launcher] Failed to save pending mode: {ex.Message}");
        }
    }

    internal static bool TryConsumePendingModeResume(out LauncherMode mode)
    {
        mode = LauncherMode.None;
        try
        {
            if (!File.Exists(PendingModeResumePath))
                return false;

            var value = File.ReadAllText(PendingModeResumePath).Trim();
            File.Delete(PendingModeResumePath);
            if (!Enum.TryParse(value, ignoreCase: false, out mode) || mode == LauncherMode.None)
            {
                mode = LauncherMode.None;
                return false;
            }

            PatchHelper.Log($"[Launcher] Resuming {mode} mode after game PCK restart");
            return true;
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Launcher] Failed to consume pending mode: {ex.Message}");
            mode = LauncherMode.None;
            return false;
        }
    }
}
