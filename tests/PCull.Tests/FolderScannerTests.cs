using PCull.Core;
using PCull.Infrastructure;
using ImageMagick;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PCull.Tests
{
    public class FolderScannerTests
    {
        [Fact]
        public async Task FindsImagesRecursiveAndCaseInsensitive()
        {
            var root = Path.Combine(Path.GetTempPath(), "pc_test_" + Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            try
            {
                var nested = Path.Combine(root, "Nested");
                Directory.CreateDirectory(nested);

                var p1 = Path.Combine(root, "a.JPG");
                var p2 = Path.Combine(nested, "b.png");
                using (var img = TestImages.Solid(100, 80, 10, 20, 30))
                {
                    TestImages.Save(img, p2, MagickFormat.Png);
                }
                using (var img = TestImages.Solid(200, 150, 10, 20, 30))
                {
                    TestImages.Save(img, p1, MagickFormat.Jpeg);
                }

                var scanner = new FolderScanner();
                var list = await scanner.ScanAsync(root, CancellationToken.None).ToListAsync();

                Assert.Contains(list, r => Path.GetFileName(r.Path) == "a.JPG");
                Assert.Contains(list, r => Path.GetFileName(r.Path) == "b.png");
                var rec = list.First(r => Path.GetFileName(r.Path) == "a.JPG");
                Assert.Equal(200, rec.Width);
                Assert.Equal(150, rec.Height);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
    }
}
