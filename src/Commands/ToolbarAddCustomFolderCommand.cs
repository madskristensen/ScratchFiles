using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

using Microsoft.VisualStudio.Shell.Interop;

using ScratchFiles.ToolWindows;

namespace ScratchFiles.Commands
{
    /// <summary>
    /// Toolbar button: Add a custom folder root to the Scratch Files tool window.
    /// The folder is persisted globally in the extension options and shown alongside Global / Solution.
    /// </summary>
    [Command(PackageIds.ToolbarAddCustomFolder)]
    internal sealed class ToolbarAddCustomFolderCommand : BaseCommand<ToolbarAddCustomFolderCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                IVsUIShell uiShell = await VS.GetServiceAsync<SVsUIShell, IVsUIShell>();
                IntPtr ownerHwnd = IntPtr.Zero;
                uiShell?.GetDialogOwnerHwnd(out ownerHwnd);

                string selected = ShowFolderPicker(ownerHwnd, "Select a folder to add to Scratch Files");

                if (string.IsNullOrWhiteSpace(selected) || !Directory.Exists(selected))
                {
                    return;
                }

                string fullPath = Path.GetFullPath(selected);

                GeneralOptions options = await GeneralOptions.GetLiveInstanceAsync();
                List<string> current = new List<string>(options.GetCustomFolders());

                if (current.Any(p => string.Equals(p, fullPath, StringComparison.OrdinalIgnoreCase)))
                {
                    await VS.MessageBox.ShowAsync(
                        "Scratch Files",
                        "That folder is already in the list.",
                        OLEMSGICON.OLEMSGICON_INFO,
                        OLEMSGBUTTON.OLEMSGBUTTON_OK);
                    return;
                }

                current.Add(fullPath);
                options.SetCustomFolders(current);
                await options.SaveAsync();

                ScratchFilesToolWindowControl.RefreshAll();
            }
            catch (Exception ex)
            {
                await ex.LogAsync();
            }
        }

        /// <summary>
        /// Shows the modern Vista-style folder picker via IFileOpenDialog COM interop.
        /// Returns the selected folder path, or null if the user cancelled.
        /// </summary>
        private static string ShowFolderPicker(IntPtr ownerHwnd, string title)
        {
            IFileOpenDialog dialog = (IFileOpenDialog)new FileOpenDialog();
            try
            {
                dialog.SetTitle(title);
                dialog.SetOptions(FOS.FOS_PICKFOLDERS | FOS.FOS_FORCEFILESYSTEM | FOS.FOS_PATHMUSTEXIST);

                int hr = dialog.Show(ownerHwnd);
                if (hr != 0)
                {
                    return null;
                }

                dialog.GetResult(out IShellItem item);
                if (item == null)
                {
                    return null;
                }

                item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out string path);
                return path;
            }
            finally
            {
                Marshal.ReleaseComObject(dialog);
            }
        }

        #region IFileOpenDialog COM interop

        [ComImport, Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
        private class FileOpenDialog
        {
        }

        [ComImport, Guid("D57C7288-D4AD-4768-BE02-9D969532D960"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            [PreserveSig]
            int Show([In] IntPtr parent);

            void SetFileTypes();
            void SetFileTypeIndex();
            void GetFileTypeIndex();
            void Advise();
            void Unadvise();
            void SetOptions(FOS fos);
            void GetOptions(out FOS fos);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder(out IShellItem ppsi);
            void GetCurrentSelection(out IShellItem ppsi);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetResult(out IShellItem ppsi);
            void AddPlace();
            void SetDefaultExtension();
            void Close(int hr);
            void SetClientGuid();
            void ClearClientData();
            void SetFilter();
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler();
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(SIGDN sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
            void GetAttributes();
            void Compare();
        }

        [Flags]
        private enum FOS : uint
        {
            FOS_PICKFOLDERS = 0x00000020,
            FOS_FORCEFILESYSTEM = 0x00000040,
            FOS_PATHMUSTEXIST = 0x00000800,
        }

        private enum SIGDN : uint
        {
            SIGDN_FILESYSPATH = 0x80058000,
        }

        #endregion
    }
}
