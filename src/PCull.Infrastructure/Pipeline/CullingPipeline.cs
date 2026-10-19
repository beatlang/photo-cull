using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PCull.Core;
using PCull.Core.Abstractions;
using PCull.Core.Culling;
using PCull.Core.Models;

namespace PCull.Infrastructure.Pipeline
{
    public enum PipelinePhase { Scanning, Analyzing, Grouping, Saving }

    public class PipelineProgress
    {
        public PipelinePhase Phase { get; set; }
        public int Scanned { get; set; }
        public int Processed { get; set; }
        public int Errors { get; set; }
    }

    public record PipelineFailure(string Path, string Message);

    public class PipelineResult
    {
        public int Scanned { get; init; }
        public int Processed { get; init; }
        public IReadOnlyList<PipelineFailure> Failures { get; init; } = Array.Empty<PipelineFailure>();
    }

    public class CullingPipeline
    {
        private readonly IFolderScanner _scanner;
        private readonly IThumbnailService _thumbnail;
        private readonly IExifReader _exifReader;
        private readonly IHashService _hashService;
        private readonly IQualityAnalyzer _qualityAnalyzer;
        private readonly IEmbeddingService? _embeddingService;
        private readonly IImageRepository _imageRepo;
        private readonly Ranker _ranker;

        public CullingPipeline(IFolderScanner scanner,
            IThumbnailService thumbnail,
            IExifReader exifReader,
            IHashService hashService,
            IQualityAnalyzer qualityAnalyzer,
            IImageRepository imageRepo,
            IEmbeddingService? embeddingService = null)
        {
            _scanner = scanner;
            _thumbnail = thumbnail;
            _exifReader = exifReader;
            _hashService = hashService;
            _qualityAnalyzer = qualityAnalyzer;
            _embeddingService = embeddingService;
            _imageRepo = imageRepo;
            _ranker = new Ranker();
        }

