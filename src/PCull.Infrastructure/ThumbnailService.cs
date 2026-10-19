using PCull.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PCull.Infrastructure
{
    public class ThumbnailService : IThumbnailService
    {
        private readonly int _maxSize;
        private readonly PCull.Core.Abstractions.IImageDecoder? _decoder;

        public ThumbnailService(int maxSize = 512, PCull.Core.Abstractions.IImageDecoder? decoder = null)
        {
            _maxSize = maxSize;
            _decoder = decoder;
        }

        public async Task<string?> GenerateThumbnailAsync(string imagePath, string cacheDirectory, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
                return null;

            if (string.IsNullOrWhiteSpace(cacheDirectory))
                throw new ArgumentException("cacheDirectory");

            Directory.CreateDirectory(cacheDirectory);

            var fi = new FileInfo(imagePath);
            var key = imagePath + "|" + fi.LastWriteTimeUtc.Ticks;
            string hash;
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
                hash = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
            }

            var cached = Path.Combine(cacheDirectory, hash + ".jpg");
            if (File.Exists(cached))
                return cached;

            try
            {
                using var stream = File.OpenRead(imagePath);
                using var image = await Image.LoadAsync(stream, cancellationToken);

                var size = Math.Max(image.Width, image.Height);
                if (size > _maxSize)
                {
                    var ratio = (double)_maxSize / size;
                    var width = (int)Math.Round(image.Width * ratio);
                    var height = (int)Math.Round(image.Height * ratio);
                    image.Mutate(x => x.Resize(width, height));
                }

                var encoder = new JpegEncoder { Quality = 85 };
                await image.SaveAsJpegAsync(cached, encoder, cancellationToken);
                return cached;
            }
            catch (SixLabors.ImageSharp.UnknownImageFormatException)
            {
                // Unsupported format (HEIC/RAW) - try external decoder if available
                if (_decoder != null)
                {
                    try
                    {
                        using var fs = File.OpenRead(imagePath);
                        var decoded = await _decoder.DecodeToJpegAsync(fs, _maxSize, Path.GetExtension(imagePath));
                        if (decoded != null)
                        {
                            decoded.Position = 0;
                            using var outFs = File.Create(cached);
                            await decoded.CopyToAsync(outFs, cancellationToken);
                            return cached;
                        }
                    }
                    catch
                    {
                        // tolerant
                    }
                }
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
            catch
            {
                // On any other error, bubble up? For service we return null to be tolerant.
                return null;
            }
        }
    }
}
