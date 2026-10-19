using System.IO;
using System.Threading.Tasks;

namespace PCull.Core.Abstractions
{
    public interface IImageDecoder
    {
        /// <summary>
        /// Decode an image stream (including HEIC/RAW) into a JPEG MemoryStream.
        /// Returns null on failure. Implementations should auto-orient and limit the long side to maxLongSide for performance.
        /// extension may hint the decoder which path to take; forceMagick forces Magick-based decoding for testing.
        /// </summary>
        Task<MemoryStream?> DecodeToJpegAsync(Stream input, int maxLongSide = 2048, string? extension = null, bool forceMagick = false);
    }
}
