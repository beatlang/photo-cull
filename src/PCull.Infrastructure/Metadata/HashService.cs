using System;
using System.IO;
using System.Numerics;
using ImageMagick;
using PCull.Core.Abstractions;

namespace PCull.Infrastructure.Metadata
{
    public class HashService : IHashService
    {
        private readonly PCull.Core.Abstractions.IImageDecoder? _decoder;

        public HashService(IImageDecoder? decoder = null)
        {
            _decoder = decoder;
        }

        private MagickImage LoadImageWithFallback(Stream stream)
        {
            if (stream.CanSeek) stream.Position = 0;
            try
            {
                return new MagickImage(stream);
            }
            catch (MagickException)
            {
                if (_decoder != null)
                {
                    if (stream.CanSeek) stream.Position = 0;
                    var decoded = _decoder.DecodeToJpegAsync(stream).GetAwaiter().GetResult();
                    if (decoded != null)
                    {
                        using (decoded)
                        {
                            decoded.Position = 0;
                            return new MagickImage(decoded);
                        }
                    }
                }
                throw;
            }
        }

        private static byte[] ResizeToRgb(MagickImage image, uint width, uint height, FilterType filter)
        {
            image.Resize(width, height, filter);
            image.ColorSpace = ColorSpace.sRGB;
            return image.GetPixels().ToByteArray("RGB");
        }

        private static double Luminance(byte[] rgb, int pixelIndex)
        {
            int offset = pixelIndex * 3;
            return rgb[offset] * 0.299 + rgb[offset + 1] * 0.587 + rgb[offset + 2] * 0.114;
        }

        public ulong ComputeAHash(Stream stream)
        {
            using var img = LoadImageWithFallback(stream);
            var rgb = ResizeToRgb(img, 8, 8, FilterType.Cubic);
            byte[] gray = new byte[64];
            int sum = 0;
            for (int i = 0; i < gray.Length; i++)
            {
                gray[i] = (byte)Math.Round(Luminance(rgb, i));
                sum += gray[i];
            }
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
            var rgb = ResizeToRgb(img, 9, 8, FilterType.Triangle);
            ulong hash = 0;
            int bit = 0;
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    if (Luminance(rgb, y * 9 + x) > Luminance(rgb, y * 9 + x + 1))
                        hash |= 1UL << bit;
                    bit++;
                }
            }
            return hash;
        }

        public ulong ComputePHash(Stream stream)
        {
            using var img = LoadImageWithFallback(stream);
            var rgb = ResizeToRgb(img, 32, 32, FilterType.Cubic);
            double[,] vals = new double[32, 32];
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                    vals[x, y] = Luminance(rgb, y * 32 + x);

            var dct = DCT2D(vals, 32);
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
                            sum += data[x, y] * cosX[u, x] * cosY[v, y];
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
