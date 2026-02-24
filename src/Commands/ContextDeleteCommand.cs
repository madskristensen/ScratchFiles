using ScratchFiles.Models;
using ScratchFiles.Services;
using ScratchFiles.ToolWindows;

namespace ScratchFiles.Commands
{
    /// <summary>
    /// Context menu: Delete the selected scratch file.
    /// </summary>
    [Command(PackageIds.ContextDelete)]
    internal sealed class ContextDeleteCommand : BaseCommand<ContextDeleteCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            ScratchNodeBase target = ScratchFilesToolWindowControl.RightClickedNode
                ?? ScratchFilesToolWindowControl.SelectedNode;

            if (target is ScratchFileNode fileNode)
            {
                if (await VS.MessageBox.ShowConfirmAsync("Delete Scratch File", $"Delete '{fileNode.Label}'?"))
                {
                    ScratchFileInfoBar.Detach(fileNode.FilePath);
                    ScratchFileService.DeleteScratchFile(fileNode.FilePath);
                    ScratchFilesToolWindowControl.RefreshAll();
                }
            }
        }
    }
}
