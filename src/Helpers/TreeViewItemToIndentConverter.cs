using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace ScratchFiles.Helpers
{
    /// <summary>
    /// Converts a <see cref="TreeViewItem"/> to a <see cref="Thickness"/> whose left value
    /// is proportional to the item's nesting depth. This allows the full-width selection
    /// highlight while keeping content properly indented.
    /// Ported from the Azure Explorer extension's tree view pattern.
    /// </summary>
    internal sealed class TreeViewItemToIndentConverter : IValueConverter
    {
        private const double IndentSize = 19.0;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is TreeViewItem item)
            {
                double indent = GetDepth(item) * IndentSize;

                if (parameter is string str && str.Equals("Negate", StringComparison.OrdinalIgnoreCase))
                {
                    return new Thickness(-indent, 0, 0, 0);
                }

                return new Thickness(indent, 0, 0, 0);
            }

            return new Thickness(0);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        private static int GetDepth(TreeViewItem item)
        {
            int depth = 0;
            DependencyObject parent = VisualTreeHelper.GetParent(item);

            while (parent != null)
            {
                if (parent is TreeViewItem)
                {
                    depth++;
                }

                if (parent is TreeView)
                {
                    break;
                }

                parent = VisualTreeHelper.GetParent(parent);
            }

            return depth;
        }
    }
}
