using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell.Interop;

using ScratchFiles.Services;
using ScratchFiles.ToolWindows;

using System.Collections.Generic;
using System.IO;

namespace ScratchFiles.Commands
{
    /// <summary>
    /// Manages the InfoBar shown at the top of scratch file editor windows. Provides language selection and Save As
    /// actions. The InfoBar is only shown for files residing in scratch folders (location is identity).
    /// </summary>
    internal sealed class ScratchFileInfoBar
    {
        private readonly DocumentView _docView;
        private readonly string _filePath;
        private InfoBar _infoBar;

        private ScratchFileInfoBar(DocumentView docView, string filePath)
        {
            _docView = docView;
            _filePath = filePath;
        }

        /// <summary>
        /// Attaches an InfoBar to the given document view if the file is a scratch file.
        /// </summary>
        /// <param name="docView">The document view to attach the InfoBar to.</param>
        /// <param name="filePath">
        /// Optional file path. If not provided, extracted from docView.Document.FilePath. Passing this explicitly
        /// handles cases where Document is not yet initialized (e.g., .cs files with Roslyn).
        /// </param>
        public static async Task AttachAsync(DocumentView docView, string filePath = null)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // Use provided filePath, or fall back to extracting from document
            filePath = filePath ?? docView?.Document?.FilePath;

            if (string.IsNullOrEmpty(filePath) || !ScratchFileService.IsScratchFile(filePath))
            {
                return;
            }

            // Ensure we have a valid WindowFrame to attach the InfoBar to
            if (docView?.WindowFrame == null)
            {
                return;
            }

            var handler = new ScratchFileInfoBar(docView, filePath);
            await handler.ShowAsync();
        }

        private async Task ShowAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            string currentExt = Path.GetExtension(_filePath);
            string langDisplay = GetLanguageDisplayName(currentExt);
            string solutionScratchFolder = ScratchFileService.GetSolutionScratchFolderPath();
            ScratchScope currentScope = ScratchFileService.GetScope(_filePath, solutionScratchFolder);
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
                    ? "Move to Global scope"
                    : "Move to Solution scope";
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

        /// <summary>
        /// Cleans up event handlers and closes the InfoBar.
        /// </summary>
        private void CloseAndCleanup()
        {
            if (_infoBar != null)
            {
                _infoBar.ActionItemClicked -= OnActionItemClicked;
                _infoBar.Close();
                _infoBar = null;
            }
        }

        private void OnActionItemClicked(object sender, InfoBarActionItemEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            string context = e.ActionItem.ActionContext as string;

            switch (context)
            {
                case "change_language":
                    ChangeLanguageAsync().FireAndForget();
                    break;

                case "save_as":
                    SaveAsAsync().FireAndForget();
                    break;

                case "change_scope":
                    ChangeScopeAsync().FireAndForget();
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
                // Clean up event handlers before closing
                CloseAndCleanup();

                // Close the old document tab (file was already saved and renamed on disk)
                if (_docView.WindowFrame != null)
                {
                    await _docView.WindowFrame.CloseFrameAsync(FrameCloseOption.NoSave);
                }

                await VS.Documents.OpenAsync(newPath);

                ScratchFilesToolWindowControl.RefreshAll();
            }
        }

        private async Task ChangeScopeAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // Use _filePath as fallback if Document is null
            string oldPath = _docView.Document?.FilePath ?? _filePath;
            string solutionScratchFolder = ScratchFileService.GetSolutionScratchFolderPath();
            ScratchScope currentScope = ScratchFileService.GetScope(oldPath, solutionScratchFolder);
            ScratchScope targetScope = currentScope == ScratchScope.Solution
                ? ScratchScope.Global
                : ScratchScope.Solution;

            // Save unsaved edits to disk before moving
            if (_docView.Document != null)
            {
                _docView.Document.Save();
            }

            string newPath = await ScratchFileService.MoveToScopeAsync(oldPath, targetScope);

            if (newPath != null)
            {
                // Clean up event handlers before closing
                CloseAndCleanup();

                // Close the old document tab (file was already saved and moved on disk)
                if (_docView.WindowFrame != null)
                {
                    await _docView.WindowFrame.CloseFrameAsync(FrameCloseOption.NoSave);
                }

                await VS.Documents.OpenAsync(newPath);

                ScratchFilesToolWindowControl.RefreshAll();
            }
        }

        private async Task SaveAsAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // Use _filePath as fallback if Document is null
            string currentPath = _docView.Document?.FilePath ?? _filePath;

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

            // Clean up the old scratch file if it's different from the new path
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

            // Get the document view to show detection infobar
            DocumentView docView = await VS.Documents.GetDocumentViewAsync(filePath);

            if (docView?.WindowFrame == null)
            {
                return;
            }

            // Show a language detection suggestion InfoBar
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
                },
                KnownMonikers.StatusInformation,
                isCloseButtonVisible: true);

            InfoBar detectionBar = await VS.InfoBar.CreateAsync(docView.WindowFrame, model);

            if (detectionBar != null)
            {
                detectionBar.ActionItemClicked += async (s, e) =>
                {
                    string context = e.ActionItem.ActionContext as string;

                    if (context == "dismiss")
                    {
                        detectionBar.Close();
                    }
                    else if (context?.StartsWith("apply_", StringComparison.Ordinal) == true)
                    {
                        string extension = context.Substring("apply_".Length);
                        detectionBar.Close();

                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                        try
                        {
                            if (docView.Document != null)
                            {
                                docView.Document.Save();
                            }

                            string newPath = await ScratchFileService.ChangeExtensionAsync(filePath, extension);

                            if (newPath != null)
                            {
                                await docView.WindowFrame.CloseFrameAsync(FrameCloseOption.NoSave);
                                await VS.Documents.OpenAsync(newPath);
                                ScratchFilesToolWindowControl.RefreshAll();
                            }
                        }
                        catch (Exception ex)
                        {
                            await ex.LogAsync();
                        }
                    }
                };

                await detectionBar.TryShowInfoBarUIAsync();
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
