using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;

using ScratchFiles.Services;

namespace ScratchFiles.Models
{
    /// <summary>
    /// Represents a group header node ("Global" or "Solution") in the Scratch Files tree view.
    /// </summary>
    internal sealed class ScratchGroupNode : ScratchNodeBase
    {
        public ScratchGroupNode(string label, ScratchScope scope)
            : base(label)
        {
            Scope = scope;
            IsExpanded = true;
        }

        public ScratchScope Scope { get; }

        public override ImageMoniker IconMoniker => KnownMonikers.FolderOpened;

        public override bool SupportsChildren => true;
    }
}
