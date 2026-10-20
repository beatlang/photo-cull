using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PCull.Core.Culling;

namespace PCull.Core.Abstractions
{
    public record QualityScores(float Sharpness, float Exposure, float Brightness, float Noise, float Quality);

    public record SetMember(long ImageId, bool IsBestShot, Recommendation Recommendation);
    public record SetInfo(long Id, string Name);
    public record SetData(string Name, IReadOnlyCollection<SetMember> Members, IReadOnlyCollection<ClusterMatch> Matches);

    public record StoredHashes(long? PHash, long? DHash, long? AHash);

    public interface IImageRepository
    {
        Task<long> UpsertImageAsync(string path, long sizeBytes, int width, int height, string format, DateTime createdUtc, DateTime modifiedUtc);
        Task<ImageRecord?> GetImageByPathAsync(string path);
        Task<ImageRecord?> GetImageByIdAsync(long id)
        {
            return Task.FromResult<ImageRecord?>(null);
        }
        Task<IEnumerable<ImageRecord>> ListImagesAsync();
        Task RemoveImagesNotInPathsAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default) => Task.CompletedTask;

        Task SaveEmbeddingAsync(long imageId, float[] embedding);
        Task<float[]?> LoadEmbeddingAsync(long imageId);

        Task SaveQualityScoresAsync(long imageId, QualityScores scores);
        Task<QualityScores?> LoadQualityScoresAsync(long imageId);

        Task SaveHashesAsync(long imageId, long? pHash, long? dHash, long? aHash);
        Task<StoredHashes?> LoadHashesAsync(long imageId) => Task.FromResult<StoredHashes?>(null);

        Task<long> CreateSetAsync(string name);
        Task SaveSetMembersAsync(long setId, IEnumerable<SetMember> members);
        Task SaveSetMatchesAsync(long setId, IEnumerable<ClusterMatch> matches) => Task.CompletedTask;
        async Task ReplaceSetsAsync(IEnumerable<SetData> sets, Action<int>? progress = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(sets);
            await ClearSetsAsync();
            var savedSets = 0;
            foreach (var set in sets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var setId = await CreateSetAsync(set.Name);
                await SaveSetMembersAsync(setId, set.Members);
                await SaveSetMatchesAsync(setId, set.Matches);
                progress?.Invoke(++savedSets);
            }
        }
        Task<IEnumerable<ClusterMatch>> LoadSetMatchesAsync(long setId) => Task.FromResult<IEnumerable<ClusterMatch>>(Array.Empty<ClusterMatch>());
        Task ClearSetsAsync() => Task.CompletedTask;
        Task<IEnumerable<SetInfo>> ListSetsAsync()
        {
            return Task.FromResult<IEnumerable<SetInfo>>(Array.Empty<SetInfo>());
        }
        Task<IEnumerable<SetMember>> LoadSetMembersAsync(long setId)
        {
            return Task.FromResult<IEnumerable<SetMember>>(Array.Empty<SetMember>());
        }

        Task SaveUserDecisionAsync(long imageId, string action);
        Task<string?> LoadUserDecisionAsync(long imageId);
    }
}
