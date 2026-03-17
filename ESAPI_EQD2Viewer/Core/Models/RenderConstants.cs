namespace ESAPI_EQD2Viewer.Core.Models
{
    /// <summary>
    /// Named constants replacing magic numbers throughout the codebase.
    /// Centralized for easy tuning and documentation.
    /// </summary>
    public static class RenderConstants
    {
        // ── CT Windowing ──
        /// <summary>HU offset threshold: if >50% of sampled voxels exceed this, apply 32768 offset.</summary>
        public const int HuOffsetRawThreshold = 30000;
        /// <summary>Offset applied when CT voxels are stored as unsigned (0-65535) instead of signed HU.</summary>
        public const int HuOffsetValue = 32768;
        /// <summary>Sampling step for HU offset auto-detection (every Nth pixel).</summary>
        public const int HuOffsetSampleStep = 8;

        // ── Normalization ──
        /// <summary>If plan normalization is below this, assume it's a fraction (0-1) and multiply by 100.</summary>
        public const double NormalizationFractionThreshold = 5.0;
        /// <summary>Minimum reference dose in Gy to be considered valid.</summary>
        public const double MinReferenceDoseGy = 0.1;

        // ── Colorwash ──
        /// <summary>Maximum dose as fraction of reference dose for colorwash upper bound.</summary>
        public const double ColorwashMaxFraction = 1.15;

        // ── Registration overlay ──
        /// <summary>Checkerboard block size in pixels for registration verification overlay.</summary>
        public const int CheckerboardBlockSize = 32;

        // ── Dose scaling ──
        /// <summary>Reference raw voxel value used for dose calibration (VoxelToDoseValue).</summary>
        public const int DoseCalibrationRawValue = 10000;

        // ── Rendering ──
        /// <summary>Minimum zoom factor for image viewer.</summary>
        public const double MinZoom = 0.1;
        /// <summary>Maximum zoom factor for image viewer.</summary>
        public const double MaxZoom = 10.0;
        /// <summary>Zoom step multiplier per scroll tick.</summary>
        public const double ZoomStepFactor = 1.1;
        /// <summary>Windowing mouse sensitivity (pixels to HU).</summary>
        public const double WindowingSensitivity = 2.0;
        /// <summary>Minimum frame interval in ms for windowing throttle.</summary>
        public const double WindowingThrottleMs = 33.0;

        // ── Marching Squares ──
        /// <summary>Quantization factor for point hashing in segment chaining (1/N pixel).</summary>
        public const double PointQuantization = 1000.0;
        /// <summary>Large multiplier to separate X and Y in hash key.</summary>
        public const long PointHashMultiplier = 100000000L;

        // ── DVH ──
        /// <summary>DVH sampling resolution in Gy.</summary>
        public const double DvhSamplingResolution = 0.01;
        /// <summary>Number of histogram bins for DVH calculation from summed dose.</summary>
        public const int DvhHistogramBins = 1000;

        // ── Structure rendering ──
        /// <summary>Default stroke thickness for structure contours in CT pixel units.</summary>
        public const double StructureContourThickness = 1.5;

        // ── Summation ──
        /// <summary>Progress report interval (every Nth slice).</summary>
        public const int SummationProgressInterval = 4;
        /// <summary>Debounce delay in ms for α/β slider during active summation.</summary>
        public const int AlphaBetaDebounceMs = 500;
    }
}
