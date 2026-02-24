using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace ScratchFiles.UI
{
    /// <summary>
    /// VS-themed input dialog for prompting the user to enter text.
    /// Replaces Microsoft.VisualBasic.Interaction.InputBox with proper theming and DPI support.
    /// </summary>
    public partial class InputDialog : Window
    {
        public InputDialog()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        /// <summary>
        /// Gets the text entered by the user, or null if cancelled.
        /// </summary>
        public string InputText { get; private set; }

        /// <summary>
        /// Shows the dialog and returns the entered text, or null if cancelled.
        /// </summary>
        /// <param name="prompt">The prompt message to display.</param>
        /// <param name="title">The dialog title.</param>
        /// <param name="defaultValue">The default value for the input field.</param>
        /// <param name="owner">The owner window (optional). If null, uses VS main window.</param>
        /// <returns>The entered text, or null if cancelled.</returns>
        public static string Show(string prompt, string title, string defaultValue = "", Window owner = null)
        {
            var dialog = new InputDialog
            {
                Title = title
            };

            dialog.PromptText.Text = prompt;
            dialog.InputTextBox.Text = defaultValue;

            // Try to get VS main window as owner if none provided
            if (owner != null)
            {
                dialog.Owner = owner;
            }
            else
            {
                try
                {
                    // Get VS main window handle and set as owner
                    nint hwnd = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;

                    if (hwnd != 0)
                    {
                        var helper = new WindowInteropHelper(dialog)
                        {
                            Owner = hwnd
                        };
                    }
                }
                catch
                {
                    // Fall back to center screen if we can't get VS window
                    dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }
            }

            bool? result = dialog.ShowDialog();

            return result == true ? dialog.InputText : null;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            InputTextBox.Focus();
            InputTextBox.SelectAll();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
                e.Handled = true;
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
            {
                DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            InputText = InputTextBox.Text;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
