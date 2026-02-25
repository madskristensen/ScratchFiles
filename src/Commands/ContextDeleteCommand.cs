using ScratchFiles.Models;
using ScratchFiles.Services;
using ScratchFiles.ToolWindows;

using System.IO;

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
                    // Get the path and parent directory BEFORE deletion
                    string deletedPath = fileNode.FilePath;
                    string parentDir = Path.GetDirectoryName(deletedPath);

                    // Get current siblings from the tree (not from disk)
                    ScratchNodeBase parent = ScratchFilesToolWindowControl.FindParentNode(target);
                    string pathToSelect = null;

                    if (parent != null)
                    {
                        int index = parent.Children.IndexOf(target);

                        // Try previous sibling (node above)
                        if (index > 0)
                        {
                            pathToSelect = ScratchFilesToolWindowControl.GetNodePath(parent.Children[index - 1]);
                        }
                        // Try next sibling
                        else if (index < parent.Children.Count - 1)
                        {
                            pathToSelect = ScratchFilesToolWindowControl.GetNodePath(parent.Children[index + 1]);
                        }
                        // Select parent folder if it's not the root
                        else if (parent is ScratchFolderNode folderNode)
                        {
                            pathToSelect = folderNode.FolderPath;
                        }
                    }

                    // VS will dispose the InfoBar automatically when the document is closed
                    await ScratchFileService.DeleteScratchFileAsync(fileNode.FilePath);

                    // Refresh and select the appropriate node
                    if (!string.IsNullOrEmpty(pathToSelect))
                    {
                        ScratchFilesToolWindowControl.RefreshAndSelectPath(pathToSelect);
                    }
                    else
                    {
                        ScratchFilesToolWindowControl.RefreshAll();
                    }
                }
            }
            else if (target is ScratchFolderNode folderNode)
            {
                if (await VS.MessageBox.ShowConfirmAsync("Delete Folder", $"Delete folder '{folderNode.Label}' and all its contents?"))
                {
                    // Get current siblings from the tree (not from disk)
                    ScratchNodeBase parent = ScratchFilesToolWindowControl.FindParentNode(target);
                    string pathToSelect = null;

                    if (parent != null)
                    {
                        int index = parent.Children.IndexOf(target);

                        // Try previous sibling (node above)
                        if (index > 0)
                        {
                            pathToSelect = ScratchFilesToolWindowControl.GetNodePath(parent.Children[index - 1]);
                        }
                        // Try next sibling
                        else if (index < parent.Children.Count - 1)
                        {
                            pathToSelect = ScratchFilesToolWindowControl.GetNodePath(parent.Children[index + 1]);
                        }
                        // Select parent folder if it's not the root
                        else if (parent is ScratchFolderNode parentFolder)
                        {
                            pathToSelect = parentFolder.FolderPath;
                        }
                    }

                    await ScratchFileService.DeleteFolderAsync(folderNode.FolderPath);

                    // Refresh and select the appropriate node
                    if (!string.IsNullOrEmpty(pathToSelect))
                    {
                        ScratchFilesToolWindowControl.RefreshAndSelectPath(pathToSelect);
                    }
                    else
                    {
                        ScratchFilesToolWindowControl.RefreshAll();
                    }
                }
            }
        }
    }
}
