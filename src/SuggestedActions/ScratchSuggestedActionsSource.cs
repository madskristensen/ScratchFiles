using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace ScratchFiles.SuggestedActions
{
    /// <summary>
    /// Provides the "Create scratch file from selection" action when the user has
    /// a non-empty text selection in the editor. Raises <see cref="SuggestedActionsChanged"/>
    /// on selection changes so the lightbulb appears/disappears reactively.
    /// </summary>
    internal sealed class ScratchSuggestedActionsSource : ISuggestedActionsSource2
    {
        private readonly ITextView _textView;
        private readonly ISuggestedActionCategoryRegistryService _categoryRegistry;

        public ScratchSuggestedActionsSource(
            ITextView textView,
            ISuggestedActionCategoryRegistryService categoryRegistry)
        {
            _textView = textView;
            _categoryRegistry = categoryRegistry;

            _textView.Selection.SelectionChanged += OnSelectionChanged;
        }

        public event EventHandler<EventArgs> SuggestedActionsChanged;

        public Task<ISuggestedActionCategorySet> GetSuggestedActionCategoriesAsync(
            ISuggestedActionCategorySet requestedActionCategories,
            SnapshotSpan range,
            CancellationToken cancellationToken)
        {
            if (TryGetSelectedSpan(out _))
            {
                return Task.FromResult(
                    _categoryRegistry.CreateSuggestedActionCategorySet(
                        new[] { PredefinedSuggestedActionCategoryNames.Refactoring }));
            }

            return Task.FromResult<ISuggestedActionCategorySet>(null);
        }

        public IEnumerable<SuggestedActionSet> GetSuggestedActions(
            ISuggestedActionCategorySet requestedActionCategories,
            SnapshotSpan range,
            CancellationToken cancellationToken)
        {
            if (!TryGetSelectedSpan(out SnapshotSpan selectionSpan))
            {
                return Enumerable.Empty<SuggestedActionSet>();
            }

            string selectedText = selectionSpan.GetText();
            ITextSnapshotLine startLine = selectionSpan.Snapshot.GetLineFromPosition(selectionSpan.Start);
            int firstLineColumn = selectionSpan.Start - startLine.Start;
            string dedentedText = DedentText(selectedText, firstLineColumn);
            string sourceExtension = GetSourceFileExtension();
            var action = new CreateScratchFromSelectionAction(dedentedText, sourceExtension);

            return new[]
            {
                new SuggestedActionSet(
                    PredefinedSuggestedActionCategoryNames.Refactoring,
                    new ISuggestedAction[] { action },
                    title: null,
                    priority: SuggestedActionSetPriority.Low,
                    applicableToSpan: selectionSpan)
            };
        }

        public Task<bool> HasSuggestedActionsAsync(
            ISuggestedActionCategorySet requestedActionCategories,
            SnapshotSpan range,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(TryGetSelectedSpan(out _));
        }

        public void Dispose()
        {
            _textView.Selection.SelectionChanged -= OnSelectionChanged;
        }

        public bool TryGetTelemetryId(out Guid telemetryId)
        {
            telemetryId = Guid.Empty;
            return false;
        }

        private void OnSelectionChanged(object sender, EventArgs e)
        {
            SuggestedActionsChanged?.Invoke(this, EventArgs.Empty);
        }

        private bool TryGetSelectedSpan(out SnapshotSpan span)
        {
            span = default;

            if (_textView.Selection.IsEmpty)
            {
                return false;
            }

            span = _textView.Selection.SelectedSpans.FirstOrDefault();

            if (span.IsEmpty)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(span.GetText());
        }

        private string GetSourceFileExtension()
        {
            if (_textView.TextBuffer.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument document)
                && !string.IsNullOrEmpty(document?.FilePath))
            {
                return Path.GetExtension(document.FilePath);
            }

            return null;
        }

        /// <summary>
        /// Removes the shared minimum indentation from all lines so the text starts at column zero.
        /// The first line's effective indentation includes <paramref name="firstLineColumn"/>
        /// (the number of characters before the selection on its line) so that mid-line
        /// selections preserve relative indentation.
        /// </summary>
        private static string DedentText(string text, int firstLineColumn)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            string[] lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            // Find the minimum effective indentation across non-blank lines
            int minIndent = int.MaxValue;

            for (int i = 0; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                {
                    continue;
                }

                int leadingWs = GetLeadingWhitespaceCount(lines[i]);
                int effectiveIndent = (i == 0) ? firstLineColumn + leadingWs : leadingWs;
                minIndent = Math.Min(minIndent, effectiveIndent);
            }

            if (minIndent <= 0 || minIndent == int.MaxValue)
            {
                return text;
            }

            // Dedent each line by minIndent
            var result = new string[lines.Length];

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                if (string.IsNullOrWhiteSpace(line))
                {
                    result[i] = line;
                    continue;
                }

                if (i == 0)
                {
                    // The selected text for line 0 is missing the first firstLineColumn chars
                    // of the original line, so we only strip from what's actually in the text.
                    int charsToRemove = minIndent - firstLineColumn;

                    if (charsToRemove > 0)
                    {
                        result[i] = line.Substring(Math.Min(charsToRemove, GetLeadingWhitespaceCount(line)));
                    }
                    else if (charsToRemove < 0)
                    {
                        // Selection started past the common indent; prepend to preserve relative indent
                        result[i] = new string(' ', -charsToRemove) + line;
                    }
                    else
                    {
                        result[i] = line;
                    }
                }
                else
                {
                    int leadingWs = GetLeadingWhitespaceCount(line);
                    result[i] = line.Substring(Math.Min(minIndent, leadingWs));
                }
            }

            string lineEnding = text.Contains("\r\n") ? "\r\n" : "\n";
            return string.Join(lineEnding, result);
        }

        private static int GetLeadingWhitespaceCount(string line)
        {
            int count = 0;

            while (count < line.Length && (line[count] == ' ' || line[count] == '\t'))
            {
                count++;
            }

            return count;
        }
    }
}
