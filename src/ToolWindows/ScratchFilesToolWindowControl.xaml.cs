using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell.Interop;

using ScratchFiles.Commands;
using ScratchFiles.Models;
using ScratchFiles.Services;

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace ScratchFiles.ToolWindows
{
    public partial class ScratchFilesToolWindowControl : UserControl
    {
        private static ScratchFilesToolWindowControl _instance;
        private static ScratchNodeBase _rightClickedNode;
        private static ScratchNodeBase _selectedNode;
        private Point _dragStartPoint;

        // Store event handlers so they can be unsubscribed
        private readonly Action<Solution> _solutionOpenedHandler;
        private readonly Action _solutionClosedHandler;

        // File system watchers to detect changes on disk
        private FileSystemWatcher _globalWatcher;
        private FileSystemWatcher _solutionWatcher;
        private CancellationTokenSource _refreshDebounce;
        private readonly object _refreshLock = new object();

        public ScratchFilesToolWindowControl()
        {
            InitializeComponent();
            _instance = this;

            RootNodes = new ObservableCollection<ScratchNodeBase>();

            SetupTreeView();

            // Defer tree population until after the control is rendered
            // This frees up the UI thread faster during initial load
            ThreadHelper.JoinableTaskFactory.StartOnIdle(() =>
            {
                RefreshTree();
                SetupFileWatchers();
            }, VsTaskRunContext.UIThreadIdlePriority).FireAndForget();

            // Store handlers for later unsubscription
            _solutionOpenedHandler = (s) =>
            {
                RefreshTree();
                SetupSolutionWatcher();
            };
            _solutionClosedHandler = () =>
            {
                DisposeSolutionWatcher();
                RefreshTree();
            };

            VS.Events.SolutionEvents.OnAfterOpenSolution += _solutionOpenedHandler;
            VS.Events.SolutionEvents.OnAfterCloseSolution += _solutionClosedHandler;

            // Handle load/unload to manage _instance and event subscriptions
            // Tool windows can be temporarily unloaded when hidden, auto-hidden, or rearranged
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Restore instance when control is loaded back into visual tree
            _instance = this;

            // Re-subscribe to solution events if not already subscribed
            VS.Events.SolutionEvents.OnAfterOpenSolution -= _solutionOpenedHandler;
            VS.Events.SolutionEvents.OnAfterCloseSolution -= _solutionClosedHandler;
            VS.Events.SolutionEvents.OnAfterOpenSolution += _solutionOpenedHandler;
            VS.Events.SolutionEvents.OnAfterCloseSolution += _solutionClosedHandler;

            // Restart file watchers
            SetupFileWatchers();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            VS.Events.SolutionEvents.OnAfterOpenSolution -= _solutionOpenedHandler;
            VS.Events.SolutionEvents.OnAfterCloseSolution -= _solutionClosedHandler;

            // Stop file watchers to prevent unnecessary processing when hidden
            DisposeFileWatchers();

            if (_instance == this)
            {
                _instance = null;
            }
        }

        /// <summary>
        /// Sets up file system watchers for both global and solution scratch folders.
        /// </summary>
        private void SetupFileWatchers()
        {
            SetupGlobalWatcher();
            SetupSolutionWatcher();
        }

        /// <summary>
        /// Sets up a file system watcher for the global scratch folder.
        /// </summary>
        private void SetupGlobalWatcher()
        {
            if (_globalWatcher != null)
            {
                return;
            }

            try
            {
                string globalFolder = ScratchFileService.GetGlobalScratchFolder();
                _globalWatcher = CreateWatcher(globalFolder);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create global watcher: {ex.Message}");
            }
        }

        /// <summary>
        /// Sets up a file system watcher for the solution scratch folder.
        /// </summary>
        private void SetupSolutionWatcher()
        {
            DisposeSolutionWatcher();

            try
            {
                string solutionFolder = ScratchFileService.GetSolutionScratchFolder();

                if (solutionFolder != null && Directory.Exists(solutionFolder))
                {
                    _solutionWatcher = CreateWatcher(solutionFolder);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create solution watcher: {ex.Message}");
            }
        }

        /// <summary>
        /// Creates a FileSystemWatcher for the specified folder.
        /// </summary>
        private FileSystemWatcher CreateWatcher(string folderPath)
        {
            var watcher = new FileSystemWatcher(folderPath)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };

            watcher.Created += OnFileSystemChanged;
            watcher.Deleted += OnFileSystemChanged;
            watcher.Renamed += OnFileSystemChanged;
            watcher.Changed += OnFileSystemChanged;

            return watcher;
        }

        /// <summary>
        /// Handles file system change events with debouncing to avoid rapid refreshes.
        /// </summary>
        private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
        {
            // Ignore internal files (like .session.json)
            string fileName = Path.GetFileName(e.FullPath);
            if (fileName.StartsWith(".", StringComparison.Ordinal))
            {
                return;
            }

            // Debounce rapid changes (e.g., during file saves or batch operations)
            lock (_refreshLock)
            {
                _refreshDebounce?.Cancel();
                _refreshDebounce = new CancellationTokenSource();
            }

            CancellationToken token = _refreshDebounce.Token;

            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    // Wait a short delay to batch rapid changes
                    await Task.Delay(200, token);

                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(token);
                    RefreshTree();
                }
                catch (OperationCanceledException)
                {
                    // Debounced - a newer refresh is pending
                }
            }).FireAndForget();
        }

        /// <summary>
        /// Disposes the solution folder watcher.
        /// </summary>
        private void DisposeSolutionWatcher()
        {
            if (_solutionWatcher != null)
            {
                _solutionWatcher.EnableRaisingEvents = false;
                _solutionWatcher.Created -= OnFileSystemChanged;
                _solutionWatcher.Deleted -= OnFileSystemChanged;
                _solutionWatcher.Renamed -= OnFileSystemChanged;
                _solutionWatcher.Changed -= OnFileSystemChanged;
                _solutionWatcher.Dispose();
                _solutionWatcher = null;
            }
        }

        /// <summary>
        /// Disposes all file system watchers.
        /// </summary>
        private void DisposeFileWatchers()
        {
            lock (_refreshLock)
            {
                _refreshDebounce?.Cancel();
                _refreshDebounce?.Dispose();
                _refreshDebounce = null;
            }

            if (_globalWatcher != null)
            {
                _globalWatcher.EnableRaisingEvents = false;
                _globalWatcher.Created -= OnFileSystemChanged;
                _globalWatcher.Deleted -= OnFileSystemChanged;
                _globalWatcher.Renamed -= OnFileSystemChanged;
                _globalWatcher.Changed -= OnFileSystemChanged;
                _globalWatcher.Dispose();
                _globalWatcher = null;
            }

            DisposeSolutionWatcher();
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
        /// Refreshes the tree view and selects the specified file.
        /// </summary>
        internal static void RefreshAndSelect(string filePath)
        {
            _instance?.RefreshTree();
            _instance?.SelectFileByPath(filePath);
        }

        /// <summary>
        /// Configures the TreeView data template in code to match the Azure Explorer pattern. Uses CrispImage for file
        /// extension icons + TextBlock for label + description.
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

            // Drag-and-drop
            ScratchTree.AllowDrop = true;
            ScratchTree.PreviewMouseLeftButtonDown += ScratchTree_PreviewMouseLeftButtonDown;
            ScratchTree.PreviewMouseMove += ScratchTree_PreviewMouseMove;
            ScratchTree.DragOver += ScratchTree_DragOver;
            ScratchTree.Drop += ScratchTree_Drop;
        }

        private void RefreshTree()
        {
            RootNodes.Clear();

            // Global group
            string globalFolder = ScratchFileService.GetGlobalScratchFolder();
            var globalGroup = new ScratchGroupNode("Global", ScratchScope.Global, globalFolder);
            PopulateFolder(globalGroup, globalFolder, ScratchScope.Global);
            RootNodes.Add(globalGroup);

            // Solution group (only if a solution is open and has a scratch folder)
            string solutionFolder = ScratchFileService.GetSolutionScratchFolder();

            if (solutionFolder != null)
            {
                string solutionName = GetSolutionName() ?? "Solution";
                var solutionGroup = new ScratchGroupNode($"Solution ({solutionName})", ScratchScope.Solution, solutionFolder);
                PopulateFolder(solutionGroup, solutionFolder, ScratchScope.Solution);
                RootNodes.Add(solutionGroup);
            }

            // Show/hide empty state
            IReadOnlyList<ScratchFileInfo> allFiles = ScratchFileService.GetAllScratchFiles();
            bool hasFiles = allFiles.Count > 0;
            ScratchTree.Visibility = hasFiles ? Visibility.Visible : Visibility.Collapsed;
            EmptyStatePanel.Visibility = hasFiles ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>
        /// Recursively populates a parent node with sub-folders and files from disk.
        /// </summary>
        private static void PopulateFolder(ScratchNodeBase parentNode, string folderPath, ScratchScope scope)
        {
            foreach (string subDir in ScratchFileService.GetSubFolders(folderPath))
            {
                var folderNode = new ScratchFolderNode(subDir, scope);
                PopulateFolder(folderNode, subDir, scope);
                parentNode.Children.Add(folderNode);
            }

            foreach (string file in ScratchFileService.GetFilesInFolder(folderPath))
            {
                parentNode.Children.Add(new ScratchFileNode(file, scope));
            }
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
            if (_rightClickedNode != null && _rightClickedNode.ContextMenuId != 0)
            {
                int contextMenuId = _rightClickedNode.ContextMenuId;

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
                            uiShell.ShowContextMenu(0, ref cmdGroup, contextMenuId, points, null);
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
                VS.Documents.OpenAsync(fileNode.FilePath).FireAndForget();
                e.Handled = true;
            }
        }

        //private static async Task OpenFileAsync(ScratchFileNode fileNode)
        //{
        //    try
        //    {
        //        await VS.Documents.OpenAsync(fileNode.FilePath);
        //    }
        //    catch (Exception ex)
        //    {
        //        await ex.LogAsync();
        //    }
        //}

        //private static void RenameFileNode(ScratchFileNode fileNode)
        //{
        //    string currentName = Path.GetFileName(fileNode.FilePath);

        //    string newName = Microsoft.VisualBasic.Interaction.InputBox(
        //        "Enter a new name for the scratch file:",
        //        "Rename Scratch File",
        //        currentName);

        //    if (!string.IsNullOrWhiteSpace(newName) && !string.Equals(newName, currentName, StringComparison.OrdinalIgnoreCase))
        //    {
        //        string oldPath = fileNode.FilePath;
        //        string newPath = ScratchFileService.RenameScratchFile(oldPath, newName);

        //        if (newPath != null)
        //        {
        //            ScratchFileInfoBar.UpdatePath(oldPath, newPath);
        //            RefreshAll();
        //        }
        //    }
        //}

        //private static async Task DeleteFileNodeAsync(ScratchFileNode fileNode)
        //{
        //    if (await VS.MessageBox.ShowConfirmAsync("Delete Scratch File", $"Delete '{fileNode.Label}'?"))
        //    {
        //        ScratchFileInfoBar.Detach(fileNode.FilePath);
        //        ScratchFileService.DeleteScratchFile(fileNode.FilePath);
        //        RefreshAll();
        //    }
        //}

        /// <summary>
        /// Moves keyboard focus from the search box into the first visible tree node. Called by the Pane when Down
        /// arrow is pressed in the search box.
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

        /// <summary>
        /// Finds and selects a file node by its file path.
        /// </summary>
        private void SelectFileByPath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return;
            }

            ScratchFileNode fileNode = FindFileNodeByPath(filePath);

            if (fileNode != null)
            {
                // Expand parent groups/folders to make the node visible
                ExpandParentsOf(fileNode);

                // Force layout update so TreeViewItem containers are generated
                ScratchTree.UpdateLayout();

                SelectNodeInTree(fileNode);
            }
        }

        private ScratchFileNode FindFileNodeByPath(string filePath)
        {
            foreach (ScratchNodeBase group in RootNodes)
            {
                ScratchFileNode found = FindFileNodeRecursive(group, filePath);

                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static ScratchFileNode FindFileNodeRecursive(ScratchNodeBase node, string filePath)
        {
            if (node is ScratchFileNode fileNode
                && string.Equals(fileNode.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
            {
                return fileNode;
            }

            foreach (ScratchNodeBase child in node.Children)
            {
                ScratchFileNode found = FindFileNodeRecursive(child, filePath);

                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private void ExpandParentsOf(ScratchNodeBase targetNode)
        {
            foreach (ScratchNodeBase group in RootNodes)
            {
                if (ExpandIfContains(group, targetNode))
                {
                    return;
                }
            }
        }

        private static bool ExpandIfContains(ScratchNodeBase parent, ScratchNodeBase target)
        {
            foreach (ScratchNodeBase child in parent.Children)
            {
                if (child == target)
                {
                    parent.IsExpanded = true;
                    return true;
                }

                if (ExpandIfContains(child, target))
                {
                    parent.IsExpanded = true;
                    return true;
                }
            }

            return false;
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
                bool anyVisible = ApplyFilterRecursive(group, query);
                group.IsVisible = anyVisible;

                if (anyVisible)
                {
                    group.IsExpanded = true;
                }
            }
        }

        private static bool ApplyFilterRecursive(ScratchNodeBase node, string query)
        {
            bool anyChildVisible = false;

            foreach (ScratchNodeBase child in node.Children)
            {
                if (child is ScratchFolderNode)
                {
                    bool folderHasMatch = ApplyFilterRecursive(child, query);
                    child.IsVisible = folderHasMatch;

                    if (folderHasMatch)
                    {
                        child.IsExpanded = true;
                        anyChildVisible = true;
                    }
                }
                else
                {
                    bool matches = child.Label.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                        || (child.Description != null && child.Description.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);

                    child.IsVisible = matches;

                    if (matches)
                    {
                        anyChildVisible = true;
                    }
                }
            }

            return anyChildVisible;
        }

        /// <summary>
        /// Clears the search filter and shows all nodes.
        /// </summary>
        internal void ClearFilter()
        {
            foreach (ScratchNodeBase group in RootNodes)
            {
                ClearFilterRecursive(group);
            }
        }

        private static void ClearFilterRecursive(ScratchNodeBase node)
        {
            node.IsVisible = true;

            foreach (ScratchNodeBase child in node.Children)
            {
                ClearFilterRecursive(child);
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

        #region Drag and Drop

        private void ScratchTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(ScratchTree);
        }

        private void ScratchTree_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            Point currentPos = e.GetPosition(ScratchTree);
            Vector diff = _dragStartPoint - currentPos;

            if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            if (_selectedNode is ScratchFileNode fileNode)
            {
                var data = new DataObject(typeof(ScratchFileNode), fileNode);
                DragDrop.DoDragDrop(ScratchTree, data, DragDropEffects.Move);
            }
            else if (_selectedNode is ScratchFolderNode folderNode)
            {
                var data = new DataObject(typeof(ScratchFolderNode), folderNode);
                DragDrop.DoDragDrop(ScratchTree, data, DragDropEffects.Move);
            }
        }

        private void ScratchTree_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.None;

            ScratchNodeBase targetNode = GetDropTargetNode(e);
            string targetFolder = GetNodeFolderPath(targetNode);

            if (targetFolder == null)
            {
                e.Handled = true;
                return;
            }

            // Handle file drag
            if (e.Data.GetDataPresent(typeof(ScratchFileNode)))
            {
                ScratchFileNode draggedFile = e.Data.GetData(typeof(ScratchFileNode)) as ScratchFileNode;

                // Don't allow drop onto the same folder the file is already in
                if (draggedFile != null
                    && !string.Equals(Path.GetDirectoryName(draggedFile.FilePath), targetFolder, StringComparison.OrdinalIgnoreCase))
                {
                    e.Effects = DragDropEffects.Move;
                }
            }
            // Handle folder drag
            else if (e.Data.GetDataPresent(typeof(ScratchFolderNode)))
            {
                ScratchFolderNode draggedFolder = e.Data.GetData(typeof(ScratchFolderNode)) as ScratchFolderNode;

                if (draggedFolder != null)
                {
                    string sourcePath = Path.GetFullPath(draggedFolder.FolderPath);
                    string destPath = Path.GetFullPath(targetFolder);

                    // Don't allow drop onto itself, its parent, or any descendant
                    bool isSameParent = string.Equals(Path.GetDirectoryName(sourcePath), destPath, StringComparison.OrdinalIgnoreCase);
                    bool isDescendant = destPath.StartsWith(sourcePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(destPath, sourcePath, StringComparison.OrdinalIgnoreCase);

                    if (!isSameParent && !isDescendant)
                    {
                        e.Effects = DragDropEffects.Move;
                    }
                }
            }

            e.Handled = true;
        }

        private void ScratchTree_Drop(object sender, DragEventArgs e)
        {
            ScratchNodeBase targetNode = GetDropTargetNode(e);
            string targetFolder = GetNodeFolderPath(targetNode);

            if (targetFolder == null)
            {
                return;
            }

            // Handle file drop
            if (e.Data.GetDataPresent(typeof(ScratchFileNode)))
            {
                ScratchFileNode draggedFile = e.Data.GetData(typeof(ScratchFileNode)) as ScratchFileNode;

                if (draggedFile != null)
                {
                    string oldPath = draggedFile.FilePath;

                    ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                    {
                        try
                        {
                            string newPath = await ScratchFileService.MoveScratchFileAsync(oldPath, targetFolder);

                            if (newPath != null)
                            {
                                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                                ScratchFileInfoBar.UpdatePath(oldPath, newPath);
                                RefreshAll();
                            }
                        }
                        catch (Exception ex)
                        {
                            await ex.LogAsync();
                        }
                    }).FireAndForget();
                }
            }
            // Handle folder drop
            else if (e.Data.GetDataPresent(typeof(ScratchFolderNode)))
            {
                ScratchFolderNode draggedFolder = e.Data.GetData(typeof(ScratchFolderNode)) as ScratchFolderNode;

                if (draggedFolder != null)
                {
                    ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                    {
                        try
                        {
                            string newPath = await ScratchFileService.MoveFolderAsync(draggedFolder.FolderPath, targetFolder);

                            if (newPath != null)
                            {
                                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                                RefreshAll();
                            }
                        }
                        catch (Exception ex)
                        {
                            await ex.LogAsync();
                        }
                    }).FireAndForget();
                }
            }

            e.Handled = true;
        }

        private ScratchNodeBase GetDropTargetNode(DragEventArgs e)
        {
            if (e.OriginalSource is DependencyObject source)
            {
                TreeViewItem treeItem = FindAncestor<TreeViewItem>(source);
                return treeItem?.DataContext as ScratchNodeBase;
            }

            return null;
        }

        private static string GetNodeFolderPath(ScratchNodeBase node)
        {
            if (node is ScratchGroupNode groupNode)
            {
                return groupNode.FolderPath;
            }

            if (node is ScratchFolderNode folderNode)
            {
                return folderNode.FolderPath;
            }

            return null;
        }

        #endregion
    }
}
