using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell.Interop;

using ScratchFiles.Commands;
using ScratchFiles.Models;
using ScratchFiles.Services;

namespace ScratchFiles.ToolWindows
{
    public partial class ScratchFilesToolWindowControl : UserControl
    {
        private static ScratchFilesToolWindowControl _instance;
        private static ScratchNodeBase _rightClickedNode;
        private static ScratchNodeBase _selectedNode;

        public ScratchFilesToolWindowControl()
        {
            InitializeComponent();
            _instance = this;

            RootNodes = new ObservableCollection<ScratchNodeBase>();

            SetupTreeView();
            RefreshTree();

            PreviewKeyDown += OnPreviewKeyDown;

            VS.Events.SolutionEvents.OnAfterOpenSolution += (s) => RefreshTree();
            VS.Events.SolutionEvents.OnAfterCloseSolution += () => RefreshTree();
        }

        internal ObservableCollection<ScratchNodeBase> RootNodes { get; }

        /// <summary>
        /// Gets the singleton instance of the control.
        /// </summary>
        internal static ScratchFilesToolWindowControl Instance => _instance;

        /// <summary>
        /// Gets the node that was right-clicked for context menu operations.
        /// </summary>
        internal static ScratchNodeBase RightClickedNode => _rightClickedNode;

        /// <summary>
        /// Gets the currently selected node in the tree view.
        /// </summary>
        internal static ScratchNodeBase SelectedNode => _selectedNode;

        /// <summary>
        /// Refreshes the tree view from both scratch folders.
        /// </summary>
        internal static void RefreshAll()
        {
            _instance?.RefreshTree();
        }

        /// <summary>
        /// Configures the TreeView data template in code to match the Azure Explorer pattern.
        /// Uses CrispImage for file extension icons + TextBlock for label + description.
        /// </summary>
        private void SetupTreeView()
        {
            var stackFactory = new FrameworkElementFactory(typeof(StackPanel));
            stackFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            stackFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 2, 0, 2));

