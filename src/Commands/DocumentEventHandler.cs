using System.IO;
using System.Threading;

using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;

using ScratchFiles.Services;
using ScratchFiles.ToolWindows;

namespace ScratchFiles.Commands
{
    /// <summary>
    /// Monitors the Running Document Table for save and close events on scratch files.
    /// </summary>
    internal sealed class DocumentEventHandler : IVsRunningDocTableEvents3, IDisposable
    {
        private readonly RunningDocumentTable _rdt;
        private readonly object _ctsLock = new object();
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
            lock (_ctsLock)
            {
                _autoDetectCts?.Cancel();
                _autoDetectCts?.Dispose();
                _autoDetectCts = null;
            }

            if (_adviseCookie != 0)
            {
                _rdt.Unadvise(_adviseCookie);
                _adviseCookie = 0;
            }
        }

        /// <summary>
        /// Shuts down the static instance. Call from package dispose.
        /// </summary>
        public static void Shutdown()
        {
            _instance?.Dispose();
            _instance = null;
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
                    CancellationToken token;

                    lock (_ctsLock)
                    {
                        _autoDetectCts?.Cancel();
                        _autoDetectCts?.Dispose();
                        _autoDetectCts = new CancellationTokenSource();
                        token = _autoDetectCts.Token;
                    }

                    ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                    {
                        try
                        {
                            await Task.Delay(500, token);
                            await ScratchFileInfoBar.TryAutoDetectAsync(filePath);
                        }
                        catch (OperationCanceledException)
                        {
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
                    if (!ScratchFileService.IsScratchFile(filePath) && ScratchFileInfoBar.HasInfoBar(filePath))
                    {
                        ScratchFileInfoBar.Detach(filePath);
                        ScratchFilesToolWindowControl.RefreshAll();
                    }
                }
            }
            catch
            {
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

                    if (!string.IsNullOrEmpty(filePath) && ScratchFileService.IsScratchFile(filePath))
                    {
                        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                        {
                            try
                            {
                                DocumentView docView = await VS.Documents.GetDocumentViewAsync(filePath);

                                if (docView != null)
                                {
                                    if (!ScratchFileInfoBar.HasInfoBar(filePath))
                                    {
                                        await ScratchFileInfoBar.AttachAsync(docView, filePath);
                                    }

                                    ITextBuffer buffer = docView.TextView?.TextBuffer;

                                    if (buffer != null)
                                    {
                                        ScratchSessionService.TrackDocument(filePath, buffer);
                                    }
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
                    ScratchSessionService.UntrackDocument(filePath);

                    ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                    {
                        try
                        {
                            bool shouldDelete = await Task.Run(() =>
                            {
                                var fileInfo = new FileInfo(filePath);
                                return fileInfo.Exists && fileInfo.Length == 0;
                            });

                            if (shouldDelete)
                            {
                                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                                ScratchFileInfoBar.Detach(filePath);
                                await Task.Run(() => ScratchFileService.DeleteScratchFile(filePath));
                                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                                ScratchFilesToolWindowControl.RefreshAll();
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
            }

            return VSConstants.S_OK;
        }

        public int OnAfterFirstDocumentLock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining) => VSConstants.S_OK;
        public int OnBeforeLastDocumentUnlock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining) => VSConstants.S_OK;
        public int OnAfterAttributeChange(uint docCookie, uint grfAttribs) => VSConstants.S_OK;
        public int OnAfterAttributeChangeEx(uint docCookie, uint grfAttribs, IVsHierarchy pHierOld, uint itemidOld, string pszMkDocumentOld, IVsHierarchy pHierNew, uint itemidNew, string pszMkDocumentNew) => VSConstants.S_OK;
        public int OnBeforeDocumentWindowShow(uint docCookie, int fFirstShow, IVsWindowFrame pFrame, out int pfCancel) { pfCancel = 0; return OnBeforeDocumentWindowShow(docCookie, fFirstShow, pFrame); }

        int IVsRunningDocTableEvents3.OnBeforeSave(uint docCookie) => OnBeforeSave(docCookie);
    }
}
