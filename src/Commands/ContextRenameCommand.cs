using System.IO;

using ScratchFiles.Models;
using ScratchFiles.Services;
using ScratchFiles.ToolWindows;
using ScratchFiles.UI;

namespace ScratchFiles.Commands
{
    /// <summary>
    /// Context menu: Rename the selected scratch file or sub-folder.
    /// Hidden for root group nodes via DynamicVisibility.
    /// </summary>
    [Command(PackageIds.ContextRename)]
    internal sealed class ContextRenameCommand : BaseCommand<ContextRenameCommand>
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
                await RenameFileAsync(fileNode);
            }
            else if (target is ScratchFolderNode folderNode)
            {
                await RenameFolderAsync(folderNode);
            }
        }

        private static async Task RenameFileAsync(ScratchFileNode fileNode)
        {
            string currentName = Path.GetFileName(fileNode.FilePath);

            string newName = InputDialog.Show(
                "Enter a new name for the scratch file:",
                "Rename Scratch File",
                currentName);

            if (!string.IsNullOrWhiteSpace(newName) && !string.Equals(newName, currentName, StringComparison.OrdinalIgnoreCase))
            {
                string oldPath = fileNode.FilePath;
                string newPath = await ScratchFileService.RenameScratchFileAsync(oldPath, newName);

                if (newPath != null)
                {
                    // VS will handle closing/reopening the document with a fresh infobar
                    ScratchFilesToolWindowControl.RefreshAll();
                }
            }
        }

        private static async Task RenameFolderAsync(ScratchFolderNode folderNode)
        {
            string currentName = Path.GetFileName(folderNode.FolderPath);

            string newName = InputDialog.Show(
                "Enter a new name for the folder:",
                "Rename Folder",
                currentName);

            if (!string.IsNullOrWhiteSpace(newName) && !string.Equals(newName, currentName, StringComparison.OrdinalIgnoreCase))
            {
                string newPath = await ScratchFileService.RenameFolderAsync(folderNode.FolderPath, newName);

                if (newPath != null)
                {
                    ScratchFilesToolWindowControl.RefreshAll();
                }
            }
        }
    }
}
