using System.Collections.Generic;
using System.Threading;

namespace PhotoCull.Core
{
    public interface IFolderScanner
    {
        IAsyncEnumerable<ImageRecord> ScanAsync(string folder, CancellationToken cancellationToken = default);
    }
}
