using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace ESAPI_EQD2Viewer.Core.Models
{
    /// <summary>
    /// How isodose thresholds are interpreted.
    /// </summary>
    public enum IsodoseMode
    {
        /// <summary>
        /// Thresholds as percentage of a reference dose (Eclipse-style).
        /// Used for single-plan viewing.
        /// </summary>
        Relative,

        /// <summary>
        /// Thresholds as absolute Gy values (no reference dose needed).
        /// Used for EQD2 summation where clinical tolerances are in Gy.
        /// </summary>
        Absolute
    }

    /// <summary>
    /// Display unit for isodose level thresholds (within Relative mode).
    /// </summary>
    public enum IsodoseUnit
    {
        Percent,
        Gy
    }

    /// <summary>
    /// Single isodose level with dual-mode threshold support.
    /// 
    /// In Relative mode: threshold = Fraction × referenceDose.
    /// In Absolute mode: threshold = AbsoluteDoseGy directly.
    /// 
    /// Both modes share the same color, visibility, and alpha settings.
    /// </summary>
    public class IsodoseLevel : INotifyPropertyChanged
    {
        private double _fraction;
        private double _absoluteDoseGy;
        private string _label;
        private uint _color;
        private byte _alpha;
        private bool _isVisible = true;

        /// <summary>
        /// Threshold as fraction of reference dose (e.g. 1.10 = 110%).
        /// Used in Relative mode.
        /// </summary>
        public double Fraction
        {
            get => _fraction;
            set { _fraction = value; OnPropertyChanged(); OnPropertyChanged(nameof(MediaColor)); }
        }

        /// <summary>
        /// Threshold as absolute dose in Gy.
        /// Used in Absolute mode (EQD2 summation).
        /// </summary>
        public double AbsoluteDoseGy
        {
            get => _absoluteDoseGy;
            set { _absoluteDoseGy = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Display label (e.g. "110%", "45.0 Gy", "50%").
        /// Updated by ViewModel when mode or reference changes.
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
        /// WPF-bindable color for UI display (DataGrid color swatch, color picker).
        /// </summary>
        public Color MediaColor => System.Windows.Media.Color.FromArgb(
            255,
            (byte)((_color >> 16) & 0xFF),
            (byte)((_color >> 8) & 0xFF),
            (byte)(_color & 0xFF));

        public IsodoseLevel(double fraction, string label, uint color, byte alpha = 140)
        {
            _fraction = fraction;
            _absoluteDoseGy = 0;
            _label = label;
            _color = color;
            _alpha = alpha;
        }

        /// <summary>
        /// Constructor for absolute mode levels.
        /// </summary>
        public IsodoseLevel(double fraction, double absoluteGy, string label, uint color, byte alpha = 140)
        {
            _fraction = fraction;
            _absoluteDoseGy = absoluteGy;
            _label = label;
            _color = color;
            _alpha = alpha;
        }

        // =================================================================
        // RELATIVE MODE PRESETS (single-plan viewing)
        // =================================================================

        /// <summary>
        /// Eclipse-like 10-level defaults. Max = 110%.
        /// </summary>
        public static IsodoseLevel[] GetEclipseDefaults()
        {
            return new[]
            {
                new IsodoseLevel(1.10, "110%", 0xFFFF0000, 160),   // Red (hot spot)
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

        // =================================================================
        // ABSOLUTE MODE PRESETS (EQD2 summation)
        // =================================================================

        /// <summary>
        /// Re-irradiation assessment: OAR tolerance thresholds in EQD2 Gy.
        /// Covers typical spinal cord (45 Gy), brainstem (50 Gy) tolerances.
        /// </summary>
        public static IsodoseLevel[] GetReIrradiationPreset()
        {
            return new[]
            {
                new IsodoseLevel(0, 60, "60 Gy", 0xFFFF0000, 160),   // Red — critical overdose
                new IsodoseLevel(0, 50, "50 Gy", 0xFFFF4400, 150),   // Orange-red — brainstem
                new IsodoseLevel(0, 45, "45 Gy", 0xFFFF8800, 140),   // Orange — spinal cord
                new IsodoseLevel(0, 40, "40 Gy", 0xFFFFFF00, 130),   // Yellow
                new IsodoseLevel(0, 35, "35 Gy", 0xFF88FF00, 120),   // Yellow-green
                new IsodoseLevel(0, 30, "30 Gy", 0xFF00FF00, 110),   // Green
                new IsodoseLevel(0, 20, "20 Gy", 0xFF00BBFF, 100),   // Light blue
                new IsodoseLevel(0, 10, "10 Gy", 0xFF0000FF, 80),    // Blue — low dose spread
            };
        }

        /// <summary>
        /// Stereotactic re-irradiation: higher dose range.
        /// </summary>
        public static IsodoseLevel[] GetStereotacticPreset()
        {
            return new[]
            {
                new IsodoseLevel(0, 80, "80 Gy", 0xFFFF0000, 160),
                new IsodoseLevel(0, 60, "60 Gy", 0xFFFF4400, 150),
                new IsodoseLevel(0, 50, "50 Gy", 0xFFFF8800, 140),
                new IsodoseLevel(0, 40, "40 Gy", 0xFFFFFF00, 130),
                new IsodoseLevel(0, 30, "30 Gy", 0xFF00FF00, 110),
                new IsodoseLevel(0, 20, "20 Gy", 0xFF00BBFF, 100),
                new IsodoseLevel(0, 12, "12 Gy", 0xFF0000FF, 80),
            };
        }

        /// <summary>
        /// Palliative re-irradiation: lower dose range.
        /// </summary>
        public static IsodoseLevel[] GetPalliativePreset()
        {
            return new[]
            {
                new IsodoseLevel(0, 45, "45 Gy", 0xFFFF0000, 160),
                new IsodoseLevel(0, 40, "40 Gy", 0xFFFF4400, 150),
                new IsodoseLevel(0, 35, "35 Gy", 0xFFFF8800, 140),
                new IsodoseLevel(0, 30, "30 Gy", 0xFFFFFF00, 130),
                new IsodoseLevel(0, 25, "25 Gy", 0xFF00FF00, 120),
                new IsodoseLevel(0, 20, "20 Gy", 0xFF00FFFF, 110),
                new IsodoseLevel(0, 10, "10 Gy", 0xFF0088FF, 90),
                new IsodoseLevel(0, 5,  "5 Gy",  0xFF0000FF, 70),
            };
        }

        // =================================================================
        // COLOR PALETTE for picker
        // =================================================================

        /// <summary>
        /// Predefined color palette for the color picker.
        /// 16 distinct colors commonly used in isodose displays.
        /// </summary>
        public static uint[] ColorPalette => new uint[]
        {
            0xFFFF0000, // Red
            0xFFFF4400, // Orange-red
            0xFFFF8800, // Orange
            0xFFFFBB00, // Gold
            0xFFFFFF00, // Yellow
            0xFF88FF00, // Yellow-green
            0xFF00FF00, // Green
            0xFF00FF88, // Spring green
            0xFF00FFFF, // Cyan
            0xFF00BBFF, // Light blue
            0xFF0088FF, // Sky blue
            0xFF0000FF, // Blue
            0xFF4400FF, // Indigo
            0xFF8800FF, // Purple
            0xFFFF00FF, // Magenta
            0xFFFF0088, // Hot pink
        };

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}