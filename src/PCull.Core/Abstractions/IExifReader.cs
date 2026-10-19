using System.IO;
using PCull.Core.Models;

namespace PCull.Core.Abstractions
{
    public interface IExifReader
    {
        ExifInfo? ReadExif(Stream stream);
    }
}
