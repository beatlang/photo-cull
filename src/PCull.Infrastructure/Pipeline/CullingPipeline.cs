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
        public int SavedSets { get; set; }
        public int TotalSets { get; set; }
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

        // Quality analysis holds several full-resolution matrices per image, so concurrency is capped to bound memory.
        private static int AnalysisParallelism => Math.Clamp(Environment.ProcessorCount / 2, 2, 4);

        private sealed record ImageAnalysis(string Path, ImageFeatures Features, QualityMetrics? Quality);

        private async Task<ImageAnalysis> AnalyzeImageAsync(ImageRecord img, long imageId, string cacheDirectory, Action<PipelineFailure> reportFailure, CancellationToken cancellationToken)
        {
            try
            {
                await _thumbnail.GenerateThumbnailAsync(img.Path, cacheDirectory, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                reportFailure(new PipelineFailure(img.Path, ex.Message));
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
                reportFailure(new PipelineFailure(img.Path, ex.Message));
            }

            // quality (reuse persisted scores for unchanged images)
            QualityMetrics? quality = null;
            try
            {
                var cached = await _imageRepo.LoadQualityScoresAsync(imageId);
                if (cached != null)
                {
                    quality = new QualityMetrics
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
                    quality = await _qualityAnalyzer.AnalyzeAsync(fsq);
                    await _imageRepo.SaveQualityScoresAsync(imageId, new QualityScores(quality.Sharpness, quality.ExposureScore, quality.MeanBrightness, quality.NoiseEstimate, quality.Quality));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                reportFailure(new PipelineFailure(img.Path, ex.Message));
            }

            var stored = await _imageRepo.LoadHashesAsync(imageId);
            var feat = new ImageFeatures(imageId, exif?.CaptureTime,
                stored?.PHash is long ph ? unchecked((ulong)ph) : null,
                stored?.DHash is long dh ? unchecked((ulong)dh) : null,
                null, null);
            return new ImageAnalysis(img.Path, feat, quality);
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
            var progressLock = new object();

            // Register images sequentially so image ids and feature order stay deterministic.
            var pending = new List<(ImageRecord Image, long Id)>();
            foreach (var img in images)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    long imageId = await _imageRepo.UpsertImageAsync(img.Path, img.SizeBytes, img.Width, img.Height, img.Format, img.CreatedUtc, img.ModifiedUtc);
                    pending.Add((img, imageId));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    p.Errors++;
                    failures.Add(new PipelineFailure(img.Path, ex.Message));
                    Report();
                }
            }

            // The expensive per-image work (decoding, thumbnails, quality) runs in parallel with bounded concurrency.
            var analyses = new ImageAnalysis?[pending.Count];
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = AnalysisParallelism,
                CancellationToken = cancellationToken
            };
            try
            {
                await Parallel.ForEachAsync(Enumerable.Range(0, pending.Count), parallelOptions, async (index, token) =>
                {
                    var (img, imageId) = pending[index];
                    try
                    {
                        analyses[index] = await AnalyzeImageAsync(img, imageId, cacheDirectory, failure =>
                        {
                            lock (progressLock) failures.Add(failure);
                        }, token);
                        lock (progressLock)
                        {
                            p.Processed++;
                            Report();
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        lock (progressLock)
                        {
                            p.Errors++;
                            failures.Add(new PipelineFailure(img.Path, ex.Message));
                            Report();
                        }
                    }
                });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Parallel.ForEachAsync may throw a derived type; normalize it.
                throw new OperationCanceledException(cancellationToken);
            }

            foreach (var analysis in analyses)
            {
                if (analysis == null) continue;
                var imageId = analysis.Features.ImageId;
                features.Add(analysis.Features);
                featuresById.Add(imageId, analysis.Features);
                imagePaths.Add(imageId, analysis.Path);
                if (analysis.Quality != null)
                    qualities[imageId] = analysis.Quality;
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
            p.SavedSets = 0;
            Report();
            cancellationToken.ThrowIfCancellationRequested();
            var membersByCluster = clusters.Select(_ => new List<SetMember>()).ToArray();
            var matchesByCluster = clusters.Select(_ => new List<ClusterMatch>()).ToArray();
            var clusterIndexByImageId = new Dictionary<long, int>();
            for (int clusterIndex = 0; clusterIndex < clusters.Count; clusterIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var imageId in clusters[clusterIndex])
                    clusterIndexByImageId.Add(imageId, clusterIndex);
            }

            foreach (var member in ranked)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!clusterIndexByImageId.TryGetValue(member.ImageId, out var clusterIndex))
                    continue;

                membersByCluster[clusterIndex].Add(member);
            }

            foreach (var match in clusterResult.Matches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (clusterIndexByImageId.TryGetValue(match.ImageId, out var clusterIndex)
                    && clusterIndexByImageId.TryGetValue(match.MatchedImageId, out var matchedImageClusterIndex)
                    && clusterIndex == matchedImageClusterIndex)
                {
                    matchesByCluster[clusterIndex].Add(match);
                }
            }

            var sets = new List<SetData>(clusters.Count);
            for (int clusterIndex = 0; clusterIndex < clusters.Count; clusterIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sets.Add(new SetData($"set-{clusterIndex + 1}", membersByCluster[clusterIndex], matchesByCluster[clusterIndex]));
            }

            p.TotalSets = sets.Count;
            Report();
            await _imageRepo.ReplaceSetsAsync(sets, savedSets =>
            {
                p.SavedSets = savedSets;
                Report();
            }, cancellationToken);

            if (images.Count > 0 || Directory.Exists(folder))
                await _imageRepo.RemoveImagesNotInPathsAsync(images.Select(image => image.Path), cancellationToken);

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
