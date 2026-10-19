using PCull.Core;
using PCull.Infrastructure;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Png;
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
                using (var img = new Image<Rgba32>(100, 80))
                {
                    img.SaveAsPng(p2, new PngEncoder());
                }
                using (var img = new Image<Rgba32>(200, 150))
                {
                    await img.SaveAsJpegAsync(p1);
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
