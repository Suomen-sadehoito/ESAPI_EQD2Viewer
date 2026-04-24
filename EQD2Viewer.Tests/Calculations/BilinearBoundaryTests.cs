using EQD2Viewer.Core.Calculations;
using FluentAssertions;

namespace EQD2Viewer.Tests.Calculations
{
    /// <summary>
    /// Regression tests guarding the bilinear boundary fix.
    /// Prior behaviour: any fx &gt;= gw-1 silently fell through to a nearest-neighbour branch
    /// that returned 0 if the rounded index landed outside the grid (fx &gt; gw-0.5). This
    /// manifested as ~0.5 voxel of peripheral dose being dropped. New behaviour clamps
    /// values within half a voxel of the boundary to the nearest valid voxel.
    /// </summary>
    public class BilinearBoundaryTests
    {
        private static double[,] MakeGrid(int w, int h, double value)
        {
            var g = new double[w, h];
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    g[x, y] = value;
            return g;
        }

        [Theory]
        [InlineData(3.0)]     // exact last index
        [InlineData(3.2)]     // within 0.5 voxel of last index
        [InlineData(3.49)]    // just inside the 0.5-voxel tolerance band
        public void BilinearSample_NearFarEdge_ReturnsGridValue_NotZero(double fxNearEdge)
        {
            // 4×4 grid uniformly filled with 5.0. Previously fx = 3.x would fall to nearest
            // and if rounded to 4 would return 0 (peripheral underdose).
            var grid = MakeGrid(4, 4, 5.0);
            ImageUtils.BilinearSample(grid, 4, 4, fxNearEdge, 1.0)
                .Should().BeApproximately(5.0, 1e-9,
                    $"a uniform grid must return the uniform value even near the far edge (fx={fxNearEdge})");
        }

        [Theory]
        [InlineData(-0.49)]
        [InlineData(-0.2)]
        [InlineData(-0.01)]
        public void BilinearSample_JustBeforeZeroEdge_ReturnsGridValue(double fxJustBelowZero)
        {
            var grid = MakeGrid(4, 4, 7.0);
            ImageUtils.BilinearSample(grid, 4, 4, fxJustBelowZero, 1.0)
                .Should().BeApproximately(7.0, 1e-9,
                    $"peripheral dose within half a voxel of the near edge must be preserved (fx={fxJustBelowZero})");
        }

        [Theory]
        [InlineData(-1.0)]
        [InlineData(-10.0)]
        [InlineData(4.0)]
        [InlineData(10.0)]
        public void BilinearSample_FarOutside_ReturnsZero(double fxOutside)
        {
            var grid = MakeGrid(4, 4, 99.0);
            ImageUtils.BilinearSample(grid, 4, 4, fxOutside, 1.0)
                .Should().Be(0, "clearly-outside samples must return 0 (no data)");
        }

        [Fact]
        public void BilinearSample_DegenerateGrid_1Voxel_DoesNotCrash()
        {
            var grid = new double[1, 1] { { 42.0 } };
            var act = () => ImageUtils.BilinearSample(grid, 1, 1, 0, 0);
            act.Should().NotThrow();
            ImageUtils.BilinearSample(grid, 1, 1, 0, 0).Should().Be(42.0);
        }

        [Fact]
        public void BilinearSampleRaw_NearFarEdge_AppliesScalingCorrectly()
        {
            // Grid is indexed [x, y]. C# initialiser is row-major, so each inner { ... } block
            // fills grid[x, 0..3] for a fixed x. Put a "5" in the last X row to verify clamping.
            var grid = new int[4, 4]
            {
                { 1, 1, 1, 1 }, // x=0
                { 1, 1, 1, 1 }, // x=1
                { 1, 1, 1, 1 }, // x=2
                { 5, 5, 5, 5 }  // x=3 — the far edge
            };
            // Near the x=3 edge (fx=3.2), should return the last row's value * scale
            double result = ImageUtils.BilinearSampleRaw(grid, 4, 4, 3.2, 1.0,
                rawScale: 0.5, rawOffset: 0.0, unitToGy: 1.0);
            result.Should().BeApproximately(5 * 0.5, 1e-9);
        }
    }
}
