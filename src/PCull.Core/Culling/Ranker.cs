using System;
using System.Collections.Generic;
using System.Linq;
using PCull.Core.Models;
using PCull.Core.Abstractions;

namespace PCull.Core.Culling
{
    public class RankerOptions
    {
        // delta below best to still be considered "Review"
        public float ReviewDelta { get; init; } = 10.0f;
        // quality threshold for singletons to be considered Keep
        public float SingletonKeepQuality { get; init; } = 40.0f;
        // quality below which we strongly recommend Delete
        public float DeleteQualityThreshold { get; init; } = 15.0f;
    }

    public class Ranker
    {
        private readonly RankerOptions _options;

        public Ranker(RankerOptions? options = null)
        {
            _options = options ?? new RankerOptions();
        }

        // members: mapping imageId -> QualityMetrics (must include Quality and IsBlurWarning)
        // clusters: list of clusters (list of image ids)
        // Returns SetMember records suitable for IImageRepository.SaveSetMembersAsync
        public List<SetMember> Rank(IEnumerable<List<long>> clusters, IDictionary<long, QualityMetrics> members)
        {
            var result = new List<SetMember>();

            foreach (var cluster in clusters)
            {
                var qualities = cluster
                    .Select(id => new { Id = id, Has = members.TryGetValue(id, out var qm), Q = qm?.Quality ?? 0f, QM = qm })
                    .ToList();

                // pick best by Quality, tiebreaker by Sharpness
                var best = qualities.OrderByDescending(x => x.Q)
                                     .ThenByDescending(x => x.QM?.Sharpness ?? 0f)
                                     .First();

                float bestQuality = best.Q;
                foreach (var item in qualities)
                {
                    var rec = Recommendation.Review;
                    bool isBest = item.Id == best.Id;

                    if (isBest)
                    {
                        rec = Recommendation.Keep;
                    }
                    else
                    {
                        // singleton case handled outside
                        float q = item.Q;
                        var qm = item.QM;

                        if (q <= _options.DeleteQualityThreshold)
                        {
                            rec = Recommendation.Delete;
                        }
                        else if (bestQuality - q <= _options.ReviewDelta)
                        {
                            // close to best
                            rec = Recommendation.Review;
                        }
                        else
                        {
                            rec = Recommendation.Delete;
                        }

                        // if the image is borderline but not blurry, prefer Review
                        if (rec == Recommendation.Delete && qm != null && !qm.IsBlurWarning && q > _options.DeleteQualityThreshold)
                        {
                            rec = Recommendation.Review;
                        }
                    }

                    result.Add(new SetMember(item.Id, isBest, rec));
                }
            }

            // Also handle any images not in clusters (shouldn't happen if clusters cover all), mark singletons
            var covered = new HashSet<long>(clusters.SelectMany(c => c));
            foreach (var kv in members)
            {
                if (covered.Contains(kv.Key)) continue;
                var q = kv.Value.Quality;
                var rec = q >= _options.SingletonKeepQuality ? Recommendation.Keep : Recommendation.Review;
                result.Add(new SetMember(kv.Key, true, rec));
            }

            return result;
        }
    }
}
