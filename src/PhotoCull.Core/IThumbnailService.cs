using System.Threading;
using System.Threading.Tasks;

namespace PhotoCull.Core
{
    public interface IThumbnailService
    {
        /// <summary>
        /// Generate or return a cached thumbnail (JPEG) for the given image path. Returns the cached file path or null if a thumbnail cannot be produced (unsupported format).
        /// </summary>
        Task<string?> GenerateThumbnailAsync(string imagePath, string cacheDirectory, CancellationToken cancellationToken = default);
    }
}
