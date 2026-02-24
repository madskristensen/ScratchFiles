using System.ComponentModel.Composition;

using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace ScratchFiles.SuggestedActions
{
    /// <summary>
    /// MEF export that provides the "Create scratch file from selection" lightbulb action
    /// whenever the user has a non-empty text selection in any editor.
    /// </summary>
    [Export(typeof(ISuggestedActionsSourceProvider))]
    [Name("Scratch Files Suggested Actions")]
    [ContentType("text")]
    internal sealed class ScratchSuggestedActionsSourceProvider : ISuggestedActionsSourceProvider
    {
        [Import]
        internal ISuggestedActionCategoryRegistryService CategoryRegistry { get; set; }

        public ISuggestedActionsSource CreateSuggestedActionsSource(ITextView textView, ITextBuffer textBuffer)
        {
            if (textView == null || textBuffer == null)
            {
                return null;
            }

            return new ScratchSuggestedActionsSource(textView, CategoryRegistry);
        }
    }
}
