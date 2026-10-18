using System;
using System.Collections.Generic;
using System.Linq;
using PhotoCull.Core;
using PhotoCull.Core.Culling;
using PhotoCull.Core.Models;
using Xunit;

namespace PhotoCull.Tests.Culling
{
    public class RankerTests
    {
        [Fact]
        public void BestIsKeepOthersDeleteOrReview()
        {
            var clusters = new List<List<long>> { new List<long> { 1, 2, 3 } };
            var members = new Dictionary<long, QualityMetrics>
            {
                {1, new QualityMetrics { Quality = 90, Sharpness = 50, IsBlurWarning = false } },
                {2, new QualityMetrics { Quality = 85, Sharpness = 40, IsBlurWarning = false } },
                {3, new QualityMetrics { Quality = 30, Sharpness = 10, IsBlurWarning = true } },
            };

            var ranker = new Ranker(new RankerOptions { ReviewDelta = 10, DeleteQualityThreshold = 20 });
            var set = ranker.Rank(clusters, members);
            var m1 = set.Find(s => s.ImageId == 1);
            var m2 = set.Find(s => s.ImageId == 2);
            var m3 = set.Find(s => s.ImageId == 3);

            Assert.True(m1.IsBestShot && m1.Recommendation == Recommendation.Keep);
            Assert.Equal(Recommendation.Review, m2.Recommendation);
            Assert.Equal(Recommendation.Delete, m3.Recommendation);
        }

        [Fact]
        public void SingletonLowQualityIsReview()
        {
            var clusters = new List<List<long>>();
            var members = new Dictionary<long, QualityMetrics>
            {
                { 10, new QualityMetrics { Quality = 35, Sharpness = 10, IsBlurWarning = false } }
            };

            var ranker = new Ranker(new RankerOptions { SingletonKeepQuality = 40 });
            var set = ranker.Rank(clusters, members);
            var m = set.Find(s => s.ImageId == 10);
            Assert.True(m.IsBestShot);
            Assert.Equal(Recommendation.Review, m.Recommendation);
        }
    }
}
