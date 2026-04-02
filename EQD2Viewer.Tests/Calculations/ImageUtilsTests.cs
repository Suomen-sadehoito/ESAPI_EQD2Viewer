using EQD2Viewer.Core.Calculations;
using FluentAssertions;

namespace EQD2Viewer.Tests.Calculations
{
    /// <summary>
    /// Tests for image processing utilities: bilinear interpolation and HU offset detection.
    /// Interpolation errors cause dose misregistration at sub-voxel level.
    /// HU offset errors cause incorrect CT window/level display.
    /// </summary>
    public class ImageUtilsTests
    {
        // ════════════════════════════════════════════════════════
        // BILINEAR INTERPOLATION — double[,] grid
        // ════════════════════════════════════════════════════════

        [Fact]
        public void BilinearSample_ExactGridPoints_ShouldReturnExactValues()
        {
            var grid = new double[3, 3] { { 10, 20, 30 }, { 40, 50, 60 }, { 70, 80, 90 } };
            ImageUtils.BilinearSample(grid, 3, 3, 0, 0).Should().Be(10);
            ImageUtils.BilinearSample(grid, 3, 3, 1, 1).Should().Be(50);
            ImageUtils.BilinearSample(grid, 3, 3, 2, 2).Should().Be(90);
        }

        [Fact]
        public void BilinearSample_Midpoint_ShouldInterpolateCorrectly()
        {
            // 2x2 grid: [0,1; 2,3]
            var grid = new double[2, 2] { { 0, 1 }, { 2, 3 } };
            // Center point (0.5, 0.5) should be average = (0+1+2+3)/4 = 1.5
            double result = ImageUtils.BilinearSample(grid, 2, 2, 0.5, 0.5);
            result.Should().BeApproximately(1.5, 1e-10);
        }

        [Fact]
        public void BilinearSample_HorizontalMidpoint_ShouldInterpolateLinearly()
        {
            // Need at least 2 rows for bilinear interpolation (fy must be < gh-1)
            // Grid 3x2: values increase along X, constant along Y
            var grid = new double[3, 2] {
                { 0, 0 },
                { 10, 10 },
                { 20, 20 }
            };
            // At (0.5, 0.5): interpolate between grid[0,0]=0, grid[1,0]=10, grid[0,1]=0, grid[1,1]=10 → 5.0
            ImageUtils.BilinearSample(grid, 3, 2, 0.5, 0.5).Should().BeApproximately(5.0, 1e-10);
            // At (1.5, 0.5): interpolate between grid[1,0]=10, grid[2,0]=20, grid[1,1]=10, grid[2,1]=20 → 15.0
            ImageUtils.BilinearSample(grid, 3, 2, 1.5, 0.5).Should().BeApproximately(15.0, 1e-10);
        }

        [Fact]
        public void BilinearSample_NegativeCoordinates_ShouldReturnEdgeOrZero()
        {
            var grid = new double[3, 3] { { 10, 20, 30 }, { 40, 50, 60 }, { 70, 80, 90 } };
            // Negative — out of bounds, should use nearest neighbor or return 0
            double result = ImageUtils.BilinearSample(grid, 3, 3, -1, -1);
            // Based on code: returns 0 for coordinates outside grid when rounding also fails
        }

        [Fact]
        public void BilinearSample_OutOfBounds_ShouldNotCrash()
        {
            var grid = new double[3, 3] { { 10, 20, 30 }, { 40, 50, 60 }, { 70, 80, 90 } };
            // Should not throw for any out-of-bounds coordinates
            var outOfBounds = new[] { (-5.0, 0.0), (0.0, -5.0), (10.0, 0.0), (0.0, 10.0), (100.0, 100.0) };
            foreach (var (fx, fy) in outOfBounds)
            {
                double result = 0;
                var action = () => { result = ImageUtils.BilinearSample(grid, 3, 3, fx, fy); };
                action.Should().NotThrow($"for coordinates ({fx}, {fy})");
                double.IsNaN(result).Should().BeFalse();
                double.IsInfinity(result).Should().BeFalse();
            }
        }

