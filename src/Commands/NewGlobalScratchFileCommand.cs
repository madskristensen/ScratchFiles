using Microsoft.VisualStudio;

using ScratchFiles.Services;
using ScratchFiles.ToolWindows;

namespace ScratchFiles.Commands
{
    /// <summary>
    /// Intercepts the standard File.NewFile (Ctrl+N) command using the toolkit's InterceptAsync.
    /// Instead of showing the VS template dialog, creates a new global scratch file instantly.
    /// The intercept can be disabled via Tools > Options > Scratch Files.
    /// </summary>
    [Command(PackageIds.NewGlobalScratchFile)]
    internal sealed class NewGlobalScratchFileCommand : BaseCommand<NewGlobalScratchFileCommand>
    {
        protected override async Task InitializeCompletedAsync()
        {
            if (GeneralOptions.Instance?.OverrideCtrlN ?? true)
            {
                await VS.Commands.InterceptAsync(VSConstants.VSStd97CmdID.FileNew, () =>
                {
                    ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                    {
                        await CreateAndOpenGlobalScratchFileAsync();
                    }).FireAndForget();

                    return CommandProgression.Stop;
                });
            }
        }

        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await CreateAndOpenGlobalScratchFileAsync();
        }

        /// <summary>
        /// Creates a new global scratch file and opens it in the editor with an InfoBar.
        /// </summary>
        internal static async Task CreateAndOpenGlobalScratchFileAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            string filePath = await ScratchFileService.CreateScratchFileAsync(ScratchScope.Global);
            DocumentView docView = await VS.Documents.OpenAsync(filePath);

            if (docView != null)
            {
                await ScratchFileInfoBar.AttachAsync(docView);
            }

            ScratchFilesToolWindowControl.RefreshAndSelect(filePath);

            // Set focus to the new document so user can start typing immediately
            docView?.TextView?.VisualElement?.Focus();
        }
    }
}
