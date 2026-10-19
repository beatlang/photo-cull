using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using PCull.Core.Models;

namespace PCull.Core.Culling
{
    [Flags]
    public enum ClusterCriterionEvaluation
    {
        None = 0,
        CaptureTime = 1,
        DHash = 2,
        PHash = 4,
        Embedding = 8
    }

    public enum ClusterFeatureStage
    {
        DHash,
        PHash,
        Embedding
    }

    public record ImageFeatures(
        long ImageId,
        DateTime? CaptureTime,
        ulong? PHash,
        ulong? DHash,
        ulong? AHash,
        float[]? Embedding);

    public record ClusterMatch(
        long ImageId,
        long MatchedImageId,
        double? CaptureTimeDifferenceSeconds,
        double? BurstWindowSeconds,
        int? PHashDistance,
        int? PHashThreshold,
        int? DHashDistance,
        int? DHashThreshold,
        double? EmbeddingCosineSimilarity,
        double? EmbeddingCosineThreshold,
        ClusterCriterionEvaluation EvaluatedCriteria = ClusterCriterionEvaluation.None);

    public record ClusterResult(IReadOnlyList<List<long>> Clusters, IReadOnlyList<ClusterMatch> Matches);

    public class ClusterOptions
    {
        public int PHashHammingThreshold { get; init; } = 8;
        public int DHashHammingThreshold { get; init; } = 8;
        public double EmbeddingCosineThreshold { get; init; } = 0.9;
        public TimeSpan BurstWindow { get; init; } = TimeSpan.FromSeconds(2);

        // Images with capture times further apart than this are never compared (pre-filter).
        // Images without a capture time are always compared with everything. Null disables the pre-filter.
        public TimeSpan? ComparisonWindow { get; init; } = TimeSpan.FromSeconds(60);
    }

    public class Clusterer
    {
        private readonly ClusterOptions _options;

        public Clusterer(ClusterOptions? options = null)
        {
            _options = options ?? new ClusterOptions();
        }

        public List<List<long>> Cluster(IEnumerable<ImageFeatures> items)
            => ClusterDetailed(items).Clusters.ToList();

        public ClusterResult ClusterDetailed(IEnumerable<ImageFeatures> items)
        {
            var list = items.ToList();
            var adjacency = CreateAdjacency(list.Count);
            var matches = new List<ClusterMatch>();

            foreach (var (i, j) in CandidatePairs(list))
            {
                var match = GetMatch(list[i], list[j]);
                if (match == null) continue;
                adjacency[i].Add(j);
                adjacency[j].Add(i);
                matches.Add(match);
            }

            return BuildResult(list, adjacency, matches);
        }

        public async Task<ClusterResult> ClusterDetailedAsync(
            IEnumerable<ImageFeatures> items,
            Func<long, ClusterFeatureStage, CancellationToken, Task<ImageFeatures>> loadFeatureAsync,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(loadFeatureAsync);
            var list = items.ToList();
            var features = list.ToDictionary(item => item.ImageId);
            var adjacency = CreateAdjacency(list.Count);
            var matches = new List<ClusterMatch>();

            foreach (var (i, j) in CandidatePairs(list))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var match = await GetMatchAsync(features[list[i].ImageId], features[list[j].ImageId],
                    features, loadFeatureAsync, cancellationToken);
                if (match == null) continue;
                adjacency[i].Add(j);
                adjacency[j].Add(i);
                matches.Add(match);
            }

            return BuildResult(list, adjacency, matches);
        }

        // Yields index pairs (i < j) that are worth comparing. With a comparison window, timed images
        // are sorted by capture time and only compared within the window.
        private IEnumerable<(int, int)> CandidatePairs(IReadOnlyList<ImageFeatures> list)
        {
            if (_options.ComparisonWindow is not { } window)
            {
                for (int i = 0; i < list.Count; i++)
                    for (int j = i + 1; j < list.Count; j++)
                        yield return (i, j);
                yield break;
            }

            double windowSeconds = window.TotalSeconds;
            var timed = Enumerable.Range(0, list.Count)
                .Where(i => list[i].CaptureTime.HasValue)
                .OrderBy(i => list[i].CaptureTime!.Value)
                .ToList();

            for (int a = 0; a < timed.Count; a++)
            {
                var timeA = list[timed[a]].CaptureTime!.Value;
                for (int b = a + 1; b < timed.Count; b++)
                {
                    if ((list[timed[b]].CaptureTime!.Value - timeA).TotalSeconds > windowSeconds)
                        break;
                    yield return (Math.Min(timed[a], timed[b]), Math.Max(timed[a], timed[b]));
                }
            }

            for (int u = 0; u < list.Count; u++)
            {
                if (list[u].CaptureTime.HasValue) continue;
                for (int k = 0; k < list.Count; k++)
                {
                    if (k == u || (k < u && !list[k].CaptureTime.HasValue)) continue;
                    yield return (Math.Min(u, k), Math.Max(u, k));
                }
            }
        }

