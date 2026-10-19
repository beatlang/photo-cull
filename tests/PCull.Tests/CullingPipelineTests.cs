using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PCull.Core;
using PCull.Core.Abstractions;
using PCull.Core.Culling;
using PCull.Core.Models;
using PCull.Infrastructure.Pipeline;
using Xunit;

namespace PCull.Tests;

public class CullingPipelineTests
{
    private static ImageRecord Image(string path) => new(0, path, 1, 1, 1, "test", default, default);
    private static string ExistingPath => typeof(CullingPipelineTests).Assembly.Location;

    [Fact]
    public async Task Reports_phases_and_final_scan_total()
    {
        var repo = new FakeRepository();
        var pipeline = Create(new[] { Image(ExistingPath), Image(ExistingPath) }, repo);
        var reports = new List<PipelineProgress>();

        var result = await pipeline.RunAsync("folder", "cache", new ImmediateProgress(reports));

        Assert.Equal(2, result.Scanned);
        Assert.Equal(2, result.Processed);
        Assert.Contains(reports, p => p.Phase == PipelinePhase.Scanning && p.Scanned == 2);
        Assert.Contains(reports, p => p.Phase == PipelinePhase.Analyzing && p.Scanned == 2);
        Assert.Contains(reports, p => p.Phase == PipelinePhase.Grouping && p.Scanned == 2);
        Assert.Contains(reports, p => p.Phase == PipelinePhase.Saving && p.Scanned == 2);
        Assert.Equal(new[] { ExistingPath, ExistingPath }, repo.PrunedPaths);
        Assert.All(reports.Skip(1), p => Assert.NotSame(reports[0], p));
    }

    [Fact]
    public async Task Isolates_and_reports_failing_image()
    {
        var repo = new FakeRepository { FailingPath = "bad" };
        var pipeline = Create(new[] { Image("bad"), Image(ExistingPath) }, repo);

        var result = await pipeline.RunAsync("folder", "cache");

        Assert.Equal(2, result.Scanned);
        Assert.Equal(1, result.Processed);
        var failure = Assert.Single(result.Failures);
        Assert.Equal("bad", failure.Path);
        Assert.Contains("upsert failed", failure.Message);
    }

    [Fact]
    public async Task Saving_partitions_members_and_matches_by_cluster_once()
    {
        var repo = new FakeRepository();
        var pipeline = Create(new[] { Image(ExistingPath), Image(ExistingPath) }, repo, hash: new UniqueHash());

        await pipeline.RunAsync("folder", "cache", clusterOptions: new ClusterOptions
        {
            DHashHammingThreshold = 0,
            PHashHammingThreshold = 0
        });

        Assert.Equal(2, repo.SavedSetMembers.Count);
        Assert.Equal(new long[] { 1, 2 }, repo.SavedSetMembers.Select(members => Assert.Single(members).ImageId));
        Assert.All(repo.SavedSetMatches, Assert.Empty);
    }

