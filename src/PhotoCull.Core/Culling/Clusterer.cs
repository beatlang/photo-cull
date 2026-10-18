using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using PhotoCull.Core.Models;

namespace PhotoCull.Core.Culling
{
    public record ImageFeatures(
        long ImageId,
        DateTime? CaptureTime,
        ulong? PHash,
        ulong? DHash,
        ulong? AHash,
        float[]? Embedding);

    public class ClusterOptions
    {
        public int PHashHammingThreshold { get; init; } = 8;
        public int DHashHammingThreshold { get; init; } = 8;
        public double EmbeddingCosineThreshold { get; init; } = 0.9;
        public TimeSpan BurstWindow { get; init; } = TimeSpan.FromSeconds(3);
    }

    public class Clusterer
    {
        private readonly ClusterOptions _options;

        public Clusterer(ClusterOptions? options = null)
        {
            _options = options ?? new ClusterOptions();
        }

        // Pure logic: group image ids into clusters (connected components) based on similarity rules
        public List<List<long>> Cluster(IEnumerable<ImageFeatures> items)
        {
            var list = items.ToList();
            int n = list.Count;
            var adj = new List<int>[n];
            for (int i = 0; i < n; i++) adj[i] = new List<int>();

            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    if (AreSimilar(list[i], list[j]))
                    {
                        adj[i].Add(j);
                        adj[j].Add(i);
                    }
                }
            }

            var seen = new bool[n];
            var clusters = new List<List<long>>();
            for (int i = 0; i < n; i++)
            {
                if (seen[i]) continue;
                var comp = new List<long>();
                var stack = new Stack<int>();
                stack.Push(i);
                seen[i] = true;
                while (stack.Count > 0)
                {
                    var v = stack.Pop();
                    comp.Add(list[v].ImageId);
                    foreach (var w in adj[v])
                    {
                        if (!seen[w]) { seen[w] = true; stack.Push(w); }
                    }
                }
                clusters.Add(comp);
            }

            return clusters;
        }

        private bool AreSimilar(ImageFeatures a, ImageFeatures b)
        {
            // Burst time proximity
            if (a.CaptureTime.HasValue && b.CaptureTime.HasValue &&
                Math.Abs((a.CaptureTime.Value - b.CaptureTime.Value).TotalSeconds) <= _options.BurstWindow.TotalSeconds)
                return true;

            // pHash similarity
            if (a.PHash.HasValue && b.PHash.HasValue)
            {
                int hd = BitOperations.PopCount(a.PHash.Value ^ b.PHash.Value);
                if (hd <= _options.PHashHammingThreshold) return true;
            }

            // dHash similarity
            if (a.DHash.HasValue && b.DHash.HasValue)
            {
                int hd = BitOperations.PopCount(a.DHash.Value ^ b.DHash.Value);
                if (hd <= _options.DHashHammingThreshold) return true;
            }

            // Embedding cosine similarity
            if (a.Embedding != null && b.Embedding != null && a.Embedding.Length == b.Embedding.Length)
            {
                double dot = 0; double na = 0; double nb = 0;
                for (int i = 0; i < a.Embedding.Length; i++)
                {
                    dot += a.Embedding[i] * b.Embedding[i];
                    na += a.Embedding[i] * a.Embedding[i];
                    nb += b.Embedding[i] * b.Embedding[i];
                }
                if (na <= 0 || nb <= 0) return false;
                double cosine = dot / (Math.Sqrt(na) * Math.Sqrt(nb));
                if (cosine >= _options.EmbeddingCosineThreshold) return true;
            }

            return false;
        }
    }
}
