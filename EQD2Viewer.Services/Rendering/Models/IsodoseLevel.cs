using EQD2Viewer.Core.Data;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace EQD2Viewer.Services.Rendering
{
    /// <summary>
    /// Represents a single isodose level with dual-mode threshold support, color, and visibility.
    /// 
    /// In <see cref="IsodoseMode.Relative"/>: threshold = <see cref="Fraction"/> � referenceDose.
    /// In <see cref="IsodoseMode.Absolute"/>: threshold = <see cref="AbsoluteDoseGy"/> directly.
    /// 
    /// Both modes share the same color, visibility, and alpha settings.
    /// Implements <see cref="INotifyPropertyChanged"/> for WPF data binding in the isodose DataGrid.
    /// 
    /// This class lives in the Services layer because of the <see cref="MediaColor"/> property
    /// which depends on System.Windows.Media.Color. The core data (Fraction, Color as uint,
    /// Alpha, IsVisible) is all WPF-free.
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
        /// Threshold as fraction of reference dose (e.g., 1.10 = 110%, 0.50 = 50%).
        /// Used in <see cref="IsodoseMode.Relative"/> mode.
        /// Range: typically 0.05�1.20. Values above 1.0 represent hot spots.
        /// </summary>
        public double Fraction
        {
            get => _fraction;
            set { _fraction = value; OnPropertyChanged(); OnPropertyChanged(nameof(MediaColor)); }
        }

        /// <summary>
        /// Threshold as absolute dose in Gy.
        /// Used in <see cref="IsodoseMode.Absolute"/> mode (EQD2 summation re-irradiation assessment).
        /// Range: typically 5�80 Gy for clinical tolerances.
        /// </summary>
        public double AbsoluteDoseGy
        {
            get => _absoluteDoseGy;
            set { _absoluteDoseGy = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Display label shown in the isodose table (e.g., "110%", "45.0 Gy", "50%").
        /// Updated by the ViewModel when isodose mode, display unit, or reference dose changes.
        /// </summary>
        public string Label
        {
            get => _label;
            set { _label = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Isodose line/fill color as packed ARGB uint (0xAARRGGBB format).
        /// The alpha channel in <see cref="Color"/> is typically 0xFF (opaque);
        /// actual overlay transparency comes from the separate <see cref="Alpha"/> property.
        /// </summary>
        public uint Color
        {
            get => _color;
            set { _color = value; OnPropertyChanged(); OnPropertyChanged(nameof(MediaColor)); }
        }

        /// <summary>
        /// Overlay alpha for Fill display mode (0 = transparent, 255 = opaque).
        /// Line mode always renders at full opacity regardless of this value.
        /// Default: 140 (~55% opacity) for subtle fill overlay.
        /// </summary>
        public byte Alpha
        {
            get => _alpha;
            set { _alpha = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Whether this isodose level is rendered on the dose overlay.
        /// Toggled via checkbox in the isodose level DataGrid.
        /// </summary>
        public bool IsVisible
        {
            get => _isVisible;
            set { _isVisible = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// WPF-bindable <see cref="System.Windows.Media.Color"/> for UI display
        /// (color swatch in DataGrid, color picker). Always fully opaque (A=255).
        /// </summary>
        public Color MediaColor => System.Windows.Media.Color.FromArgb(
 255,
            (byte)((_color >> 16) & 0xFF),
   (byte)((_color >> 8) & 0xFF),
            (byte)(_color & 0xFF));

        /// <summary>
        /// Creates an isodose level for relative (percentage) mode.
        /// </summary>
        public IsodoseLevel(double fraction, string label, uint color, byte alpha = 140)
        {
            _fraction = fraction;
            _absoluteDoseGy = 0;
            _label = label;
            _color = color;
            _alpha = alpha;
        }

        /// <summary>
        /// Creates an isodose level for absolute (Gy) mode.
        /// </summary>
        public IsodoseLevel(double fraction, double absoluteGy, string label, uint color, byte alpha = 140)
        {
            _fraction = fraction;
            _absoluteDoseGy = absoluteGy;
            _label = label;
            _color = color;
            _alpha = alpha;
        }

        /// <summary>
        /// Creates an isodose level from a Core data transfer object.
        /// Bridges the pure data layer with the WPF-bindable layer.
        /// </summary>
        public IsodoseLevel(IsodoseLevelData data)
        {
            _fraction = data.Fraction;
            _absoluteDoseGy = data.AbsoluteDoseGy;
            _label = "";
            _color = data.Color;
            _alpha = data.Alpha;
            _isVisible = data.IsVisible;
        }

        /// <summary>
        /// Converts to a Core data transfer object (no WPF dependencies).
        /// </summary>
        public IsodoseLevelData ToData()
        {
            return new IsodoseLevelData(_fraction, _absoluteDoseGy, _color, _alpha, _isVisible);
        }

        // =================================================================
        // RELATIVE MODE PRESETS (single-plan viewing)
        // =================================================================

        public static IsodoseLevel[] GetEclipseDefaults()
        {
            return new[]
            {
         new IsodoseLevel(1.10, "110%", 0xFFFF0000, 160),
        new IsodoseLevel(1.05, "105%", 0xFFFF4400, 150),
              new IsodoseLevel(1.00, "100%", 0xFFFF8800, 140),
       new IsodoseLevel(0.95, "95%",  0xFFFFFF00, 130),
          new IsodoseLevel(0.90, "90%",  0xFF00FF00, 120),
     new IsodoseLevel(0.80, "80%",  0xFF00FFFF, 110),
                new IsodoseLevel(0.70, "70%",  0xFF0088FF, 100),
       new IsodoseLevel(0.50, "50%",  0xFF0000FF, 90),
   new IsodoseLevel(0.30, "30%",  0xFF8800FF, 80),
        new IsodoseLevel(0.10, "10%",  0xFFFF00FF, 70),
       };
        }

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
        // ABSOLUTE MODE PRESETS (EQD2 summation / re-irradiation)
        // =================================================================

        public static IsodoseLevel[] GetReIrradiationPreset()
        {
            return new[]
                  {
          new IsodoseLevel(0, 60, "60 Gy", 0xFFFF0000, 160),
       new IsodoseLevel(0, 50, "50 Gy", 0xFFFF4400, 150),
  new IsodoseLevel(0, 45, "45 Gy", 0xFFFF8800, 140),
      new IsodoseLevel(0, 40, "40 Gy", 0xFFFFFF00, 130),
    new IsodoseLevel(0, 35, "35 Gy", 0xFF88FF00, 120),
    new IsodoseLevel(0, 30, "30 Gy", 0xFF00FF00, 110),
         new IsodoseLevel(0, 20, "20 Gy", 0xFF00BBFF, 100),
    new IsodoseLevel(0, 10, "10 Gy", 0xFF0000FF, 80),
    };
        }

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
        // COLOR PALETTE
        // =================================================================

        public static uint[] ColorPalette => new uint[]
              {
0xFFFF0000, 0xFFFF4400, 0xFFFF8800, 0xFFFFBB00,
    0xFFFFFF00, 0xFF88FF00, 0xFF00FF00, 0xFF00FF88,
            0xFF00FFFF, 0xFF00BBFF, 0xFF0088FF, 0xFF0000FF,
    0xFF4400FF, 0xFF8800FF, 0xFFFF00FF, 0xFFFF0088,
         };

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

    }
}
