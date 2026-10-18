using System;

namespace PhotoCull.Core.Models
{
    public class QualityMetrics
    {
        public float Sharpness { get; set; }
        public bool IsBlurWarning { get; set; }
        public float ExposureScore { get; set; }
        public float MeanBrightness { get; set; }
        public float NoiseEstimate { get; set; }
        public float Quality { get; set; }

        public int[] HistogramR { get; set; } = Array.Empty<int>();
        public int[] HistogramG { get; set; } = Array.Empty<int>();
        public int[] HistogramB { get; set; } = Array.Empty<int>();
        public int[] HistogramLuminance { get; set; } = Array.Empty<int>();
    }
}
