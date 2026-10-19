using System;
using System.IO;
using ImageMagick;

namespace PCull.Tests
{
    /// <summary>Helpers to generate synthetic test images with Magick.NET.</summary>
    internal static class TestImages
    {
        public static MagickImage FromRgb(int width, int height, Func<int, int, (byte R, byte G, byte B)> pixel)
        {
            var data = new byte[width * height * 3];
            int i = 0;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var (r, g, b) = pixel(x, y);
                data[i++] = r;
                data[i++] = g;
                data[i++] = b;
            }
            return new MagickImage(data, new PixelReadSettings((uint)width, (uint)height, StorageType.Char, PixelMapping.RGB));
        }

        public static MagickImage Solid(int width, int height, byte r, byte g, byte b)
            => FromRgb(width, height, (_, _) => (r, g, b));

        public static MagickImage Checkerboard(int width, int height, int cell)
            => FromRgb(width, height, (x, y) => (((x / cell) + (y / cell)) % 2 == 0) ? ((byte)255, (byte)255, (byte)255) : ((byte)0, (byte)0, (byte)0));

        public static MemoryStream ToStream(MagickImage img, MagickFormat format, uint quality = 0)
        {
            var ms = new MemoryStream();
            if (quality > 0) img.Quality = quality;
            img.Write(ms, format);
            ms.Position = 0;
            return ms;
        }

        public static void Save(MagickImage img, string path, MagickFormat format)
            => img.Write(path, format);
    }
}
