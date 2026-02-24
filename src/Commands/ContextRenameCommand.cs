using System.IO;

using ScratchFiles.Models;
using ScratchFiles.Services;
using ScratchFiles.ToolWindows;

namespace ScratchFiles.Commands
{
    /// <summary>
    /// Context menu: Rename the selected scratch file.
    /// </summary>
    [Command(PackageIds.ContextRename)]
    internal sealed class ContextRenameCommand : BaseCommand<ContextRenameCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            ScratchNodeBase target = ScratchFilesToolWindowControl.RightClickedNode
                ?? ScratchFilesToolWindowControl.SelectedNode;

            if (target is ScratchFileNode fileNode)
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
                    else
                    {
                        await VS.MessageBox.ShowWarningAsync("Rename Failed", "A file with that name already exists.");
                    }
                }
            }
        }
    }
}