        public async Task<PipelineResult> RunAsync(string folder, string cacheDirectory, IProgress<PipelineProgress>? progress = null, CancellationToken cancellationToken = default, ClusterOptions? clusterOptions = null)
        {
            var images = new List<ImageRecord>();
            var failures = new List<PipelineFailure>();
            var p = new PipelineProgress { Phase = PipelinePhase.Scanning };
            var features = new List<ImageFeatures>();
            var featuresById = new Dictionary<long, ImageFeatures>();
            var imagePaths = new Dictionary<long, string>();
            var qualities = new Dictionary<long, QualityMetrics>();

            await foreach (var img in _scanner.ScanAsync(folder, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                images.Add(img);
                p.Scanned++;
                Report();
            }

            p.Phase = PipelinePhase.Analyzing;
            Report();
            foreach (var img in images)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    long imageId = await _imageRepo.UpsertImageAsync(img.Path, img.SizeBytes, img.Width, img.Height, img.Format, img.CreatedUtc, img.ModifiedUtc);

                    // thumbnail
                    string? thumb = null;
                    try
                    {
                        thumb = await _thumbnail.GenerateThumbnailAsync(img.Path, cacheDirectory, cancellationToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        failures.Add(new PipelineFailure(img.Path, ex.Message));
                    }

                    // Capture time - best-effort. Similarity features are loaded lazily after timestamp checks.
                    ExifInfo? exif = null;
                    try
                    {
                        using var fs = File.OpenRead(img.Path);
                        exif = _exifReader.ReadExif(fs);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        failures.Add(new PipelineFailure(img.Path, ex.Message));
                    }

                    // quality (reuse persisted scores for unchanged images)
                    try
                    {
                        var cached = await _imageRepo.LoadQualityScoresAsync(imageId);
                        if (cached != null)
                        {
                            qualities[imageId] = new QualityMetrics
                            {
                                Sharpness = cached.Sharpness,
                                IsBlurWarning = cached.Sharpness < 15.0f,
                                ExposureScore = cached.Exposure,
                                MeanBrightness = cached.Brightness,
                                NoiseEstimate = cached.Noise,
                                Quality = cached.Quality
                            };
                        }
                        else
                        {
                            using var fsq = File.OpenRead(img.Path);
                            var qm = await _qualityAnalyzer.AnalyzeAsync(fsq);
                            qualities[imageId] = qm;
                            await _imageRepo.SaveQualityScoresAsync(imageId, new QualityScores(qm.Sharpness, qm.ExposureScore, qm.MeanBrightness, qm.NoiseEstimate, qm.Quality));
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        failures.Add(new PipelineFailure(img.Path, ex.Message));
                    }

                    var stored = await _imageRepo.LoadHashesAsync(imageId);
                    var feat = new ImageFeatures(imageId, exif?.CaptureTime,
                        stored?.PHash is long ph ? unchecked((ulong)ph) : null,
                        stored?.DHash is long dh ? unchecked((ulong)dh) : null,
                        null, null);
                    features.Add(feat);
                    featuresById.Add(imageId, feat);
                    imagePaths.Add(imageId, img.Path);

                    p.Processed++;
                    Report();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    p.Errors++;
                    failures.Add(new PipelineFailure(img.Path, ex.Message));
                    Report();
                }
            }

            // cluster & rank
            p.Phase = PipelinePhase.Grouping;
            Report();
            var clusterer = new Clusterer(clusterOptions);
            var clusterResult = await clusterer.ClusterDetailedAsync(features, LoadClusterFeatureAsync, cancellationToken);
            var clusters = clusterResult.Clusters;
            var ranked = _ranker.Rank(clusters, qualities);

            // persist sets
            p.Phase = PipelinePhase.Saving;
            Report();
            cancellationToken.ThrowIfCancellationRequested();
            await _imageRepo.ClearSetsAsync();
            int setIndex = 1;
            foreach (var cluster in clusters)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = $"set-{setIndex++}";
                long setId = await _imageRepo.CreateSetAsync(name);
                var members = ranked.Where(m => cluster.Contains(m.ImageId));
                await _imageRepo.SaveSetMembersAsync(setId, members);
                var matches = clusterResult.Matches.Where(match =>
                    cluster.Contains(match.ImageId) && cluster.Contains(match.MatchedImageId));
                await _imageRepo.SaveSetMatchesAsync(setId, matches);
            }

            Report();
            return new PipelineResult { Scanned = p.Scanned, Processed = p.Processed, Failures = failures.AsReadOnly() };

            void Report() => progress?.Report(new PipelineProgress
            {
                Phase = p.Phase,
                Scanned = p.Scanned,
                Processed = p.Processed,
                Errors = p.Errors
            });

            async Task<ImageFeatures> LoadClusterFeatureAsync(long imageId, ClusterFeatureStage stage, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                var current = featuresById[imageId];
                var path = imagePaths[imageId];

                try
                {
                    switch (stage)
                    {
                        case ClusterFeatureStage.DHash:
                        {
                            using var stream = File.OpenRead(path);
                            var hash = _hashService.ComputeDHash(stream);
                            var updated = current with { DHash = hash };
                            featuresById[imageId] = updated;
                            await _imageRepo.SaveHashesAsync(imageId, null, unchecked((long)hash), null);
                            return updated;
                        }
                        case ClusterFeatureStage.PHash:
                        {
                            using var stream = File.OpenRead(path);
                            var hash = _hashService.ComputePHash(stream);
                            var updated = current with { PHash = hash };
                            featuresById[imageId] = updated;
                            await _imageRepo.SaveHashesAsync(imageId, unchecked((long)hash), null, null);
                            return updated;
                        }
                        case ClusterFeatureStage.Embedding:
                        {
                            if (_embeddingService == null)
                                return current;

                            var storedEmbedding = await _imageRepo.LoadEmbeddingAsync(imageId);
                            if (storedEmbedding is { Length: > 0 })
                            {
                                var cachedFeature = current with { Embedding = storedEmbedding };
                                featuresById[imageId] = cachedFeature;
                                return cachedFeature;
                            }

                            using var stream = File.OpenRead(path);
                            var embedding = await _embeddingService.CreateEmbeddingAsync(stream, token);
                            var updated = current with { Embedding = embedding };
                            featuresById[imageId] = updated;
                            if (embedding.Length > 0)
                                await _imageRepo.SaveEmbeddingAsync(imageId, embedding);
                            return updated;
                        }
                        default:
                            throw new ArgumentOutOfRangeException(nameof(stage));
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failures.Add(new PipelineFailure(path, ex.Message));
                    return current;
                }
            }
        }
    }
}
