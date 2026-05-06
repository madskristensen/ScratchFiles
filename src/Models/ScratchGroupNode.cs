using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;

using ScratchFiles.Services;

namespace ScratchFiles.Models
{
    /// <summary>
    /// Represents a root group header node ("Global" or "Solution") in the Scratch Files tree view.
    /// </summary>
    internal sealed class ScratchGroupNode : ScratchNodeBase
    {
        public ScratchGroupNode(string label, ScratchScope scope, string folderPath, bool isCustom = false)
            : base(label)
        {
            Scope = scope;
            FolderPath = folderPath;
            IsCustom = isCustom;
            IsExpanded = true;
        }

        public ScratchScope Scope { get; }

        public string FolderPath { get; }

        /// <summary>
        /// True for user-added custom folder roots; false for the built-in Global/Solution roots.
        /// </summary>
        public bool IsCustom { get; }

        public override ImageMoniker IconMoniker => KnownMonikers.FolderOpened;

        public override bool SupportsChildren => true;

        public override int ContextMenuId => PackageIds.FolderContextMenu;
    }
}
