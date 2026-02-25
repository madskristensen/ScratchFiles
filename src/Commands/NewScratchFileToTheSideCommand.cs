using ScratchFiles.Services;
using ScratchFiles.ToolWindows;

namespace ScratchFiles.Commands
{
    /// <summary>
    /// Creates a new global scratch file and opens it in a vertical tab group to the side.
    /// If no document is open, behaves like the regular new scratch file command.
    /// Keyboard shortcut: Ctrl+Alt+N (global scope).
    /// </summary>
    [Command(PackageIds.NewScratchFileToTheSide)]
    internal sealed class NewScratchFileToTheSideCommand : BaseCommand<NewScratchFileToTheSideCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // Check if there's an active document before creating the scratch file
            DocumentView activeDoc = await VS.Documents.GetActiveDocumentViewAsync();

            // Create the scratch file
            string filePath = await ScratchFileService.CreateScratchFileAsync(ScratchScope.Global);
            DocumentView docView = await VS.Documents.OpenAsync(filePath);

            if (docView != null)
            {
                // Track buffer for auto-save and session persistence
                if (docView.TextView?.TextBuffer != null)
                {
                    ScratchSessionService.TrackDocument(filePath, docView.TextView.TextBuffer);
                }

                // Mark as dirty to show * indicator (content is auto-saved but appears unsaved)
                docView.Document?.UpdateDirtyState(isDirty: true, DateTime.Now);

                // If there was an active document, open the scratch file to the side
                if (activeDoc != null)
                {
                    await OpenToTheSideAsync();
                }
            }

            ScratchFilesToolWindowControl.RefreshAndSelect(filePath);

            // Set focus to the new document so user can start typing immediately
            docView?.TextView?.VisualElement?.Focus();
        }

        /// <summary>
        /// Moves the current document to a vertical tab group to the side.
        /// If a tab group already exists, reuses it; otherwise creates a new one.
        /// </summary>
        internal static async Task OpenToTheSideAsync()
        {
            // Try to move to an existing tab group first
            bool moved = await VS.Commands.ExecuteAsync("Window.MoveToNextTabGroup");

            // If no next tab group exists, create a new vertical tab group
            if (!moved)
            {
                await VS.Commands.ExecuteAsync("Window.NewVerticalTabGroup");
            }
        }
    }
}
