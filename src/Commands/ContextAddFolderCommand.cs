using ScratchFiles.Models;
using ScratchFiles.Services;
using ScratchFiles.ToolWindows;

namespace ScratchFiles.Commands
{
    /// <summary>
    /// Context menu: Add a new sub-folder under the selected group or folder node.
    /// </summary>
    [Command(PackageIds.ContextAddFolder)]
    internal sealed class ContextAddFolderCommand : BaseCommand<ContextAddFolderCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            ScratchNodeBase target = ScratchFilesToolWindowControl.RightClickedNode
                ?? ScratchFilesToolWindowControl.SelectedNode;

            string parentFolder = GetFolderPath(target);

            if (parentFolder == null)
            {
                return;
            }

            string name = Microsoft.VisualBasic.Interaction.InputBox(
                "Enter a name for the new folder:",
                "Add Folder",
                "New Folder");

            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            ScratchFileService.CreateSubFolder(parentFolder, name);
            ScratchFilesToolWindowControl.RefreshAll();
        }

        private static string GetFolderPath(ScratchNodeBase node)
        {
            if (node is ScratchGroupNode groupNode)
            {
                return groupNode.FolderPath;
            }

            if (node is ScratchFolderNode folderNode)
            {
                return folderNode.FolderPath;
            }

            return null;
        }
    }
}
