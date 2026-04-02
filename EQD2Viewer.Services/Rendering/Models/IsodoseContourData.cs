using System.Windows.Media;

namespace EQD2Viewer.Services.Rendering
{
    public class IsodoseContourData
    {
        public StreamGeometry Geometry { get; set; } = null!;
        public SolidColorBrush Stroke { get; set; } = null!;
        public double StrokeThickness { get; set; } = 1.0;
    }
}
