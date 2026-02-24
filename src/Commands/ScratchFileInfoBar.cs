using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell.Interop;

using ScratchFiles.Services;
using ScratchFiles.ToolWindows;

using System.Collections.Generic;
using System.IO;

namespace ScratchFiles.Commands
{
    /// <summary>
    /// Manages the InfoBar shown at the top of scratch file editor windows.
    /// Provides language selection and Save As actions.
    /// The InfoBar is only shown for files residing in scratch folders (location is identity).
    /// </summary>
    internal sealed class ScratchFileInfoBar
    {
        private static readonly Dictionary<string, ScratchFileInfoBar> _activeBars =
            new Dictionary<string, ScratchFileInfoBar>(StringComparer.OrdinalIgnoreCase);

        private readonly DocumentView _docView;
        private readonly string _filePath;
        private InfoBar _infoBar;
        private bool _isDetached;

        private ScratchFileInfoBar(DocumentView docView, string filePath)
        {
            _docView = docView;
            _filePath = filePath;
        }

        /// <summary>
        /// Attaches an InfoBar to the given document view if the file is a scratch file.
        /// </summary>
        public static async Task AttachAsync(DocumentView docView)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            string filePath = docView?.Document?.FilePath;

            if (string.IsNullOrEmpty(filePath) || !ScratchFileService.IsScratchFile(filePath))
            {
                return;
            }

            // Don't attach a duplicate
            if (_activeBars.ContainsKey(filePath))
            {
                return;
            }

            var handler = new ScratchFileInfoBar(docView, filePath);
            await handler.ShowAsync();
            _activeBars[filePath] = handler;
        }

        /// <summary>
        /// Removes the InfoBar for the given file path if one exists.
        /// </summary>
        public static void Detach(string filePath)
        {
            if (filePath != null && _activeBars.TryGetValue(filePath, out ScratchFileInfoBar handler))
            {
                handler._isDetached = true;
                handler._infoBar?.Close();
                _activeBars.Remove(filePath);
            }
        }

        /// <summary>
        /// Updates the tracking key when a scratch file is renamed/moved on disk.
        /// </summary>
        public static void UpdatePath(string oldPath, string newPath)
        {
            if (oldPath != null && _activeBars.TryGetValue(oldPath, out ScratchFileInfoBar handler))
            {
                _activeBars.Remove(oldPath);

                if (ScratchFileService.IsScratchFile(newPath))
                {
                    _activeBars[newPath] = handler;
                }
                else
                {
                    handler._isDetached = true;
                    handler._infoBar?.Close();
                }
            }
        }

        /// <summary>
        /// Checks if the given file has an active InfoBar.
        /// </summary>
        public static bool HasInfoBar(string filePath)
        {
            return filePath != null && _activeBars.ContainsKey(filePath);
        }

        /// <summary>
        /// Closes all active InfoBars and clears the tracking dictionary.
        /// Call on solution close or package dispose to prevent memory leaks.
        /// </summary>
        public static void ClearAll()
        {
            foreach (ScratchFileInfoBar handler in _activeBars.Values)
            {
                handler._isDetached = true;
                handler._infoBar?.Close();
            }

            _activeBars.Clear();
        }

        private async Task ShowAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            string currentExt = Path.GetExtension(_filePath);
            string langDisplay = GetLanguageDisplayName(currentExt);
            ScratchScope currentScope = ScratchFileService.GetScope(_filePath);
            string scopeDisplay = currentScope == ScratchScope.Solution ? "Solution" : "Global";

            // Build action items - include scope change only if a solution is open
            var actions = new List<IVsInfoBarActionItem>
            {
                new InfoBarHyperlink("Change Language", "change_language"),
                new InfoBarHyperlink("Save As...", "save_as"),
            };

            // Only show scope change option if a solution is open
            if (ScratchFileService.GetSolutionScratchFolderPath() != null)
            {
                string scopeAction = currentScope == ScratchScope.Solution
                    ? "Move to Global"
                    : "Move to Solution";
                actions.Add(new InfoBarHyperlink(scopeAction, "change_scope"));
            }

            var model = new InfoBarModel(
                new IVsInfoBarTextSpan[]
                {
                    new InfoBarTextSpan($"Scratch File ({scopeDisplay}) — Language: "),
                    new InfoBarTextSpan(langDisplay, true),
                },
                actions.ToArray(),
                KnownMonikers.Log,
                isCloseButtonVisible: true);

            _infoBar = await VS.InfoBar.CreateAsync(_docView.WindowFrame, model);

