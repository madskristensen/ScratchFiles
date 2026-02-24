using System.Collections.Generic;

using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Shell.Interop;

namespace ScratchFiles.Services
{
    /// <summary>
    /// Provides file extension icons by querying IVsImageService2 for the moniker
    /// that VS would normally show for a given filename. Results are cached per extension.
    /// </summary>
    internal static class FileIconService
    {
        private static readonly Dictionary<string, ImageMoniker> _cache = new Dictionary<string, ImageMoniker>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Returns the VS image moniker for the given filename based on its extension.
        /// Falls back to KnownMonikers.Document if the image service is unavailable.
        /// </summary>
        public static ImageMoniker GetImageMonikerForFile(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return KnownMonikers.Document;
            }

            string extension = System.IO.Path.GetExtension(fileName);

            if (string.IsNullOrEmpty(extension))
            {
                return KnownMonikers.Document;
            }

            if (_cache.TryGetValue(extension, out ImageMoniker cached))
            {
                return cached;
            }

            ImageMoniker moniker = ResolveMoniker(fileName);
            _cache[extension] = moniker;
            return moniker;
        }

        /// <summary>
        /// Returns the VS image moniker for a group/folder node.
        /// </summary>
        public static ImageMoniker GetFolderMoniker()
        {
            return KnownMonikers.FolderOpened;
        }

        /// <summary>
        /// Returns the VS image moniker for the scratch file default (.scratch) extension.
        /// </summary>
        public static ImageMoniker GetScratchMoniker()
        {
            return KnownMonikers.Document;
        }

        private static ImageMoniker ResolveMoniker(string fileName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var imageService = ServiceProvider.GlobalProvider.GetService(typeof(SVsImageService)) as IVsImageService2;

                if (imageService != null)
                {
                    ImageMoniker moniker = imageService.GetImageMonikerForFile(fileName);

                    if (moniker.Id != 0 || moniker.Guid != System.Guid.Empty)
                    {
                        return moniker;
                    }
                }
            }
            catch
            {
                // Fall through to default
            }

            return KnownMonikers.Document;
        }
    }
}
