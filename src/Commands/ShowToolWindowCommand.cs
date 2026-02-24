using ScratchFiles.ToolWindows;

namespace ScratchFiles.Commands
{
    /// <summary>
    /// Opens the Scratch Files tool window (View > Other Windows > Scratch Files).
    /// </summary>
    [Command(PackageIds.ShowToolWindow)]
    internal sealed class ShowToolWindowCommand : BaseCommand<ShowToolWindowCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ScratchFilesToolWindow.ShowAsync();
        }
    }
}
