using System;
using System.Linq;
using System.Numerics;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PhotoCull.Core.Culling;
using Xunit;

namespace PhotoCull.Tests.Culling
{
    public class ClustererTests
    {
        [Fact]
        public void BurstTimeShouldCluster()
        {
            var now = DateTime.UtcNow;
            var items = new[] {
                new ImageFeatures(1, now, null, null, null, null),
                new ImageFeatures(2, now.AddSeconds(1), null, null, null, null),
                new ImageFeatures(3, now.AddSeconds(10), null, null, null, null),
            };

            var clusterer = new Clusterer(new ClusterOptions { BurstWindow = TimeSpan.FromSeconds(3) });
            var clusters = clusterer.Cluster(items);
            Assert.Equal(2, clusters.Count);
            Assert.Contains(clusters.SelectMany(c => c), id => id == 1);
            Assert.Contains(clusters.SelectMany(c => c), id => id == 3);
        }

        [Fact]
        public void MissingCaptureTimesDoNotCluster()
        {
            var items = new[] {
                new ImageFeatures(1, null, null, null, null, null),
                new ImageFeatures(2, null, null, null, null, null),
            };

            var clusters = new Clusterer().Cluster(items);

            Assert.Equal(2, clusters.Count);
        }

        [Fact]
        public void DefaultBurstWindowIsTwoSeconds()
        {
            var now = DateTime.UtcNow;
            var items = new[] {
                new ImageFeatures(1, now, null, null, null, null),
                new ImageFeatures(2, now.AddSeconds(2), null, null, null, null),
                new ImageFeatures(3, now.AddMilliseconds(4001), null, null, null, null),
            };

            var clusters = new Clusterer().Cluster(items);

            Assert.Equal(2, clusters.Count);
            Assert.Contains(clusters, cluster => cluster.Contains(1) && cluster.Contains(2));
            Assert.Contains(clusters, cluster => cluster.Contains(3));
        }

        [Fact]
        public void DetailedClusterResultIncludesEveryMatchingCriterionAndThreshold()
        {
            var now = DateTime.UtcNow;
            var items = new[]
            {
                new ImageFeatures(1, now, 0UL, 0UL, null, new[] { 1f, 0f }),
                new ImageFeatures(2, now.AddMinutes(1), 3UL, 1UL, null, new[] { 0.99f, 0.1f })
            };
            var options = new ClusterOptions
            {
                BurstWindow = TimeSpan.FromSeconds(2),
                PHashHammingThreshold = 1,
                DHashHammingThreshold = 0,
                EmbeddingCosineThreshold = 0.99
            };

            var result = new Clusterer(options).ClusterDetailed(items);

            var match = Assert.Single(result.Matches);
            Assert.Equal(1, match.ImageId);
            Assert.Equal(2, match.MatchedImageId);
            Assert.Equal(60, match.CaptureTimeDifferenceSeconds);
            Assert.Equal(2, match.BurstWindowSeconds);
            Assert.Equal(2, match.PHashDistance);
            Assert.Equal(1, match.PHashThreshold);
            Assert.Equal(1, match.DHashDistance);
            Assert.Equal(0, match.DHashThreshold);
            Assert.True(match.EmbeddingCosineSimilarity >= 0.99);
            Assert.Equal(0.99, match.EmbeddingCosineThreshold);
            Assert.Equal(
                ClusterCriterionEvaluation.CaptureTime | ClusterCriterionEvaluation.DHash |
                ClusterCriterionEvaluation.PHash | ClusterCriterionEvaluation.Embedding,
                match.EvaluatedCriteria);
        }

        [Fact]
        public async Task LazyClusteringStopsLoadingFeaturesAfterFirstMatch()
        {
            var items = new[]
            {
                new ImageFeatures(1, DateTime.UnixEpoch, null, null, null, null),
                new ImageFeatures(2, DateTime.UnixEpoch.AddMinutes(1), null, null, null, null)
            };
            var loadedStages = new List<ClusterFeatureStage>();
            var clusterer = new Clusterer(new ClusterOptions { DHashHammingThreshold = 0, PHashHammingThreshold = 1 });

            var result = await clusterer.ClusterDetailedAsync(items, (id, stage, _) =>
            {
                loadedStages.Add(stage);
                var features = stage switch
                {
                    ClusterFeatureStage.DHash => new ImageFeatures(id, null, null, id == 1 ? 0UL : 1UL, null, null),
                    ClusterFeatureStage.PHash => new ImageFeatures(id, null, id == 1 ? 0UL : 1UL, null, null, null),
                    _ => new ImageFeatures(id, null, null, null, null, new[] { 1f, 0f })
                };
                return Task.FromResult(features);
            }, CancellationToken.None);

            var match = Assert.Single(result.Matches);
            Assert.Equal(
                new[] { ClusterFeatureStage.DHash, ClusterFeatureStage.DHash, ClusterFeatureStage.PHash, ClusterFeatureStage.PHash },
                loadedStages);
            Assert.Equal(
                ClusterCriterionEvaluation.CaptureTime | ClusterCriterionEvaluation.DHash | ClusterCriterionEvaluation.PHash,
                match.EvaluatedCriteria);
            Assert.Null(match.EmbeddingCosineSimilarity);
        }

