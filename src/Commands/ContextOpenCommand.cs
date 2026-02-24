using ScratchFiles.Models;
using ScratchFiles.ToolWindows;

namespace ScratchFiles.Commands
{
    /// <summary>
    /// Context menu: Open the selected scratch file in the editor.
    /// </summary>
    [Command(PackageIds.ContextOpen)]
    internal sealed class ContextOpenCommand : BaseCommand<ContextOpenCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            ScratchNodeBase target = ScratchFilesToolWindowControl.RightClickedNode
                ?? ScratchFilesToolWindowControl.SelectedNode;

            if (target is ScratchFileNode fileNode)
            {
                await VS.Documents.OpenAsync(fileNode.FilePath);
            }
        }
    }
}
