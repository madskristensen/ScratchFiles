using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;

using System.Collections.Concurrent;
using System.IO;
using System.Threading;

namespace ScratchFiles.Services
{
    /// <summary>
    /// Manages scratch file auto-save and session persistence.
    /// - Tracks open scratch files in a session file
    /// - Auto-saves dirty content to disk (debounced)
    /// - Suppresses spurious "file changed externally" prompts caused by
    ///   post-save activity from other extensions, antivirus, or sync clients
    /// - Restores open files on VS startup
    /// </summary>
    internal static class ScratchSessionService
    {
        private static readonly ConcurrentDictionary<string, TrackedBuffer> _trackedBuffers
            = new ConcurrentDictionary<string, TrackedBuffer>(StringComparer.OrdinalIgnoreCase);

        // Reverse lookup for O(1) buffer-to-path mapping (avoids O(n) search on every keystroke)
        private static readonly ConcurrentDictionary<ITextBuffer, string> _bufferToPath
            = new ConcurrentDictionary<ITextBuffer, string>();

        /// <summary>
        /// Begins tracking a scratch file for auto-save and session persistence.
        /// </summary>
        public static void TrackDocument(string filePath, ITextBuffer buffer)
        {
            if (string.IsNullOrEmpty(filePath) || buffer == null)
            {
                return;
            }

            if (!ScratchFileService.IsScratchFile(filePath))
            {
                return;
            }

            // Only track global scratch files
            if (ScratchFileService.GetScope(filePath) != ScratchScope.Global)
            {
                return;
            }

            var tracked = new TrackedBuffer(filePath, buffer);

            if (_trackedBuffers.TryAdd(filePath, tracked))
            {
                _bufferToPath.TryAdd(buffer, filePath);
                buffer.Changed += OnBufferChanged;

                // Add to session tracking
                ScratchFileService.AddToSession(filePath);
            }
        }

        /// <summary>
        /// Stops tracking a scratch file.
        /// </summary>
        public static void UntrackDocument(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return;
            }

            if (_trackedBuffers.TryRemove(filePath, out TrackedBuffer tracked))
            {
                _bufferToPath.TryRemove(tracked.Buffer, out _);
                tracked.Buffer.Changed -= OnBufferChanged;
                tracked.CancelPendingSync();
                tracked.Dispose();

                // Remove from session tracking
                ScratchFileService.RemoveFromSession(filePath);
            }
        }

        /// <summary>
        /// Restores scratch files from the previous session.
        /// </summary>
        public static async Task RestoreSessionAsync()
        {
            System.Collections.Generic.IReadOnlyList<string> sessionFiles;

            try
            {
                sessionFiles = await Task.Run(() => ScratchFileService.GetSessionFiles());
            }
            catch (Exception ex)
            {
                await ex.LogAsync();
                return;
            }

            if (sessionFiles.Count == 0)
            {
                return;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // Clear the session file - we'll rebuild it as files are opened
            ScratchFileService.ClearSession();

            foreach (string filePath in sessionFiles)
            {
                try
                {
                    // Only restore if the file still exists
                    if (await Task.Run(() => System.IO.File.Exists(filePath)))
                    {
                        await OpenAndTrackFileAsync(filePath);

                        // Yield to allow VS to process other messages between opens
                        await Task.Yield();
                    }
                }
                catch (Exception ex)
                {
                    await ex.LogAsync();
                }
            }
        }

        /// <summary>
        /// Opens a scratch file and starts tracking it for auto-save.
        /// </summary>
        public static async Task OpenAndTrackFileAsync(string filePath)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            DocumentView docView = await VS.Documents.OpenAsync(filePath);

            if (docView?.TextView?.TextBuffer == null)
            {
                return;
            }

            // Start tracking for auto-save
            TrackDocument(filePath, docView.TextView.TextBuffer);
        }

        private static void OnBufferChanged(object sender, TextContentChangedEventArgs e)
        {
            if (!(sender is ITextBuffer buffer))
            {
                return;
            }

            // O(1) lookup using reverse dictionary instead of O(n) iteration
            if (_bufferToPath.TryGetValue(buffer, out string filePath) &&
                _trackedBuffers.TryGetValue(filePath, out TrackedBuffer tracked))
            {
                ScheduleDebouncedAutoSave(filePath, tracked);
            }
        }

        private static void ScheduleDebouncedAutoSave(string filePath, TrackedBuffer tracked)
        {
            // Cancel any pending auto-save
            tracked.CancelPendingSync();

            var cts = new CancellationTokenSource();
            tracked.SyncCts = cts;

            // Auto-save after 3 seconds of idle
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    await Task.Delay(3000, cts.Token);

                    if (!cts.Token.IsCancellationRequested)
                    {
                        await AutoSaveAsync(filePath);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected when newer changes supersede this save
                }
                catch (Exception ex)
                {
                    await ex.LogAsync();
                }
            }).FireAndForget();
        }

        private static async Task AutoSaveAsync(string filePath)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                DocumentView docView = await VS.Documents.GetDocumentViewAsync(filePath);

                if (docView?.Document == null || docView.TextBuffer == null)
                {
                    return;
                }

                // Skip if the buffer is in the middle of an edit (e.g. another
                // extension is mutating it). Saving mid-edit can produce a
                // snapshot that doesn't match the buffer afterwards, which
                // some watchers interpret as an external change.
                if (docView.TextBuffer.EditInProgress)
                {
                    return;
                }

                // Only save when actually dirty. Re-saving a clean document
                // bumps the file timestamp for no reason and can race with
                // post-save activity (HTML preview generation, AV scanners,
                // OneDrive, etc.) that then trips VS's external-change check.
                ITextDocument textDoc;
                docView.TextBuffer.Properties.TryGetProperty(typeof(ITextDocument), out textDoc);

                if (textDoc != null && !textDoc.IsDirty)
                {
                    return;
                }

                IVsFileChangeEx fileChange = await VS.GetServiceAsync<SVsFileChangeEx, IVsFileChangeEx>();

                // Tell VS to ignore disk-change notifications for this file
                // while we save. This prevents transient timestamp/content
                // diffs (BOM, CRLF normalization, post-save extensions) from
                // surfacing as a "file changed externally" prompt.
                fileChange?.IgnoreFile(0, filePath, 1);

                try
                {
                    docView.Document.Save();
                }
                finally
                {
                    fileChange?.IgnoreFile(0, filePath, 0);

                    // Re-sync VS's last-known timestamp with what's now on
                    // disk. Without this, anything that touches the file
                    // after our save (antivirus, sync clients, MDE2's TOC
                    // rewrite, etc.) can make the next user edit trigger
                    // the reload prompt.
                    try
                    {
                        if (File.Exists(filePath))
                        {
                            DateTime stamp = File.GetLastWriteTime(filePath);
                            docView.Document.UpdateDirtyState(false, stamp);
                            fileChange?.SyncFile(filePath);
                        }
                    }
                    catch (Exception syncEx)
                    {
                        await syncEx.LogAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                await ex.LogAsync();
            }
        }

        private sealed class TrackedBuffer : IDisposable
        {
            public string FilePath { get; }
            public ITextBuffer Buffer { get; }
            public CancellationTokenSource SyncCts { get; set; }

            public TrackedBuffer(string filePath, ITextBuffer buffer)
            {
                FilePath = filePath;
                Buffer = buffer;
            }

            public void CancelPendingSync()
            {
                SyncCts?.Cancel();
                SyncCts?.Dispose();
                SyncCts = null;
            }

            public void Dispose()
            {
                CancelPendingSync();
            }
        }
    }
}
