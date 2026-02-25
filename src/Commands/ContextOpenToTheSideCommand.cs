using ScratchFiles.Models;
using ScratchFiles.ToolWindows;

namespace ScratchFiles.Commands
{
    /// <summary>
    /// Context menu command: Opens the selected scratch file in a vertical tab group to the side.
    /// Keyboard shortcut: Alt+Enter (in tool window context).
    /// </summary>
    [Command(PackageIds.ContextOpenToTheSide)]
    internal sealed class ContextOpenToTheSideCommand : BaseCommand<ContextOpenToTheSideCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            ScratchNodeBase target = ScratchFilesToolWindowControl.RightClickedNode
                ?? ScratchFilesToolWindowControl.SelectedNode;

            if (target is ScratchFileNode fileNode)
            {
                // Check if there's an active document before opening the file
                DocumentView activeDoc = await VS.Documents.GetActiveDocumentViewAsync();

                DocumentView docView = await VS.Documents.OpenAsync(fileNode.FilePath);

                // If there was an active document, open the scratch file to the side
                if (docView != null && activeDoc != null)
                {
                    await NewScratchFileToTheSideCommand.OpenToTheSideAsync();
                }
            }
        }
    }
}