            // Icon from ImageMoniker
            var imageFactory = new FrameworkElementFactory(typeof(CrispImage));
            imageFactory.SetBinding(CrispImage.MonikerProperty, new Binding("IconMoniker"));
            imageFactory.SetValue(FrameworkElement.WidthProperty, 16.0);
            imageFactory.SetValue(FrameworkElement.HeightProperty, 16.0);
            imageFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 4, 0));
            imageFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            stackFactory.AppendChild(imageFactory);

            // Label
            var labelFactory = new FrameworkElementFactory(typeof(TextBlock));
            labelFactory.SetBinding(TextBlock.TextProperty, new Binding("Label"));
            labelFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            stackFactory.AppendChild(labelFactory);

            // Description (e.g., file type)
            var descFactory = new FrameworkElementFactory(typeof(TextBlock));
            descFactory.SetBinding(TextBlock.TextProperty, new Binding("Description"));
            descFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 0, 0, 0));
            descFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            descFactory.SetValue(UIElement.OpacityProperty, 0.6);
            descFactory.SetValue(TextBlock.FontStyleProperty, FontStyles.Italic);
            stackFactory.AppendChild(descFactory);

            var template = new HierarchicalDataTemplate(typeof(ScratchNodeBase))
            {
                ItemsSource = new Binding("Children"),
                VisualTree = stackFactory
            };

            ScratchTree.Resources.Add(new DataTemplateKey(typeof(ScratchNodeBase)), template);
            ScratchTree.ItemsSource = RootNodes;

            // Wire events
            ScratchTree.SelectedItemChanged += (s, e) => _selectedNode = e.NewValue as ScratchNodeBase;
            ScratchTree.PreviewMouseRightButtonDown += ScratchTree_PreviewMouseRightButtonDown;
            ScratchTree.PreviewMouseRightButtonUp += ScratchTree_PreviewMouseRightButtonUp;
            ScratchTree.MouseDoubleClick += ScratchTree_MouseDoubleClick;
            ScratchTree.KeyDown += ScratchTree_KeyDown;
        }

        private void RefreshTree()
        {
            RootNodes.Clear();

            IReadOnlyList<ScratchFileInfo> allFiles = ScratchFileService.GetAllScratchFiles();
            IEnumerable<ScratchFileInfo> globalFiles = allFiles.Where(f => f.Scope == ScratchScope.Global);
            IEnumerable<ScratchFileInfo> solutionFiles = allFiles.Where(f => f.Scope == ScratchScope.Solution);

            // Global group
            var globalGroup = new ScratchGroupNode("Global", ScratchScope.Global);

            foreach (ScratchFileInfo file in globalFiles)
            {
                globalGroup.Children.Add(new ScratchFileNode(file.FilePath, ScratchScope.Global));
            }

            RootNodes.Add(globalGroup);

            // Solution group (only if a solution is open and has a scratch folder)
            string solutionFolder = ScratchFileService.GetSolutionScratchFolder();

            if (solutionFolder != null)
            {
                string solutionName = GetSolutionName() ?? "Solution";
                var solutionGroup = new ScratchGroupNode($"Solution ({solutionName})", ScratchScope.Solution);

                foreach (ScratchFileInfo file in solutionFiles)
                {
                    solutionGroup.Children.Add(new ScratchFileNode(file.FilePath, ScratchScope.Solution));
                }

                RootNodes.Add(solutionGroup);
            }

            // Show/hide empty state
            bool hasFiles = allFiles.Count > 0;
            ScratchTree.Visibility = hasFiles ? Visibility.Visible : Visibility.Collapsed;
            EmptyStatePanel.Visibility = hasFiles ? Visibility.Collapsed : Visibility.Visible;
        }

        private void ScratchTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Walk up the visual tree to find the TreeViewItem under the cursor
            if (e.OriginalSource is DependencyObject source)
            {
                TreeViewItem treeViewItem = FindAncestor<TreeViewItem>(source);

                if (treeViewItem != null)
                {
                    treeViewItem.IsSelected = true;
                    _rightClickedNode = treeViewItem.DataContext as ScratchNodeBase;
                    e.Handled = true;
                }
                else
                {
                    _rightClickedNode = null;
                }
            }
        }

        private void ScratchTree_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_rightClickedNode is ScratchFileNode fileNode && fileNode.ContextMenuId != 0)
            {
                ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                {
                    try
                    {
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                        var uiShell = await VS.GetServiceAsync<Microsoft.VisualStudio.Shell.Interop.SVsUIShell,
                            Microsoft.VisualStudio.Shell.Interop.IVsUIShell>();

                        if (uiShell != null)
                        {
                            System.Windows.Point screenPoint = PointToScreen(Mouse.GetPosition(this));
                            var points = new Microsoft.VisualStudio.Shell.Interop.POINTS[]
                            {
                                new Microsoft.VisualStudio.Shell.Interop.POINTS
                                {
                                    x = (short)screenPoint.X,
                                    y = (short)screenPoint.Y
                                }
                            };

                            Guid cmdGroup = PackageGuids.ScratchFiles;
                            uiShell.ShowContextMenu(0, ref cmdGroup, fileNode.ContextMenuId, points, null);
                        }
                    }
                    catch (System.Exception ex)
                    {
                        await ex.LogAsync();
                    }
                }).FireAndForget();
            }
        }

        private void ScratchTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (SelectedNode is ScratchFileNode fileNode)
            {
                OpenFileAsync(fileNode).FireAndForget();
                e.Handled = true;
            }
        }

        private void ScratchTree_KeyDown(object sender, KeyEventArgs e)
        {
            if (SelectedNode is not ScratchFileNode fileNode)
            {
                return;
            }

            switch (e.Key)
            {
                case Key.Enter:
                    OpenFileAsync(fileNode).FireAndForget();
                    e.Handled = true;
                    break;

                case Key.F2:
                    RenameFileNode(fileNode);
                    e.Handled = true;
                    break;

                case Key.Delete:
                    DeleteFileNodeAsync(fileNode).FireAndForget();
                    e.Handled = true;
                    break;
            }
        }

        private static async Task OpenFileAsync(ScratchFileNode fileNode)
        {
            try
            {
                await VS.Documents.OpenAsync(fileNode.FilePath);
            }
            catch (Exception ex)
            {
                await ex.LogAsync();
            }
        }

        private static void RenameFileNode(ScratchFileNode fileNode)
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
                    RefreshAll();
                }
            }
        }

        private static async Task DeleteFileNodeAsync(ScratchFileNode fileNode)
        {
            if (await VS.MessageBox.ShowConfirmAsync("Delete Scratch File", $"Delete '{fileNode.Label}'?"))
            {
                ScratchFileInfoBar.Detach(fileNode.FilePath);
                ScratchFileService.DeleteScratchFile(fileNode.FilePath);
                RefreshAll();
            }
        }

        /// <summary>
        /// Moves keyboard focus from the search box into the first visible tree node.
        /// Called by the Pane when Down arrow is pressed in the search box.
        /// </summary>
        internal void FocusFirstTreeNode()
        {
            if (ScratchTree.Items.Count == 0)
            {
                return;
            }

            ScratchTree.Focus();

            // Find the first visible group node and select its first visible child
            foreach (ScratchNodeBase group in RootNodes)
            {
                if (!group.IsVisible)
                {
                    continue;
                }

                if (group is ScratchGroupNode groupNode && groupNode.Children.Count > 0)
                {
                    groupNode.IsExpanded = true;

                    foreach (ScratchNodeBase child in groupNode.Children)
                    {
                        if (child.IsVisible)
                        {
                            SelectNodeInTree(child);
                            return;
                        }
                    }
                }

                // Fallback: select the group node itself
                SelectNodeInTree(group);
                return;
            }
        }

        private void SelectNodeInTree(ScratchNodeBase node)
        {
            // Walk the ItemContainerGenerators to find and focus the TreeViewItem
            foreach (ScratchNodeBase rootNode in RootNodes)
            {
                TreeViewItem rootItem = ScratchTree.ItemContainerGenerator.ContainerFromItem(rootNode) as TreeViewItem;

                if (rootItem == null)
                {
                    continue;
                }

                if (rootNode == node)
                {
                    rootItem.IsSelected = true;
                    rootItem.Focus();
                    return;
                }

                // Check children
                rootItem.UpdateLayout();

                TreeViewItem childItem = rootItem.ItemContainerGenerator.ContainerFromItem(node) as TreeViewItem;

                if (childItem != null)
                {
                    childItem.IsSelected = true;
                    childItem.Focus();
                    return;
                }
            }
        }

        private static string GetSolutionName()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var solution = VS.GetRequiredService<SVsSolution, IVsSolution>();
                solution.GetProperty((int)__VSPROPID.VSPROPID_SolutionBaseName, out object name);
                return name as string;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Filters the tree view to show only files matching the query.
        /// </summary>
        internal void ApplyFilter(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                ClearFilter();
                return;
            }

            foreach (ScratchNodeBase group in RootNodes)
            {
                if (group is ScratchGroupNode groupNode)
                {
                    bool anyVisible = false;

                    foreach (ScratchNodeBase child in groupNode.Children)
                    {
                        bool matches = child.Label.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                            || (child.Description != null && child.Description.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);

                        child.IsVisible = matches;

                        if (matches)
                        {
                            anyVisible = true;
                        }
                    }

                    groupNode.IsVisible = anyVisible;

                    if (anyVisible)
                    {
                        groupNode.IsExpanded = true;
                    }
                }
            }
        }

        /// <summary>
        /// Clears the search filter and shows all nodes.
        /// </summary>
        internal void ClearFilter()
        {
            foreach (ScratchNodeBase group in RootNodes)
            {
                group.IsVisible = true;

                if (group is ScratchGroupNode groupNode)
                {
                    foreach (ScratchNodeBase child in groupNode.Children)
                    {
                        child.IsVisible = true;
                    }
                }
            }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                ScratchFilesToolWindow.Pane.ActivateSearch();
                e.Handled = true;
            }
        }

        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T target)
                {
                    return target;
                }

                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }

            return null;
        }
    }
}
