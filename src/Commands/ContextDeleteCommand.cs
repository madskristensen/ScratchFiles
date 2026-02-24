using ScratchFiles.Models;
using ScratchFiles.Services;
using ScratchFiles.ToolWindows;

namespace ScratchFiles.Commands
{
    /// <summary>
    /// Context menu: Delete the selected scratch file or sub-folder.
    /// Hidden for root group nodes via DynamicVisibility.
    /// </summary>
    [Command(PackageIds.ContextDelete)]
    internal sealed class ContextDeleteCommand : BaseCommand<ContextDeleteCommand>
    {
        protected override void BeforeQueryStatus(EventArgs e)
        {
            ScratchNodeBase target = ScratchFilesToolWindowControl.RightClickedNode
                ?? ScratchFilesToolWindowControl.SelectedNode;

            Command.Visible = target is ScratchFileNode || target is ScratchFolderNode;
        }

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
                    await ScratchFileService.DeleteScratchFileAsync(fileNode.FilePath);
                    ScratchFilesToolWindowControl.RefreshAll();
                }
            }
            else if (target is ScratchFolderNode folderNode)
            {
                if (await VS.MessageBox.ShowConfirmAsync("Delete Folder", $"Delete folder '{folderNode.Label}' and all its contents?"))
                {
                    await ScratchFileService.DeleteFolderAsync(folderNode.FolderPath);
                    ScratchFilesToolWindowControl.RefreshAll();
                }
            }
        }
    }
}
