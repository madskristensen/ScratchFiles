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
        private static volatile ScratchFilesToolWindowControl _instance;
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
        private string _pendingSelectionPath;

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
            CancellationToken token;
            lock (_refreshLock)
            {
                _refreshDebounce?.Cancel();
                _refreshDebounce?.Dispose();
                _refreshDebounce = new CancellationTokenSource();
                token = _refreshDebounce.Token;
            }

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
        /// Refreshes the tree view and selects the specified file or folder by path.
        /// </summary>
        internal static void RefreshAndSelectPath(string path)
        {
            if (_instance == null || string.IsNullOrEmpty(path))
            {
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[ScratchFiles] RefreshAndSelectPath called with: {path}");

            // Set the pending selection path - this will be applied after ANY refresh (including file watcher refreshes)
            _instance._pendingSelectionPath = path;

            // Cancel any pending debounced refreshes from file system watcher
            lock (_instance._refreshLock)
            {
                _instance._refreshDebounce?.Cancel();
                _instance._refreshDebounce = null;
            }

            // Temporarily disable file system watchers to prevent new refresh events
            _instance.DisableWatchers();

            _instance.RefreshTree();

            // Clear the pending selection after a delay (once all refreshes have settled)
            _instance.Dispatcher.BeginInvoke(new Action(() =>
            {
                System.Diagnostics.Debug.WriteLine($"[ScratchFiles] Clearing pending selection and re-enabling watchers");
                _instance._pendingSelectionPath = null;
                _instance.EnableWatchers();
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        /// <summary>
        /// Temporarily disables file system watchers.
        /// </summary>
        private void DisableWatchers()
        {
            if (_globalWatcher != null)
            {
                _globalWatcher.EnableRaisingEvents = false;
            }

            if (_solutionWatcher != null)
            {
                _solutionWatcher.EnableRaisingEvents = false;
            }
        }

        /// <summary>
        /// Re-enables file system watchers.
        /// </summary>
        private void EnableWatchers()
        {
            if (_globalWatcher != null)
            {
                _globalWatcher.EnableRaisingEvents = true;
            }

            if (_solutionWatcher != null)
            {
                _solutionWatcher.EnableRaisingEvents = true;
            }
        }

        /// <summary>
        /// Gets the path of the node that should be selected after the specified node is deleted.
        /// Returns the previous sibling, next sibling, or parent path.
        /// </summary>
        internal static string GetPathToSelectAfterDeletion(ScratchNodeBase nodeToDelete)
        {
            if (nodeToDelete == null)
            {
                return null;
            }

            // Get the path and parent directory of the node being deleted
            string deletedPath = GetNodePath(nodeToDelete);

            if (string.IsNullOrEmpty(deletedPath))
            {
                return null;
            }

            // For files: get sibling files in the same directory
            if (nodeToDelete is ScratchFileNode)
            {
                string parentDir = Path.GetDirectoryName(deletedPath);

                if (Directory.Exists(parentDir))
                {
                    // Get all files in the parent directory (sorted the same way the tree builds)
                    IReadOnlyList<string> files = ScratchFileService.GetFilesInFolder(parentDir);

                    if (files.Count > 0)
                    {
                        // Find the index of the file being deleted
                        int deleteIndex = -1;
                        for (int i = 0; i < files.Count; i++)
                        {
                            if (string.Equals(files[i], deletedPath, StringComparison.OrdinalIgnoreCase))
                            {
                                deleteIndex = i;
                                break;
                            }
                        }

                        if (deleteIndex >= 0)
                        {
                            // Try to select the previous file (node above)
                            if (deleteIndex > 0)
                            {
                                return files[deleteIndex - 1];
                            }

                            // Try to select the next file
                            if (deleteIndex < files.Count - 1)
                            {
                                return files[deleteIndex + 1];
                            }
                        }
                    }

                    // No siblings - select the parent folder if it's not the root
                    string globalRoot = ScratchFileService.GetGlobalScratchFolder();
                    string solutionRoot = ScratchFileService.GetSolutionScratchFolder();

                    if (!string.Equals(parentDir, globalRoot, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(parentDir, solutionRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        return parentDir;
                    }
                }
            }
            // For folders: get sibling folders in the same parent directory
            else if (nodeToDelete is ScratchFolderNode)
            {
                string parentDir = Path.GetDirectoryName(deletedPath);

                if (Directory.Exists(parentDir))
                {
                    // Get all subfolders in the parent directory
                    IReadOnlyList<string> folders = ScratchFileService.GetSubFolders(parentDir);

                    if (folders.Count > 0)
                    {
                        // Find the index of the folder being deleted
                        int deleteIndex = -1;
                        for (int i = 0; i < folders.Count; i++)
                        {
                            if (string.Equals(folders[i], deletedPath, StringComparison.OrdinalIgnoreCase))
                            {
                                deleteIndex = i;
                                break;
                            }
                        }

                        if (deleteIndex >= 0)
                        {
                            // Try to select the previous folder (node above)
                            if (deleteIndex > 0)
                            {
                                return folders[deleteIndex - 1];
                            }

                            // Try to select the next folder
                            if (deleteIndex < folders.Count - 1)
                            {
                                return folders[deleteIndex + 1];
                            }
                        }
                    }

                    // Check if there are any files to select instead
                    IReadOnlyList<string> files = ScratchFileService.GetFilesInFolder(parentDir);
                    if (files.Count > 0)
                    {
                        return files[files.Count - 1]; // Select the last file
                    }

                    // No siblings - select the parent folder if it's not the root
                    string globalRoot = ScratchFileService.GetGlobalScratchFolder();
                    string solutionRoot = ScratchFileService.GetSolutionScratchFolder();

                    if (!string.Equals(parentDir, globalRoot, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(parentDir, solutionRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        return parentDir;
                    }
                }
            }

            return null;
        }

        private static ScratchNodeBase FindParentOfNode(ScratchNodeBase target)
        {
            if (_instance == null)
            {
                return null;
            }

            foreach (ScratchNodeBase rootNode in _instance.RootNodes)
            {
                if (rootNode.Children.Contains(target))
                {
                    return rootNode;
                }

                ScratchNodeBase parent = FindParentRecursive(rootNode, target);

                if (parent != null)
                {
                    return parent;
                }
            }

            return null;
        }

        /// <summary>
        /// Finds the parent node of the specified target node in the tree.
        /// </summary>
        internal static ScratchNodeBase FindParentNode(ScratchNodeBase target)
        {
            return FindParentOfNode(target);
        }

        /// <summary>
        /// Gets the file or folder path from a tree node.
        /// </summary>
        internal static string GetNodePath(ScratchNodeBase node)
        {
            if (node is ScratchFileNode fileNode)
            {
                return fileNode.FilePath;
            }

            if (node is ScratchFolderNode folderNode)
            {
                return folderNode.FolderPath;
            }

            if (node is ScratchGroupNode groupNode)
            {
                return groupNode.FolderPath;
            }

            return null;
        }

        private static ScratchNodeBase FindParentRecursive(ScratchNodeBase parent, ScratchNodeBase target)
        {
            foreach (ScratchNodeBase child in parent.Children)
            {
                if (child.Children.Contains(target))
                {
                    return child;
                }

                ScratchNodeBase found = FindParentRecursive(child, target);

                if (found != null)
                {
                    return found;
                }
            }

            return null;
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
            ScratchTree.SelectedItemChanged += (s, e) =>
            {
                _selectedNode = e.NewValue as ScratchNodeBase;
                _rightClickedNode = null; // Clear right-clicked node when selection changes
            };
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

            // Apply any pending selection after refresh completes
            if (!string.IsNullOrEmpty(_pendingSelectionPath))
            {
                string pathToSelect = _pendingSelectionPath;
                System.Diagnostics.Debug.WriteLine($"[ScratchFiles] RefreshTree: Applying pending selection for: {pathToSelect}");

                // Schedule selection after layout completes
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    SelectNodeByPath(pathToSelect);
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
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

                // Mark handled to prevent WPF from processing the event further,
                // which could interfere with the context menu or clear _rightClickedNode
                e.Handled = true;

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

                // Search recursively through all children
                rootItem.UpdateLayout();

                if (SelectNodeInSubtree(rootItem, node))
                {
                    return;
                }
            }
        }

        private bool SelectNodeInSubtree(TreeViewItem parentItem, ScratchNodeBase targetNode)
        {
            // Check all children of this item
            ScratchNodeBase parentData = parentItem.DataContext as ScratchNodeBase;

            if (parentData == null)
            {
                return false;
            }

            foreach (ScratchNodeBase child in parentData.Children)
            {
                TreeViewItem childItem = parentItem.ItemContainerGenerator.ContainerFromItem(child) as TreeViewItem;

                if (childItem != null)
                {
                    if (child == targetNode)
                    {
                        childItem.IsSelected = true;
                        childItem.Focus();
                        childItem.BringIntoView();
                        return true;
                    }

                    // Recursively search this child's subtree
                    childItem.UpdateLayout();

                    if (SelectNodeInSubtree(childItem, targetNode))
                    {
                        return true;
                    }
                }
            }

            return false;
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

        /// <summary>
        /// Finds and selects a node (file or folder) by its path.
        /// </summary>
        private void SelectNodeByPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                System.Diagnostics.Debug.WriteLine("[ScratchFiles] SelectNodeByPath: path is null or empty");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[ScratchFiles] SelectNodeByPath: Looking for path: {path}");

            // Clear all existing selections first
            ClearAllSelections();

            ScratchNodeBase node = FindNodeByPath(path);

            if (node != null)
            {
                System.Diagnostics.Debug.WriteLine($"[ScratchFiles] SelectNodeByPath: Found node: {node.Label}");

                // Expand parent groups/folders to make the node visible
                ExpandParentsOf(node);

                // Set IsSelected on the data model - the binding will update the UI
                node.IsSelected = true;

                System.Diagnostics.Debug.WriteLine($"[ScratchFiles] SelectNodeByPath: Set IsSelected=true on node: {node.Label}");

                // Ensure the TreeView has focus
                ScratchTree.Focus();
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[ScratchFiles] SelectNodeByPath: Node NOT FOUND for path: {path}");
                System.Diagnostics.Debug.WriteLine($"[ScratchFiles] Current tree has {RootNodes.Count} root nodes");

                foreach (var root in RootNodes)
                {
                    System.Diagnostics.Debug.WriteLine($"[ScratchFiles]   Root: {root.Label} with {root.Children.Count} children");
                }
            }
        }

        /// <summary>
        /// Clears IsSelected on all nodes in the tree.
        /// </summary>
        private void ClearAllSelections()
        {
            foreach (ScratchNodeBase rootNode in RootNodes)
            {
                ClearSelectionRecursive(rootNode);
            }
        }

        private static void ClearSelectionRecursive(ScratchNodeBase node)
        {
            node.IsSelected = false;

            foreach (ScratchNodeBase child in node.Children)
            {
                ClearSelectionRecursive(child);
            }
        }

        private ScratchNodeBase FindNodeByPath(string path)
        {
            foreach (ScratchNodeBase group in RootNodes)
            {
                ScratchNodeBase found = FindNodeByPathRecursive(group, path);

                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static ScratchNodeBase FindNodeByPathRecursive(ScratchNodeBase node, string path)
        {
            // Check if this node matches the path
            string nodePath = GetNodePath(node);

            if (nodePath != null && string.Equals(nodePath, path, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }

            // Check children
            foreach (ScratchNodeBase child in node.Children)
            {
                ScratchNodeBase found = FindNodeByPathRecursive(child, path);

                if (found != null)
                {
                    return found;
                }
            }

            return null;
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
                                // VS will handle the InfoBar lifecycle automatically
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
