using System.Diagnostics;
using System.IO;

using ScratchFiles.Models;
using ScratchFiles.ToolWindows;

namespace ScratchFiles.Commands
{
    /// <summary>
    /// Context menu: Open the containing folder for the selected file, sub-folder, or group node.
    /// </summary>
    [Command(PackageIds.ContextOpenFolder)]
    internal sealed class ContextOpenFolderCommand : BaseCommand<ContextOpenFolderCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            ScratchNodeBase target = ScratchFilesToolWindowControl.RightClickedNode
                ?? ScratchFilesToolWindowControl.SelectedNode;

            if (target is ScratchFileNode fileNode)
            {
                string directory = Path.GetDirectoryName(fileNode.FilePath);

                if (Directory.Exists(directory))
                {
                    Process.Start("explorer.exe", $"/select,\"{fileNode.FilePath}\"");
                }
            }
            else if (target is ScratchFolderNode folderNode)
            {
                if (Directory.Exists(folderNode.FolderPath))
                {
                    Process.Start("explorer.exe", folderNode.FolderPath);
                }
            }
            else if (target is ScratchGroupNode groupNode)
            {
                if (Directory.Exists(groupNode.FolderPath))
                {
                    Process.Start("explorer.exe", groupNode.FolderPath);
                }
            }
        }
    }
}
