using Xunit;
using FluentAssertions;
using EQD2Viewer.Core.Calculations;

namespace ESAPI_EQD2Viewer.Tests.Calculations
{
    /// <summary>
    /// Tests for 4x4 matrix operations used in spatial registration.
    /// Registration errors directly cause dose misalignment —
    /// this is safety-critical for re-irradiation summation.
    /// </summary>
    public class MatrixMathTests
    {
        private const double Tolerance = 1e-9;

        // ════════════════════════════════════════════════════════
        // IDENTITY MATRIX
        // ════════════════════════════════════════════════════════

        [Fact]
        public void Invert4x4_IdentityMatrix_ShouldReturnIdentity()
        {
            var I = IdentityMatrix();
            var inv = MatrixMath.Invert4x4(I);

            inv.Should().NotBeNull();
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 4; c++)
                    inv[r, c].Should().BeApproximately(r == c ? 1.0 : 0.0, Tolerance,
                        $"Identity inverse should be identity at [{r},{c}]");
        }

        // ════════════════════════════════════════════════════════
        // PURE TRANSLATION
        // ════════════════════════════════════════════════════════

        [Theory]
        [InlineData(10, 20, 30)]
        [InlineData(-50, 0, 100)]
        [InlineData(0.5, -0.5, 0)]
        public void Invert4x4_PureTranslation_ShouldNegateTranslation(
            double tx, double ty, double tz)
        {
            var M = IdentityMatrix();
            M[0, 3] = tx; M[1, 3] = ty; M[2, 3] = tz;

            var inv = MatrixMath.Invert4x4(M);

            inv.Should().NotBeNull();
            inv[0, 3].Should().BeApproximately(-tx, Tolerance);
            inv[1, 3].Should().BeApproximately(-ty, Tolerance);
            inv[2, 3].Should().BeApproximately(-tz, Tolerance);
        }

        // ════════════════════════════════════════════════════════
        // ROTATION MATRICES (orthogonal — should use fast path)
        // ════════════════════════════════════════════════════════

        [Fact]
        public void Invert4x4_90DegRotationZ_ShouldInvertCorrectly()
        {
            // 90° rotation around Z: [0,-1,0; 1,0,0; 0,0,1]
            var M = IdentityMatrix();
            M[0, 0] = 0; M[0, 1] = -1;
            M[1, 0] = 1; M[1, 1] = 0;

            var inv = MatrixMath.Invert4x4(M);
            inv.Should().NotBeNull();

            // M * M^(-1) should = I
            var product = Multiply4x4(M, inv);
            AssertIsIdentity(product, 1e-9);
        }

        [Fact]
        public void Invert4x4_ArbitraryRotationWithTranslation_MTimesInverse_ShouldBeIdentity()
        {
            // 45° rotation around Z with translation
            double cos45 = Math.Cos(Math.PI / 4);
            double sin45 = Math.Sin(Math.PI / 4);
            var M = IdentityMatrix();
            M[0, 0] = cos45; M[0, 1] = -sin45; M[0, 3] = 100;
            M[1, 0] = sin45; M[1, 1] = cos45; M[1, 3] = -50;
            M[2, 3] = 25;

            var inv = MatrixMath.Invert4x4(M);
            inv.Should().NotBeNull();

            var product = Multiply4x4(M, inv);
            AssertIsIdentity(product, 1e-9);
        }

        // ════════════════════════════════════════════════════════
        // NON-ORTHOGONAL (scaling/shear — should use Gauss-Jordan)
        // ════════════════════════════════════════════════════════

        [Fact]
        public void Invert4x4_ScalingMatrix_ShouldInvertCorrectly()
        {
            var M = IdentityMatrix();
            M[0, 0] = 2.0; M[1, 1] = 3.0; M[2, 2] = 0.5;

            var inv = MatrixMath.Invert4x4(M);
            inv.Should().NotBeNull();

            inv[0, 0].Should().BeApproximately(0.5, Tolerance);
            inv[1, 1].Should().BeApproximately(1.0 / 3.0, Tolerance);
            inv[2, 2].Should().BeApproximately(2.0, Tolerance);

            var product = Multiply4x4(M, inv);
            AssertIsIdentity(product, 1e-9);
        }

        [Fact]
        public void Invert4x4_AffineWithShear_ShouldInvertCorrectly()
        {
            var M = IdentityMatrix();
            M[0, 0] = 1.0; M[0, 1] = 0.5; M[0, 3] = 10;
            M[1, 0] = 0.0; M[1, 1] = 2.0; M[1, 3] = 20;
            M[2, 2] = 1.5; M[2, 3] = -5;

            var inv = MatrixMath.Invert4x4(M);
            inv.Should().NotBeNull();

            var product = Multiply4x4(M, inv);
            AssertIsIdentity(product, 1e-8);
        }

        // ════════════════════════════════════════════════════════
        // SINGULAR MATRIX
        // ════════════════════════════════════════════════════════

        [Fact]
        public void Invert4x4_SingularMatrix_ShouldReturnNull()
        {
            // All-zero matrix is singular
            var M = new double[4, 4]; // all zeros
            var inv = MatrixMath.Invert4x4(M);
            inv.Should().BeNull("singular matrix has no inverse");
        }

