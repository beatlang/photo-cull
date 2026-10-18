using System;
using System.IO;
using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using PhotoCull.Core.Abstractions;

namespace PhotoCull.Infrastructure.Metadata
{
    public class HashService : IHashService
    {
        private readonly PhotoCull.Core.Abstractions.IImageDecoder? _decoder;

        public HashService(PhotoCull.Core.Abstractions.IImageDecoder? decoder = null)
        {
            _decoder = decoder;
        }

        private Image<Rgba32> LoadImageWithFallback(Stream stream)
        {
            if (stream.CanSeek) stream.Position = 0;
            try
            {
                return Image.Load<Rgba32>(stream);
            }
            catch (SixLabors.ImageSharp.UnknownImageFormatException)
            {
                if (_decoder != null)
                {
                    var decoded = _decoder.DecodeToJpegAsync(stream).GetAwaiter().GetResult();
                    if (decoded != null)
                    {
                        decoded.Position = 0;
                        return Image.Load<Rgba32>(decoded);
                    }
                }
                throw;
            }
        }

        public ulong ComputeAHash(Stream stream)
        {
            using var img = LoadImageWithFallback(stream);
            img.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(8, 8), Mode = ResizeMode.Stretch, Sampler = KnownResamplers.Bicubic }));
            byte[] gray = new byte[64];
            int idx = 0;
            for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
            {
                var p = img[x, y];
                gray[idx++] = (byte)Math.Round(p.R * 0.299 + p.G * 0.587 + p.B * 0.114);
            }
            int sum = 0;
            for (int i = 0; i < 64; i++) sum += gray[i];
            double avg = sum / 64.0;
            ulong hash = 0;
            for (int i = 0; i < 64; i++)
            {
                if (gray[i] > avg)
                    hash |= 1UL << i;
            }
            return hash;
        }

        public ulong ComputeDHash(Stream stream)
        {
            using var img = LoadImageWithFallback(stream);
            // dHash: resize to 9x8, compare left-right using luminance
            img.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(9, 8), Mode = ResizeMode.Stretch, Sampler = KnownResamplers.Lanczos3 }));
            ulong hash = 0;
            int bit = 0;
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    var a = img[x, y];
                    var b = img[x + 1, y];
                    double ga = a.R * 0.299 + a.G * 0.587 + a.B * 0.114;
                    double gb = b.R * 0.299 + b.G * 0.587 + b.B * 0.114;
                    if (ga > gb)
                        hash |= 1UL << bit;
                    bit++;
                }
            }
            return hash;
        }

        public ulong ComputePHash(Stream stream)
        {
            using var img = LoadImageWithFallback(stream);
            // resize to 32x32 grayscale with good resampler
            img.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(32, 32), Mode = ResizeMode.Stretch, Sampler = KnownResamplers.Bicubic }));
            double[,] vals = new double[32, 32];
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    var p = img[x, y];
                    vals[x, y] = p.R * 0.299 + p.G * 0.587 + p.B * 0.114;
                }

            // compute 2D DCT (type-II)
            var dct = DCT2D(vals, 32);

            // take top-left 8x8 coefficients (u,v = 0..7)
            double[] coefs = new double[64];
            int idx = 0;
            for (int v = 0; v < 8; v++)
                for (int u = 0; u < 8; u++)
                    coefs[idx++] = dct[u, v];

            double[] tmp = new double[63];
            Array.Copy(coefs, 1, tmp, 0, 63);
            Array.Sort(tmp);
            double median = tmp[tmp.Length / 2];

            ulong hash = 0UL;
            for (int i = 0; i < 64; i++)
            {
                if (coefs[i] > median)
                    hash |= 1UL << i;
            }
            return hash;
        }

        public int HammingDistance(ulong a, ulong b)
        {
            return BitOperations.PopCount(a ^ b);
        }

        public bool IsNearDuplicate(ulong a, ulong b, int maxDistance)
        {
            return HammingDistance(a, b) <= maxDistance;
        }

        // 2D DCT (type-II) implementation optimized a bit using precomputed cosines
        private static double[,] DCT2D(double[,] data, int n)
        {
            double[,] result = new double[n, n];
            double[,] cosX = new double[n, n];
            double[,] cosY = new double[n, n];
            double c = Math.PI / (2.0 * n);
            for (int k = 0; k < n; k++)
            {
                for (int i = 0; i < n; i++)
                {
                    cosX[k, i] = Math.Cos((2 * i + 1) * k * c);
                    cosY[k, i] = cosX[k, i];
                }
            }

            for (int u = 0; u < n; u++)
            {
                for (int v = 0; v < n; v++)
                {
                    double sum = 0.0;
                    for (int x = 0; x < n; x++)
                    {
                        for (int y = 0; y < n; y++)
                        {
                            sum += data[x, y] * cosX[u, x] * cosY[v, y];
                        }
                    }
                    double cu = u == 0 ? 1.0 / Math.Sqrt(2.0) : 1.0;
                    double cv = v == 0 ? 1.0 / Math.Sqrt(2.0) : 1.0;
                    result[u, v] = 0.25 * cu * cv * sum;
                }
            }
            return result;
        }
    }
}
