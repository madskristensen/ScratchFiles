global using Community.VisualStudio.Toolkit;

global using Microsoft.VisualStudio.Shell;

global using System;

global using Task = System.Threading.Tasks.Task;

using Microsoft.VisualStudio;

using ScratchFiles.Commands;
using ScratchFiles.Services;
using ScratchFiles.ToolWindows;

using System.Runtime.InteropServices;
using System.Threading;

namespace ScratchFiles
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration(Vsix.Name, Vsix.Description, Vsix.Version)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideKeyBindingTable(PackageGuids.ScratchFilesToolWindowString, 110)]
    [ProvideToolWindow(typeof(ScratchFilesToolWindow.Pane), Style = VsDockStyle.Tabbed, Window = WindowGuids.SolutionExplorer)]
    [ProvideOptionPage(typeof(OptionsProvider.GeneralOptionsPage), "Scratch Files", "General", 0, 0, true)]
    [Guid(PackageGuids.ScratchFilesString)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.ShellInitialized_string, PackageAutoLoadFlags.BackgroundLoad)]
    public sealed class ScratchFilesPackage : ToolkitPackage
    {
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await this.RegisterCommandsAsync();

            this.RegisterToolWindows();

            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            DocumentEventHandler.Initialize();

            // Restore scratch files from previous session (deferred to not block startup)
            JoinableTaskFactory.StartOnIdle(async () =>
            {
                try
                {
                    await ScratchSessionService.RestoreSessionAsync();
                }
                catch (Exception ex)
                {
                    await ex.LogAsync();
                }
            }, VsTaskRunContext.UIThreadIdlePriority).FireAndForget();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ScratchFileInfoBar.ClearAll();
                DocumentEventHandler.Shutdown();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Provides the options page registration for the GeneralOptions dialog page.
    /// </summary>
    internal sealed class OptionsProvider
    {
        [ComVisible(true)]
        public class GeneralOptionsPage : BaseOptionPage<GeneralOptions> { }
    }
}