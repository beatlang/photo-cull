using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualBasic.FileIO;

namespace PCull.Infrastructure.Deletion
{
    public class WindowsFileOperations : IFileOperations
    {
        public Task<long> GetFileSizeAsync(string path, CancellationToken cancellationToken = default)
        {
            var fi = new FileInfo(path);
            return Task.FromResult(fi.Exists ? fi.Length : 0L);
        }

        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(File.Exists(path));
        }

        public Task DeletePermanentAsync(string path, CancellationToken cancellationToken = default)
        {
            File.Delete(path);
            return Task.CompletedTask;
        }

        public Task SendToRecycleBinAsync(string path, CancellationToken cancellationToken = default)
        {
            // Use Microsoft.VisualBasic.FileIO to send to recycle bin
            FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            return Task.CompletedTask;
        }

        public Task<bool> RestoreFromRecycleBinAsync(string path, CancellationToken cancellationToken = default)
        {
            // Restoring a specific file from the recycle bin programmatically is non-trivial.
            // Implementing a full restore via Shell32 would add complex COM interactions.
            // Provide a best-effort placeholder that always returns false to indicate it did not restore.
            // Tests should use a fake IFileOperations that implements restore behavior.
            return Task.FromResult(false);
        }
    }
}
