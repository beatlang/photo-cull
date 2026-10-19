using PCull.Core;
using ImageMagick;
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
                cancellationToken.ThrowIfCancellationRequested();
                using var stream = File.OpenRead(imagePath);
                using var memory = new MemoryStream();
                await stream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
                using var image = new MagickImage(memory.ToArray());
                image.AutoOrient();

                var size = Math.Max(image.Width, image.Height);
                if (size > _maxSize)
                {
                    var ratio = (double)_maxSize / size;
                    var width = (uint)Math.Max(1, Math.Round(image.Width * ratio));
                    var height = (uint)Math.Max(1, Math.Round(image.Height * ratio));
                    image.Resize(width, height, FilterType.Cubic);
                }

                image.Format = MagickFormat.Jpeg;
                image.Quality = 85;
                cancellationToken.ThrowIfCancellationRequested();
                var jpeg = image.ToByteArray(MagickFormat.Jpeg);
                await File.WriteAllBytesAsync(cached, jpeg, cancellationToken).ConfigureAwait(false);
                return cached;
            }
            catch (MagickException)
            {
                // Unsupported formats (including HEIC/RAW) may be handled by the configured decoder.
                if (_decoder != null)
                {
                    try
                    {
                        using var fs = File.OpenRead(imagePath);
                        var decoded = await _decoder.DecodeToJpegAsync(fs, _maxSize, Path.GetExtension(imagePath)).ConfigureAwait(false);
                        if (decoded != null)
                        {
                            using (decoded)
                            {
                                decoded.Position = 0;
                                using var outFs = File.Create(cached);
                                await decoded.CopyToAsync(outFs, cancellationToken).ConfigureAwait(false);
                            }
                            return cached;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        // Tolerate unsupported or corrupt images.
                    }
                }
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return null;
            }
        }
    }
}
