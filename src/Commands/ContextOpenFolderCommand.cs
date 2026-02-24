using System.Diagnostics;
using System.IO;

using ScratchFiles.Models;
using ScratchFiles.ToolWindows;

namespace ScratchFiles.Commands
{
    /// <summary>
    /// Context menu: Open the containing folder for the selected scratch file.
    /// </summary>
    [Command(PackageIds.ContextOpenFolder)]
    internal sealed class ContextOpenFolderCommand : BaseCommand<ContextOpenFolderCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (ScratchFilesToolWindowControl.RightClickedNode is ScratchFileNode fileNode)
            {
                string directory = Path.GetDirectoryName(fileNode.FilePath);

                if (Directory.Exists(directory))
                {
                    Process.Start("explorer.exe", $"/select,\"{fileNode.FilePath}\"");
                }
            }
        }
    }
}
