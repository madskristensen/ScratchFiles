using System.IO;

using ScratchFiles.Models;
using ScratchFiles.Services;
using ScratchFiles.ToolWindows;

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
                RenameFile(fileNode);
            }
            else if (target is ScratchFolderNode folderNode)
            {
                RenameFolder(folderNode);
            }
        }

        private static void RenameFile(ScratchFileNode fileNode)
        {
            string currentName = Path.GetFileName(fileNode.FilePath);

            string newName = Microsoft.VisualBasic.Interaction.InputBox(
                "Enter a new name for the scratch file:",
                "Rename Scratch File",
                currentName);

            if (!string.IsNullOrWhiteSpace(newName) && !string.Equals(newName, currentName, StringComparison.OrdinalIgnoreCase))
            {
                string oldPath = fileNode.FilePath;
                string newPath = ScratchFileService.RenameScratchFile(oldPath, newName);

                if (newPath != null)
                {
                    ScratchFileInfoBar.UpdatePath(oldPath, newPath);
                    ScratchFilesToolWindowControl.RefreshAll();
                }
            }
        }

        private static void RenameFolder(ScratchFolderNode folderNode)
        {
            string currentName = Path.GetFileName(folderNode.FolderPath);

            string newName = Microsoft.VisualBasic.Interaction.InputBox(
                "Enter a new name for the folder:",
                "Rename Folder",
                currentName);

            if (!string.IsNullOrWhiteSpace(newName) && !string.Equals(newName, currentName, StringComparison.OrdinalIgnoreCase))
            {
                string newPath = ScratchFileService.RenameFolder(folderNode.FolderPath, newName);

                if (newPath != null)
                {
                    ScratchFilesToolWindowControl.RefreshAll();
                }
            }
        }
    }
}
