using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PCull.Core;
using PCull.Core.Abstractions;
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
        var pipeline = Create(new[] { Image(ExistingPath), Image(ExistingPath) });
        var reports = new List<PipelineProgress>();

        var result = await pipeline.RunAsync("folder", "cache", new ImmediateProgress(reports));

        Assert.Equal(2, result.Scanned);
        Assert.Equal(2, result.Processed);
        Assert.Contains(reports, p => p.Phase == PipelinePhase.Scanning && p.Scanned == 2);
        Assert.Contains(reports, p => p.Phase == PipelinePhase.Analyzing && p.Scanned == 2);
        Assert.Contains(reports, p => p.Phase == PipelinePhase.Grouping && p.Scanned == 2);
        Assert.Contains(reports, p => p.Phase == PipelinePhase.Saving && p.Scanned == 2);
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
    public async Task Cancellation_propagates_without_counting_error_or_saving()
    {
        using var cts = new CancellationTokenSource();
        var repo = new FakeRepository();
        var pipeline = Create(new[] { Image("one") }, repo, new CancelingThumbnail(cts));

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            pipeline.RunAsync("folder", "cache", cancellationToken: cts.Token));
        Assert.False(repo.SetsCleared);
    }

    private static CullingPipeline Create(IEnumerable<ImageRecord> images, FakeRepository? repo = null, IThumbnailService? thumbnail = null)
        => new(new FakeScanner(images), thumbnail ?? new FakeThumbnail(), new FakeExif(), new FakeHash(),
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

    private sealed class FakeQuality : IQualityAnalyzer
    {
        public Task<QualityMetrics> AnalyzeAsync(Stream imageStream) => Task.FromResult(new QualityMetrics());
    }

    private sealed class FakeRepository : IImageRepository
    {
        private long _nextId;
        public string? FailingPath { get; init; }
        public bool SetsCleared { get; private set; }

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
        public Task SaveSetMembersAsync(long setId, IEnumerable<SetMember> members) => Task.CompletedTask;
        public Task SaveSetMatchesAsync(long setId, IEnumerable<PCull.Core.Culling.ClusterMatch> matches) => Task.CompletedTask;
        public Task ClearSetsAsync() { SetsCleared = true; return Task.CompletedTask; }
        public Task SaveUserDecisionAsync(long imageId, string action) => Task.CompletedTask;
        public Task<string?> LoadUserDecisionAsync(long imageId) => Task.FromResult<string?>(null);
    }
}
