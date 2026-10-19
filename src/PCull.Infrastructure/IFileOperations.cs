using System.Threading;
using System.Threading.Tasks;

namespace PCull.Infrastructure.Deletion
{
    public interface IFileOperations
    {
        Task<long> GetFileSizeAsync(string path, CancellationToken cancellationToken = default);
        Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default);
        Task DeletePermanentAsync(string path, CancellationToken cancellationToken = default);
        Task SendToRecycleBinAsync(string path, CancellationToken cancellationToken = default);
        /// <summary>
        /// Attempt to restore a file previously deleted to the recycle bin. Best-effort.
        /// Return true if restore reported success by the underlying mechanism.
        /// </summary>
        Task<bool> RestoreFromRecycleBinAsync(string path, CancellationToken cancellationToken = default);
    }
}
