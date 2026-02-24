using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Utilities;

using ScratchFiles.Services;

namespace ScratchFiles.UI
{
    /// <summary>
    /// VS-themed dialog for selecting a file language/extension.
    /// Shows a curated list of top-used extensions plus all extensions registered in VS.
    /// The user can also type a custom extension directly into the filter TextBox.
    /// </summary>
    public partial class LanguagePickerDialog : Window
    {
        private readonly ObservableCollection<LanguageItem> _allItems = new ObservableCollection<LanguageItem>();
        private readonly ICollectionView _filteredView;

        public LanguagePickerDialog()
        {
            InitializeComponent();

            _filteredView = CollectionViewSource.GetDefaultView(_allItems);
            _filteredView.Filter = FilterItem;
            ResultsListBox.ItemsSource = _filteredView;

            Loaded += OnLoaded;
        }

        /// <summary>
        /// Gets the selected file extension (e.g., ".cs"), or null if cancelled.
        /// </summary>
        public string SelectedExtension { get; private set; }

        /// <summary>
        /// Shows the dialog and returns the selected extension, or null if cancelled.
        /// </summary>
        public static string Show(Window owner = null)
        {
            var dialog = new LanguagePickerDialog();

            if (owner != null)
            {
                dialog.Owner = owner;
            }
            else
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            bool? result = dialog.ShowDialog();

            return result == true ? dialog.SelectedExtension : null;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            PopulateLanguages();

            FilterTextBox.Focus();

            UpdateStatus();

            if (_allItems.Count > 0)
            {
                ResultsListBox.SelectedIndex = 0;
            }
        }

        private void PopulateLanguages()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // Start with curated top-used languages (these appear first)
            var topExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".cs", ".vb", ".json", ".xml", ".html", ".css", ".js", ".ts",
                ".sql", ".ps1", ".md", ".yaml", ".xaml", ".razor", ".bicep",
                ".txt", ".csv", ".config", ".csproj", ".sln"
            };

            var topLanguages = new List<LanguageItem>
            {
                new LanguageItem("C#", ".cs"),
                new LanguageItem("Visual Basic", ".vb"),
                new LanguageItem("JSON", ".json"),
                new LanguageItem("XML", ".xml"),
                new LanguageItem("HTML", ".html"),
                new LanguageItem("CSS", ".css"),
                new LanguageItem("JavaScript", ".js"),
                new LanguageItem("TypeScript", ".ts"),
                new LanguageItem("SQL", ".sql"),
                new LanguageItem("PowerShell", ".ps1"),
                new LanguageItem("Markdown", ".md"),
                new LanguageItem("YAML", ".yaml"),
                new LanguageItem("XAML", ".xaml"),
                new LanguageItem("Razor", ".razor"),
                new LanguageItem("Bicep", ".bicep"),
                new LanguageItem("Plain Text", ".txt"),
            };

            // Resolve icons for the top items
            foreach (LanguageItem item in topLanguages)
            {
                item.IconMoniker = FileIconService.GetImageMonikerForFile("file" + item.Extension);
                _allItems.Add(item);
            }

