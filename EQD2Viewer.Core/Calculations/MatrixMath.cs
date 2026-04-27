using EQD2Viewer.Core.Data;
using System;

namespace EQD2Viewer.Core.Calculations
{
    /// <summary>
    /// 4x4 matrix operations for spatial registration transforms.
    /// Supports both rigid (R^T shortcut) and general affine (Gauss-Jordan) inversion.
    /// </summary>
    public static class MatrixMath
    {
        /// <summary>
        /// Packs an affine 4x4 transform from the images of the origin and the
        /// three unit basis vectors into a flat 16-element row-major array.
        ///
        /// Given a transform T, callers pass <paramref name="origin"/>=T(0,0,0),
        /// <paramref name="xImage"/>=T(1,0,0), <paramref name="yImage"/>=T(0,1,0),
        /// <paramref name="zImage"/>=T(0,0,1). The packed layout is:
        ///
        ///   [ xImg.x-o.x   yImg.x-o.x   zImg.x-o.x   o.x ]
        ///   [ xImg.y-o.y   yImg.y-o.y   zImg.y-o.y   o.y ]
        ///   [ xImg.z-o.z   yImg.z-o.z   zImg.z-o.z   o.z ]
        ///   [          0            0            0     1 ]
        ///
        /// — i.e. columns 0-2 hold the basis-image deltas, column 3 holds the
        /// translation, and the bottom row is the homogeneous [0 0 0 1].
        ///
        /// Centralises the math previously inlined at three sites that consume
        /// Varian ESAPI <c>VVector reg.TransformPoint(...)</c> calls. Each call
        /// site retains its own try/catch fallback policy; only the packing
        /// math is shared.
        /// </summary>
        public static double[] BuildAffineFromBasisImages(
            Vec3 origin, Vec3 xImage, Vec3 yImage, Vec3 zImage)
        {
            return new double[]
            {
                xImage.X - origin.X, yImage.X - origin.X, zImage.X - origin.X, origin.X,
                xImage.Y - origin.Y, yImage.Y - origin.Y, zImage.Y - origin.Y, origin.Y,
                xImage.Z - origin.Z, yImage.Z - origin.Z, zImage.Z - origin.Z, origin.Z,
                0,                   0,                   0,                   1
            };
        }

        /// <summary>
        /// Inverts a 4x4 affine transformation matrix.
        /// First tries the fast rigid shortcut (R^T, -R^T*t).
        /// If the rotation part is not orthogonal (affine/deformable), 
        /// falls back to general Gauss-Jordan elimination.
        /// </summary>
        public static double[,]? Invert4x4(double[,]? M)
        {
            if (M == null) return null;

            // Check if the 3x3 rotation part is orthogonal (R * R^T â‰ˆ I)
            if (IsOrthogonal3x3(M))
                return InvertRigid(M);

            return InvertGaussJordan(M);
        }

