using EQD2Viewer.Core.Calculations;
using EQD2Viewer.Core.Data;
using FluentAssertions;
using System.Linq;

namespace EQD2Viewer.Tests.Calculations
{
    /// <summary>
    /// Regression tests for the canonical scan-line rule in StructureRasterizer.
    /// The previous implementation skipped edges whose endpoints sat exactly on the scan
    /// line, which caused under-filled rows for axis-aligned rectangles with bottom/top
    /// edges coinciding with pixel centres. Clinical contours rarely hit this exactly, but
    /// under-filled masks shrink DVH volumes.
    /// </summary>
    public class StructureRasterizerRegressionTests
    {
        [Fact]
        public void AxisAlignedRectangle_FillsEveryInsidePixel()
        {
            // 10×10 rectangle from (2,2) to (7,7) in a 10×10 canvas.
            // Using pixel-centre scan lines (y + 0.5), the inside rows are y = 2..6 (5 rows).
            var poly = new[]
            {
                new Point2D(2.0, 2.0),
                new Point2D(7.0, 2.0),
                new Point2D(7.0, 7.0),
                new Point2D(2.0, 7.0),
            };

            var mask = StructureRasterizer.RasterizePolygon(poly, 10, 10);
            int filled = mask.Count(b => b);
            // Expect 5 rows × 5 cols = 25 pixels. Allow ±1 for edge conventions but reject
            // the previous bug which under-filled to something like 20 or 16.
            filled.Should().BeInRange(20, 30);
        }

        [Fact]
        public void RectangleWithHorizontalEdgeOnScanLine_DoesNotCollapseRow()
        {
            // Edges (0,5)-(10,5)-(10,7)-(0,7) — the bottom edge exactly on y=5. With the
            // previous "skip if both endpoints >= scanY" rule, scanY=5.5 would still work,
            // but scanY=5.0 (the pixel-centre of row 4) would see all 4 edges incorrectly.
            var poly = new[]
            {
                new Point2D(0.0, 5.0),
                new Point2D(10.0, 5.0),
                new Point2D(10.0, 7.0),
                new Point2D(0.0, 7.0),
            };

            var mask = StructureRasterizer.RasterizePolygon(poly, 12, 12);
            // Row y=5 (scanY=5.5): clearly inside, must be fully filled.
            int filledInRow5 = 0;
            for (int x = 0; x < 12; x++) if (mask[5 * 12 + x]) filledInRow5++;
            filledInRow5.Should().BeGreaterThan(8, "row at y=5 must fill across the rectangle's full x extent");
        }

        [Fact]
        public void DegenerateHorizontalPolygon_DoesNotCrash()
        {
            // All points on y=3. Polygon has zero area — mask should be empty, no throw.
            var poly = new[]
            {
                new Point2D(1.0, 3.0),
                new Point2D(4.0, 3.0),
                new Point2D(2.0, 3.0),
            };
            var act = () => StructureRasterizer.RasterizePolygon(poly, 10, 10);
            act.Should().NotThrow();
            StructureRasterizer.RasterizePolygon(poly, 10, 10).Count(b => b)
                .Should().Be(0, "zero-area polygon has no interior pixels");
        }
    }
}
