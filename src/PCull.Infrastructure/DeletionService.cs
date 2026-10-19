using PCull.Core.Abstractions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PCull.Infrastructure.Deletion
{
    public class DeletionService : IDeletionService
    {
        private readonly IFileOperations _fileOps;
        // undo journal stores original paths of items sent to recycle bin in the last recycle execute
        private readonly List<string> _lastRecycled = new();
        private readonly object _journalLock = new();

        public DeletionService(IFileOperations fileOps)
        {
            _fileOps = fileOps ?? throw new ArgumentNullException(nameof(fileOps));
        }

        public async Task<DeletionPreview> PreviewAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default)
        {
            if (paths == null) return new DeletionPreview(0, 0);
            long total = 0;
            int count = 0;
            foreach (var p in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await _fileOps.ExistsAsync(p, cancellationToken))
                {
                    total += await _fileOps.GetFileSizeAsync(p, cancellationToken);
                    count++;
                }
            }
            return new DeletionPreview(count, total);
        }

        public async Task<DeletionReport> ExecuteAsync(IEnumerable<string> paths, DeletionMode mode, CancellationToken cancellationToken = default)
        {
            if (paths == null) return new DeletionReport(Array.Empty<string>(), Array.Empty<DeletionFailure>(), 0);

            var succeeded = new List<string>();
            var failed = new List<DeletionFailure>();
            long bytesFreed = 0;

            if (mode == DeletionMode.Recycle)
            {
                lock (_journalLock) { _lastRecycled.Clear(); }
            }

            foreach (var p in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (!await _fileOps.ExistsAsync(p, cancellationToken))
                    {
                        failed.Add(new DeletionFailure(p, "NotFound"));
                        continue;
                    }

                    var size = await _fileOps.GetFileSizeAsync(p, cancellationToken);
                    if (mode == DeletionMode.Permanent)
                    {
                        await _fileOps.DeletePermanentAsync(p, cancellationToken);
                        bytesFreed += size;
                        succeeded.Add(p);
                    }
                    else
                    {
                        await _fileOps.SendToRecycleBinAsync(p, cancellationToken);
                        bytesFreed += size;
                        succeeded.Add(p);
                        lock (_journalLock)
                        {
                            _lastRecycled.Add(p);
                        }
                    }
                }
                catch (Exception ex)
                {
                    failed.Add(new DeletionFailure(p, ex.Message));
                }
            }

            return new DeletionReport(succeeded, failed, bytesFreed);
        }

        public async Task<bool> UndoLastRecycleAsync(CancellationToken cancellationToken = default)
        {
            List<string> toRestore;
            lock (_journalLock)
            {
                toRestore = _lastRecycled.ToList();
            }

            if (!toRestore.Any()) return true;

            bool allOk = true;
            foreach (var p in toRestore)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var ok = await _fileOps.RestoreFromRecycleBinAsync(p, cancellationToken);
                    if (!ok) allOk = false;
                }
                catch
                {
                    allOk = false;
                }
            }

            lock (_journalLock)
            {
                if (allOk) _lastRecycled.Clear();
            }

            return allOk;
        }
    }
}
