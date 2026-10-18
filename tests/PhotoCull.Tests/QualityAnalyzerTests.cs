using System.IO;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;

namespace PhotoCull.Tests.Quality
{
    public class QualityAnalyzerTests
    {
        [Fact]
        public async Task SharpImage_IsSharperThanBlurred()
        {
            var analyzer = new PhotoCull.Infrastructure.Quality.QualityAnalyzer();

            using var sharp = CreateCheckerboard(256, 256, 8);
            using var msSharp = new MemoryStream();
            await sharp.SaveAsPngAsync(msSharp);
            msSharp.Position = 0;

            using var blurredImg = sharp.Clone(ctx => ctx.GaussianBlur(5f));
            using var msBlur = new MemoryStream();
            await blurredImg.SaveAsPngAsync(msBlur);
            msBlur.Position = 0;

            var sharpMetrics = await analyzer.AnalyzeAsync(msSharp);
            var blurMetrics = await analyzer.AnalyzeAsync(msBlur);

            Assert.True(sharpMetrics.Sharpness > blurMetrics.Sharpness, "Sharp image should have higher sharpness than blurred one");
            Assert.True(sharpMetrics.Quality > blurMetrics.Quality, "Sharp image should have higher overall quality than blurred one");
        }

        [Fact]
        public async Task Exposure_DarkVsBright()
        {
            var analyzer = new PhotoCull.Infrastructure.Quality.QualityAnalyzer();

            using var dark = CreateSolid(200, 200, new Rgba32(5,5,5));
            using var msDark = new MemoryStream(); await dark.SaveAsPngAsync(msDark); msDark.Position = 0;

            using var normal = CreateCheckerboard(200,200,10);
            using var msNormal = new MemoryStream(); await normal.SaveAsPngAsync(msNormal); msNormal.Position = 0;

            using var bright = CreateSolid(200,200,new Rgba32(250,250,250));
            using var msBright = new MemoryStream(); await bright.SaveAsPngAsync(msBright); msBright.Position = 0;

            var mDark = await analyzer.AnalyzeAsync(msDark);
            var mNormal = await analyzer.AnalyzeAsync(msNormal);
            var mBright = await analyzer.AnalyzeAsync(msBright);

            Assert.True(mNormal.ExposureScore > mDark.ExposureScore, "Normal should have higher exposure score than dark");
            Assert.True(mBright.ExposureScore >= mDark.ExposureScore, "Bright should have higher or equal exposure score than dark");
        }

        [Fact]
        public async Task Noise_NoisyVsClean()
        {
            var analyzer = new PhotoCull.Infrastructure.Quality.QualityAnalyzer();

            using var clean = CreateCheckerboard(256,256,8);
            using var msClean = new MemoryStream(); await clean.SaveAsPngAsync(msClean); msClean.Position = 0;

            using var noisy = AddNoise(clean);
            using var msNoisy = new MemoryStream(); await noisy.SaveAsPngAsync(msNoisy); msNoisy.Position = 0;

            var mClean = await analyzer.AnalyzeAsync(msClean);
            var mNoisy = await analyzer.AnalyzeAsync(msNoisy);

            Assert.True(mNoisy.NoiseEstimate > mClean.NoiseEstimate, "Noisy image should have higher noise estimate");
            Assert.True(mClean.Quality > mNoisy.Quality, "Clean image should have higher quality than noisy one");
        }

        private static Image<Rgba32> CreateCheckerboard(int w, int h, int cellSize)
        {
            var img = new Image<Rgba32>(w, h);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool white = ((x / cellSize) + (y / cellSize)) % 2 == 0;
                img[x,y] = white ? new Rgba32(255,255,255) : new Rgba32(0,0,0);
            }
            return img;
        }

        private static Image<Rgba32> CreateSolid(int w, int h, Rgba32 color)
        {
            var img = new Image<Rgba32>(w, h);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) img[x,y] = color;
            return img;
        }

        private static Image<Rgba32> AddNoise(Image<Rgba32> src)
        {
            var img = src.Clone();
            var rand = new System.Random(123);
            img.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < row.Length; x++)
                    {
                        var p = row[x];
                        byte n = (byte)rand.Next(0,40);
                        row[x] = new Rgba32(
                            (byte)System.Math.Min(255, p.R + n),
                            (byte)System.Math.Min(255, p.G + n),
                            (byte)System.Math.Min(255, p.B + n));
                    }
                }
            });
            return img;
        }
    }
}
