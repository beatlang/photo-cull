using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using PCull.Infrastructure.Metadata;
using Xunit;

namespace PCull.Tests.Metadata
{
    public class HashServiceTests
    {
        private static MemoryStream CreatePatternedImage(int width, int height, Rgba32 baseColor, Rgba32 altColor, bool jpeg = false, int quality = 75)
        {
            var ms = new MemoryStream();
            using var img = new Image<Rgba32>(width, height);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                // simple checker-like pattern to introduce structure
                if (((x / 10) + (y / 10)) % 2 == 0)
                    img[x, y] = baseColor;
                else
                    img[x, y] = altColor;
            }
            if (jpeg)
            {
                img.Save(ms, new JpegEncoder { Quality = quality });
            }
            else
            {
                img.SaveAsPng(ms);
            }
            ms.Position = 0;
            return ms;
        }

        private static MemoryStream CreateGradientImage(int width, int height, Rgba32 leftColor, Rgba32 rightColor, bool jpeg = false, int quality = 75)
        {
            var ms = new MemoryStream();
            using var img = new Image<Rgba32>(width, height);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float t = x / (float)(width - 1);
                byte r = (byte)(leftColor.R * (1 - t) + rightColor.R * t);
                byte g = (byte)(leftColor.G * (1 - t) + rightColor.G * t);
                byte b = (byte)(leftColor.B * (1 - t) + rightColor.B * t);
                img[x, y] = new Rgba32(r, g, b);
            }
            if (jpeg)
            {
                img.Save(ms, new JpegEncoder { Quality = quality });
            }
            else
            {
                img.SaveAsPng(ms);
            }
            ms.Position = 0;
            return ms;
        }

        private static MemoryStream CreateResizedFromStream(Stream original, int width, int height, bool jpeg = false, int quality = 75)
        {
            original.Position = 0;
            var ms = new MemoryStream();
            using var img = Image.Load<Rgba32>(original);
            img.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(width, height), Mode = ResizeMode.Stretch, Sampler = KnownResamplers.Bicubic }));
            if (jpeg)
                img.Save(ms, new JpegEncoder { Quality = quality });
            else
                img.SaveAsPng(ms);
            ms.Position = 0;
            return ms;
        }

        [Fact]
        public void IdenticalImages_HammingDistanceIsZero()
        {
            using var a = CreatePatternedImage(200, 200, new Rgba32(255, 0, 0), new Rgba32(200, 50, 50));
            using var b = CreatePatternedImage(200, 200, new Rgba32(255, 0, 0), new Rgba32(200, 50, 50));
            var svc = new HashService();
            var ha = svc.ComputeAHash(a);
            a.Position = 0;
            var hb = svc.ComputeAHash(b);
            Assert.Equal(0, svc.HammingDistance(ha, hb));
        }

                private static MemoryStream CreateBlocksImage(int size)
        {
            var rnd = new Random(7);
            var ms = new MemoryStream();
            using var img = new Image<Rgba32>(size, size);
            var levels = new byte[8, 8];
            for (int i = 0; i < 8; i++) for (int j = 0; j < 8; j++) levels[i, j] = (byte)rnd.Next(256);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                byte v = levels[x * 8 / size, y * 8 / size];
                img[x, y] = new Rgba32(v, v, v);
            }
            img.SaveAsPng(ms);
            ms.Position = 0;
            return ms;
        }

        [Fact]
        public void ResizedOrRecompressed_IsNearDuplicate()
        {
            using var orig = CreateBlocksImage(200);
            using var resized = CreateResizedFromStream(orig, 100, 100, jpeg: true, quality: 80);
            orig.Position = 0;
            resized.Position = 0;
            var svc = new HashService();
            var h1 = svc.ComputePHash(orig);
            orig.Position = 0;
            var h2 = svc.ComputePHash(resized);
            int d = svc.HammingDistance(h1, h2);
            Assert.True(d <= 12, $"Distance was {d}");
        }

        [Fact]
        public void DifferentImage_IsNotNearDuplicate()
        {
            using var a = CreateGradientImage(200, 200, new Rgba32(255, 0, 0), new Rgba32(0, 0, 255));
            using var b = CreatePatternedImage(200, 200, new Rgba32(0, 0, 255), new Rgba32(50, 50, 200));
            var svc = new HashService();
            var ha = svc.ComputePHash(a);
            a.Position = 0;
            var hb = svc.ComputePHash(b);
            int d = svc.HammingDistance(ha, hb);
            Assert.True(d >= 20, $"Distance was {d}");
        }
    }
}



