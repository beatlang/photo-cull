using System.IO;
using ImageMagick;
using PCull.Infrastructure.Metadata;
using Xunit;

namespace PCull.Tests.Metadata
{
    public class HashServiceTests
    {
        private static MemoryStream Encode(MagickImage img, bool jpeg, int quality)
            => jpeg ? TestImages.ToStream(img, MagickFormat.Jpeg, (uint)quality) : TestImages.ToStream(img, MagickFormat.Png);

        private static MemoryStream CreatePatternedImage(int width, int height, (byte R, byte G, byte B) baseColor, (byte R, byte G, byte B) altColor, bool jpeg = false, int quality = 75)
        {
            // simple checker-like pattern to introduce structure
            using var img = TestImages.FromRgb(width, height, (x, y) => (((x / 10) + (y / 10)) % 2 == 0) ? baseColor : altColor);
            return Encode(img, jpeg, quality);
        }

        private static MemoryStream CreateGradientImage(int width, int height, (byte R, byte G, byte B) leftColor, (byte R, byte G, byte B) rightColor, bool jpeg = false, int quality = 75)
        {
            using var img = TestImages.FromRgb(width, height, (x, y) =>
            {
                float t = x / (float)(width - 1);
                return ((byte)(leftColor.R * (1 - t) + rightColor.R * t),
                        (byte)(leftColor.G * (1 - t) + rightColor.G * t),
                        (byte)(leftColor.B * (1 - t) + rightColor.B * t));
            });
            return Encode(img, jpeg, quality);
        }

        private static MemoryStream CreateResizedFromStream(Stream original, int width, int height, bool jpeg = false, int quality = 75)
        {
            original.Position = 0;
            using var img = new MagickImage(original);
            img.FilterType = FilterType.Cubic;
            img.Resize(new MagickGeometry((uint)width, (uint)height) { IgnoreAspectRatio = true });
            return Encode(img, jpeg, quality);
        }
        [Fact]
        public void IdenticalImages_HammingDistanceIsZero()
        {
            using var a = CreatePatternedImage(200, 200, ((byte)255, (byte)0, (byte)0), ((byte)200, (byte)50, (byte)50));
            using var b = CreatePatternedImage(200, 200, ((byte)255, (byte)0, (byte)0), ((byte)200, (byte)50, (byte)50));
            var svc = new HashService();
            var ha = svc.ComputeAHash(a);
            a.Position = 0;
            var hb = svc.ComputeAHash(b);
            Assert.Equal(0, svc.HammingDistance(ha, hb));
        }

        private static MemoryStream CreateBlocksImage(int size)
        {
            var rnd = new Random(7);
            var levels = new byte[8, 8];
            for (int i = 0; i < 8; i++) for (int j = 0; j < 8; j++) levels[i, j] = (byte)rnd.Next(256);
            using var img = TestImages.FromRgb(size, size, (x, y) =>
            {
                byte v = levels[x * 8 / size, y * 8 / size];
                return (v, v, v);
            });
            return Encode(img, jpeg: false, quality: 0);
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
            using var a = CreateGradientImage(200, 200, ((byte)255, (byte)0, (byte)0), ((byte)0, (byte)0, (byte)255));
            using var b = CreatePatternedImage(200, 200, ((byte)0, (byte)0, (byte)255), ((byte)50, (byte)50, (byte)200));
            var svc = new HashService();
            var ha = svc.ComputePHash(a);
            a.Position = 0;
            var hb = svc.ComputePHash(b);
            int d = svc.HammingDistance(ha, hb);
            Assert.True(d >= 20, $"Distance was {d}");
        }
    }
}



