using System.Collections.Generic;

namespace EQD2Viewer.Core.Data
{
    /// <summary>
    /// Optional rendering/display parameters captured at export time.
    /// 
    /// When a snapshot is exported from Eclipse, these settings record the exact
    /// display state so that the application's rendering can be verified to produce
    /// visually identical results.
    /// 
    /// All fields are optional -- older snapshots without these settings still load
    /// and render normally using default parameters.
    /// </summary>
    public class RenderSettings
    {
        /// <summary>CT window level (HU) at time of export. Default: 40 (soft tissue).</summary>
        public double WindowLevel { get; set; } = 40;

        /// <summary>CT window width (HU) at time of export. Default: 400 (soft tissue).</summary>
        public double WindowWidth { get; set; } = 400;

        /// <summary>
        /// Isodose levels active at export time.
        /// Each entry: { "fraction": 0.95, "absoluteGy": 0, "color": 0xFFFFFF00, "visible": true }
        /// Null if isodose settings were not captured.
        /// </summary>
        public List<IsodoseLevelSetting> IsodoseLevels { get; set; } = new List<IsodoseLevelSetting>();

        /// <summary>
        /// Reference dose points with Eclipse-computed dose values.
        /// Used for numerical verification: does the app compute the same Gy at each point?
        /// </summary>
        public List<ReferenceDosePoint> ReferenceDosePoints { get; set; } = new List<ReferenceDosePoint>();
    }

    /// <summary>
    /// A single isodose level setting captured from Eclipse.
    /// </summary>
    public class IsodoseLevelSetting
    {
        public double Fraction { get; set; }
        public double AbsoluteDoseGy { get; set; }
        public uint Color { get; set; }
        public bool IsVisible { get; set; } = true;
    }

    /// <summary>
    /// A reference point with Eclipse-computed dose, used for end-to-end verification.
    /// The app should compute the same Gy value (within tolerance) at this CT pixel location.
    /// </summary>
    public class ReferenceDosePoint
    {
        public int CtPixelX { get; set; }
        public int CtPixelY { get; set; }
        public int CtSlice { get; set; }
        public double ExpectedDoseGy { get; set; }
        public bool IsInsideDoseGrid { get; set; }
    }
}