        private static List<int>[] CreateAdjacency(int count)
        {
            var adjacency = new List<int>[count];
            for (int i = 0; i < count; i++)
                adjacency[i] = new List<int>();
            return adjacency;
        }

        private static ClusterResult BuildResult(
            IReadOnlyList<ImageFeatures> items,
            IReadOnlyList<List<int>> adjacency,
            IReadOnlyList<ClusterMatch> matches)
        {
            var seen = new bool[items.Count];
            var clusters = new List<List<long>>();
            for (int i = 0; i < items.Count; i++)
            {
                if (seen[i]) continue;
                var component = new List<long>();
                var stack = new Stack<int>();
                stack.Push(i);
                seen[i] = true;
                while (stack.Count > 0)
                {
                    var current = stack.Pop();
                    component.Add(items[current].ImageId);
                    foreach (var neighbor in adjacency[current])
                    {
                        if (!seen[neighbor])
                        {
                            seen[neighbor] = true;
                            stack.Push(neighbor);
                        }
                    }
                }
                clusters.Add(component);
            }

            return new ClusterResult(clusters, matches);
        }

        private ClusterMatch? GetMatch(ImageFeatures a, ImageFeatures b)
        {
            double? timeDifference = null;
            int? dHashDistance = null;
            int? pHashDistance = null;
            double? embeddingSimilarity = null;
            var evaluated = ClusterCriterionEvaluation.CaptureTime;

            if (a.CaptureTime.HasValue && b.CaptureTime.HasValue)
            {
                timeDifference = Math.Abs((a.CaptureTime.Value - b.CaptureTime.Value).TotalSeconds);
                if (timeDifference <= _options.BurstWindow.TotalSeconds)
                    return CreateMatch(a, b, timeDifference, dHashDistance, pHashDistance, embeddingSimilarity, evaluated);
            }

            evaluated |= ClusterCriterionEvaluation.DHash;
            if (a.DHash.HasValue && b.DHash.HasValue)
            {
                dHashDistance = BitOperations.PopCount(a.DHash.Value ^ b.DHash.Value);
                if (dHashDistance <= _options.DHashHammingThreshold)
                    return CreateMatch(a, b, timeDifference, dHashDistance, pHashDistance, embeddingSimilarity, evaluated);
            }

            evaluated |= ClusterCriterionEvaluation.PHash;
            if (a.PHash.HasValue && b.PHash.HasValue)
            {
                pHashDistance = BitOperations.PopCount(a.PHash.Value ^ b.PHash.Value);
                if (pHashDistance <= _options.PHashHammingThreshold)
                    return CreateMatch(a, b, timeDifference, dHashDistance, pHashDistance, embeddingSimilarity, evaluated);
            }

            evaluated |= ClusterCriterionEvaluation.Embedding;
            embeddingSimilarity = CalculateCosineSimilarity(a.Embedding, b.Embedding);
            if (!embeddingSimilarity.HasValue || embeddingSimilarity < _options.EmbeddingCosineThreshold)
                return null;

            return CreateMatch(a, b, timeDifference, dHashDistance, pHashDistance, embeddingSimilarity, evaluated);
        }

