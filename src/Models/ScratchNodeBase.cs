using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using Microsoft.VisualStudio.Imaging.Interop;

namespace ScratchFiles.Models
{
    /// <summary>
    /// Base class for all nodes in the Scratch Files tool window tree view.
    /// Provides INPC, icon moniker, label, and child collection.
    /// </summary>
    internal abstract class ScratchNodeBase : INotifyPropertyChanged
    {
        private string _label;
        private bool _isExpanded;
        private bool _isVisible = true;
        private bool _isSelected;

        protected ScratchNodeBase(string label)
        {
            _label = label;
        }

        public string Label
        {
            get => _label;
            set => SetProperty(ref _label, value);
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        /// <summary>
        /// Controls whether this node is visible in the tree view (used by search filtering).
        /// </summary>
        public bool IsVisible
        {
            get => _isVisible;
            set => SetProperty(ref _isVisible, value);
        }

        /// <summary>
        /// Controls whether this node is selected in the tree view.
        /// </summary>
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        /// <summary>
        /// The icon displayed next to this node in the tree view.
        /// </summary>
        public abstract ImageMoniker IconMoniker { get; }

        /// <summary>
        /// Whether this node can contain children.
        /// </summary>
        public abstract bool SupportsChildren { get; }

        /// <summary>
        /// Secondary description text shown after the label.
        /// </summary>
        public virtual string Description => null;

        /// <summary>
        /// The VSCT context menu ID to show on right-click. Return 0 for no menu.
        /// </summary>
        public virtual int ContextMenuId => 0;

        public ObservableCollection<ScratchNodeBase> Children { get; } = new ObservableCollection<ScratchNodeBase>();

        public event PropertyChangedEventHandler PropertyChanged;

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
