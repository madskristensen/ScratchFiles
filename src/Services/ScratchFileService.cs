using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ScratchFiles.Services
{
    /// <summary>
    /// Manages scratch file creation, enumeration, and lifecycle.
    /// Scratch files live in either a global folder (%APPDATA%) or a solution-local folder (.vs\ScratchFiles).
    /// A file is a scratch file if and only if it resides in one of these folders.
    /// </summary>
    internal static class ScratchFileService
    {
        private const string DefaultPrefix = "scratch";
        private const string ScratchExtension = ".scratch";
        private const string SolutionSubFolder = @".vs\ScratchFiles";

        private static readonly Regex _numberPattern = new Regex(@"^(?<prefix>.+?)(?<num>\d+)\..*$", RegexOptions.Compiled);

        /// <summary>
        /// Returns the global scratch folder path, creating it if necessary.
        /// </summary>
        public static string GetGlobalScratchFolder()
        {
            string folder = GetGlobalScratchFolderPath();
            Directory.CreateDirectory(folder);
            return folder;
        }

        /// <summary>
        /// Returns the global scratch folder path without creating it.
        /// </summary>
        public static string GetGlobalScratchFolderPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ScratchFiles");
        }

        /// <summary>
        /// Returns the solution scratch folder path, or null if no solution is open.
        /// Creates the folder if it does not exist.
        /// </summary>
        public static string GetSolutionScratchFolder()
        {
            string folder = GetSolutionScratchFolderPath();

            if (folder == null)
            {
                return null;
            }

            Directory.CreateDirectory(folder);
            return folder;
        }

        /// <summary>
        /// Returns the solution scratch folder path without creating it, or null if no solution is open.
        /// </summary>
        public static string GetSolutionScratchFolderPath()
        {
            string solutionDir = GetSolutionDirectory();

            if (string.IsNullOrWhiteSpace(solutionDir))
            {
                return null;
            }

            return Path.Combine(solutionDir, SolutionSubFolder);
        }

        /// <summary>
        /// Determines whether the given file path resides in a scratch folder.
        /// </summary>
        public static bool IsScratchFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return false;
            }

            string fullPath = Path.GetFullPath(filePath);
            string globalFolder = GetGlobalScratchFolderPath();

            if (fullPath.StartsWith(globalFolder, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string solutionFolder = GetSolutionScratchFolderPath();

            return solutionFolder != null
                && fullPath.StartsWith(solutionFolder, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Creates a new scratch file in the specified scope and returns its full path.
        /// The file is created with an auto-incremented number (e.g., scratch1.scratch, scratch2.scratch).
        /// </summary>
        public static string CreateScratchFile(ScratchScope scope)
        {
            string folder = scope == ScratchScope.Solution
                ? GetSolutionScratchFolder() ?? GetGlobalScratchFolder()
                : GetGlobalScratchFolder();

            string prefix = DefaultPrefix;
            int nextNumber = GetNextNumber(folder, prefix);
            string fileName = $"{prefix}{nextNumber}{ScratchExtension}";
            string filePath = Path.Combine(folder, fileName);

            File.WriteAllText(filePath, string.Empty);
            return filePath;
        }

        /// <summary>
        /// Creates a new scratch file with initial content and returns its full path.
        /// </summary>
        public static string CreateScratchFileWithContent(ScratchScope scope, string content)
        {
            string filePath = CreateScratchFile(scope);
            File.WriteAllText(filePath, content ?? string.Empty);
            return filePath;
        }

        /// <summary>
        /// Returns all scratch files from both global and solution folders.
        /// </summary>
        public static IReadOnlyList<ScratchFileInfo> GetAllScratchFiles()
        {
            var files = new List<ScratchFileInfo>();

            files.AddRange(GetFilesFromFolder(GetGlobalScratchFolder(), ScratchScope.Global));

            string solutionFolder = GetSolutionScratchFolder();

            if (solutionFolder != null && Directory.Exists(solutionFolder))
            {
                files.AddRange(GetFilesFromFolder(solutionFolder, ScratchScope.Solution));
            }

            return files;
        }

        /// <summary>
        /// Deletes a scratch file if it exists and resides in a scratch folder.
        /// </summary>
        public static bool DeleteScratchFile(string filePath)
        {
            if (!IsScratchFile(filePath) || !File.Exists(filePath))
            {
                return false;
            }

            File.Delete(filePath);
            return true;
        }

        /// <summary>
        /// Deletes all empty scratch files (zero-length) from both folders.
        /// Returns the number of files deleted.
        /// </summary>
        public static int DeleteEmptyScratchFiles()
        {
            int count = 0;

            foreach (ScratchFileInfo info in GetAllScratchFiles())
            {
                var fileInfo = new FileInfo(info.FilePath);

                if (fileInfo.Exists && fileInfo.Length == 0)
                {
                    fileInfo.Delete();
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Renames a scratch file, keeping it in the same folder.
        /// Returns the new full path, or null if the rename failed.
        /// </summary>
        public static string RenameScratchFile(string oldPath, string newName)
        {
            if (!IsScratchFile(oldPath) || !File.Exists(oldPath))
            {
                return null;
            }

            string directory = Path.GetDirectoryName(oldPath);
            string newPath = Path.Combine(directory, newName);

            if (File.Exists(newPath))
            {
                return null;
            }

            File.Move(oldPath, newPath);
            return newPath;
        }

        /// <summary>
        /// Changes the file extension of a scratch file on disk.
        /// Returns the new full path.
        /// </summary>
        public static string ChangeExtension(string filePath, string newExtension)
        {
            if (!IsScratchFile(filePath) || !File.Exists(filePath))
            {
                return null;
            }

            if (!newExtension.StartsWith(".", StringComparison.Ordinal))
            {
                newExtension = "." + newExtension;
            }

            string directory = Path.GetDirectoryName(filePath);
            string nameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
            string newPath = Path.Combine(directory, nameWithoutExt + newExtension);

            if (string.Equals(filePath, newPath, StringComparison.OrdinalIgnoreCase))
            {
                return filePath;
            }

            File.Move(filePath, newPath);
            return newPath;
        }

        /// <summary>
        /// Determines which scope a scratch file belongs to based on its path.
        /// </summary>
        public static ScratchScope GetScope(string filePath)
        {
            string solutionFolder = GetSolutionScratchFolderPath();

            if (solutionFolder != null
                && Path.GetFullPath(filePath).StartsWith(solutionFolder, StringComparison.OrdinalIgnoreCase))
            {
                return ScratchScope.Solution;
            }

            return ScratchScope.Global;
        }

        private static int GetNextNumber(string folder, string prefix)
        {
            int max = 0;

            if (!Directory.Exists(folder))
            {
                return 1;
            }

            foreach (string file in Directory.GetFiles(folder))
            {
                string name = Path.GetFileName(file);
                Match match = _numberPattern.Match(name);

                if (match.Success
                    && match.Groups["prefix"].Value.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(match.Groups["num"].Value, out int num))
                {
                    max = Math.Max(max, num);
                }
            }

            return max + 1;
        }

        private static IEnumerable<ScratchFileInfo> GetFilesFromFolder(string folder, ScratchScope scope)
        {
            if (!Directory.Exists(folder))
            {
                yield break;
            }

            foreach (string file in Directory.GetFiles(folder).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                yield return new ScratchFileInfo(file, scope);
            }
        }

        private static string GetSolutionDirectory()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var solution = VS.GetRequiredService<Microsoft.VisualStudio.Shell.Interop.SVsSolution,
                    Microsoft.VisualStudio.Shell.Interop.IVsSolution>();

                solution.GetSolutionInfo(out string solutionDir, out _, out _);
                return solutionDir;
            }
            catch (Exception ex)
            {
                ex.Log();
                return null;
            }
        }
    }

    internal enum ScratchScope
    {
        Global,
        Solution
    }

    internal sealed class ScratchFileInfo
    {
        public ScratchFileInfo(string filePath, ScratchScope scope)
        {
            FilePath = filePath;
            Scope = scope;
        }

        public string FilePath { get; }
        public ScratchScope Scope { get; }
        public string FileName => Path.GetFileName(FilePath);
        public string Extension => Path.GetExtension(FilePath);
        public DateTime LastModified => File.GetLastWriteTime(FilePath);
    }
}
