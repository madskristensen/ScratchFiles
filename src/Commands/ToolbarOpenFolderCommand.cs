using System.Diagnostics;
using System.IO;

using ScratchFiles.Services;

namespace ScratchFiles.Commands
{
    /// <summary>
    /// Toolbar button: Open the global scratch folder in Windows Explorer.
    /// </summary>
    [Command(PackageIds.ToolbarOpenFolder)]
    internal sealed class ToolbarOpenFolderCommand : BaseCommand<ToolbarOpenFolderCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            string folder = ScratchFileService.GetGlobalScratchFolder();

            if (Directory.Exists(folder))
            {
                Process.Start("explorer.exe", folder);
            }
        }
    }
}