        [Fact]
        public async Task TimestampMatchDoesNotLoadHashesOrEmbedding()
        {
            var now = DateTime.UnixEpoch;
            var items = new[]
            {
                new ImageFeatures(1, now, null, null, null, null),
                new ImageFeatures(2, now.AddSeconds(1), null, null, null, null)
            };
            var loadCount = 0;

            var result = await new Clusterer().ClusterDetailedAsync(items, (_, _, _) =>
            {
                loadCount++;
                return Task.FromResult(new ImageFeatures(0, null, null, null, null, null));
            });

            var match = Assert.Single(result.Matches);
            Assert.Equal(0, loadCount);
            Assert.Equal(ClusterCriterionEvaluation.CaptureTime, match.EvaluatedCriteria);
        }

        [Fact]
        public void PHashShouldClusterCloseHashes()
        {
            // two hashes differing by 2 bits
            ulong baseHash = 0b1111_1111_0000_0000_1111_0000_0000UL;
            ulong h2 = baseHash ^ (1UL << 5) ^ (1UL << 20);
            var now = DateTime.UtcNow;
            var items = new[] {
                new ImageFeatures(1, now, baseHash, null, null, null),
                new ImageFeatures(2, now.AddMinutes(10), h2, null, null, null),
                new ImageFeatures(3, now.AddMinutes(20), null, null, null, null),
            };
            var clusterer = new Clusterer(new ClusterOptions { PHashHammingThreshold = 3, DHashHammingThreshold = 3, ComparisonWindow = null });
            var clusters = clusterer.Cluster(items);
            Assert.Equal(2, clusters.Count);
            // cluster containing 1 and 2
            Assert.Contains(clusters, c => c.Contains(1) && c.Contains(2));
        }

        [Fact]
        public void EmbeddingShouldClusterByCosine()
        {
            var e1 = new float[] { 1f, 0f, 0f };
            var e2 = new float[] { 0.99f, 0.1f, 0f };
            var e3 = new float[] { 0f, 1f, 0f };
            var now = DateTime.UtcNow;
            var items = new[] {
                new ImageFeatures(1, now, null, null, null, e1),
                new ImageFeatures(2, now.AddMinutes(5), null, null, null, e2),
                new ImageFeatures(3, now.AddMinutes(10), null, null, null, e3),
            };
            var clusterer = new Clusterer(new ClusterOptions { EmbeddingCosineThreshold = 0.95, ComparisonWindow = null });
            var clusters = clusterer.Cluster(items);
            Assert.Contains(clusters, c => c.Contains(1) && c.Contains(2));
            Assert.DoesNotContain(clusters, c => c.Contains(1) && c.Contains(3));
        }

        [Fact]
        public async Task ComparisonWindowSkipsPairsOutsideWindowButComparesUntimedImages()
        {
            var now = DateTime.UnixEpoch;
            const ulong hash = 0xF0F0UL;
            var items = new[] {
                new ImageFeatures(1, now, null, hash, null, null),
                new ImageFeatures(2, now.AddSeconds(30), null, hash, null, null),
                new ImageFeatures(3, now.AddMinutes(10), null, hash, null, null),
                new ImageFeatures(4, null, null, hash, null, null),
            };
            var compared = new System.Collections.Generic.HashSet<long>();
            var clusterer = new Clusterer(new ClusterOptions { ComparisonWindow = TimeSpan.FromSeconds(60) });
            var result = await clusterer.ClusterDetailedAsync(items, (id, stage, _) =>
            {
                compared.Add(id);
                return Task.FromResult(items.Single(i => i.ImageId == id));
            });

            // 3 is outside the window of 1 and 2, but untimed image 4 links everything.
            Assert.Single(result.Clusters);
            Assert.DoesNotContain(result.Matches, m =>
                (m.ImageId == 1 && m.MatchedImageId == 3) || (m.ImageId == 3 && m.MatchedImageId == 1));

            var withoutUntimed = items.Take(3).ToArray();
            var result2 = await clusterer.ClusterDetailedAsync(withoutUntimed, (id, stage, _) =>
                Task.FromResult(withoutUntimed.Single(i => i.ImageId == id)));
            Assert.Equal(2, result2.Clusters.Count);
        }
    }
}
