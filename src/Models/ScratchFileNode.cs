using System.IO;

using Microsoft.VisualStudio.Imaging.Interop;

using ScratchFiles.Services;

namespace ScratchFiles.Models
{
    /// <summary>
    /// Represents an individual scratch file in the Scratch Files tree view.
    /// Icon is resolved from IVsImageService2 based on the file extension.
    /// </summary>
    internal sealed class ScratchFileNode : ScratchNodeBase
    {
        private string _filePath;
        private ImageMoniker _iconMoniker;

        public ScratchFileNode(string filePath, ScratchScope scope)
            : base(Path.GetFileName(filePath))
        {
            _filePath = filePath;
            Scope = scope;
        }

        public string FilePath
        {
            get => _filePath;
            set
            {
                if (SetProperty(ref _filePath, value))
                {
                    Label = Path.GetFileName(value);
                    _iconMoniker = default;
                    OnPropertyChanged(nameof(IconMoniker));
                    OnPropertyChanged(nameof(Description));
                }
            }
        }

        public ScratchScope Scope { get; }

        public override ImageMoniker IconMoniker
        {
            get
            {
                if (_iconMoniker.Id == 0 && _iconMoniker.Guid == System.Guid.Empty)
                {
                    string ext = Path.GetExtension(_filePath);

                    _iconMoniker = string.Equals(ext, ".scratch", System.StringComparison.OrdinalIgnoreCase)
                        ? FileIconService.GetScratchMoniker()
                        : FileIconService.GetImageMonikerForFile(Path.GetFileName(_filePath));
                }

                return _iconMoniker;
            }
        }

        public override bool SupportsChildren => false;

        public override string Description
        {
            get
            {
                string ext = Path.GetExtension(_filePath);

                return string.Equals(ext, ".scratch", System.StringComparison.OrdinalIgnoreCase)
                    ? "Plain Text"
                    : ext.TrimStart('.').ToUpperInvariant();
            }
        }

        public override int ContextMenuId => PackageIds.TreeContextMenu;
    }
}
