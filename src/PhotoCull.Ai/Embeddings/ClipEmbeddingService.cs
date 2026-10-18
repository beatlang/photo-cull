using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime.Tensors;
using PhotoCull.Core.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PhotoCull.Ai.Embeddings
{
    public class ClipEmbeddingService : IEmbeddingService
    {
        private readonly IInferenceEngine _engine;
        private readonly IImageRepository _repository;
        private readonly string _inputName;

        // CLIP mean/std (float)
        private static readonly float[] Mean = { 0.48145466f, 0.4578275f, 0.40821073f };
        private static readonly float[] Std = { 0.26862954f, 0.26130258f, 0.27577711f };

        public ClipEmbeddingService(IInferenceEngine engine, IImageRepository repository, string inputName = "input")
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _inputName = inputName;
        }

        public async Task<float[]> CreateEmbeddingAsync(Stream imageStream, CancellationToken cancellationToken = default)
        {
            if (imageStream == null) throw new ArgumentNullException(nameof(imageStream));

            // Preprocess image to 224x224 NCHW float tensor
            Tensor<float> input = await Task.Run(() => Preprocess(imageStream), cancellationToken).ConfigureAwait(false);

            var inputs = new Dictionary<string, Tensor<float>> { { _inputName, input } };
            var outputs = _engine.Run(inputs);
            // take first output
            var first = outputs.Values.FirstOrDefault();
            if (first == null) return Array.Empty<float>();

            float[] embedding = first;
            // L2-normalize
            float norm = (float)Math.Sqrt(embedding.Select(x => (double)x * x).Sum());
            if (norm > 0)
            {
                for (int i = 0; i < embedding.Length; i++) embedding[i] /= norm;
            }

            return embedding;
        }

        public async Task SaveEmbeddingAsync(long imageId, Stream imageStream, CancellationToken cancellationToken = default)
        {
            var embedding = await CreateEmbeddingAsync(imageStream, cancellationToken).ConfigureAwait(false);
            await _repository.SaveEmbeddingAsync(imageId, embedding).ConfigureAwait(false);
        }

        private static Tensor<float> Preprocess(Stream imageStream)
        {
            imageStream.Position = 0;
            using var img = Image.Load<Rgb24>(imageStream);
            int target = 224;
            int w = img.Width;
            int h = img.Height;
            // scale so shortest side == 224
            float scale = (float)target / Math.Min(w, h);
            int newW = Math.Max(1, (int)Math.Round(w * scale));
            int newH = Math.Max(1, (int)Math.Round(h * scale));

            img.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(newW, newH),
                Sampler = KnownResamplers.Bicubic,
                Mode = ResizeMode.Stretch
            }));

            // center crop
            int left = Math.Max(0, (newW - target) / 2);
            int top = Math.Max(0, (newH - target) / 2);
            img.Mutate(x => x.Crop(new Rectangle(left, top, target, target)));

            // create tensor [1,3,224,224]
            var tensor = new DenseTensor<float>(new[] { 1, 3, target, target });

            img.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < target; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < target; x++)
                    {
                        var p = row[x];
                        // normalize to 0..1
                        float r = p.R / 255f;
                        float g = p.G / 255f;
                        float b = p.B / 255f;
                        // apply mean/std
                        float rn = (r - Mean[0]) / Std[0];
                        float gn = (g - Mean[1]) / Std[1];
                        float bn = (b - Mean[2]) / Std[2];
                        // NCHW
                        tensor[0, 0, y, x] = rn;
                        tensor[0, 1, y, x] = gn;
                        tensor[0, 2, y, x] = bn;
                    }
                }
            });

            return tensor;
        }
    }

    public static class EmbeddingUtils
    {
        public static float CosineSimilarity(float[] a, float[] b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            if (a.Length != b.Length) throw new ArgumentException("Vectors must have same length");

            double dot = 0, na = 0, nb = 0;
            for (int i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                na += a[i] * a[i];
                nb += b[i] * b[i];
            }
            if (na == 0 || nb == 0) return 0f;
            return (float)(dot / (Math.Sqrt(na) * Math.Sqrt(nb)));
        }
    }
}
