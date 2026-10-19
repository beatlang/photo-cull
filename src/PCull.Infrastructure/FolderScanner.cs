using PCull.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PCull.Infrastructure
{
    public class FolderScanner : IFolderScanner
    {
        private static readonly HashSet<string> _extensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".heic", ".webp", ".tiff", ".tif",
            ".cr2", ".cr3", ".dng", ".nef", ".arw", ".orf", ".raf"
        };

        public async IAsyncEnumerable<ImageRecord> ScanAsync(string folder, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(folder))
                yield break;

            if (!Directory.Exists(folder))
                yield break;

            var files = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                .Where(f => _extensions.Contains(Path.GetExtension(f))).ToList();

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                FileInfo fi;
                try { fi = new FileInfo(file); }
                catch { continue; }

                int width = 0, height = 0;
                string formatName = Path.GetExtension(file).TrimStart('.').ToUpperInvariant();

                try
                {
                    // Try to identify image metadata without fully decoding large images.
                    using var stream = File.OpenRead(file);
                    var info = await Image.IdentifyAsync(stream, cancellationToken);
                    if (info != null)
                    {
                        width = info.Width;
                        height = info.Height;
                    }
                }
                catch
                {
                    // ImageSharp might not support HEIC/RAW; skip metadata in that case (TODO: add external decoders)
                }

                yield return new ImageRecord(0, file, fi.Length, width, height, formatName, fi.CreationTimeUtc, fi.LastWriteTimeUtc);
            }
        }
    }
}
