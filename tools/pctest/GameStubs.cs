using System;
using System.Threading.Tasks;

// Compile-time stand-ins for the game types the Steam save layer implements.
// The real definitions live in the game's sts2.dll (GameReferenceDir build);
// this harness only needs the shapes so the launcher layer type-checks on a
// machine without game files. Members throw: the harness never executes them.
namespace MegaCrit.Sts2.Core.Saves
{
    internal interface ISaveStore
    {
        string ReadFile(string path);
        Task<string> ReadFileAsync(string path);
        void WriteFile(string path, string content);
        void WriteFile(string path, byte[] bytes);
        Task WriteFileAsync(string path, string content);
        Task WriteFileAsync(string path, byte[] bytes);
        bool FileExists(string path);
        bool DirectoryExists(string path);
        void DeleteFile(string path);
        void RenameFile(string sourcePath, string destinationPath);
        string[] GetFilesInDirectory(string directoryPath);
        string[] GetDirectoriesInDirectory(string directoryPath);
        void CreateDirectory(string directoryPath);
        void DeleteDirectory(string directoryPath);
        void DeleteTemporaryFiles(string directoryPath);
        DateTimeOffset GetLastModifiedTime(string path);
        int GetFileSize(string path);
        void SetLastModifiedTime(string path, DateTimeOffset time);
        string GetFullPath(string filename);
    }

    internal interface ICloudSaveStore : ISaveStore
    {
        void BeginSaveBatch();
        void EndSaveBatch();
    }

    internal static class UserDataPathProvider
    {
        internal static bool IsRunningModded { get; set; }

        internal static string GetAccountScopedBasePath(string accountId)
            => throw new NotSupportedException("pctest stub");
    }

    internal sealed class GodotFileIo : ISaveStore
    {
        public GodotFileIo(string basePath)
        {
        }

        private static Exception Stub() => new NotSupportedException("pctest stub");

        public string ReadFile(string path) => throw Stub();
        public Task<string> ReadFileAsync(string path) => throw Stub();
        public void WriteFile(string path, string content) => throw Stub();
        public void WriteFile(string path, byte[] bytes) => throw Stub();
        public Task WriteFileAsync(string path, string content) => throw Stub();
        public Task WriteFileAsync(string path, byte[] bytes) => throw Stub();
        public bool FileExists(string path) => throw Stub();
        public bool DirectoryExists(string path) => throw Stub();
        public void DeleteFile(string path) => throw Stub();
        public void RenameFile(string sourcePath, string destinationPath) => throw Stub();
        public string[] GetFilesInDirectory(string directoryPath) => throw Stub();
        public string[] GetDirectoriesInDirectory(string directoryPath) => throw Stub();
        public void CreateDirectory(string directoryPath) => throw Stub();
        public void DeleteDirectory(string directoryPath) => throw Stub();
        public void DeleteTemporaryFiles(string directoryPath) => throw Stub();
        public DateTimeOffset GetLastModifiedTime(string path) => throw Stub();
        public int GetFileSize(string path) => throw Stub();
        public void SetLastModifiedTime(string path, DateTimeOffset time) => throw Stub();
        public string GetFullPath(string filename) => throw Stub();
    }
}

// Namespace anchor: launcher files import STS2Mobile.Patches, whose types live
// in the Harmony patch layer this harness excludes. Concrete patch types that
// the launcher genuinely depends on must be stubbed here explicitly.
namespace STS2Mobile.Patches
{
}

namespace MegaCrit.Sts2.Core.Saves.Managers
{
    internal static class ProgressSaveManager
    {
        internal static string GetProgressPathForProfile(int profileId)
            => throw new NotSupportedException("pctest stub");
    }

    internal static class RunSaveManager
    {
        internal static string GetRunSavePath(int profileId, string fileName)
            => throw new NotSupportedException("pctest stub");
    }

    internal static class PrefsSaveManager
    {
        internal static string GetPrefsPath(int profileId)
            => throw new NotSupportedException("pctest stub");
    }

    internal static class RunHistorySaveManager
    {
        internal static string GetHistoryPath(int profileId)
            => throw new NotSupportedException("pctest stub");
    }
}
