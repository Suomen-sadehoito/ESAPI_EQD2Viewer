namespace EQD2Viewer.Core.Data
{
    /// <summary>
    /// Pure data representation of a single isodose level.
    /// No WPF dependencies -- used by IImageRenderingService and rendering logic.
    /// 
    /// The WPF-bindable <c>IsodoseLevel</c> in the Services layer extends this
    /// with INotifyPropertyChanged and MediaColor for UI data binding.
    /// </summary>
    public class IsodoseLevelData
    {
        /// <summary>
        /// Threshold as fraction of reference dose (e.g., 1.10 = 110%, 0.50 = 50%).
        /// Used in relative mode.
        /// </summary>
        public double Fraction { get; set; }

        /// <summary>
        /// Threshold as absolute dose in Gy.
        /// Used in absolute mode (EQD2 summation re-irradiation assessment).
        /// </summary>
        public double AbsoluteDoseGy { get; set; }

        /// <summary>
        /// Isodose line/fill color as packed ARGB uint (0xAARRGGBB format).
        /// </summary>
        public uint Color { get; set; }

        /// <summary>
        /// Overlay alpha for Fill display mode (0 = transparent, 255 = opaque).
        /// </summary>
        public byte Alpha { get; set; }

        /// <summary>
        /// Whether this isodose level is rendered on the dose overlay.
        /// </summary>
        public bool IsVisible { get; set; }

        public IsodoseLevelData() { }

        public IsodoseLevelData(double fraction, double absoluteDoseGy, uint color, byte alpha, bool isVisible)
        {
            Fraction = fraction;
            AbsoluteDoseGy = absoluteDoseGy;
            Color = color;
            Alpha = alpha;
            IsVisible = isVisible;
        }
    }
}
