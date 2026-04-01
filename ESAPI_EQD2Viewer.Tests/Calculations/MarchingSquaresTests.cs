using Xunit;
using FluentAssertions;
using EQD2Viewer.Core.Calculations;
using System.Linq;
using Point = System.Windows.Point;  // ← tämä ratkaisee ambiguity-ongelman

namespace ESAPI_EQD2Viewer.Tests.Calculations
{
    /// <summary>
    /// Tests for the marching squares algorithm used for isodose contour generation.
    /// Incorrect contours could mislead clinical dose coverage assessment.
    /// </summary>
    public class MarchingSquaresTests
    {
        // ════════════════════════════════════════════════════════
        // BASIC CASES
        // ════════════════════════════════════════════════════════

        [Fact]
        public void GenerateContours_UniformFieldAboveThreshold_ShouldReturnEmpty()
        {
            // All values above threshold → no contour
            int w = 4, h = 4;
            double[] field = Enumerable.Repeat(10.0, w * h).ToArray();
            var contours = MarchingSquares.GenerateContours(field, w, h, 5.0);
            contours.Should().BeEmpty("uniform field above threshold has no boundary");
        }

        [Fact]
        public void GenerateContours_UniformFieldBelowThreshold_ShouldReturnEmpty()
        {
            int w = 4, h = 4;
            double[] field = Enumerable.Repeat(1.0, w * h).ToArray();
            var contours = MarchingSquares.GenerateContours(field, w, h, 5.0);
            contours.Should().BeEmpty("uniform field below threshold has no boundary");
        }

        [Fact]
        public void GenerateContours_SimpleStep_ShouldProduceContour()
        {
            // Left half = 0, right half = 10
            // Threshold at 5 should produce a vertical contour
            int w = 6, h = 4;
            double[] field = new double[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    field[y * w + x] = x < 3 ? 0.0 : 10.0;

            var contours = MarchingSquares.GenerateContours(field, w, h, 5.0);
            contours.Should().NotBeEmpty("step function should produce a contour");
        }

        [Fact]
        public void GenerateContours_CircularDoseDistribution_ShouldProduceClosedContour()
        {
            // Simulate a dose blob: Gaussian-like peak in center
            int w = 20, h = 20;
            double cx = 10, cy = 10;
            double[] field = new double[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    double dx = x - cx, dy = y - cy;
                    field[y * w + x] = 100.0 * System.Math.Exp(-(dx * dx + dy * dy) / 25.0);
                }

            // Should have contours at various levels
            var contours50 = MarchingSquares.GenerateContours(field, w, h, 50.0);
            var contours10 = MarchingSquares.GenerateContours(field, w, h, 10.0);

            contours50.Should().NotBeEmpty("50% contour of Gaussian should exist");
            contours10.Should().NotBeEmpty("10% contour of Gaussian should exist");

            // Lower threshold should give larger contour
            var totalPoints50 = contours50.Sum(c => c.Count);
            var totalPoints10 = contours10.Sum(c => c.Count);
            totalPoints10.Should().BeGreaterThan(totalPoints50,
                "lower threshold contour should be larger");
        }

        // ════════════════════════════════════════════════════════
        // CONTOUR TOPOLOGY
        // ════════════════════════════════════════════════════════

        [Fact]
        public void GenerateContours_ContourPoints_ShouldBeNearThreshold()
        {
            // All contour points should lie near the threshold value
            int w = 20, h = 20;
            double[] field = new double[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    field[y * w + x] = x * 5.0; // Linear gradient 0-95

            double threshold = 50.0;
            var contours = MarchingSquares.GenerateContours(field, w, h, threshold);
            contours.Should().NotBeEmpty();

            foreach (var chain in contours)
                foreach (var pt in chain)
                {
                    // Contour x-coordinate should be near where dose = threshold
                    // In our linear gradient: dose = x * 5, so x ≈ 10
                    pt.X.Should().BeInRange(9.0, 11.0,
                        "contour should be near x=10 where dose=50");
                }
        }

        // ════════════════════════════════════════════════════════
        // EDGE CASES
        // ════════════════════════════════════════════════════════

        [Fact]
        public void GenerateContours_NullField_ShouldReturnEmpty()
        {
            var contours = MarchingSquares.GenerateContours(null, 4, 4, 5.0);
            contours.Should().BeEmpty();
        }

        [Fact]
        public void GenerateContours_EmptyField_ShouldReturnEmpty()
        {
            var contours = MarchingSquares.GenerateContours(new double[0], 4, 4, 5.0);
            contours.Should().BeEmpty();
        }

        [Fact]
        public void GenerateContours_TooSmallGrid_ShouldReturnEmpty()
        {
            var contours = MarchingSquares.GenerateContours(new double[1], 1, 1, 5.0);
            contours.Should().BeEmpty("grid must be at least 2x2");
        }

        [Fact]
        public void GenerateContours_MinimumGrid2x2_ShouldWork()
        {
            double[] field = { 0, 10, 0, 10 }; // step at x=0.5
            var contours = MarchingSquares.GenerateContours(field, 2, 2, 5.0);
            // Should produce at least one contour segment
            contours.Should().NotBeEmpty();
        }

        [Fact]
        public void GenerateContours_AllValuesExactlyAtThreshold_ShouldReturnEmpty()
        {
            // Ambiguous case: all values == threshold
            int w = 4, h = 4;
            double[] field = Enumerable.Repeat(5.0, w * h).ToArray();
            var contours = MarchingSquares.GenerateContours(field, w, h, 5.0);
            // All points are >= threshold, so case = 15 → no segments
            contours.Should().BeEmpty();
        }

        // ════════════════════════════════════════════════════════
        // AMBIGUOUS CASES (saddle points)
        // ════════════════════════════════════════════════════════

        [Fact]
        public void GenerateContours_SaddlePoint_ShouldHandleAmbiguity()
        {
            // Classic saddle point: diagonal corners high, others low
            // Should not crash and should produce some contour
            double[] field = { 10, 0, 0, 10 }; // 2×2: TL=10, TR=0, BL=0, BR=10
            var contours = MarchingSquares.GenerateContours(field, 2, 2, 5.0);
            contours.Should().NotBeEmpty("saddle point should produce contours");
        }

        // ════════════════════════════════════════════════════════
        // PERFORMANCE SMOKE TEST
        // ════════════════════════════════════════════════════════

        [Fact]
        public void GenerateContours_LargeGrid_ShouldCompleteInReasonableTime()
        {
            // Typical CT resolution: 512×512
            int w = 512, h = 512;
            double cx = 256, cy = 256;
            double[] field = new double[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    double dx = x - cx, dy = y - cy;
                    field[y * w + x] = 50.0 * System.Math.Exp(-(dx * dx + dy * dy) / 5000.0);
                }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var contours = MarchingSquares.GenerateContours(field, w, h, 25.0);
            sw.Stop();

            contours.Should().NotBeEmpty();
            sw.ElapsedMilliseconds.Should().BeLessThan(2000,
                "512×512 contour generation should complete in < 2 seconds");
        }
    }
}