            // Query VS for all registered file extensions via IContentTypeRegistryService + IFileExtensionRegistryService
            try
            {
                var componentModel = VS.GetRequiredService<
                    Microsoft.VisualStudio.ComponentModelHost.SComponentModel,
                    Microsoft.VisualStudio.ComponentModelHost.IComponentModel>();

                var extensionRegistry = componentModel.GetService<IFileExtensionRegistryService>();
                var contentTypeRegistry = componentModel.GetService<IContentTypeRegistryService>();

                if (extensionRegistry != null && contentTypeRegistry != null)
                {
                    var seenExtensions = new HashSet<string>(topExtensions, StringComparer.OrdinalIgnoreCase);

                    foreach (IContentType contentType in contentTypeRegistry.ContentTypes)
                    {
                        if (contentType == contentTypeRegistry.UnknownContentType)
                        {
                            continue;
                        }

                        IEnumerable<string> extensions = extensionRegistry.GetExtensionsForContentType(contentType);

                        foreach (string ext in extensions)
                        {
                            string dotExt = ext.StartsWith(".", StringComparison.Ordinal) ? ext : "." + ext;

                            if (seenExtensions.Add(dotExt))
                            {
                                string displayName = contentType.DisplayName;

                                if (string.IsNullOrWhiteSpace(displayName) || displayName == "UNKNOWN")
                                {
                                    displayName = dotExt.TrimStart('.').ToUpperInvariant();
                                }

                                var item = new LanguageItem(displayName, dotExt)
                                {
                                    IconMoniker = FileIconService.GetImageMonikerForFile("file" + dotExt)
                                };

                                _allItems.Add(item);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to enumerate VS content types: {ex.Message}");
            }
        }

        private bool FilterItem(object obj)
        {
            if (obj is LanguageItem item)
            {
                string filterText = FilterTextBox.Text?.Trim();

                if (string.IsNullOrEmpty(filterText))
                {
                    return true;
                }

                return item.DisplayName.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0
                    || item.Extension.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0;
            }

            return false;
        }

        private void FilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Toggle placeholder visibility
            PlaceholderText.Visibility = string.IsNullOrEmpty(FilterTextBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;

            _filteredView.Refresh();
            UpdateStatus();

            if (ResultsListBox.Items.Count > 0 && ResultsListBox.SelectedIndex < 0)
            {
                ResultsListBox.SelectedIndex = 0;
            }
        }

        private void UpdateStatus()
        {
            int visible = ResultsListBox.Items.Count;
            int total = _allItems.Count;

            StatusText.Text = visible == total
                ? $"{total} languages"
                : $"{visible} of {total} languages";
        }

        private void ResultsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            AcceptSelection();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            AcceptSelection();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                AcceptSelection();
                e.Handled = true;
            }
            else if (e.Key == Key.Down && ResultsListBox.Items.Count > 0)
            {
                if (ResultsListBox.SelectedIndex < ResultsListBox.Items.Count - 1)
                {
                    ResultsListBox.SelectedIndex++;
                    ResultsListBox.ScrollIntoView(ResultsListBox.SelectedItem);
                }

                e.Handled = true;
            }
            else if (e.Key == Key.Up && ResultsListBox.Items.Count > 0)
            {
                if (ResultsListBox.SelectedIndex > 0)
                {
                    ResultsListBox.SelectedIndex--;
                    ResultsListBox.ScrollIntoView(ResultsListBox.SelectedItem);
                }

                e.Handled = true;
            }
        }

        private void AcceptSelection()
        {
            // Priority: selected item in the list, then typed text in the filter box
            if (ResultsListBox.SelectedItem is LanguageItem selectedItem)
            {
                SelectedExtension = selectedItem.Extension;
            }
            else
            {
                string typed = FilterTextBox.Text?.Trim();

                if (!string.IsNullOrEmpty(typed))
                {
                    // Check if the typed text matches a known item
                    LanguageItem match = _allItems.FirstOrDefault(i =>
                        i.DisplayName.Equals(typed, StringComparison.OrdinalIgnoreCase)
                        || i.Extension.Equals(typed, StringComparison.OrdinalIgnoreCase));

                    if (match != null)
                    {
                        SelectedExtension = match.Extension;
                    }
                    else
                    {
                        // Treat as a custom extension — normalize to include leading dot
                        SelectedExtension = typed.StartsWith(".", StringComparison.Ordinal) ? typed : "." + typed;
                    }
                }
            }

            if (!string.IsNullOrEmpty(SelectedExtension))
            {
                DialogResult = true;
                Close();
            }
        }
    }

    /// <summary>
    /// Represents a single language/extension entry in the picker dialog.
    /// </summary>
    internal sealed class LanguageItem
    {
        public LanguageItem(string displayName, string extension)
        {
            DisplayName = displayName;
            Extension = extension;
        }

        public string DisplayName { get; }
        public string Extension { get; }
        public ImageMoniker IconMoniker { get; set; }

        public override string ToString() => $"{DisplayName} ({Extension})";
    }
}
