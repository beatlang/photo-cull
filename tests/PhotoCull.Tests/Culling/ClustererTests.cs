using System;
using System.Linq;
using System.Numerics;
using System.Collections.Generic;
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
            var clusterer = new Clusterer(new ClusterOptions { PHashHammingThreshold = 3, DHashHammingThreshold = 3 });
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
            var clusterer = new Clusterer(new ClusterOptions { EmbeddingCosineThreshold = 0.95 });
            var clusters = clusterer.Cluster(items);
            Assert.Contains(clusters, c => c.Contains(1) && c.Contains(2));
            Assert.DoesNotContain(clusters, c => c.Contains(1) && c.Contains(3));
        }
    }
}
