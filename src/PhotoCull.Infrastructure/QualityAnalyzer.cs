using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenCvSharp;
using PhotoCull.Core.Models;

namespace PhotoCull.Infrastructure.Quality
{
    public class QualityAnalyzer : PhotoCull.Core.Abstractions.IQualityAnalyzer
    {
        private readonly PhotoCull.Core.Abstractions.IImageDecoder? _decoder;
        public QualityAnalyzer(PhotoCull.Core.Abstractions.IImageDecoder? decoder = null)
        {
            _decoder = decoder;
        }

        public async Task<QualityMetrics> AnalyzeAsync(Stream imageStream)
        {
            byte[] data;
            using (var ms = new MemoryStream())
            {
                await imageStream.CopyToAsync(ms);
                data = ms.ToArray();
            }

            var mat = Cv2.ImDecode(data, ImreadModes.Color);
            if (mat.Empty())
            {
                if (_decoder != null)
                {
                    var decoded = await _decoder.DecodeToJpegAsync(new MemoryStream(data));
                    if (decoded != null)
                    {
                        var decBytes = decoded.ToArray();
                        mat = Cv2.ImDecode(decBytes, ImreadModes.Color);
                    }
                }
                if (mat.Empty()) throw new InvalidDataException("Could not decode image");
                }

            var metrics = new QualityMetrics();

            var gray = new Mat();
            Cv2.CvtColor(mat, gray, ColorConversionCodes.BGR2GRAY);

            var lap = new Mat();
            Cv2.Laplacian(gray, lap, MatType.CV_64F);
            Cv2.MeanStdDev(lap, out Scalar meanL, out Scalar stdL);
            double var = stdL.Val0 * stdL.Val0;
            float sharpness = (float)Math.Min(100.0, var / 10.0);
            metrics.Sharpness = sharpness;
            metrics.IsBlurWarning = sharpness < 15.0f;

            var mean = Cv2.Mean(gray).Val0;
            metrics.MeanBrightness = (float)mean;

            int total = Math.Max(1, gray.Rows * gray.Cols);
            var overMask = new Mat();
            Cv2.Threshold(gray, overMask, 250, 255, ThresholdTypes.Binary);
            var underMask = new Mat();
            Cv2.Threshold(gray, underMask, 5, 255, ThresholdTypes.BinaryInv);
            int overCount = Cv2.CountNonZero(overMask);
            int underCount = Cv2.CountNonZero(underMask);
            double overFrac = (double)overCount / total;
            double underFrac = (double)underCount / total;
            float exposureScore = (float)(100.0 * (1.0 - Math.Max(overFrac, underFrac)));
            metrics.ExposureScore = exposureScore;

            var med = new Mat();
            Cv2.MedianBlur(gray, med, 3);
            var diff = new Mat();
            Cv2.Absdiff(gray, med, diff);
            var meanDiff = Cv2.Mean(diff).Val0;
            float noiseEstimate = (float)Math.Min(100.0, meanDiff * 2.0);
            metrics.NoiseEstimate = noiseEstimate;

            metrics.HistogramB = CalcHistogram(mat, 0);
            metrics.HistogramG = CalcHistogram(mat, 1);
            metrics.HistogramR = CalcHistogram(mat, 2);
            metrics.HistogramLuminance = CalcHistogram(gray, -1);

            float noiseQuality = 100.0f - metrics.NoiseEstimate;
            float brightnessQuality = (float)(100.0 * (1.0 - Math.Abs(mean - 127.0) / 127.0));
            if (brightnessQuality < 0) brightnessQuality = 0;

            metrics.Quality = (float)(0.5 * metrics.Sharpness + 0.2 * exposureScore + 0.1 * brightnessQuality + 0.2 * noiseQuality);
            if (metrics.Quality < 0) metrics.Quality = 0;
            if (metrics.Quality > 100) metrics.Quality = 100;

            mat.Dispose(); gray.Dispose(); lap.Dispose(); overMask.Dispose(); underMask.Dispose(); med.Dispose(); diff.Dispose();

            return metrics;
        }

        private int[] CalcHistogram(Mat mat, int channel)
        {
            var res = new int[256];
            if (channel >= 0)
            {
                // color image - iterate pixels
                for (int y = 0; y < mat.Rows; y++)
                {
                    for (int x = 0; x < mat.Cols; x++)
                    {
                        var v = mat.At<Vec3b>(y, x)[channel];
                        res[v]++;
                    }
                }
                return res;
            }
            else
            {
                // single-channel image
                for (int y = 0; y < mat.Rows; y++)
                {
                    for (int x = 0; x < mat.Cols; x++)
                    {
                        var v = mat.At<byte>(y, x);
                        res[v]++;
                    }
                }
                return res;
            }
        }
    }
}
