using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime.Tensors;
using PCull.Ai.Embeddings;
using PCull.Core.Abstractions;
using PCull.Core;
using ImageMagick;
using Xunit;

namespace PCull.Tests.Embeddings
{
    public class ClipEmbeddingServiceTests
    {
        [Fact]
        public async Task Preprocessing_ShapeAndValues()
        {
            // white image
            using var img = TestImages.Solid(300, 300, 255, 255, 255);
            using var ms = TestImages.ToStream(img, MagickFormat.Png);

            var engine = new CaptureInferenceEngine(returnVectorLength: 4);
            var repo = new NoopImageRepo();
            var svc = new ImageEmbeddingService(engine, repo);

            await svc.CreateEmbeddingAsync(ms);

            Assert.NotNull(engine.LastInput);
            var tensor = engine.LastInput as DenseTensor<float>;
            Assert.NotNull(tensor);
            Assert.Equal(4, tensor.Rank); // dims
            Assert.Equal(1, tensor.Dimensions[0]);
            Assert.Equal(3, tensor.Dimensions[1]);
            Assert.Equal(224, tensor.Dimensions[2]);
            Assert.Equal(224, tensor.Dimensions[3]);

            // check first pixel normalized for white: (1 - mean)/std
            float expectedR = (1f - 0.48145466f) / 0.26862954f;
            float actualR = tensor[0, 0, 0, 0];
            Assert.True(Math.Abs(expectedR - actualR) < 1e-4, $"expected {expectedR} got {actualR}");
        }

        [Fact]
        public async Task Preprocessing_GrayscalePixelBuffer_ExpandsGrayAcrossRgbChannels()
        {
            using var img = TestImages.Solid(7, 5, 128, 128, 128);
            img.ColorType = ColorType.Grayscale;
            Assert.Equal(7 * 5 * 3, img.GetPixels().ToByteArray("RGB").Length);
            using var ms = TestImages.ToStream(img, MagickFormat.Png);

            var options = new PreprocessingOptions(
                2,
                new[] { 0f, 0f, 0f },
                new[] { 1f, 1f, 1f },
                StretchNoCrop: true,
                Resampler: "Bicubic",
                Rescale: 1f / 255f);
            var engine = new CaptureInferenceEngine(returnVectorLength: 1);
            var svc = new ImageEmbeddingService(engine, new NoopImageRepo(), options);

            await svc.CreateEmbeddingAsync(ms);

            var tensor = Assert.IsType<DenseTensor<float>>(engine.LastInput);
            Assert.Equal(2, tensor.Dimensions[2]);
            Assert.Equal(2, tensor.Dimensions[3]);
            float expected = 128f / 255f;
            Assert.Equal(expected, tensor[0, 0, 0, 0], 5);
            Assert.Equal(expected, tensor[0, 1, 0, 0], 5);
            Assert.Equal(expected, tensor[0, 2, 0, 0], 5);
        }

        [Fact]
        public async Task Embedding_IsL2Normalized_And_Saved()
        {
            using var img = TestImages.Solid(224, 224, 0, 0, 0);
            using var ms = TestImages.ToStream(img, MagickFormat.Png);

            var engine = new ReturnVectorInferenceEngine(new float[] { 3f, 4f });
            var repo = new CaptureImageRepo();
            var svc = new ImageEmbeddingService(engine, repo);

            await svc.SaveEmbeddingAsync(123, ms);

            Assert.NotNull(repo.Saved);
            Assert.Equal(2, repo.Saved.Length);
            // normalized should be [0.6,0.8]
            Assert.True(Math.Abs(repo.Saved[0] - 0.6f) < 1e-6);
            Assert.True(Math.Abs(repo.Saved[1] - 0.8f) < 1e-6);
        }

        [Fact]
        public void CosineSimilarity_Works()
        {
            float[] a = { 1f, 0f };
            float[] b = { 0f, 1f };
            Assert.Equal(0f, EmbeddingUtils.CosineSimilarity(a, b));

            float[] c = { 1f, 2f, 3f };
            float[] d = { 1f, 2f, 3f };
            Assert.Equal(1f, EmbeddingUtils.CosineSimilarity(c, d));
        }

