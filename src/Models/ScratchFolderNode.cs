using System.IO;

using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;

using ScratchFiles.Services;

namespace ScratchFiles.Models
{
    /// <summary>
    /// Represents a user-created sub-folder within a scratch scope directory.
    /// </summary>
    internal sealed class ScratchFolderNode : ScratchNodeBase
    {
        public ScratchFolderNode(string folderPath, ScratchScope scope)
            : base(Path.GetFileName(folderPath))
        {
            FolderPath = folderPath;
            Scope = scope;
            IsExpanded = true;
        }

        public string FolderPath { get; }

        public ScratchScope Scope { get; }

        public override ImageMoniker IconMoniker => KnownMonikers.FolderOpened;

        public override bool SupportsChildren => true;

        public override int ContextMenuId => PackageIds.FolderContextMenu;
    }
}
