using System;
using System.IO;
using System.Linq;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using PhotoCull.Core.Abstractions;
using PhotoCull.Core.Models;

namespace PhotoCull.Infrastructure.Metadata
{
    public class ExifReader : IExifReader
    {
        public ExifInfo? ReadExif(Stream stream)
        {
            try
            {
                // Ensure stream is seekable
                if (stream.CanSeek)
                    stream.Position = 0;

                var directories = ImageMetadataReader.ReadMetadata(stream);

                var exifIfd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
                var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
                var info = new ExifInfo();

                // Camera make/model
                var make = exifIfd0?.GetDescription(ExifDirectoryBase.TagMake);
                var model = exifIfd0?.GetDescription(ExifDirectoryBase.TagModel);
                info.Camera = string.Join(" ", new[] { make, model }.Where(s => !string.IsNullOrWhiteSpace(s)));

                // Lens (try LensModel tag, fallback to MakerNote if present)
                var lens = subIfd?.GetDescription(ExifDirectoryBase.TagLensModel)
                    ?? directories.SelectMany(d => d.Tags).FirstOrDefault(t => t.Name?.ToLowerInvariant().Contains("lens") == true)?.Description;
                info.Lens = lens;

                // ISO
                if (subIfd != null && subIfd.TryGetInt32(ExifDirectoryBase.TagIsoEquivalent, out var iso))
                    info.Iso = iso;

                // Aperture
                if (subIfd != null && subIfd.TryGetDouble(ExifDirectoryBase.TagFNumber, out var fnum))
                    info.Aperture = fnum;

                // Focal length
                if (subIfd != null && subIfd.TryGetDouble(ExifDirectoryBase.TagFocalLength, out var focal))
                    info.FocalLength = focal;

                // Shutter speed (exposure time)
                var shutter = subIfd?.GetDescription(ExifDirectoryBase.TagExposureTime) ?? subIfd?.GetDescription(ExifDirectoryBase.TagShutterSpeed);
                info.ShutterSpeed = shutter;

                // Orientation
                if (exifIfd0 != null && exifIfd0.TryGetInt32(ExifDirectoryBase.TagOrientation, out var orient))
                    info.Orientation = orient;

                return info;
            }
            catch
            {
                // Tolerate any errors and return null
                return null;
            }
        }
    }
}