        /// <summary>
        /// Tests whether the upper-left 3x3 submatrix is orthogonal (columns are unit vectors).
        /// Tolerance accounts for floating-point imprecision in ESAPI registration data.
        /// </summary>
        private static bool IsOrthogonal3x3(double[,] M, double tolerance = 1e-6)
        {
            // Check that each column has unit length
            for (int col = 0; col < 3; col++)
            {
                double lenSq = 0;
                for (int row = 0; row < 3; row++)
                    lenSq += M[row, col] * M[row, col];

                if (Math.Abs(lenSq - 1.0) > tolerance)
                    return false;
            }

            // Check that columns are orthogonal (dot products â‰ˆ 0)
            for (int c1 = 0; c1 < 3; c1++)
            {
                for (int c2 = c1 + 1; c2 < 3; c2++)
                {
                    double dot = 0;
                    for (int row = 0; row < 3; row++)
                        dot += M[row, c1] * M[row, c2];

                    if (Math.Abs(dot) > tolerance)
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Fast inverse for rigid transforms: inv = [R^T | -R^T * t ; 0 0 0 1].
        /// Numerically exact for orthogonal rotation matrices.
        /// </summary>
        private static double[,] InvertRigid(double[,] M)
        {
            var inv = new double[4, 4];

            // Transpose the 3x3 rotation part
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                    inv[r, c] = M[c, r];

            // Translation: -R^T * t
            double tx = M[0, 3], ty = M[1, 3], tz = M[2, 3];
            inv[0, 3] = -(inv[0, 0] * tx + inv[0, 1] * ty + inv[0, 2] * tz);
            inv[1, 3] = -(inv[1, 0] * tx + inv[1, 1] * ty + inv[1, 2] * tz);
            inv[2, 3] = -(inv[2, 0] * tx + inv[2, 1] * ty + inv[2, 2] * tz);

            // Bottom row
            inv[3, 0] = 0; inv[3, 1] = 0; inv[3, 2] = 0; inv[3, 3] = 1;

            return inv;
        }

        /// <summary>
        /// General 4x4 matrix inversion using Gauss-Jordan elimination with partial pivoting.
        /// Works for any invertible matrix including affine transforms with scaling/shear.
        /// Returns null if matrix is singular (determinant â‰ˆ 0).
        /// </summary>
        private static double[,]? InvertGaussJordan(double[,] M)
        {
            // Augmented matrix [M | I]
            double[,] aug = new double[4, 8];
            for (int r = 0; r < 4; r++)
            {
                for (int c = 0; c < 4; c++)
                    aug[r, c] = M[r, c];
                aug[r, r + 4] = 1.0;
            }

            // Scale-relative singularity tolerance: 1e-12 absolute would mis-classify any
            // matrix with consistently small magnitudes (e.g. cm-scale transforms instead of mm).
            double matrixNorm = 0;
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 4; c++)
                {
                    double a = Math.Abs(M[r, c]);
                    if (a > matrixNorm) matrixNorm = a;
                }
            double singTol = Math.Max(1e-15, matrixNorm * 1e-12);

            // Forward elimination with partial pivoting
            for (int col = 0; col < 4; col++)
            {
                // Find pivot row
                int pivotRow = col;
                double maxVal = Math.Abs(aug[col, col]);
                for (int row = col + 1; row < 4; row++)
                {
                    double absVal = Math.Abs(aug[row, col]);
                    if (absVal > maxVal)
                    {
                        maxVal = absVal;
                        pivotRow = row;
                    }
                }

                // Check for singularity
                if (maxVal < singTol)
                    return null;

                // Swap rows if needed
                if (pivotRow != col)
                {
                    for (int c = 0; c < 8; c++)
                    {
                        double tmp = aug[col, c];
                        aug[col, c] = aug[pivotRow, c];
                        aug[pivotRow, c] = tmp;
                    }
                }

                // Scale pivot row
                double pivot = aug[col, col];
                for (int c = 0; c < 8; c++)
                    aug[col, c] /= pivot;

                // Eliminate column in all other rows
                for (int row = 0; row < 4; row++)
                {
                    if (row == col) continue;
                    double factor = aug[row, col];
                    for (int c = 0; c < 8; c++)
                        aug[row, c] -= factor * aug[col, c];
                }
            }

            // Extract inverse from right half
            var inv = new double[4, 4];
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 4; c++)
                    inv[r, c] = aug[r, c + 4];

            return inv;
        }

        /// <summary>
        /// Transforms a 3D point using a 4x4 affine matrix.
        /// </summary>
        public static void TransformPoint(double[,] M, double x, double y, double z,
            out double rx, out double ry, out double rz)
        {
            rx = M[0, 0] * x + M[0, 1] * y + M[0, 2] * z + M[0, 3];
            ry = M[1, 0] * x + M[1, 1] * y + M[1, 2] * z + M[1, 3];
            rz = M[2, 0] * x + M[2, 1] * y + M[2, 2] * z + M[2, 3];
        }
    }
}
