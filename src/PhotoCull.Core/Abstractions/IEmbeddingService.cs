using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoCull.Core.Abstractions
{
    public interface IEmbeddingService
    {
        Task<float[]> CreateEmbeddingAsync(Stream imageStream, CancellationToken cancellationToken = default);
        Task SaveEmbeddingAsync(long imageId, Stream imageStream, CancellationToken cancellationToken = default);
    }
}
