using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using STS2Mobile.Patches;

namespace STS2Mobile.Launcher;

// Imports files supplied by the user from shared storage. The live installation
// is never touched until a complete staged copy passes structural checks.
internal sealed class OfflineGameImporter
{
    private const string AssembliesDirectory = "data_sts2_windows_x86_64";
    private const string BackupDirectory = "game-backup";
    private const string RequiredGameAssembly = "sts2.dll";
    private const string StagingDirectory = "offline-import-staging";

    internal event Action<ImportProgress> ProgressChanged;

    internal Task ImportAsync(string dataDir)
        => Task.Run(() => Import(dataDir));

    private void Import(string dataDir)
    {
        ValidateSource(out var sourcePck, out var sourceAssemblies);

        var staging = Path.Combine(dataDir, StagingDirectory);
        var destination = LauncherGameFiles.GameDirectoryPath(dataDir);
        var backup = Path.Combine(dataDir, BackupDirectory);

        ResetStaging(staging);
        Directory.CreateDirectory(staging);

        try
        {
            var files = BuildCopyPlan(sourcePck, sourceAssemblies, staging);
            var totalBytes = files.Sum(item => item.Length);
            long copiedBytes = 0;

            foreach (var item in files)
            {
                var parent = Path.GetDirectoryName(item.Destination);
                if (!string.IsNullOrWhiteSpace(parent))
                    Directory.CreateDirectory(parent);

                CopyFile(item.Source, item.Destination, bytes =>
                {
                    copiedBytes += bytes;
                    RaiseProgress(copiedBytes, totalBytes, Path.GetFileName(item.Source));
                });
            }

            ValidateStaging(staging);
            InstallStaging(staging, destination, backup);
            ClearRuntimeCaches(dataDir);
            RaiseProgress(totalBytes, totalBytes, "Import complete");
            PatchHelper.Log($"[Offline] Imported user game files from {AppPaths.ExternalOfflineImportDir}");
        }
        catch
        {
            ResetStaging(staging);
            throw;
        }
    }

    private static void ValidateSource(out string pck, out string assemblies)
    {
        if (!AppPaths.HasStoragePermission())
            throw new InvalidOperationException(
                "Shared-storage access is required. Grant file access, then try again."
            );

        pck = Path.Combine(AppPaths.ExternalOfflineImportDir, LauncherStorageNames.GamePck);
        assemblies = Path.Combine(AppPaths.ExternalOfflineImportDir, AssembliesDirectory);

        if (!LauncherGameFiles.IsValidPck(pck))
            throw new InvalidDataException(
                $"A valid {LauncherStorageNames.GamePck} was not found in {AppPaths.ExternalOfflineImportDir}."
            );
        if (!File.Exists(Path.Combine(assemblies, RequiredGameAssembly)))
            throw new InvalidDataException(
                $"{AssembliesDirectory} is missing or does not contain {RequiredGameAssembly}."
            );
    }

    private static List<CopyItem> BuildCopyPlan(
        string sourcePck,
        string sourceAssemblies,
        string staging
    )
    {
        var result = new List<CopyItem>
        {
            new(
                sourcePck,
                Path.Combine(staging, LauncherStorageNames.GamePck),
                new FileInfo(sourcePck).Length
            ),
        };

        var sourceRoot = Path.GetFullPath(sourceAssemblies) + Path.DirectorySeparatorChar;
        foreach (var source in Directory.EnumerateFiles(sourceAssemblies, "*", SearchOption.AllDirectories))
        {
            var fullSource = Path.GetFullPath(source);
            if (!fullSource.StartsWith(sourceRoot, StringComparison.Ordinal))
                throw new InvalidDataException("Offline source contained a path outside its data directory.");

            var relative = Path.GetRelativePath(sourceAssemblies, fullSource);
            var destination = Path.Combine(staging, AssembliesDirectory, relative);
            result.Add(new CopyItem(fullSource, destination, new FileInfo(fullSource).Length));
        }

        return result;
    }

    private static void CopyFile(string source, string destination, Action<int> onCopied)
    {
        const int bufferSize = 1024 * 1024;
        var buffer = new byte[bufferSize];
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize);
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize);

        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            output.Write(buffer, 0, read);
            onCopied(read);
        }
        output.Flush(flushToDisk: true);
    }

    private static void ValidateStaging(string staging)
    {
        var pck = Path.Combine(staging, LauncherStorageNames.GamePck);
        var assembly = Path.Combine(staging, AssembliesDirectory, RequiredGameAssembly);
        if (!LauncherGameFiles.IsValidPck(pck) || !File.Exists(assembly))
            throw new InvalidDataException("The staged offline installation failed validation.");
    }

    private static void InstallStaging(string staging, string destination, string backup)
    {
        if (Directory.Exists(backup))
            Directory.Delete(backup, recursive: true);
        if (Directory.Exists(destination))
            Directory.Move(destination, backup);

        try
        {
            Directory.Move(staging, destination);
        }
        catch
        {
            if (!Directory.Exists(destination) && Directory.Exists(backup))
                Directory.Move(backup, destination);
            throw;
        }
    }

    private static void ResetStaging(string staging)
    {
        if (Directory.Exists(staging))
            Directory.Delete(staging, recursive: true);
    }

    private static void ClearRuntimeCaches(string dataDir)
    {
        try
        {
            var publish = Path.Combine(
                dataDir,
                LauncherStorageNames.GodotDirectory,
                LauncherStorageNames.MonoDirectory,
                LauncherStorageNames.PublishDirectory
            );
            if (Directory.Exists(publish))
                Directory.Delete(publish, recursive: true);

            var warmupMarker = Path.Combine(dataDir, LauncherStorageNames.ShaderWarmupVersion);
            if (File.Exists(warmupMarker))
                File.Delete(warmupMarker);
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Offline] Runtime cache cleanup will retry on launch: {ex.Message}");
        }
    }

    private void RaiseProgress(long copied, long total, string file)
    {
        try
        {
            ProgressChanged?.Invoke(new ImportProgress(copied, total, file));
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Offline] Progress callback failed: {ex.Message}");
        }
    }

    private readonly record struct CopyItem(string Source, string Destination, long Length);

    internal readonly record struct ImportProgress(long CopiedBytes, long TotalBytes, string CurrentFile)
    {
        internal double Percentage => TotalBytes <= 0 ? 0 : CopiedBytes * 100.0 / TotalBytes;
    }
}
