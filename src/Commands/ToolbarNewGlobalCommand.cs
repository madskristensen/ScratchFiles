using ScratchFiles.Services;
using ScratchFiles.ToolWindows;

namespace ScratchFiles.Commands
{
    /// <summary>
    /// Toolbar button: New Global Scratch File.
    /// </summary>
    [Command(PackageIds.ToolbarNewGlobal)]
    internal sealed class ToolbarNewGlobalCommand : BaseCommand<ToolbarNewGlobalCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await NewGlobalScratchFileCommand.CreateAndOpenGlobalScratchFileAsync();
        }
    }
}
