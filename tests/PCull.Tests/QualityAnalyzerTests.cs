using System.IO;
using System.Threading.Tasks;
using ImageMagick;
using Xunit;

namespace PCull.Tests.Quality
{
    public class QualityAnalyzerTests
    {
        [Fact]
        public async Task SharpImage_IsSharperThanBlurred()
        {
            var analyzer = new PCull.Infrastructure.Quality.QualityAnalyzer();

            using var sharp = CreateCheckerboard(256, 256, 8);
            using var msSharp = new MemoryStream();
            sharp.Write(msSharp, MagickFormat.Png);
            msSharp.Position = 0;

            using var blurredImg = (MagickImage)sharp.Clone();
            blurredImg.GaussianBlur(0, 5);
            using var msBlur = new MemoryStream();
            blurredImg.Write(msBlur, MagickFormat.Png);
            msBlur.Position = 0;

            var sharpMetrics = await analyzer.AnalyzeAsync(msSharp);
            var blurMetrics = await analyzer.AnalyzeAsync(msBlur);

            Assert.True(sharpMetrics.Sharpness > blurMetrics.Sharpness, "Sharp image should have higher sharpness than blurred one");
            Assert.True(sharpMetrics.Quality > blurMetrics.Quality, "Sharp image should have higher overall quality than blurred one");
        }

        [Fact]
        public async Task Exposure_DarkVsBright()
        {
            var analyzer = new PCull.Infrastructure.Quality.QualityAnalyzer();

            using var dark = CreateSolid(200, 200, 5);
            using var msDark = new MemoryStream(); dark.Write(msDark, MagickFormat.Png); msDark.Position = 0;

            using var normal = CreateCheckerboard(200,200,10);
            using var msNormal = new MemoryStream(); normal.Write(msNormal, MagickFormat.Png); msNormal.Position = 0;

            using var bright = CreateSolid(200,200,250);
            using var msBright = new MemoryStream(); bright.Write(msBright, MagickFormat.Png); msBright.Position = 0;

            var mDark = await analyzer.AnalyzeAsync(msDark);
            var mNormal = await analyzer.AnalyzeAsync(msNormal);
            var mBright = await analyzer.AnalyzeAsync(msBright);

            Assert.True(mNormal.ExposureScore > mDark.ExposureScore, "Normal should have higher exposure score than dark");
            Assert.True(mBright.ExposureScore >= mDark.ExposureScore, "Bright should have higher or equal exposure score than dark");
        }

        [Fact]
        public async Task Noise_NoisyVsClean()
        {
            var analyzer = new PCull.Infrastructure.Quality.QualityAnalyzer();

            using var clean = CreateCheckerboard(256,256,8);
            using var msClean = new MemoryStream(); clean.Write(msClean, MagickFormat.Png); msClean.Position = 0;

            using var noisy = AddNoise(clean);
            using var msNoisy = new MemoryStream(); noisy.Write(msNoisy, MagickFormat.Png); msNoisy.Position = 0;

            var mClean = await analyzer.AnalyzeAsync(msClean);
            var mNoisy = await analyzer.AnalyzeAsync(msNoisy);

            Assert.True(mNoisy.NoiseEstimate > mClean.NoiseEstimate, "Noisy image should have higher noise estimate");
            Assert.True(mClean.Quality > mNoisy.Quality, "Clean image should have higher quality than noisy one");
        }

        private static MagickImage CreateCheckerboard(int w, int h, int cellSize) => TestImages.Checkerboard(w, h, cellSize);

        private static MagickImage CreateSolid(int w, int h, byte v) => TestImages.Solid(w, h, v, v, v);

        private static MagickImage AddNoise(MagickImage src)
        {
            var rand = new System.Random(123);
            var data = src.ToByteArray(MagickFormat.Rgb);
            using var raw = new MagickImage(data, new PixelReadSettings(src.Width, src.Height, StorageType.Char, PixelMapping.RGB));
            for (int i = 0; i < data.Length; i += 3)
            {
                byte n = (byte)rand.Next(0, 40);
                for (int c = 0; c < 3; c++) data[i + c] = (byte)System.Math.Min(255, data[i + c] + n);
            }
            return new MagickImage(data, new PixelReadSettings(src.Width, src.Height, StorageType.Char, PixelMapping.RGB));
        }
    }
}