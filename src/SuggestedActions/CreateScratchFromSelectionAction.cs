using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Language.Intellisense;

using ScratchFiles.Commands;
using ScratchFiles.Services;
using ScratchFiles.ToolWindows;

namespace ScratchFiles.SuggestedActions
{
    /// <summary>
    /// Lightbulb action that creates a new global scratch file pre-filled with
    /// the current editor selection. Language is auto-detected from the content.
    /// </summary>
    internal sealed class CreateScratchFromSelectionAction : ISuggestedAction2
    {
        private readonly string _selectedText;
        private readonly string _sourceExtension;
        private readonly string _languageDisplayName;

        public CreateScratchFromSelectionAction(string selectedText, string sourceExtension)
        {
            _selectedText = selectedText;
            _sourceExtension = sourceExtension;
            _languageDisplayName = ResolveLanguageDisplayName(sourceExtension, selectedText);
        }

        public string DisplayText => "Create scratch file from selection";

        public string DisplayTextSuffix => _languageDisplayName != null
            ? $"({_languageDisplayName})"
            : null;

        public bool HasActionSets => false;

        public bool HasPreview => false;

        public string IconAutomationText => null;

        public ImageMoniker IconMoniker => KnownMonikers.NewDocument;

        public string InputGestureText => null;

        public void Invoke(CancellationToken cancellationToken)
        {
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

                string filePath = ScratchFileService.CreateScratchFileWithContent(ScratchScope.Global, _selectedText);

                string extension = ResolveExtension(_sourceExtension, _selectedText);

                if (extension != null)
                {
                    string newPath = ScratchFileService.ChangeExtension(filePath, extension);

                    if (newPath != null)
                    {
                        filePath = newPath;
                    }
                }

                DocumentView docView = await VS.Documents.OpenAsync(filePath);

                if (docView != null)
                {
                    await ScratchFileInfoBar.AttachAsync(docView);
                }

                ScratchFilesToolWindowControl.RefreshAll();
            });
        }

        public Task<IEnumerable<SuggestedActionSet>> GetActionSetsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IEnumerable<SuggestedActionSet>>(null);
        }

        public Task<object> GetPreviewAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<object>(null);
        }

        public void Dispose()
        {
        }

        public bool TryGetTelemetryId(out Guid telemetryId)
        {
            telemetryId = Guid.Empty;
            return false;
        }

        /// <summary>
        /// Returns the file extension to use: prefers the source file's extension,
        /// falls back to content-based detection.
        /// </summary>
        private static string ResolveExtension(string sourceExtension, string content)
        {
            if (!string.IsNullOrEmpty(sourceExtension))
            {
                return sourceExtension;
            }

            LanguageDetectionResult detected = LanguageDetectionService.Detect(content);
            return detected?.Extension;
        }

        /// <summary>
        /// Returns a display name for the language: prefers looking up the source extension,
        /// falls back to content-based detection.
        /// </summary>
        private static string ResolveLanguageDisplayName(string sourceExtension, string content)
        {
            if (!string.IsNullOrEmpty(sourceExtension))
            {
                string name = LanguageDetectionService.GetLanguageNameForExtension(sourceExtension);

                if (name != null)
                {
                    return name;
                }
            }

            return LanguageDetectionService.Detect(content)?.LanguageName;
        }
    }
}