    [Fact]
    public async Task Cancellation_propagates_without_counting_error_or_saving()
    {
        using var cts = new CancellationTokenSource();
        var repo = new FakeRepository();
        var pipeline = Create(new[] { Image("one") }, repo, new CancelingThumbnail(cts));

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            pipeline.RunAsync("folder", "cache", cancellationToken: cts.Token));
        Assert.False(repo.SetsCleared);
        Assert.Null(repo.PrunedPaths);
    }

    [Fact]
    public async Task Empty_valid_folder_prunes_all_images()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"pcull-empty-{System.Guid.NewGuid()}");
        Directory.CreateDirectory(folder);
        try
        {
            var repo = new FakeRepository();
            var pipeline = Create(Array.Empty<ImageRecord>(), repo);

            await pipeline.RunAsync(folder, "cache");

            Assert.Empty(repo.PrunedPaths!);
        }
        finally
        {
            Directory.Delete(folder);
        }
    }

    [Fact]
    public async Task Missing_folder_does_not_prune_images()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"pcull-missing-{System.Guid.NewGuid()}");
        var repo = new FakeRepository();
        var pipeline = Create(Array.Empty<ImageRecord>(), repo);

        await pipeline.RunAsync(folder, "cache");

        Assert.Null(repo.PrunedPaths);
    }

    private static CullingPipeline Create(IEnumerable<ImageRecord> images, FakeRepository? repo = null, IThumbnailService? thumbnail = null, IHashService? hash = null)
        => new(new FakeScanner(images), thumbnail ?? new FakeThumbnail(), new FakeExif(), hash ?? new FakeHash(),
            new FakeQuality(), repo ?? new FakeRepository());

    private sealed class ImmediateProgress(List<PipelineProgress> reports) : IProgress<PipelineProgress>
    {
        public void Report(PipelineProgress value) => reports.Add(value);
    }

    private sealed class FakeScanner(IEnumerable<ImageRecord> images) : IFolderScanner
    {
        public async IAsyncEnumerable<ImageRecord> ScanAsync(string folder, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var image in images)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return image;
                await Task.Yield();
            }
        }
    }

    private sealed class FakeThumbnail : IThumbnailService
    {
        public Task<string?> GenerateThumbnailAsync(string imagePath, string cacheDirectory, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);
    }

    private sealed class CancelingThumbnail(CancellationTokenSource source) : IThumbnailService
    {
        public Task<string?> GenerateThumbnailAsync(string imagePath, string cacheDirectory, CancellationToken cancellationToken = default)
        {
            source.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<string?>(null);
        }
    }

    private sealed class FakeExif : IExifReader
    {
        public ExifInfo? ReadExif(Stream stream) => null;
    }

    private sealed class FakeHash : IHashService
    {
        public ulong ComputeAHash(Stream stream) => 0;
        public ulong ComputeDHash(Stream stream) => 0;
        public ulong ComputePHash(Stream stream) => 0;
        public int HammingDistance(ulong a, ulong b) => 0;
        public bool IsNearDuplicate(ulong a, ulong b, int maxDistance) => true;
    }

    private sealed class UniqueHash : IHashService
    {
        private ulong _nextHash;
        public ulong ComputeAHash(Stream stream) => _nextHash++;
        public ulong ComputeDHash(Stream stream) => _nextHash++;
        public ulong ComputePHash(Stream stream) => _nextHash++;
        public int HammingDistance(ulong a, ulong b) => 0;
        public bool IsNearDuplicate(ulong a, ulong b, int maxDistance) => true;
    }

    private sealed class FakeQuality : IQualityAnalyzer
    {
        public Task<QualityMetrics> AnalyzeAsync(Stream imageStream) => Task.FromResult(new QualityMetrics());
    }

    private sealed class FakeRepository : IImageRepository
    {
        private long _nextId;
        public string? FailingPath { get; init; }
        public bool SetsCleared { get; private set; }
        public string[]? PrunedPaths { get; private set; }
        public List<SetMember[]> SavedSetMembers { get; } = new();
        public List<PCull.Core.Culling.ClusterMatch[]> SavedSetMatches { get; } = new();

        public Task<long> UpsertImageAsync(string path, long sizeBytes, int width, int height, string format, System.DateTime createdUtc, System.DateTime modifiedUtc)
            => FailingPath == path ? Task.FromException<long>(new InvalidOperationException("upsert failed")) : Task.FromResult(++_nextId);
        public Task<ImageRecord?> GetImageByPathAsync(string path) => Task.FromResult<ImageRecord?>(null);
        public Task<IEnumerable<ImageRecord>> ListImagesAsync() => Task.FromResult<IEnumerable<ImageRecord>>(Array.Empty<ImageRecord>());
        public Task SaveEmbeddingAsync(long imageId, float[] embedding) => Task.CompletedTask;
        public Task<float[]?> LoadEmbeddingAsync(long imageId) => Task.FromResult<float[]?>(null);
        public Task SaveQualityScoresAsync(long imageId, QualityScores scores) => Task.CompletedTask;
        public Task<QualityScores?> LoadQualityScoresAsync(long imageId) => Task.FromResult<QualityScores?>(null);
        public Task SaveHashesAsync(long imageId, long? pHash, long? dHash, long? aHash) => Task.CompletedTask;
        public Task<StoredHashes?> LoadHashesAsync(long imageId) => Task.FromResult<StoredHashes?>(null);
        public Task<long> CreateSetAsync(string name) => Task.FromResult(1L);
        public Task SaveSetMembersAsync(long setId, IEnumerable<SetMember> members) { SavedSetMembers.Add(members.ToArray()); return Task.CompletedTask; }
        public Task SaveSetMatchesAsync(long setId, IEnumerable<PCull.Core.Culling.ClusterMatch> matches) { SavedSetMatches.Add(matches.ToArray()); return Task.CompletedTask; }
        public Task ClearSetsAsync() { SetsCleared = true; return Task.CompletedTask; }
        public Task RemoveImagesNotInPathsAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default) { PrunedPaths = paths.ToArray(); return Task.CompletedTask; }
        public Task SaveUserDecisionAsync(long imageId, string action) => Task.CompletedTask;
        public Task<string?> LoadUserDecisionAsync(long imageId) => Task.FromResult<string?>(null);
    }
}
