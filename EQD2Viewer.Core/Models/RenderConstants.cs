namespace EQD2Viewer.Core.Models
{
    /// <summary>
    /// Provides constants related to UI rendering, image manipulation, and interaction logic.
    /// </summary>
    /// <remarks>
    /// Domain and physics-specific constants are located in the DomainConstants class.
    /// </remarks>
    public static class RenderConstants
    {
        /// <summary>
        /// The maximum dose represented as a fraction of the reference dose, used to determine the upper bound for the colorwash rendering.
        /// </summary>
        public const double ColorwashMaxFraction = 1.15;

        /// <summary>
        /// The size of the checkerboard blocks, in pixels, used for the image registration verification overlay.
        /// </summary>
        public const int CheckerboardBlockSize = 32;

        /// <summary>
        /// The absolute minimum zoom factor permitted for the interactive image viewer.
        /// </summary>
        public const double MinZoom = 0.1;

        /// <summary>
        /// The absolute maximum zoom factor permitted for the interactive image viewer.
        /// </summary>
        public const double MaxZoom = 10.0;

        /// <summary>
        /// The multiplicative factor applied to the current zoom level per discrete mouse scroll tick.
        /// </summary>
        public const double ZoomStepFactor = 1.1;

        /// <summary>
        /// The mouse movement sensitivity scaling factor (pixels to Hounsfield Units) used for adjusting image window width and level.
        /// </summary>
        public const double WindowingSensitivity = 2.0;

        /// <summary>
        /// The minimum allowable interval between frame updates, in milliseconds, used to throttle windowing operations to maintain performance.
        /// </summary>
        public const double WindowingThrottleMs = 33.0;

        /// <summary>
        /// The default stroke thickness applied to anatomical structure contours, measured in CT image pixel units.
        /// </summary>
        public const double StructureContourThickness = 1.5;

        /// <summary>
        /// The interval at which progress reporting is triggered during dose summation, measured in the number of slices processed.
        /// </summary>
        public const int SummationProgressInterval = 4;

        /// <summary>
        /// The debounce delay, in milliseconds, applied to alpha/beta parameter sliders to prevent excessive recalculations during active summation adjustments.
        /// </summary>
        public const int AlphaBetaDebounceMs = 500;
    }
}