using System.IO;
using PhotoCull.Core.Models;

namespace PhotoCull.Core.Abstractions
{
    public interface IExifReader
    {
        ExifInfo? ReadExif(Stream stream);
    }
}
