namespace EQD2Viewer.Core.Models
{
    public static class DomainConstants
    {
        public const int HuOffsetRawThreshold = 30000;
        public const int HuOffsetValue = 32768;
        public const int HuOffsetSampleStep = 8;
        public const double NormalizationFractionThreshold = 5.0;
        public const double MinReferenceDoseGy = 0.1;
        public const int DoseCalibrationRawValue = 10000;
        public const double DvhSamplingResolution = 0.01;
        public const int DvhHistogramBins = 1000;
        public const double PointQuantization = 1000.0;
        public const long PointHashMultiplier = 100000000L;

        /// <summary>α/β difference below which the "isodose and summation agree" label is
        /// shown. Below 0.05 Gy the physical EQD2 curve is indistinguishable numerically.</summary>
        public const double AlphaBetaMatchTolerance = 0.05;
    }
}