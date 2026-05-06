using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

using Microsoft.VisualStudio.Shell;

namespace ScratchFiles
{
    /// <summary>
    /// Options page for Tools > Options > Scratch Files.
    /// </summary>
    internal sealed class GeneralOptions : BaseOptionModel<GeneralOptions>
    {
        [Category("General")]
        [DisplayName("Override Ctrl+N")]
        [Description("Replace the default File > New File template dialog with instant scratch file creation. Requires restart.")]
        [DefaultValue(true)]
        public bool OverrideCtrlN { get; set; } = true;

        /// <summary>
        /// Newline-separated list of additional folders to show as roots in the Scratch Files tool window.
        /// Managed via the tool window toolbar; not shown in the Tools &gt; Options grid.
        /// </summary>
        [Browsable(false)]
        [DefaultValue("")]
        public string CustomFolders { get; set; } = string.Empty;

        /// <summary>
        /// Returns the parsed, de-duplicated list of custom folder paths.
        /// </summary>
        public IReadOnlyList<string> GetCustomFolders()
        {
            if (string.IsNullOrWhiteSpace(CustomFolders))
            {
                return Array.Empty<string>();
            }

            return CustomFolders
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        /// <summary>
        /// Replaces the custom folder list and persists it.
        /// </summary>
        public void SetCustomFolders(IEnumerable<string> folders)
        {
            CustomFolders = folders == null
                ? string.Empty
                : string.Join(Environment.NewLine, folders
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Select(p => p.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase));
        }
    }
}
