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
    public record PreprocessingOptions(int Size, float[] Mean, float[] Std, bool StretchNoCrop, string Resampler, float Rescale)
    {
        public static PreprocessingOptions ClipPreset => new PreprocessingOptions(
            224,
            new float[] { 0.48145466f, 0.4578275f, 0.40821073f },
            new float[] { 0.26862954f, 0.26130258f, 0.27577711f },
            StretchNoCrop: false,
            Resampler: "Bicubic",
            Rescale: 1f / 255f
        );

        public static PreprocessingOptions SiglipPreset => new PreprocessingOptions(
            224,
            new float[] { 0.5f, 0.5f, 0.5f },
            new float[] { 0.5f, 0.5f, 0.5f },
            StretchNoCrop: true,
            Resampler: "Bicubic",
            Rescale: 1f / 255f
        );
    }

    public class ImageEmbeddingService : IEmbeddingService
    {
        private readonly IInferenceEngine _engine;
        private readonly IImageRepository _repository;
        private readonly PreprocessingOptions _options;
        private readonly string _inputName;
        private readonly PhotoCull.Core.Abstractions.IImageDecoder? _decoder;

        public ImageEmbeddingService(IInferenceEngine engine, IImageRepository repository, PreprocessingOptions? options = null, string? inputName = null, PhotoCull.Core.Abstractions.IImageDecoder? decoder = null)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _options = options ?? PreprocessingOptions.ClipPreset;
            _decoder = decoder;

            // determine input name: prefer provided, else inference engine metadata, else fallback
            if (!string.IsNullOrEmpty(inputName)) _inputName = inputName;
            else _inputName = engine.InputNames?.FirstOrDefault() ?? "input";
        }

        public async Task<float[]> CreateEmbeddingAsync(Stream imageStream, CancellationToken cancellationToken = default)
        {
            if (imageStream == null) throw new ArgumentNullException(nameof(imageStream));

            // Preprocess image to NCHW float tensor according to options
            Tensor<float> input = await Task.Run(() => Preprocess(imageStream, _options), cancellationToken).ConfigureAwait(false);

            var inputs = new Dictionary<string, Tensor<float>> { { _inputName, input } };
            var outputs = _engine.Run(inputs);
            if (outputs == null || outputs.Count == 0) return Array.Empty<float>();

            // pick output name from engine metadata if available
            string chosen = outputs.Keys.FirstOrDefault(k => string.Equals(k, "image_embeds", StringComparison.OrdinalIgnoreCase))
                            ?? outputs.Keys.FirstOrDefault(k => string.Equals(k, "pooler_output", StringComparison.OrdinalIgnoreCase))
                            ?? outputs.Keys.First();

            var first = outputs[chosen];
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

        private Tensor<float> Preprocess(Stream imageStream, PreprocessingOptions opts)
        {
            imageStream.Position = 0;
            try
            {
                using var img = Image.Load<Rgb24>(imageStream);
                return PreprocessImage(img, opts);
            }
            catch (SixLabors.ImageSharp.UnknownImageFormatException)
            {
                if (_decoder != null)
                {
                    var decoded = _decoder.DecodeToJpegAsync(imageStream).GetAwaiter().GetResult();
                    if (decoded != null)
                    {
                        decoded.Position = 0;
                        using var img = Image.Load<Rgb24>(decoded);
                        return PreprocessImage(img, opts);
                    }
                }
                throw;
            }
        }

        private static Tensor<float> PreprocessImage(Image<Rgb24> img, PreprocessingOptions opts)
        {
            int target = opts.Size;

            if (opts.StretchNoCrop)
            {
                img.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(target, target),
                    Sampler = (opts.Resampler == "Bicubic") ? KnownResamplers.Bicubic : KnownResamplers.Bicubic,
                    Mode = ResizeMode.Stretch
                }));
            }
            else
            {
                img.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(target, target),
                    Sampler = (opts.Resampler == "Bicubic") ? KnownResamplers.Bicubic : KnownResamplers.Bicubic,
                    Mode = ResizeMode.Crop,
                    Position = AnchorPositionMode.Center
                }));
            }

            // create tensor [1,3,H,W]
            var tensor = new DenseTensor<float>(new[] { 1, 3, target, target });

            img.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < target; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < target; x++)
                    {
                        var p = row[x];
                        // rescale (p.R is 0..255)
                        float r = p.R * opts.Rescale;
                        float g = p.G * opts.Rescale;
                        float b = p.B * opts.Rescale;
                        // apply mean/std
                        float rn = (r - opts.Mean[0]) / opts.Std[0];
                        float gn = (g - opts.Mean[1]) / opts.Std[1];
                        float bn = (b - opts.Mean[2]) / opts.Std[2];
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
