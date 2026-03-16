using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace ESAPI_EQD2Viewer.Core.Models
{
    /// <summary>
    /// Display unit for isodose level thresholds.
    /// </summary>
    public enum IsodoseUnit
    {
        /// <summary>
        /// Percentage of reference dose (Eclipse default).
        /// </summary>
        Percent,

        /// <summary>
        /// Absolute dose in Gy.
        /// </summary>
        Gy
    }

    /// <summary>
    /// Single isodose level definition with color, visibility, and threshold.
    /// Internally stores fraction of reference dose; can display as % or Gy.
    /// </summary>
    public class IsodoseLevel : INotifyPropertyChanged
    {
        private double _fraction;
        private string _label;
        private uint _color;
        private byte _alpha;
        private bool _isVisible = true;

        /// <summary>
        /// Threshold as fraction of reference dose (e.g. 1.07 = 107%).
        /// This is the canonical internal representation used by rendering.
        /// </summary>
        public double Fraction
        {
            get => _fraction;
            set { _fraction = value; OnPropertyChanged(); OnPropertyChanged(nameof(MediaColor)); }
        }

        /// <summary>
        /// Display label (e.g. "107%" or "53.5 Gy").
        /// </summary>
        public string Label
        {
            get => _label;
            set { _label = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// ARGB color as uint (0xAARRGGBB). Alpha in Color is typically 0xFF;
        /// actual overlay alpha comes from the Alpha property.
        /// </summary>
        public uint Color
        {
            get => _color;
            set { _color = value; OnPropertyChanged(); OnPropertyChanged(nameof(MediaColor)); }
        }

        /// <summary>
        /// Overlay alpha for fill mode (0-255). Line mode always uses 255.
        /// </summary>
        public byte Alpha
        {
            get => _alpha;
            set { _alpha = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Whether this level is drawn on the dose overlay.
        /// </summary>
        public bool IsVisible
        {
            get => _isVisible;
            set { _isVisible = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// WPF-bindable color for UI display (DataGrid color swatch).
        /// </summary>
        public Color MediaColor => System.Windows.Media.Color.FromArgb(
            255,
            (byte)((_color >> 16) & 0xFF),
            (byte)((_color >> 8) & 0xFF),
            (byte)(_color & 0xFF));

        public IsodoseLevel(double fraction, string label, uint color, byte alpha = 140)
        {
            _fraction = fraction;
            _label = label;
            _color = color;
            _alpha = alpha;
        }

        /// <summary>
        /// Eclipse-like 10-level defaults.
        /// </summary>
        public static IsodoseLevel[] GetEclipseDefaults()
        {
            return new[]
            {
                new IsodoseLevel(1.07, "107%", 0xFFFF0000, 160),   // Red (hot spot)
                new IsodoseLevel(1.05, "105%", 0xFFFF4400, 150),   // Orange-red
                new IsodoseLevel(1.00, "100%", 0xFFFF8800, 140),   // Orange
                new IsodoseLevel(0.95, "95%",  0xFFFFFF00, 130),   // Yellow
                new IsodoseLevel(0.90, "90%",  0xFF00FF00, 120),   // Green
                new IsodoseLevel(0.80, "80%",  0xFF00FFFF, 110),   // Cyan
                new IsodoseLevel(0.70, "70%",  0xFF0088FF, 100),   // Light blue
                new IsodoseLevel(0.50, "50%",  0xFF0000FF, 90),    // Blue
                new IsodoseLevel(0.30, "30%",  0xFF8800FF, 80),    // Purple
                new IsodoseLevel(0.10, "10%",  0xFFFF00FF, 70),    // Magenta
            };
        }

        /// <summary>
        /// Basic 4-level set.
        /// </summary>
        public static IsodoseLevel[] GetDefaults()
        {
            return new[]
            {
                new IsodoseLevel(1.05, "105%", 0xFFFF0000, 140),
                new IsodoseLevel(1.00, "100%", 0xFFFF8800, 130),
                new IsodoseLevel(0.95, "95%",  0xFFFFFF00, 120),
                new IsodoseLevel(0.50, "50%",  0xFF0000FF, 100),
            };
        }

        /// <summary>
        /// Minimal 3-level set.
        /// </summary>
        public static IsodoseLevel[] GetMinimalSet()
        {
            return new[]
            {
                new IsodoseLevel(1.05, "105%", 0xFFFF0000, 140),
                new IsodoseLevel(0.95, "95%",  0xFFFFFF00, 120),
                new IsodoseLevel(0.50, "50%",  0xFF0000FF, 100),
            };
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}