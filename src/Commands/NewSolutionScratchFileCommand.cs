using ScratchFiles.Services;
using ScratchFiles.ToolWindows;

namespace ScratchFiles.Commands
{
    /// <summary>
    /// Creates a new solution-scoped scratch file (Ctrl+Alt+N).
    /// Also placed on the Solution node context menu in Solution Explorer.
    /// Falls back to global scope if no solution is open.
    /// </summary>
    [Command(PackageIds.NewSolutionScratchFile)]
    internal sealed class NewSolutionScratchFileCommand : BaseCommand<NewSolutionScratchFileCommand>
    {
        protected override void BeforeQueryStatus(EventArgs e)
        {
            // Only visible when a solution is open
            Command.Visible = ScratchFileService.GetSolutionScratchFolderPath() != null;
        }

        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await CreateAndOpenSolutionScratchFileAsync();
        }

        internal static async Task CreateAndOpenSolutionScratchFileAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // Fall back to global if no solution is open
            ScratchScope scope = ScratchFileService.GetSolutionScratchFolder() != null
                ? ScratchScope.Solution
                : ScratchScope.Global;

            string filePath = await ScratchFileService.CreateScratchFileAsync(scope);
            // InfoBar is attached by DocumentEventHandler.OnBeforeDocumentWindowShow
            DocumentView docView = await VS.Documents.OpenAsync(filePath);

            ScratchFilesToolWindowControl.RefreshAndSelect(filePath);

            // Set focus to the new document so user can start typing immediately
            docView?.TextView?.VisualElement?.Focus();
        }
    }
}
