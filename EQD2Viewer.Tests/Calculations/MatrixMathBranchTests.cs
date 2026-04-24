using EQD2Viewer.Core.Calculations;
using FluentAssertions;
using System;

namespace EQD2Viewer.Tests.Calculations
{
    /// <summary>
    /// Branch coverage tests for Invert4x4: the dispatch between the fast InvertRigid
    /// (orthogonal rotation shortcut) and the InvertGaussJordan fallback drives numeric
    /// accuracy and performance. Both paths must give mathematically consistent results.
    /// </summary>
    public class MatrixMathBranchTests
    {
        private const double Tol = 1e-9;

        private static double[,] Identity()
        {
            var m = new double[4, 4];
            for (int i = 0; i < 4; i++) m[i, i] = 1.0;
            return m;
        }

        /// <summary>Rotation about Z by angle θ, followed by translation (tx, ty, tz).</summary>
        private static double[,] RigidTransform(double theta, double tx, double ty, double tz)
        {
            double c = Math.Cos(theta), s = Math.Sin(theta);
            return new double[4, 4]
            {
                { c, -s, 0, tx },
                { s,  c, 0, ty },
                { 0,  0, 1, tz },
                { 0,  0, 0, 1  }
            };
        }

        /// <summary>Multiply 4x4 matrices for round-trip verification.</summary>
        private static double[,] Multiply(double[,] a, double[,] b)
        {
            var r = new double[4, 4];
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                {
                    double sum = 0;
                    for (int k = 0; k < 4; k++) sum += a[i, k] * b[k, j];
                    r[i, j] = sum;
                }
            return r;
        }

        private static void AssertIsIdentity(double[,] m)
        {
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                    m[i, j].Should().BeApproximately(i == j ? 1.0 : 0.0, Tol,
                        $"expected identity at [{i},{j}]");
        }

        [Fact]
        public void Invert4x4_PureRotationZ_TakesRigidFastPath_RoundTripIsIdentity()
        {
            // 45° about Z — orthogonal, so InvertRigid fires (R^T shortcut).
            var M = RigidTransform(Math.PI / 4, 0, 0, 0);
            var Mi = MatrixMath.Invert4x4(M);
            Mi.Should().NotBeNull();
            AssertIsIdentity(Multiply(M, Mi!));
        }

        [Fact]
        public void Invert4x4_RotationPlusTranslation_TakesRigidFastPath_RoundTripIsIdentity()
        {
            // Rigid transform with both rotation and translation — most common in clinical
            // FOR-to-FOR registrations. Fast path should handle it exactly via -R^T·t.
            var M = RigidTransform(Math.PI / 6, 12.3, -4.5, 99.0);
            var Mi = MatrixMath.Invert4x4(M);
            Mi.Should().NotBeNull();
            AssertIsIdentity(Multiply(M, Mi!));
            AssertIsIdentity(Multiply(Mi!, M));  // Inverse must also be left-inverse
        }

        [Fact]
        public void Invert4x4_NonOrthogonalScaleShear_FallsBackToGaussJordan()
        {
            // Scaling: diag(2, 3, 4) — NOT orthogonal (columns not unit length).
            // Forces the InvertGaussJordan fallback branch.
            var M = new double[4, 4]
            {
                { 2, 0, 0, 5 },
                { 0, 3, 0, 6 },
                { 0, 0, 4, 7 },
                { 0, 0, 0, 1 }
            };
            var Mi = MatrixMath.Invert4x4(M);
            Mi.Should().NotBeNull();
            // Inverse should contain 1/2, 1/3, 1/4 on diagonal
            Mi![0, 0].Should().BeApproximately(0.5, Tol);
            Mi[1, 1].Should().BeApproximately(1.0 / 3.0, Tol);
            Mi[2, 2].Should().BeApproximately(0.25, Tol);
            AssertIsIdentity(Multiply(M, Mi!));
        }

        [Fact]
        public void Invert4x4_ShearMatrix_FallsBackToGaussJordan()
        {
            // Shear: x' = x + 0.5y. Columns are NOT orthogonal; fast path rejects it.
            var M = new double[4, 4]
            {
                { 1, 0.5, 0, 0 },
                { 0, 1,   0, 0 },
                { 0, 0,   1, 0 },
                { 0, 0,   0, 1 }
            };
            var Mi = MatrixMath.Invert4x4(M);
            Mi.Should().NotBeNull();
            AssertIsIdentity(Multiply(M, Mi!));
        }

        [Fact]
        public void Invert4x4_SlightlyNonOrthogonal_UsesGaussJordan()
        {
            // Rotation matrix with tiny numerical perturbation — just outside the 1e-6
            // orthogonality tolerance. Fast path should reject, fallback should handle.
            var M = new double[4, 4]
            {
                { 1.0 + 1e-4, 0, 0, 0 },
                { 0,          1, 0, 0 },
                { 0,          0, 1, 0 },
                { 0,          0, 0, 1 }
            };
            var Mi = MatrixMath.Invert4x4(M);
            Mi.Should().NotBeNull();
            AssertIsIdentity(Multiply(M, Mi!));
        }

        [Fact]
        public void TransformPoint_RigidRoundTrip_RestoresOriginal()
        {
            var M = RigidTransform(Math.PI / 3, 100, -50, 25);
            var Mi = MatrixMath.Invert4x4(M)!;

            // Transform a point forward, then back — must match original within tolerance.
            MatrixMath.TransformPoint(M, 10, 20, 30, out double rx, out double ry, out double rz);
            MatrixMath.TransformPoint(Mi, rx, ry, rz, out double bx, out double by, out double bz);
            bx.Should().BeApproximately(10, Tol);
            by.Should().BeApproximately(20, Tol);
            bz.Should().BeApproximately(30, Tol);
        }

        [Fact]
        public void Invert4x4_NullInput_ReturnsNull()
        {
            MatrixMath.Invert4x4(null).Should().BeNull();
        }
    }
}
