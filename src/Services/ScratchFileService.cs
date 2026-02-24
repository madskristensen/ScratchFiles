using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

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
        private const string ShadowExtension = ".unsaved";
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

        #region Shadow File Helpers

        /// <summary>
        /// Returns the shadow file path for a scratch file (used for session persistence).
        /// </summary>
        public static string GetShadowPath(string scratchFilePath)
        {
            return scratchFilePath + ShadowExtension;
        }

        /// <summary>
        /// Returns the original scratch file path from a shadow file path.
        /// </summary>
        public static string GetOriginalPathFromShadow(string shadowFilePath)
        {
            if (!IsShadowFile(shadowFilePath))
            {
                return shadowFilePath;
            }

            return shadowFilePath.Substring(0, shadowFilePath.Length - ShadowExtension.Length);
        }

        /// <summary>
        /// Determines whether the given file path is a shadow file (.unsaved).
        /// </summary>
        public static bool IsShadowFile(string filePath)
        {
            return filePath?.EndsWith(ShadowExtension, StringComparison.OrdinalIgnoreCase) == true;
        }

        /// <summary>
        /// Gets the display name for a scratch file (strips .unsaved extension if present).
        /// </summary>
        public static string GetDisplayName(string filePath)
        {
            string fileName = Path.GetFileName(filePath);

            if (IsShadowFile(filePath))
            {
                return fileName.Substring(0, fileName.Length - ShadowExtension.Length);
            }

            return fileName;
        }

        /// <summary>
        /// Returns all pending shadow files from the global scratch folder.
        /// These represent unsaved scratch files from a previous session.
        /// </summary>
        public static IReadOnlyList<string> GetPendingShadowFiles()
        {
            string globalFolder = GetGlobalScratchFolderPath();

            if (!Directory.Exists(globalFolder))
            {
                return Array.Empty<string>();
            }

            return Directory.GetFiles(globalFolder, "*" + ShadowExtension, SearchOption.AllDirectories);
        }

        /// <summary>
        /// Writes content to a shadow file for session persistence.
        /// </summary>
        public static void WriteShadowFile(string scratchFilePath, string content)
        {
            string shadowPath = GetShadowPath(scratchFilePath);
            File.WriteAllText(shadowPath, content ?? string.Empty);
        }

        /// <summary>
        /// Writes content to a shadow file asynchronously.
        /// </summary>
        public static async Task WriteShadowFileAsync(string scratchFilePath, string content)
        {
            string shadowPath = GetShadowPath(scratchFilePath);
            string directory = Path.GetDirectoryName(shadowPath);

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using (var writer = new StreamWriter(shadowPath, append: false))
            {
                await writer.WriteAsync(content ?? string.Empty);
            }
        }

        /// <summary>
        /// Reads content from a shadow file.
        /// </summary>
        public static string ReadShadowFile(string shadowFilePath)
        {
            if (!File.Exists(shadowFilePath))
            {
                return null;
            }

            return File.ReadAllText(shadowFilePath);
        }

        /// <summary>
        /// Deletes a shadow file if it exists.
        /// </summary>
        public static bool DeleteShadowFile(string scratchFilePath)
        {
            string shadowPath = GetShadowPath(scratchFilePath);

            if (File.Exists(shadowPath))
            {
                File.Delete(shadowPath);
                return true;
            }

            return false;
        }

        #endregion

        #region Session File Tracking

        private const string SessionFileName = ".session.json";

        /// <summary>
        /// Gets the path to the session file that tracks open scratch files.
        /// </summary>
        public static string GetSessionFilePath()
        {
            return Path.Combine(GetGlobalScratchFolderPath(), SessionFileName);
        }

        /// <summary>
        /// Gets the list of scratch files that were open in the previous session.
        /// </summary>
        public static IReadOnlyList<string> GetSessionFiles()
        {
            string sessionPath = GetSessionFilePath();

            if (!File.Exists(sessionPath))
            {
                return Array.Empty<string>();
            }

            try
            {
                string json = File.ReadAllText(sessionPath);
                var session = Newtonsoft.Json.JsonConvert.DeserializeObject<SessionData>(json);
                return session?.OpenFiles ?? Array.Empty<string>();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// Saves the list of open scratch files to the session file.
        /// </summary>
        public static void SaveSessionFiles(IEnumerable<string> openFiles)
        {
            string sessionPath = GetSessionFilePath();
            string directory = Path.GetDirectoryName(sessionPath);

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var session = new SessionData { OpenFiles = openFiles.ToArray() };
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(session, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(sessionPath, json);
        }

        /// <summary>
        /// Adds a scratch file to the session tracking.
        /// </summary>
        public static void AddToSession(string filePath)
        {
            var currentFiles = new HashSet<string>(GetSessionFiles(), StringComparer.OrdinalIgnoreCase);

            if (currentFiles.Add(filePath))
            {
                SaveSessionFiles(currentFiles);
            }
        }

        /// <summary>
        /// Removes a scratch file from the session tracking.
        /// </summary>
        public static void RemoveFromSession(string filePath)
        {
            var currentFiles = new HashSet<string>(GetSessionFiles(), StringComparer.OrdinalIgnoreCase);

            if (currentFiles.Remove(filePath))
            {
                SaveSessionFiles(currentFiles);
            }
        }

        /// <summary>
        /// Clears all files from the session tracking.
        /// </summary>
        public static void ClearSession()
        {
            string sessionPath = GetSessionFilePath();

            if (File.Exists(sessionPath))
            {
                File.Delete(sessionPath);
            }
        }

        private class SessionData
        {
            public string[] OpenFiles { get; set; } = Array.Empty<string>();
        }

        #endregion

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

            foreach (string subDir in Directory.GetDirectories(folder).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                foreach (ScratchFileInfo file in GetFilesFromFolder(subDir, scope))
                {
                    yield return file;
                }
            }
        }

        /// <summary>
        /// Moves a scratch file to a different folder within the scratch roots.
        /// If a file with the same name exists, auto-renames with the next available number.
        /// Returns the new file path, or null if the move failed.
        /// </summary>
        public static string MoveScratchFile(string sourcePath, string destinationFolder)
        {
            if (!IsScratchFile(sourcePath) || !File.Exists(sourcePath))
            {
                return null;
            }

            Directory.CreateDirectory(destinationFolder);

            string fileName = Path.GetFileName(sourcePath);
            string destPath = Path.Combine(destinationFolder, fileName);

            // If file exists, auto-rename with next available number
            if (File.Exists(destPath))
            {
                destPath = GetUniqueDestinationPath(sourcePath, destinationFolder);
            }

            File.Move(sourcePath, destPath);
            return destPath;
        }

        /// <summary>
        /// Generates a unique destination path by incrementing the number in the filename.
        /// </summary>
        private static string GetUniqueDestinationPath(string sourcePath, string destinationFolder)
        {
            string fileName = Path.GetFileName(sourcePath);
            string extension = Path.GetExtension(sourcePath);
            Match match = _numberPattern.Match(fileName);

            string prefix;
            if (match.Success)
            {
                // File has a number pattern (e.g., "scratch1.cs") - use existing prefix
                prefix = match.Groups["prefix"].Value;
            }
            else
            {
                // No number pattern - use filename without extension as prefix
                prefix = Path.GetFileNameWithoutExtension(sourcePath);
            }

            int nextNum = GetNextNumber(destinationFolder, prefix);
            return Path.Combine(destinationFolder, $"{prefix}{nextNum}{extension}");
        }

        /// <summary>
        /// Moves a scratch file to the other scope (Global to Solution or Solution to Global).
        /// Returns the new file path, or null if the move failed or no solution is open.
        /// </summary>
        public static string MoveToScope(string sourcePath, ScratchScope targetScope)
        {
            if (!IsScratchFile(sourcePath) || !File.Exists(sourcePath))
            {
                return null;
            }

            string targetFolder = targetScope == ScratchScope.Solution
                ? GetSolutionScratchFolder()
                : GetGlobalScratchFolder();

            if (targetFolder == null)
            {
                return null;
            }

            return MoveScratchFile(sourcePath, targetFolder);
        }

        /// <summary>
        /// Creates a sub-folder inside a scratch folder. Returns the new folder path.
        /// </summary>
        public static string CreateSubFolder(string parentFolder, string folderName)
        {
            if (parentFolder == null)
            {
                throw new ArgumentNullException(nameof(parentFolder));
            }

            if (folderName == null)
            {
                throw new ArgumentNullException(nameof(folderName));
            }

            string newFolder = Path.Combine(parentFolder, folderName);
            Directory.CreateDirectory(newFolder);
            return newFolder;
        }

        /// <summary>
        /// Deletes a sub-folder and all its contents if it resides inside a scratch root.
        /// Returns true if the folder was deleted.
        /// </summary>
        public static bool DeleteFolder(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            {
                return false;
            }

            // Safety: only delete folders inside a scratch root
            string fullPath = Path.GetFullPath(folderPath);
            string globalRoot = GetGlobalScratchFolderPath();
            string solutionRoot = GetSolutionScratchFolderPath();

            bool insideGlobal = fullPath.StartsWith(globalRoot, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(fullPath, globalRoot, StringComparison.OrdinalIgnoreCase);

            bool insideSolution = solutionRoot != null
                && fullPath.StartsWith(solutionRoot, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(fullPath, solutionRoot, StringComparison.OrdinalIgnoreCase);

            if (!insideGlobal && !insideSolution)
            {
                return false;
            }

            Directory.Delete(folderPath, recursive: true);
            return true;
        }

        /// <summary>
        /// Renames a sub-folder inside a scratch root.
        /// Returns the new folder path, or null if the rename failed.
        /// </summary>
        public static string RenameFolder(string oldPath, string newName)
        {
            if (string.IsNullOrWhiteSpace(oldPath) || !Directory.Exists(oldPath))
            {
                return null;
            }

            string parentDir = Path.GetDirectoryName(oldPath);
            string newPath = Path.Combine(parentDir, newName);

            if (Directory.Exists(newPath))
            {
                return null;
            }

            Directory.Move(oldPath, newPath);
            return newPath;
        }

        /// <summary>
        /// Moves a folder and all its contents to a new parent folder.
        /// Returns the new folder path, or null if the move failed.
        /// </summary>
        public static string MoveFolder(string sourceFolderPath, string destinationParentFolder)
        {
            if (string.IsNullOrWhiteSpace(sourceFolderPath) || !Directory.Exists(sourceFolderPath))
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(destinationParentFolder))
            {
                return null;
            }

            // Safety: only move folders inside a scratch root
            string fullSourcePath = Path.GetFullPath(sourceFolderPath);
            string globalRoot = GetGlobalScratchFolderPath();
            string solutionRoot = GetSolutionScratchFolderPath();

            bool sourceInsideGlobal = fullSourcePath.StartsWith(globalRoot, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(fullSourcePath, globalRoot, StringComparison.OrdinalIgnoreCase);

            bool sourceInsideSolution = solutionRoot != null
                && fullSourcePath.StartsWith(solutionRoot, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(fullSourcePath, solutionRoot, StringComparison.OrdinalIgnoreCase);

            if (!sourceInsideGlobal && !sourceInsideSolution)
            {
                return null;
            }

            // Prevent moving a folder into itself or its descendants
            string fullDestPath = Path.GetFullPath(destinationParentFolder);
            if (fullDestPath.StartsWith(fullSourcePath, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            Directory.CreateDirectory(destinationParentFolder);

            string folderName = Path.GetFileName(sourceFolderPath);
            string newPath = Path.Combine(destinationParentFolder, folderName);

            // Avoid overwriting an existing folder
            if (Directory.Exists(newPath))
            {
                return null;
            }

            Directory.Move(sourceFolderPath, newPath);
            return newPath;
        }

        /// <summary>
        /// Returns the immediate sub-directories of a folder, sorted by name.
        /// </summary>
        public static IReadOnlyList<string> GetSubFolders(string folder)
        {
            if (!Directory.Exists(folder))
            {
                return Array.Empty<string>();
            }

            return Directory.GetDirectories(folder)
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        /// <summary>
        /// Returns the immediate files in a folder (non-recursive), sorted by name.
        /// </summary>
        public static IReadOnlyList<string> GetFilesInFolder(string folder)
        {
            if (!Directory.Exists(folder))
            {
                return Array.Empty<string>();
            }

            return Directory.GetFiles(folder)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();
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

        #region Async File Operations (UI Thread Safe)

        /// <summary>
        /// Creates a new scratch file asynchronously, off the UI thread.
        /// </summary>
        public static Task<string> CreateScratchFileAsync(ScratchScope scope)
        {
            return Task.Run(() => CreateScratchFile(scope));
        }

        /// <summary>
        /// Creates a new scratch file with initial content asynchronously, off the UI thread.
        /// </summary>
        public static Task<string> CreateScratchFileWithContentAsync(ScratchScope scope, string content)
        {
            return Task.Run(() => CreateScratchFileWithContent(scope, content));
        }

        /// <summary>
        /// Deletes a scratch file asynchronously, off the UI thread.
        /// </summary>
        public static Task<bool> DeleteScratchFileAsync(string filePath)
        {
            return Task.Run(() => DeleteScratchFile(filePath));
        }

        /// <summary>
        /// Renames a scratch file asynchronously, off the UI thread.
        /// Returns the new full path, or null if the rename failed.
        /// </summary>
        public static Task<string> RenameScratchFileAsync(string oldPath, string newName)
        {
            return Task.Run(() => RenameScratchFile(oldPath, newName));
        }

        /// <summary>
        /// Changes the file extension asynchronously, off the UI thread.
        /// Returns the new full path.
        /// </summary>
        public static Task<string> ChangeExtensionAsync(string filePath, string newExtension)
        {
            return Task.Run(() => ChangeExtension(filePath, newExtension));
        }

        /// <summary>
        /// Moves a scratch file to a different folder asynchronously, off the UI thread.
        /// Returns the new file path, or null if the move failed.
        /// </summary>
        public static Task<string> MoveScratchFileAsync(string sourcePath, string destinationFolder)
        {
            return Task.Run(() => MoveScratchFile(sourcePath, destinationFolder));
        }

        /// <summary>
        /// Moves a scratch file to the other scope asynchronously, off the UI thread.
        /// Returns the new file path, or null if the move failed.
        /// </summary>
        public static Task<string> MoveToScopeAsync(string sourcePath, ScratchScope targetScope)
        {
            return Task.Run(() => MoveToScope(sourcePath, targetScope));
        }

        /// <summary>
        /// Creates a sub-folder asynchronously, off the UI thread.
        /// </summary>
        public static Task<string> CreateSubFolderAsync(string parentFolder, string folderName)
        {
            return Task.Run(() => CreateSubFolder(parentFolder, folderName));
        }

        /// <summary>
        /// Deletes a folder and all its contents asynchronously, off the UI thread.
        /// </summary>
        public static Task<bool> DeleteFolderAsync(string folderPath)
        {
            return Task.Run(() => DeleteFolder(folderPath));
        }

        /// <summary>
        /// Renames a folder asynchronously, off the UI thread.
        /// Returns the new folder path, or null if the rename failed.
        /// </summary>
        public static Task<string> RenameFolderAsync(string oldPath, string newName)
        {
            return Task.Run(() => RenameFolder(oldPath, newName));
        }

        /// <summary>
        /// Moves a folder asynchronously, off the UI thread.
        /// Returns the new folder path, or null if the move failed.
        /// </summary>
        public static Task<string> MoveFolderAsync(string sourceFolderPath, string destinationParentFolder)
        {
            return Task.Run(() => MoveFolder(sourceFolderPath, destinationParentFolder));
        }

        #endregion
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
