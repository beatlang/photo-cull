using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PCull.Core.Abstractions
{
    public enum DeletionMode
    {
        Recycle,
        Permanent
    }

    public record DeletionPreview(int Count, long TotalBytesSaved);

    public record DeletionFailure(string Path, string Reason);

    public record DeletionReport(IEnumerable<string> Succeeded, IEnumerable<DeletionFailure> Failed, long BytesFreed);

    public interface IDeletionService
    {
        /// <summary>
        /// Preview deletion for the given paths. Does not perform any deletes.
        /// Returns count of files that would be affected and total bytes that would be freed.
        /// </summary>
        Task<DeletionPreview> PreviewAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default);

        /// <summary>
        /// Execute deletion for the given paths using the specified mode.
        /// Returns a report containing succeeded paths, failures with reasons, and total bytes freed.
        /// </summary>
        Task<DeletionReport> ExecuteAsync(IEnumerable<string> paths, DeletionMode mode, CancellationToken cancellationToken = default);

        /// <summary>
        /// If the last operation was a recycle operation, attempt to undo it by restoring entries from the Recycle Bin.
        /// Best-effort; returns true if all restores reported success by the underlying file operations.
        /// </summary>
        Task<bool> UndoLastRecycleAsync(CancellationToken cancellationToken = default);
    }
}
