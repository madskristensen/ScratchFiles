using ScratchFiles.Services;
using ScratchFiles.ToolWindows;

namespace ScratchFiles.Commands
{
    /// <summary>
    /// Toolbar button: New Solution Scratch File.
    /// </summary>
    [Command(PackageIds.ToolbarNewSolution)]
    internal sealed class ToolbarNewSolutionCommand : BaseCommand<ToolbarNewSolutionCommand>
    {
        protected override void BeforeQueryStatus(EventArgs e)
        {
            Command.Visible = ScratchFileService.GetSolutionScratchFolderPath() != null;
        }

        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await NewSolutionScratchFileCommand.CreateAndOpenSolutionScratchFileAsync();
        }
    }
}
