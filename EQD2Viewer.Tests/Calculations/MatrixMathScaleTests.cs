using EQD2Viewer.Core.Calculations;
using FluentAssertions;

namespace EQD2Viewer.Tests.Calculations
{
    /// <summary>
    /// Regression guard for the scale-relative singularity tolerance in MatrixMath.
    /// A 1e-12 absolute pivot tolerance would incorrectly reject well-conditioned matrices
    /// whose entries are consistently small (e.g. if anyone expresses a registration in
    /// metres instead of millimetres). The tolerance should scale with the matrix norm.
    /// </summary>
    public class MatrixMathScaleTests
    {
        [Fact]
        public void Invert4x4_VerySmallScaleAffine_InvertsSuccessfully()
        {
            // Rotation-free scaling by 1e-6 in all axes (m→µm hypothetical). Non-singular,
            // condition number 1, but all pivots are 1e-6.
            double s = 1e-6;
            var M = new double[4, 4]
            {
                { s, 0, 0, 0 },
                { 0, s, 0, 0 },
                { 0, 0, s, 0 },
                { 0, 0, 0, 1 }
            };
            var inv = MatrixMath.Invert4x4(M);
            inv.Should().NotBeNull("well-conditioned matrix must invert regardless of absolute magnitude");
            inv![0, 0].Should().BeApproximately(1 / s, 1e-3);
        }

        [Fact]
        public void Invert4x4_VeryLargeScaleAffine_InvertsSuccessfully()
        {
            // 1e9 scale — also well-conditioned, but the old absolute 1e-12 rule would pass;
            // guard against regressions in the other direction.
            double s = 1e9;
            var M = new double[4, 4]
            {
                { s, 0, 0, 0 },
                { 0, s, 0, 0 },
                { 0, 0, s, 0 },
                { 0, 0, 0, 1 }
            };
            var inv = MatrixMath.Invert4x4(M);
            inv.Should().NotBeNull();
            inv![0, 0].Should().BeApproximately(1 / s, 1e-20);
        }

        [Fact]
        public void Invert4x4_GenuinelySingular_ReturnsNull()
        {
            // Rank-deficient matrix (two identical columns). Must still be rejected.
            var M = new double[4, 4]
            {
                { 1, 1, 0, 0 },
                { 2, 2, 0, 0 },
                { 0, 0, 1, 0 },
                { 0, 0, 0, 1 }
            };
            MatrixMath.Invert4x4(M).Should().BeNull();
        }
    }
}
