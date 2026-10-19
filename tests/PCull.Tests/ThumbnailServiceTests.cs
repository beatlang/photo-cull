using PCull.Infrastructure;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ImageMagick;
using Xunit;

namespace PCull.Tests
{
    public class ThumbnailServiceTests
    {
        [Fact]
        public async Task GeneratesAndCachesJpegThumbnail()
        {
            var root = Path.Combine(Path.GetTempPath(), "pc_thumb_" + Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            try
            {
                var imagePath = Path.Combine(root, "orig.jpg");
                using (var img = TestImages.Solid(1200, 800, 90, 120, 200))
                {
                    TestImages.Save(img, imagePath, MagickFormat.Jpeg);
                }

                var cacheDir = Path.Combine(root, "cache");
                var svc = new ThumbnailService(512);
                var thumb = await svc.GenerateThumbnailAsync(imagePath, cacheDir, CancellationToken.None);
                Assert.NotNull(thumb);
                Assert.True(File.Exists(thumb));

                var thumb2 = await svc.GenerateThumbnailAsync(imagePath, cacheDir, CancellationToken.None);
                Assert.Equal(thumb, thumb2);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
    }
}
