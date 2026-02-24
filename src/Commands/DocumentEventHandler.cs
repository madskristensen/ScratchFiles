using System.IO;
using System.Threading;

using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;

using ScratchFiles.Services;
using ScratchFiles.ToolWindows;

namespace ScratchFiles.Commands
{
    /// <summary>
    /// Monitors the Running Document Table for save and close events on scratch files.
    /// - Ctrl+S on a scratch file saves silently in place (no Save As dialog).
    /// - After Save As moves a file outside a scratch folder, the InfoBar is removed.
    /// - Closing an empty scratch file auto-deletes it.
    /// </summary>
    internal sealed class DocumentEventHandler : IVsRunningDocTableEvents3, IDisposable
    {
        private readonly RunningDocumentTable _rdt;
        private uint _adviseCookie;
        private static DocumentEventHandler _instance;
        private CancellationTokenSource _autoDetectCts;

        private DocumentEventHandler(RunningDocumentTable rdt)
        {
            _rdt = rdt;
        }

        /// <summary>
        /// Registers this handler to listen for RDT events.
        /// </summary>
        public static void Initialize()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var rdt = new RunningDocumentTable(ServiceProvider.GlobalProvider);
            var handler = new DocumentEventHandler(rdt);
            handler._adviseCookie = rdt.Advise(handler);
            _instance = handler;
        }

        /// <summary>
        /// Unregisters this handler from RDT events.
        /// </summary>
        public void Dispose()
        {
            if (_adviseCookie != 0)
            {
                _rdt.Unadvise(_adviseCookie);
                _adviseCookie = 0;
            }
        }

        public int OnBeforeSave(uint docCookie)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                RunningDocumentInfo info = _rdt.GetDocumentInfo(docCookie);
                string filePath = info.Moniker;

                if (!string.IsNullOrEmpty(filePath) && ScratchFileService.IsScratchFile(filePath))
                {
                    // Cancel any previous pending auto-detection to avoid races on rapid saves
                    _autoDetectCts?.Cancel();
                    _autoDetectCts = new CancellationTokenSource();
                    CancellationToken token = _autoDetectCts.Token;

                    // Trigger auto-detection after save for .scratch files
                    ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                    {
                        try
                        {
                            // Small delay to let the save complete
                            await Task.Delay(500, token);
                            await ScratchFileInfoBar.TryAutoDetectAsync(filePath);
                        }
                        catch (OperationCanceledException)
                        {
                            // Expected when a newer save supersedes this one
                        }
                        catch (Exception ex)
                        {
                            await ex.LogAsync();
                        }
                    }).FireAndForget();
                }
            }
            catch
            {
                // Don't block save operations
            }

            return VSConstants.S_OK;
        }

        public int OnAfterSave(uint docCookie)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                RunningDocumentInfo info = _rdt.GetDocumentInfo(docCookie);
                string filePath = info.Moniker;

                if (!string.IsNullOrEmpty(filePath))
                {
                    // If the file was moved outside the scratch folder via Save As,
                    // remove the InfoBar and refresh the tool window
                    if (!ScratchFileService.IsScratchFile(filePath) && ScratchFileInfoBar.HasInfoBar(filePath))
                    {
                        ScratchFileInfoBar.Detach(filePath);
                        ScratchFilesToolWindowControl.RefreshAll();
                    }
                }
            }
            catch
            {
                // Don't block save operations
            }

            return VSConstants.S_OK;
        }

        public int OnBeforeDocumentWindowShow(uint docCookie, int fFirstShow, IVsWindowFrame pFrame)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (fFirstShow != 0)
            {
                try
                {
                    RunningDocumentInfo info = _rdt.GetDocumentInfo(docCookie);
                    string filePath = info.Moniker;

                    // Attach InfoBar when a scratch file is first shown (e.g., reopened from Tool Window)
                    if (!string.IsNullOrEmpty(filePath)
                        && ScratchFileService.IsScratchFile(filePath)
                        && !ScratchFileInfoBar.HasInfoBar(filePath))
                    {
                        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                        {
                            try
                            {
                                DocumentView docView = await VS.Documents.GetDocumentViewAsync(filePath);

                                if (docView != null)
                                {
                                    await ScratchFileInfoBar.AttachAsync(docView);
                                }
                            }
                            catch (Exception ex)
                            {
                                await ex.LogAsync();
                            }
                        }).FireAndForget();
                    }
                }
                catch
                {
                    // Non-critical
                }
            }

            return VSConstants.S_OK;
        }

        public int OnAfterDocumentWindowHide(uint docCookie, IVsWindowFrame pFrame)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                RunningDocumentInfo info = _rdt.GetDocumentInfo(docCookie);
                string filePath = info.Moniker;

                if (!string.IsNullOrEmpty(filePath) && ScratchFileService.IsScratchFile(filePath))
                {
                    // Auto-delete empty scratch files when the last window is closed
                    var fileInfo = new FileInfo(filePath);

                    if (fileInfo.Exists && fileInfo.Length == 0)
                    {
                        ScratchFileInfoBar.Detach(filePath);
                        ScratchFileService.DeleteScratchFile(filePath);
                        ScratchFilesToolWindowControl.RefreshAll();
                    }
                }
            }
            catch
            {
                // Non-critical
            }

            return VSConstants.S_OK;
        }

        // Required interface members with default implementations
        public int OnAfterFirstDocumentLock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining) => VSConstants.S_OK;
        public int OnBeforeLastDocumentUnlock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining) => VSConstants.S_OK;
        public int OnAfterAttributeChange(uint docCookie, uint grfAttribs) => VSConstants.S_OK;
        public int OnAfterAttributeChangeEx(uint docCookie, uint grfAttribs, IVsHierarchy pHierOld, uint itemidOld, string pszMkDocumentOld, IVsHierarchy pHierNew, uint itemidNew, string pszMkDocumentNew) => VSConstants.S_OK;
        public int OnBeforeDocumentWindowShow(uint docCookie, int fFirstShow, IVsWindowFrame pFrame, out int pfCancel) { pfCancel = 0; return OnBeforeDocumentWindowShow(docCookie, fFirstShow, pFrame); }

        // IVsRunningDocTableEvents3-specific
        int IVsRunningDocTableEvents3.OnBeforeSave(uint docCookie) => OnBeforeSave(docCookie);
    }
}