        [Fact]
        public void Invert4x4_MatrixWithDuplicateRows_ShouldReturnNull()
        {
            var M = IdentityMatrix();
            // Make row 0 = row 1
            for (int c = 0; c < 4; c++) M[0, c] = M[1, c];
            var inv = MatrixMath.Invert4x4(M);
            inv.Should().BeNull("matrix with duplicate rows is singular");
        }

        // ════════════════════════════════════════════════════════
        // NULL INPUT
        // ════════════════════════════════════════════════════════

        [Fact]
        public void Invert4x4_NullInput_ShouldReturnNull()
        {
            var inv = MatrixMath.Invert4x4(null);
            inv.Should().BeNull();
        }

        // ════════════════════════════════════════════════════════
        // TRANSFORM POINT
        // ════════════════════════════════════════════════════════

        [Fact]
        public void TransformPoint_Identity_ShouldReturnSamePoint()
        {
            var I = IdentityMatrix();
            MatrixMath.TransformPoint(I, 10, 20, 30, out double rx, out double ry, out double rz);
            rx.Should().BeApproximately(10, Tolerance);
            ry.Should().BeApproximately(20, Tolerance);
            rz.Should().BeApproximately(30, Tolerance);
        }

        [Fact]
        public void TransformPoint_Translation_ShouldAddOffset()
        {
            var M = IdentityMatrix();
            M[0, 3] = 5; M[1, 3] = -10; M[2, 3] = 15;
            MatrixMath.TransformPoint(M, 1, 2, 3, out double rx, out double ry, out double rz);
            rx.Should().BeApproximately(6, Tolerance);
            ry.Should().BeApproximately(-8, Tolerance);
            rz.Should().BeApproximately(18, Tolerance);
        }

        [Fact]
        public void TransformPoint_ThenInverseTransform_ShouldReturnOriginal()
        {
            // Round-trip test: transform → inverse transform → original point
            double cos30 = Math.Cos(Math.PI / 6);
            double sin30 = Math.Sin(Math.PI / 6);
            var M = IdentityMatrix();
            M[0, 0] = cos30; M[0, 1] = -sin30; M[0, 3] = 50;
            M[1, 0] = sin30; M[1, 1] = cos30; M[1, 3] = -30;
            M[2, 3] = 10;

            var inv = MatrixMath.Invert4x4(M);
            inv.Should().NotBeNull();

            double ox = 100, oy = 200, oz = 300;
            MatrixMath.TransformPoint(M, ox, oy, oz, out double tx, out double ty, out double tz);
            MatrixMath.TransformPoint(inv, tx, ty, tz, out double rx, out double ry, out double rz);

            rx.Should().BeApproximately(ox, 1e-8, "round-trip should preserve X");
            ry.Should().BeApproximately(oy, 1e-8, "round-trip should preserve Y");
            rz.Should().BeApproximately(oz, 1e-8, "round-trip should preserve Z");
        }

        // ════════════════════════════════════════════════════════
        // CLINICAL REGISTRATION SCENARIO
        // ════════════════════════════════════════════════════════

        [Fact]
        public void TransformPoint_TypicalClinicalRegistration_ShouldBeReversible()
        {
            // Simulate a typical CT-CT registration:
            // Small rotation (2°) + translation (5mm, 3mm, -2mm)
            double angle = 2.0 * Math.PI / 180.0;
            double cos = Math.Cos(angle), sin = Math.Sin(angle);
            var M = IdentityMatrix();
            M[0, 0] = cos; M[0, 2] = sin; M[0, 3] = 5.0;
            M[2, 0] = -sin; M[2, 2] = cos; M[2, 3] = -2.0;
            M[1, 3] = 3.0;

            var inv = MatrixMath.Invert4x4(M);
            inv.Should().NotBeNull();

            // Test multiple anatomical positions
            var testPoints = new[] {
                (0.0, 0.0, 0.0),       // isocenter
                (100.0, 0.0, 0.0),     // lateral
                (0.0, 150.0, 0.0),     // AP
                (0.0, 0.0, 200.0),     // SI
                (-50.0, 100.0, -100.0) // oblique
            };

            foreach (var (px, py, pz) in testPoints)
            {
                MatrixMath.TransformPoint(M, px, py, pz, out double tx, out double ty, out double tz);
                MatrixMath.TransformPoint(inv, tx, ty, tz, out double rx, out double ry, out double rz);
                rx.Should().BeApproximately(px, 1e-6, $"X round-trip for ({px},{py},{pz})");
                ry.Should().BeApproximately(py, 1e-6, $"Y round-trip for ({px},{py},{pz})");
                rz.Should().BeApproximately(pz, 1e-6, $"Z round-trip for ({px},{py},{pz})");
            }
        }

        // ════════════════════════════════════════════════════════
        // HELPERS
        // ════════════════════════════════════════════════════════

        private static double[,] IdentityMatrix()
        {
            var M = new double[4, 4];
            M[0, 0] = 1; M[1, 1] = 1; M[2, 2] = 1; M[3, 3] = 1;
            return M;
        }

        private static double[,] Multiply4x4(double[,] A, double[,] B)
        {
            var C = new double[4, 4];
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 4; c++)
                    for (int k = 0; k < 4; k++)
                        C[r, c] += A[r, k] * B[k, c];
            return C;
        }

        private static void AssertIsIdentity(double[,] M, double tolerance)
        {
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 4; c++)
                    M[r, c].Should().BeApproximately(r == c ? 1.0 : 0.0, tolerance,
                        $"Product should be identity at [{r},{c}], was {M[r, c]}");
        }
    }
}