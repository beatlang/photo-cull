using System.IO;
using System.Threading.Tasks;
using ImageMagick;
using PhotoCull.Infrastructure.Imaging;
using SixLabors.ImageSharp;
using Xunit;

namespace PhotoCull.Tests
{
    public class ImageDecoderTests
    {
        [Fact]
        public async Task MagickDecoder_DecodesTiffRoundtrip()
        {
            using var img = new MagickImage(MagickColors.CornflowerBlue, 64, 48);
            using var ms = new MemoryStream();
            img.Format = MagickFormat.Tiff;
            img.Write(ms);
            ms.Position = 0;

            var decoder = new MagickImageDecoder();
            var decoded = await decoder.DecodeToJpegAsync(ms, 2048, ".tiff", forceMagick: true);
            Assert.NotNull(decoded);
            decoded.Position = 0;
            using var loaded = SixLabors.ImageSharp.Image.Load(decoded);
            Assert.True(loaded.Width > 0 && loaded.Height > 0);
        }
    }
}
