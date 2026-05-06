using System.Collections.Generic;
using System.Linq;

using ScratchFiles.Models;
using ScratchFiles.ToolWindows;

namespace ScratchFiles.Commands
{
    /// <summary>
    /// Context menu: Remove a custom folder root from the Scratch Files tool window.
    /// Only visible on user-added custom group nodes; never deletes the folder on disk.
    /// </summary>
    [Command(PackageIds.ContextRemoveCustomFolder)]
    internal sealed class ContextRemoveCustomFolderCommand : BaseCommand<ContextRemoveCustomFolderCommand>
    {
        protected override void BeforeQueryStatus(EventArgs e)
        {
            ScratchNodeBase target = ScratchFilesToolWindowControl.RightClickedNode
                ?? ScratchFilesToolWindowControl.SelectedNode;

            Command.Visible = target is ScratchGroupNode group && group.IsCustom;
        }

        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            ScratchNodeBase target = ScratchFilesToolWindowControl.RightClickedNode
                ?? ScratchFilesToolWindowControl.SelectedNode;

            if (!(target is ScratchGroupNode group) || !group.IsCustom)
            {
                return;
            }

            if (!await VS.MessageBox.ShowConfirmAsync(
                    "Remove Custom Folder",
                    $"Remove '{group.Label}' from Scratch Files?\n\nThe folder and its contents will not be deleted from disk."))
            {
                return;
            }

            GeneralOptions options = await GeneralOptions.GetLiveInstanceAsync();
            List<string> current = options.GetCustomFolders().ToList();

            current.RemoveAll(p => string.Equals(p, group.FolderPath, System.StringComparison.OrdinalIgnoreCase));

            options.SetCustomFolders(current);
            await options.SaveAsync();

            ScratchFilesToolWindowControl.RefreshAll();
        }
    }
}
