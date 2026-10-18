using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PhotoCull.Core;
using PhotoCull.Core.Abstractions;
using PhotoCull.Core.Culling;
using PhotoCull.Core.Models;

namespace PhotoCull.Infrastructure.Pipeline
{
    public class PipelineProgress
    {
        public int Scanned { get; set; }
        public int Processed { get; set; }
        public int Errors { get; set; }
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
        private readonly Clusterer _clusterer;
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
            _clusterer = new Clusterer();
            _ranker = new Ranker();
        }

        public async Task RunAsync(string folder, string cacheDirectory, IProgress<PipelineProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            var p = new PipelineProgress();
            var features = new List<ImageFeatures>();
            var qualities = new Dictionary<long, QualityMetrics>();

            await foreach (var img in _scanner.ScanAsync(folder, cancellationToken))
            {
                p.Scanned++;
                progress?.Report(p);
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
                    catch { /* swallow thumbnail errors */ }

                    // exif - best-effort
                    ExifInfo? exif = null;
                    try
                    {
                        using var fs = File.OpenRead(img.Path);
                        exif = _exifReader.ReadExif(fs);
                        if (fs.CanSeek) fs.Position = 0;

                        // compute hashes
                        ulong aHash = _hashService.ComputeAHash(fs);
                        if (fs.CanSeek) fs.Position = 0;
                        ulong dHash = _hashService.ComputeDHash(fs);
                        if (fs.CanSeek) fs.Position = 0;
                        ulong pHash = _hashService.ComputePHash(fs);

                        // store hashed as long? convert
                        await _imageRepo.SaveHashesAsync(imageId, (long?)pHash, (long?)dHash, (long?)aHash);
                    }
                    catch
                    {
                        // tolerate hash/exif errors
                    }

                    // quality
                    try
                    {
                        using var fsq = File.OpenRead(img.Path);
                        var qm = await _qualityAnalyzer.AnalyzeAsync(fsq);
                        qualities[imageId] = qm;
                        await _imageRepo.SaveQualityScoresAsync(imageId, new QualityScores(qm.Sharpness, qm.ExposureScore, qm.MeanBrightness, qm.NoiseEstimate, qm.Quality));
                    }
                    catch
                    {
                        // tolerate
                    }

                    // embeddings optional
                    float[]? embedding = null;
                    if (_embeddingService != null)
                    {
                        try
                        {
                            using var fsb = File.OpenRead(img.Path);
                            embedding = await _embeddingService.CreateEmbeddingAsync(fsb, cancellationToken);
                            if (embedding != null)
                                await _imageRepo.SaveEmbeddingAsync(imageId, embedding);
                        }
                        catch
                        {
                            // tolerate
                        }
                    }

                    var feat = new ImageFeatures(imageId, img.CreatedUtc, null, null, null, embedding);
                    features.Add(feat);

                    p.Processed++;
                    progress?.Report(p);
                }
                catch (Exception ex)
                {
                    p.Errors++;
                    progress?.Report(p);
                    // continue
                }
            }

            // cluster & rank
            var clusters = _clusterer.Cluster(features);
            var ranked = _ranker.Rank(clusters, qualities);

            // persist sets
            int setIndex = 1;
            foreach (var cluster in clusters)
            {
                var name = $"set-{setIndex++}";
                long setId = await _imageRepo.CreateSetAsync(name);
                var members = ranked.Where(m => cluster.Contains(m.ImageId));
                await _imageRepo.SaveSetMembersAsync(setId, members);
            }
        }
    }
}
