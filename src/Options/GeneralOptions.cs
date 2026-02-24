using System.ComponentModel;

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
    }
}
