using Microsoft.Internal.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell.Interop;

using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace ScratchFiles.ToolWindows
{
    /// <summary>
    /// Hosts the Scratch Files tool window with a toolbar, search, and tree view.
    /// </summary>
    public class ScratchFilesToolWindow : BaseToolWindow<ScratchFilesToolWindow>
    {
        public override string GetTitle(int toolWindowId) => "Scratch Files";

        public override Type PaneType => typeof(Pane);

        public override async Task<FrameworkElement> CreateAsync(int toolWindowId, CancellationToken cancellationToken)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            try
            {
                return new ScratchFilesToolWindowControl();
            }
            catch (Exception ex)
            {
                await ex.LogAsync();
                return new TextBlock
                {
                    Text = $"Failed to load Scratch Files:\n{ex.Message}",
                    Margin = new Thickness(10)
                };
            }
        }

        [Guid("a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d")]
        internal class Pane : ToolWindowPane
        {
            private static Pane _instance;

            public Pane()
            {
                _instance = this;
                BitmapImageMoniker = KnownMonikers.DocumentGroup;
                ToolBar = new CommandID(PackageGuids.ScratchFiles, PackageIds.ToolWindowToolbar);
                ToolBarLocation = (int)VSTWT_LOCATION.VSTWT_TOP;
            }

            /// <summary>
            /// Enables the search box in the tool window toolbar.
            /// </summary>
            public override bool SearchEnabled => true;

            /// <summary>
            /// Handles Down arrow in the search box to move focus into the tree view.
            /// </summary>
            public override bool OnNavigationKeyDown(uint dwNavigationKey, uint dwModifiers)
            {
                // VSSEARCHNAVIGATIONKEY_DOWN = 1
                if (dwNavigationKey == 1 && Content is ScratchFilesToolWindowControl control)
                {
                    control.FocusFirstTreeNode();
                    return true;
                }

                return base.OnNavigationKeyDown(dwNavigationKey, dwModifiers);
            }

            /// <summary>
            /// Activates the search box. Called when Ctrl+F is pressed in the tool window.
            /// </summary>
            internal static void ActivateSearch()
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                _instance?.SearchHost?.Activate();
            }

            /// <summary>
            /// Creates a search task that filters the tree view.
            /// </summary>
            public override IVsSearchTask CreateSearch(uint dwCookie, IVsSearchQuery pSearchQuery, IVsSearchCallback pSearchCallback)
            {
                if (pSearchQuery == null || pSearchCallback == null)
                {
                    return null;
                }

                return new ScratchSearchTask(dwCookie, pSearchQuery, pSearchCallback, this);
            }

            /// <summary>
            /// Clears search results and restores the full tree view.
            /// </summary>
            public override void ClearSearch()
            {
                if (Content is ScratchFilesToolWindowControl control)
                {
                    control.ClearFilter();
                }
            }

            /// <summary>
            /// Configures search settings for instant search.
            /// </summary>
            public override void ProvideSearchSettings(IVsUIDataSource pSearchSettings)
            {
                ThreadHelper.ThrowIfNotOnUIThread();

                Utilities.SetValue(pSearchSettings,
                    SearchSettingsDataSource.SearchStartTypeProperty.Name,
                    (uint)VSSEARCHSTARTTYPE.SST_INSTANT);

                Utilities.SetValue(pSearchSettings,
                    SearchSettingsDataSource.SearchStartMinCharsProperty.Name,
                    (uint)1);

                string shortcut = GetCommandShortcut("Window.WindowSearch");
                string watermark = string.IsNullOrEmpty(shortcut) ? "Search" : $"Search ({shortcut})";

                Utilities.SetValue(pSearchSettings,
                    SearchSettingsDataSource.SearchWatermarkProperty.Name,
                    watermark);
            }

            private static string GetCommandShortcut(string commandName)
            {
                ThreadHelper.ThrowIfNotOnUIThread();

                try
                {
                    var dte = ServiceProvider.GlobalProvider.GetService(typeof(EnvDTE.DTE)) as EnvDTE80.DTE2;
                    EnvDTE.Command command = dte?.Commands.Item(commandName);

                    if (command?.Bindings is object[] bindings && bindings.Length > 0)
                    {
                        // Return the first binding, strip scope prefix if present (e.g., "Global::Ctrl+E")
                        string binding = bindings[0].ToString();
                        int scopeEnd = binding.IndexOf("::", StringComparison.Ordinal);

                        return scopeEnd >= 0 ? binding.Substring(scopeEnd + 2) : binding;
                    }
                }
                catch
                {
                    // Ignore errors - just return null to use default watermark
                }

                return null;
            }
        }

        /// <summary>
        /// Search task that filters the scratch files tree view by filename.
        /// </summary>
        private class ScratchSearchTask : VsSearchTask
        {
            private readonly Pane _pane;

            public ScratchSearchTask(uint dwCookie, IVsSearchQuery pSearchQuery, IVsSearchCallback pSearchCallback, Pane pane)
                : base(dwCookie, pSearchQuery, pSearchCallback)
            {
                _pane = pane;
            }

            protected override void OnStartSearch()
            {
                string query = SearchQuery.SearchString;

                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                    if (_pane.Content is ScratchFilesToolWindowControl control)
                    {
                        control.ApplyFilter(query);
                    }
                });

                SearchResults = 1;
                base.OnStartSearch();
            }
        }
    }
}
