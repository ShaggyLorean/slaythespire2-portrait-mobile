using System;
using System.IO;
using Godot;

namespace STS2Mobile.Launcher;

// Ported from upstream: records what GPU and renderer this device actually
// booted with, as evidence for the Java-side renderer policy on the NEXT
// start. PowerVR devices get OpenGL pinned outright, because upstream's field
// reports show Vulkan touch input is unreliable there. Kept self-contained:
// instead of upstream's preference enum plumbing, the policy's own
// renderer_mode file is written directly.
internal static class LauncherGraphicsDeviceEvidence
{
    private const string RendererModeFile = "renderer_mode";

    internal static bool CaptureAndApplyCompatibility(string dataDir)
    {
        if (!OperatingSystem.IsAndroid())
            return false;

        try
        {
            var adapterName = Sanitize(RenderingServer.GetVideoAdapterName());
            var adapterVendor = Sanitize(RenderingServer.GetVideoAdapterVendor());
            var renderingDriver = Sanitize(RenderingServer.GetCurrentRenderingDriverName());
            var renderingMethod = Sanitize(RenderingServer.GetCurrentRenderingMethod());
            var powerVr = IsPowerVr(adapterName, adapterVendor);
            var text =
                "StS2 Android graphics device\n"
                + $"UTC: {DateTime.UtcNow:O}\n"
                + $"Adapter name: {adapterName}\n"
                + $"Adapter vendor: {adapterVendor}\n"
                + $"Rendering driver: {renderingDriver}\n"
                + $"Rendering method: {renderingMethod}\n"
                + $"PowerVR compatibility required: {powerVr}\n";
            Directory.CreateDirectory(dataDir);
            File.WriteAllText(Path.Combine(dataDir, LauncherStorageNames.GraphicsDevice), text);
            PatchHelper.Log(
                $"Android graphics device recorded: adapter={adapterName} vendor={adapterVendor} "
                    + $"driver={renderingDriver} method={renderingMethod} powerVr={powerVr}"
            );

            if (powerVr)
            {
                File.WriteAllText(Path.Combine(dataDir, RendererModeFile), "opengl");
                PatchHelper.Log(
                    "PowerVR detected: renderer pinned to OpenGL (upstream evidence shows Vulkan touch input is unreliable there)."
                );
            }

            return powerVr;
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"Android graphics device evidence unavailable: {ex.Message}");
            return false;
        }
    }

    internal static bool IsPowerVr(string adapterName, string adapterVendor)
    {
        var evidence = $"{adapterName}\n{adapterVendor}";
        return evidence.Contains("PowerVR", StringComparison.OrdinalIgnoreCase)
            || evidence.Contains("ImgTec", StringComparison.OrdinalIgnoreCase)
            || evidence.Contains("Imagination Technologies", StringComparison.OrdinalIgnoreCase);
    }

    private static string Sanitize(string value)
        => string.IsNullOrWhiteSpace(value)
            ? "<unknown>"
            : value.Replace('\r', ' ').Replace('\n', ' ').Trim();
}