        private class CaptureInferenceEngine : IInferenceEngine
        {
            public Tensor<float>? LastInput { get; private set; }
            private readonly int _len;
            public InferenceProvider ActiveProvider => InferenceProvider.CPU;
            public IReadOnlyList<string> InputNames => new[] { "input" };
            public IReadOnlyList<string> OutputNames => new[] { "out" };
            public CaptureInferenceEngine(int returnVectorLength = 256) { _len = returnVectorLength; }
            public IReadOnlyDictionary<string, float[]> Run(IReadOnlyDictionary<string, Tensor<float>> inputs)
            {
                LastInput = inputs.Values.First();
                return new System.Collections.Generic.Dictionary<string, float[]> { { "out", new float[_len] } };
            }
            public void Dispose() { }
        }

        private class ReturnVectorInferenceEngine : IInferenceEngine
        {
            private readonly float[] _vec;
            public ReturnVectorInferenceEngine(float[] vec) { _vec = vec; }
            public InferenceProvider ActiveProvider => InferenceProvider.CPU;
            public IReadOnlyList<string> InputNames => new[] { "input" };
            public IReadOnlyList<string> OutputNames => new[] { "out" };
            public IReadOnlyDictionary<string, float[]> Run(IReadOnlyDictionary<string, Tensor<float>> inputs)
            {
                return new System.Collections.Generic.Dictionary<string, float[]> { { "out", _vec.ToArray() } };
            }
            public void Dispose() { }
        }

        private class NoopImageRepo : IImageRepository
        {
            public Task<long> UpsertImageAsync(string path, long sizeBytes, int width, int height, string format, DateTime createdUtc, DateTime modifiedUtc) => Task.FromResult(0L);
            public Task<ImageRecord?> GetImageByPathAsync(string path) => Task.FromResult<ImageRecord?>(null);
            public Task<System.Collections.Generic.IEnumerable<ImageRecord>> ListImagesAsync() => Task.FromResult((System.Collections.Generic.IEnumerable<ImageRecord>)Array.Empty<ImageRecord>());
            public Task SaveEmbeddingAsync(long imageId, float[] embedding) => Task.CompletedTask;
            public Task<float[]?> LoadEmbeddingAsync(long imageId) => Task.FromResult<float[]?>(null);
            public Task SaveQualityScoresAsync(long imageId, QualityScores scores) => Task.CompletedTask;
            public Task<QualityScores?> LoadQualityScoresAsync(long imageId) => Task.FromResult<QualityScores?>(null);
            public Task SaveHashesAsync(long imageId, long? pHash, long? dHash, long? aHash) => Task.CompletedTask;
            public Task<long> CreateSetAsync(string name) => Task.FromResult(0L);
            public Task SaveSetMembersAsync(long setId, System.Collections.Generic.IEnumerable<SetMember> members) => Task.CompletedTask;
            public Task SaveUserDecisionAsync(long imageId, string action) => Task.CompletedTask;
            public Task<string?> LoadUserDecisionAsync(long imageId) => Task.FromResult<string?>(null);
        }

        private class CaptureImageRepo : IImageRepository
        {
            public float[]? Saved { get; private set; }
            public Task<long> UpsertImageAsync(string path, long sizeBytes, int width, int height, string format, DateTime createdUtc, DateTime modifiedUtc) => Task.FromResult(0L);
            public Task<ImageRecord?> GetImageByPathAsync(string path) => Task.FromResult<ImageRecord?>(null);
            public Task<System.Collections.Generic.IEnumerable<ImageRecord>> ListImagesAsync() => Task.FromResult((System.Collections.Generic.IEnumerable<ImageRecord>)Array.Empty<ImageRecord>());
            public Task SaveEmbeddingAsync(long imageId, float[] embedding) { Saved = embedding; return Task.CompletedTask; }
            public Task<float[]?> LoadEmbeddingAsync(long imageId) => Task.FromResult<float[]?>(null);
            public Task SaveQualityScoresAsync(long imageId, QualityScores scores) => Task.CompletedTask;
            public Task<QualityScores?> LoadQualityScoresAsync(long imageId) => Task.FromResult<QualityScores?>(null);
            public Task SaveHashesAsync(long imageId, long? pHash, long? dHash, long? aHash) => Task.CompletedTask;
            public Task<long> CreateSetAsync(string name) => Task.FromResult(0L);
            public Task SaveSetMembersAsync(long setId, System.Collections.Generic.IEnumerable<SetMember> members) => Task.CompletedTask;
            public Task SaveUserDecisionAsync(long imageId, string action) => Task.CompletedTask;
            public Task<string?> LoadUserDecisionAsync(long imageId) => Task.FromResult<string?>(null);
        }
    }
}
