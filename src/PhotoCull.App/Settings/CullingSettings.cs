using System;
using PhotoCull.Core.Culling;

namespace PhotoCull.App.Settings
{
    public sealed class CullingSettings
    {
        public double BurstWindowSeconds { get; init; } = 2;
        public int PHashHammingThreshold { get; init; } = 8;
        public int DHashHammingThreshold { get; init; } = 8;
        public double EmbeddingCosineThreshold { get; init; } = 0.9;

        public ClusterOptions ToClusterOptions()
        {
            Validate();
            return new ClusterOptions
            {
                BurstWindow = TimeSpan.FromSeconds(BurstWindowSeconds),
                PHashHammingThreshold = PHashHammingThreshold,
                DHashHammingThreshold = DHashHammingThreshold,
                EmbeddingCosineThreshold = EmbeddingCosineThreshold
            };
        }

        public void Validate()
        {
            if (!double.IsFinite(BurstWindowSeconds) || BurstWindowSeconds < 0 || BurstWindowSeconds > 600)
                throw new ArgumentOutOfRangeException(nameof(BurstWindowSeconds), "Burst window must be between 0 and 600 seconds.");
            if (PHashHammingThreshold is < 0 or > 64)
                throw new ArgumentOutOfRangeException(nameof(PHashHammingThreshold), "pHash threshold must be between 0 and 64.");
            if (DHashHammingThreshold is < 0 or > 64)
                throw new ArgumentOutOfRangeException(nameof(DHashHammingThreshold), "dHash threshold must be between 0 and 64.");
            if (!double.IsFinite(EmbeddingCosineThreshold) || EmbeddingCosineThreshold < 0 || EmbeddingCosineThreshold > 1)
                throw new ArgumentOutOfRangeException(nameof(EmbeddingCosineThreshold), "Embedding similarity must be between 0 and 1.");
        }
    }
}
