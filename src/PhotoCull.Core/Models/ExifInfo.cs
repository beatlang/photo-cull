namespace PhotoCull.Core.Models
{
    public class ExifInfo
    {
        public DateTime? CaptureTime { get; set; }
        public string? Camera { get; set; }
        public string? Lens { get; set; }
        public int? Iso { get; set; }
        public double? Aperture { get; set; }
        public double? FocalLength { get; set; }
        public string? ShutterSpeed { get; set; }
        public int? Orientation { get; set; }
    }
}