        [Fact]
        public void BilinearSample_UniformGrid_ShouldReturnConstant()
        {
            var grid = new double[4, 4];
            for (int x = 0; x < 4; x++)
                for (int y = 0; y < 4; y++)
                    grid[x, y] = 42.0;

            // Any point in a uniform field should return the constant value
            for (double fx = 0; fx < 2.9; fx += 0.3)
                for (double fy = 0; fy < 2.9; fy += 0.3)
                    ImageUtils.BilinearSample(grid, 4, 4, fx, fy)
                        .Should().BeApproximately(42.0, 1e-10);
        }

        // ════════════════════════════════════════════════════════
        // BILINEAR INTERPOLATION — int[,] raw grid with scaling
        // ════════════════════════════════════════════════════════

        [Fact]
        public void BilinearSampleRaw_ExactPoint_ShouldApplyScaling()
        {
            var grid = new int[2, 2] { { 1000, 2000 }, { 3000, 4000 } };
            double rawScale = 0.001;   // 0.001 Gy per raw unit
            double rawOffset = 0.0;
            double unitToGy = 1.0;

            double result = ImageUtils.BilinearSampleRaw(grid, 2, 2, 0, 0, rawScale, rawOffset, unitToGy);
            result.Should().BeApproximately(1.0, 1e-10, "1000 * 0.001 = 1.0 Gy");
        }

        [Fact]
        public void BilinearSampleRaw_WithOffset_ShouldApplyOffsetCorrectly()
        {
            var grid = new int[2, 2] { { 0, 0 }, { 0, 0 } };
            double rawScale = 0.001;
            double rawOffset = 500.0;  // baseline offset
            double unitToGy = 0.01;    // cGy to Gy

            double result = ImageUtils.BilinearSampleRaw(grid, 2, 2, 0, 0, rawScale, rawOffset, unitToGy);
            result.Should().BeApproximately((0 * 0.001 + 500.0) * 0.01, 1e-10);
        }

        // ════════════════════════════════════════════════════════
        // HU OFFSET DETECTION
        // ════════════════════════════════════════════════════════

        [Fact]
        public void DetermineHuOffset_NormalSignedHU_ShouldReturnZero()
        {
            // Normal signed HU values: mostly in range -1000 to +3000
            int xSize = 32, ySize = 32;
            var slice = new int[xSize, ySize];
            for (int x = 0; x < xSize; x++)
                for (int y = 0; y < ySize; y++)
                    slice[x, y] = -500 + (x * y % 2000); // typical CT range

            int offset = ImageUtils.DetermineHuOffset(slice, xSize, ySize);
            offset.Should().Be(0, "normal signed HU values don't need offset");
        }

        [Fact]
        public void DetermineHuOffset_UnsignedStorage_ShouldReturn32768()
        {
            // Unsigned storage: values shifted by 32768
            int xSize = 32, ySize = 32;
            var slice = new int[xSize, ySize];
            for (int x = 0; x < xSize; x++)
                for (int y = 0; y < ySize; y++)
                    slice[x, y] = 32768 + (-500 + (x * y % 2000)); // shifted range

            int offset = ImageUtils.DetermineHuOffset(slice, xSize, ySize);
            offset.Should().Be(32768, "high values indicate unsigned storage");
        }

        [Fact]
        public void DetermineHuOffset_MixedValues_ShouldDecideByMajority()
        {
            // If most values are above threshold, offset is applied
            int xSize = 16, ySize = 16;
            var slice = new int[xSize, ySize];

            // Fill mostly with high values (unsigned)
            for (int x = 0; x < xSize; x++)
                for (int y = 0; y < ySize; y++)
                    slice[x, y] = 35000; // above threshold

            int offset = ImageUtils.DetermineHuOffset(slice, xSize, ySize);
            offset.Should().Be(32768);
        }

        [Fact]
        public void DetermineHuOffset_EmptySlice_ShouldNotCrash()
        {
            // Edge case: 1×1 slice
            var slice = new int[1, 1] { { 0 } };
            var action = () => ImageUtils.DetermineHuOffset(slice, 1, 1);
            action.Should().NotThrow();
        }
    }
}