        private async Task<ClusterMatch?> GetMatchAsync(
            ImageFeatures a,
            ImageFeatures b,
            IDictionary<long, ImageFeatures> features,
            Func<long, ClusterFeatureStage, CancellationToken, Task<ImageFeatures>> loadFeatureAsync,
            CancellationToken cancellationToken)
        {
            double? timeDifference = null;
            int? dHashDistance = null;
            int? pHashDistance = null;
            double? embeddingSimilarity = null;
            var evaluated = ClusterCriterionEvaluation.CaptureTime;

            if (a.CaptureTime.HasValue && b.CaptureTime.HasValue)
            {
                timeDifference = Math.Abs((a.CaptureTime.Value - b.CaptureTime.Value).TotalSeconds);
                if (timeDifference <= _options.BurstWindow.TotalSeconds)
                    return CreateMatch(a, b, timeDifference, dHashDistance, pHashDistance, embeddingSimilarity, evaluated);
            }

            evaluated |= ClusterCriterionEvaluation.DHash;
            a = await EnsureFeatureAsync(a.ImageId, ClusterFeatureStage.DHash, features, loadFeatureAsync, cancellationToken);
            b = await EnsureFeatureAsync(b.ImageId, ClusterFeatureStage.DHash, features, loadFeatureAsync, cancellationToken);
            if (a.DHash.HasValue && b.DHash.HasValue)
            {
                dHashDistance = BitOperations.PopCount(a.DHash.Value ^ b.DHash.Value);
                if (dHashDistance <= _options.DHashHammingThreshold)
                    return CreateMatch(a, b, timeDifference, dHashDistance, pHashDistance, embeddingSimilarity, evaluated);
            }

            evaluated |= ClusterCriterionEvaluation.PHash;
            a = await EnsureFeatureAsync(a.ImageId, ClusterFeatureStage.PHash, features, loadFeatureAsync, cancellationToken);
            b = await EnsureFeatureAsync(b.ImageId, ClusterFeatureStage.PHash, features, loadFeatureAsync, cancellationToken);
            if (a.PHash.HasValue && b.PHash.HasValue)
            {
                pHashDistance = BitOperations.PopCount(a.PHash.Value ^ b.PHash.Value);
                if (pHashDistance <= _options.PHashHammingThreshold)
                    return CreateMatch(a, b, timeDifference, dHashDistance, pHashDistance, embeddingSimilarity, evaluated);
            }

            evaluated |= ClusterCriterionEvaluation.Embedding;
            a = await EnsureFeatureAsync(a.ImageId, ClusterFeatureStage.Embedding, features, loadFeatureAsync, cancellationToken);
            b = await EnsureFeatureAsync(b.ImageId, ClusterFeatureStage.Embedding, features, loadFeatureAsync, cancellationToken);
            embeddingSimilarity = CalculateCosineSimilarity(a.Embedding, b.Embedding);
            if (!embeddingSimilarity.HasValue || embeddingSimilarity < _options.EmbeddingCosineThreshold)
                return null;

            return CreateMatch(a, b, timeDifference, dHashDistance, pHashDistance, embeddingSimilarity, evaluated);
        }

        private static async Task<ImageFeatures> EnsureFeatureAsync(
            long imageId,
            ClusterFeatureStage stage,
            IDictionary<long, ImageFeatures> features,
            Func<long, ClusterFeatureStage, CancellationToken, Task<ImageFeatures>> loadFeatureAsync,
            CancellationToken cancellationToken)
        {
            var current = features[imageId];
            bool available = stage switch
            {
                ClusterFeatureStage.DHash => current.DHash.HasValue,
                ClusterFeatureStage.PHash => current.PHash.HasValue,
                ClusterFeatureStage.Embedding => current.Embedding != null,
                _ => throw new ArgumentOutOfRangeException(nameof(stage))
            };
            if (available) return current;

            var loaded = await loadFeatureAsync(imageId, stage, cancellationToken);
            current = stage switch
            {
                ClusterFeatureStage.DHash => current with { DHash = loaded.DHash },
                ClusterFeatureStage.PHash => current with { PHash = loaded.PHash },
                ClusterFeatureStage.Embedding => current with { Embedding = loaded.Embedding },
                _ => throw new ArgumentOutOfRangeException(nameof(stage))
            };
            features[imageId] = current;
            return current;
        }

        private static double? CalculateCosineSimilarity(float[]? a, float[]? b)
        {
            if (a == null || b == null || a.Length != b.Length)
                return null;

            double dot = 0;
            double normA = 0;
            double normB = 0;
            for (int i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                normA += a[i] * a[i];
                normB += b[i] * b[i];
            }
            if (normA <= 0 || normB <= 0)
                return null;
            return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
        }

        private ClusterMatch CreateMatch(
            ImageFeatures a,
            ImageFeatures b,
            double? timeDifference,
            int? dHashDistance,
            int? pHashDistance,
            double? embeddingSimilarity,
            ClusterCriterionEvaluation evaluated)
        {
            return new ClusterMatch(a.ImageId, b.ImageId,
                timeDifference, _options.BurstWindow.TotalSeconds,
                pHashDistance, _options.PHashHammingThreshold,
                dHashDistance, _options.DHashHammingThreshold,
                embeddingSimilarity, _options.EmbeddingCosineThreshold,
                evaluated);
        }
    }
}