            if (_infoBar != null)
            {
                _infoBar.ActionItemClicked += OnActionItemClicked;
                await _infoBar.TryShowInfoBarUIAsync();
            }
        }

        private void OnActionItemClicked(object sender, InfoBarActionItemEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_isDetached)
            {
                return;
            }

            string context = e.ActionItem.ActionContext as string;

            switch (context)
            {
                case "change_language":
                    ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                    {
                        try
                        {
                            await ChangeLanguageAsync();
                        }
                        catch (Exception ex)
                        {
                            await ex.LogAsync();
                        }
                    }).FireAndForget();
                    break;

                case "save_as":
                    ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                    {
                        try
                        {
                            await SaveAsAsync();
                        }
                        catch (Exception ex)
                        {
                            await ex.LogAsync();
                        }
                    }).FireAndForget();
                    break;

                case "change_scope":
                    ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                    {
                        try
                        {
                            await ChangeScopeAsync();
                        }
                        catch (Exception ex)
                        {
                            await ex.LogAsync();
                        }
                    }).FireAndForget();
                    break;
            }
        }

        private async Task ChangeLanguageAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            string extension = UI.LanguagePickerDialog.Show();

            if (string.IsNullOrEmpty(extension))
            {
                return;
            }

            string oldPath = _docView.Document.FilePath;

            // Save unsaved edits to disk before renaming
            _docView.Document.Save();

            string newPath = await ScratchFileService.ChangeExtensionAsync(oldPath, extension);

            if (newPath != null && !string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
            {
                Detach(oldPath);

                // Close the old document tab (file was already saved and renamed on disk)
                if (_docView.WindowFrame != null)
                {
                    await _docView.WindowFrame.CloseFrameAsync(FrameCloseOption.NoSave);
                }

                await VS.Documents.OpenAsync(newPath);

                DocumentView newDocView = await VS.Documents.GetDocumentViewAsync(newPath);

                if (newDocView != null)
                {
                    await AttachAsync(newDocView);
                }

                ScratchFilesToolWindowControl.RefreshAll();
            }
        }

        private async Task ChangeScopeAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            string oldPath = _docView.Document.FilePath;
            ScratchScope currentScope = ScratchFileService.GetScope(oldPath);
            ScratchScope targetScope = currentScope == ScratchScope.Solution
                ? ScratchScope.Global
                : ScratchScope.Solution;

            // Save unsaved edits to disk before moving
            _docView.Document.Save();

            string newPath = await ScratchFileService.MoveToScopeAsync(oldPath, targetScope);

            if (newPath != null)
            {
                Detach(oldPath);

                // Close the old document tab (file was already saved and moved on disk)
                if (_docView.WindowFrame != null)
                {
                    await _docView.WindowFrame.CloseFrameAsync(FrameCloseOption.NoSave);
                }

                await VS.Documents.OpenAsync(newPath);

                DocumentView newDocView = await VS.Documents.GetDocumentViewAsync(newPath);

                if (newDocView != null)
                {
                    await AttachAsync(newDocView);
                }

                ScratchFilesToolWindowControl.RefreshAll();
            }
        }

        private async Task SaveAsAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            string currentPath = _docView.Document?.FilePath;

            if (string.IsNullOrEmpty(currentPath))
            {
                return;
            }

            string currentExtension = Path.GetExtension(currentPath);
            string fileName = Path.GetFileName(currentPath);

            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                FileName = fileName,
                DefaultExt = currentExtension,
                Filter = $"Current type (*{currentExtension})|*{currentExtension}|All files (*.*)|*.*"
            };

            bool? dialogResult = saveDialog.ShowDialog();

            if (dialogResult != true)
            {
                return;
            }

            string newPath = saveDialog.FileName;

            // Read content from the editor buffer (not disk) to preserve unsaved edits
            string content = _docView.TextBuffer?.CurrentSnapshot?.GetText();

            if (content == null)
            {
                // Fallback to disk if buffer is unavailable - do on background thread
                content = await Task.Run(() => File.ReadAllText(currentPath));
            }

            // Write to new file on background thread
            await Task.Run(() => File.WriteAllText(newPath, content));

            // Open the new file in VS
            await VS.Documents.OpenAsync(newPath);

            // Detach InfoBar and clean up the old scratch file
            Detach(_filePath);

            if (!string.Equals(_filePath, newPath, StringComparison.OrdinalIgnoreCase))
            {
                await ScratchFileService.DeleteScratchFileAsync(_filePath);
            }

            ToolWindows.ScratchFilesToolWindowControl.RefreshAll();
        }

        /// <summary>
        /// Attempts to auto-detect the language from the document content and update the InfoBar.
        /// </summary>
        internal static async Task TryAutoDetectAsync(string filePath)
        {
            // Only auto-detect for .scratch files (user hasn't chosen a language yet)
            if (!string.Equals(Path.GetExtension(filePath), ".scratch", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string content = null;

            try
            {
                content = File.ReadAllText(filePath);
            }
            catch
            {
                return;
            }

            LanguageDetectionResult result = LanguageDetectionService.Detect(content);

            if (result == null)
            {
                return;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // IsScratchFile requires the UI thread (solution directory lookup)
            if (!ScratchFileService.IsScratchFile(filePath))
            {
                return;
            }

            // Show a suggestion InfoBar
            if (_activeBars.TryGetValue(filePath, out ScratchFileInfoBar handler) && handler._infoBar != null)
            {
                // Close the existing InfoBar and show one with the detection suggestion
                handler._infoBar.Close();

                DocumentView docView = await VS.Documents.GetDocumentViewAsync(filePath);

                if (docView == null)
                {
                    return;
                }

                var model = new InfoBarModel(
                    new IVsInfoBarTextSpan[]
                    {
                        new InfoBarTextSpan("Detected: "),
                        new InfoBarTextSpan(result.LanguageName, true),
                    },
                    new IVsInfoBarActionItem[]
                    {
                        new InfoBarHyperlink("Apply", $"apply_{result.Extension}"),
                        new InfoBarHyperlink("Dismiss", "dismiss"),
                        new InfoBarHyperlink("Change Language", "change_language"),
                        new InfoBarHyperlink("Save As...", "save_as"),
                    },
                    KnownMonikers.StatusInformation,
                    isCloseButtonVisible: true);

                handler._infoBar = await VS.InfoBar.CreateAsync(docView.WindowFrame, model);

                if (handler._infoBar != null)
                {
                    handler._infoBar.ActionItemClicked += handler.OnDetectionActionClicked;
                    await handler._infoBar.TryShowInfoBarUIAsync();
                }
            }
        }

        private void OnDetectionActionClicked(object sender, InfoBarActionItemEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_isDetached)
            {
                return;
            }

            string context = e.ActionItem.ActionContext as string;

            if (context == "dismiss")
            {
                // Re-show the standard InfoBar
                ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                {
                    _infoBar?.Close();
                    await ShowAsync();
                }).FireAndForget();
            }
            else if (context?.StartsWith("apply_", StringComparison.Ordinal) == true)
            {
                string extension = context.Substring("apply_".Length);

                ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                {
                    try
                    {
                        await ApplyDetectedLanguageAsync(extension);
                    }
                    catch (Exception ex)
                    {
                        await ex.LogAsync();
                    }
                }).FireAndForget();
            }
            else if (context == "change_language")
            {
                ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                {
                    try
                    {
                        await ChangeLanguageAsync();
                    }
                    catch (Exception ex)
                    {
                        await ex.LogAsync();
                    }
                }).FireAndForget();
            }
            else if (context == "save_as")
            {
                ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                {
                    try
                    {
                        await SaveAsAsync();
                    }
                    catch (Exception ex)
                    {
                        await ex.LogAsync();
                    }
                }).FireAndForget();
            }
        }

        private async Task ApplyDetectedLanguageAsync(string extension)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            string oldPath = _docView.Document?.FilePath ?? _filePath;

            // Save unsaved edits to disk before renaming
            _docView.Document?.Save();

            string newPath = await ScratchFileService.ChangeExtensionAsync(oldPath, extension);

            if (newPath != null && !string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
            {
                Detach(oldPath);

                // Close the old document tab (file was already saved and renamed on disk)
                if (_docView.WindowFrame != null)
                {
                    await _docView.WindowFrame.CloseFrameAsync(FrameCloseOption.NoSave);
                }

                await VS.Documents.OpenAsync(newPath);

                ScratchFilesToolWindowControl.RefreshAll();
            }
        }

        private static string GetLanguageDisplayName(string extension)
        {
            if (string.Equals(extension, ".scratch", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrEmpty(extension))
            {
                return "Plain Text";
            }

            IReadOnlyList<LanguageOption> languages = LanguageDetectionService.GetAvailableLanguages();

            foreach (LanguageOption lang in languages)
            {
                if (string.Equals(lang.Extension, extension, StringComparison.OrdinalIgnoreCase))
                {
                    return lang.DisplayName;
                }
            }

            return extension.TrimStart('.').ToUpperInvariant();
        }
    }
}
