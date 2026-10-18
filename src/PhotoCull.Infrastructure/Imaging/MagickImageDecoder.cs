using System;
using System.IO;
using System.Threading.Tasks;
using ImageMagick;
using PhotoCull.Core.Abstractions;

namespace PhotoCull.Infrastructure.Imaging
{
    public class MagickImageDecoder : IImageDecoder
    {
        private readonly int _defaultMax = 2048;

        public MagickImageDecoder() { }

        public async Task<MemoryStream?> DecodeToJpegAsync(Stream input, int maxLongSide = 2048, string? extension = null, bool forceMagick = false)
        {
            if (input == null) return null;
            if (maxLongSide <= 0) maxLongSide = _defaultMax;

            try
            {
                // Read input into memory first
                using var ms = new MemoryStream();
                if (input.CanSeek)
                {
                    input.Position = 0;
                }
                await input.CopyToAsync(ms).ConfigureAwait(false);
                var bytes = ms.ToArray();

                // Use Magick to read image (this supports HEIC and many RAW formats if supported by native delegates)
                using var images = new MagickImageCollection();
                // Read may throw if format not supported; let caller handle null
                images.Read(bytes);
                if (images.Count == 0) return null;

                // Prefer the first image/frame (embedded preview often appears first for RAW)
                using var img = (MagickImage)images[0].Clone();

                // Auto-orient based on EXIF
                try { img.AutoOrient(); } catch { /* tolerate */ }

                // Resize if needed while preserving aspect ratio
                var longSide = Math.Max(img.Width, img.Height);
                if (longSide > maxLongSide)
                {
                    double ratio = (double)maxLongSide / longSide;
                    int newW = (int)Math.Max(1, Math.Round(img.Width * ratio));
                    int newH = (int)Math.Max(1, Math.Round(img.Height * ratio));
                    img.Resize(newW, newH);
                }

                img.Format = MagickFormat.Jpeg;
                img.Quality = 85;

                var outMs = new MemoryStream();
                img.Write(outMs);
                outMs.Position = 0;
                return outMs;
            }
            catch
            {
                return null;
            }
        }
    }
}
