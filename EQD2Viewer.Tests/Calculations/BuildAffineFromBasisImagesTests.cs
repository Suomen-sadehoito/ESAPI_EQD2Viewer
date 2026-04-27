using EQD2Viewer.Core.Calculations;
using EQD2Viewer.Core.Data;
using FluentAssertions;

namespace EQD2Viewer.Tests.Calculations
{
    /// <summary>
    /// Pins the row-major 16-element affine layout produced by
    /// <see cref="MatrixMath.BuildAffineFromBasisImages"/>.
    ///
    /// The helper exists to centralise the basis-vector → affine-matrix packing
    /// shared by three ESAPI-side call sites (EsapiDataSource, EsapiSummationDataLoader,
    /// FixtureGenerator/FixtureExporter). All three previously emitted byte-identical
    /// math; these tests pin the convention so a future change to the helper cannot
    /// drift any of them.
    ///
    /// Layout (row-major, where column N of the upper-left 3x3 is the transform's
    /// image of basis vector N minus the image of the origin, and column 3 is the
    /// image of the origin = translation):
    ///
    ///   [  xImg.x-o.x   yImg.x-o.x   zImg.x-o.x   o.x  ]
    ///   [  xImg.y-o.y   yImg.y-o.y   zImg.y-o.y   o.y  ]
    ///   [  xImg.z-o.z   yImg.z-o.z   zImg.z-o.z   o.z  ]
    ///   [           0            0            0     1  ]
    /// </summary>
    public class BuildAffineFromBasisImagesTests
    {
        private const double Tol = 1e-12;

        [Fact]
        public void Identity_ProducesIdentityMatrix()
        {
            // T sends (0,0,0)→(0,0,0) and unit basis vectors to themselves.
            double[] m = MatrixMath.BuildAffineFromBasisImages(
                origin: new Vec3(0, 0, 0),
                xImage: new Vec3(1, 0, 0),
                yImage: new Vec3(0, 1, 0),
                zImage: new Vec3(0, 0, 1));

            m.Should().HaveCount(16);
            m.Should().Equal(new double[]
            {
                1, 0, 0, 0,
                0, 1, 0, 0,
                0, 0, 1, 0,
                0, 0, 0, 1
            });
        }

        [Fact]
        public void PureTranslation_PutsTranslationInColumn3()
        {
            // T sends (0,0,0)→(5,7,11). The basis images are origin + each unit vector.
            double[] m = MatrixMath.BuildAffineFromBasisImages(
                origin: new Vec3(5, 7, 11),
                xImage: new Vec3(6, 7, 11),
                yImage: new Vec3(5, 8, 11),
                zImage: new Vec3(5, 7, 12));

            // 3x3 part: identity. Column 3: translation.
            m[0].Should().BeApproximately(1, Tol);
            m[1].Should().BeApproximately(0, Tol);
            m[2].Should().BeApproximately(0, Tol);
            m[3].Should().BeApproximately(5, Tol);

            m[4].Should().BeApproximately(0, Tol);
            m[5].Should().BeApproximately(1, Tol);
            m[6].Should().BeApproximately(0, Tol);
            m[7].Should().BeApproximately(7, Tol);

            m[8].Should().BeApproximately(0, Tol);
            m[9].Should().BeApproximately(0, Tol);
            m[10].Should().BeApproximately(1, Tol);
            m[11].Should().BeApproximately(11, Tol);

            // Bottom row [0, 0, 0, 1]
            m[12].Should().Be(0);
            m[13].Should().Be(0);
            m[14].Should().Be(0);
            m[15].Should().Be(1);
        }

        [Fact]
        public void NinetyDegreeRotationAroundZ_ProducesExpectedMatrix()
        {
            // R_z(+90°): (1,0,0)→(0,1,0), (0,1,0)→(-1,0,0), (0,0,1)→(0,0,1).
            double[] m = MatrixMath.BuildAffineFromBasisImages(
                origin: new Vec3(0, 0, 0),
                xImage: new Vec3(0, 1, 0),
                yImage: new Vec3(-1, 0, 0),
                zImage: new Vec3(0, 0, 1));

            m.Should().Equal(new double[]
            {
                 0, -1,  0,  0,
                 1,  0,  0,  0,
                 0,  0,  1,  0,
                 0,  0,  0,  1
            });
        }

        [Fact]
        public void TranslationPlusRotationPlusNonUnitScale_ProducesExpectedMatrix()
        {
            // T: scale by (2, 3, 4), then 90° Z-rotation, then translate by (10, 20, 30).
            //
            // Step-by-step computed images:
            //   origin (0,0,0) → translate → (10, 20, 30)
            //   xImg from (1,0,0) → scale → (2,0,0) → rot Z+90 → (0, 2, 0) → translate → (10, 22, 30)
            //   yImg from (0,1,0) → scale → (0,3,0) → rot Z+90 → (-3, 0, 0) → translate → (7, 20, 30)
            //   zImg from (0,0,1) → scale → (0,0,4) → rot Z+90 → (0, 0, 4) → translate → (10, 20, 34)
            double[] m = MatrixMath.BuildAffineFromBasisImages(
                origin: new Vec3(10, 20, 30),
                xImage: new Vec3(10, 22, 30),
                yImage: new Vec3(7, 20, 30),
                zImage: new Vec3(10, 20, 34));

            // Expected 3x3 columns: (0, 2, 0), (-3, 0, 0), (0, 0, 4).
            //   m[0,0] m[0,1] m[0,2] m[0,3] = 0   -3    0   10
            //   m[1,0] m[1,1] m[1,2] m[1,3] = 2    0    0   20
            //   m[2,0] m[2,1] m[2,2] m[2,3] = 0    0    4   30
            //   m[3,*] = 0 0 0 1
            m.Should().Equal(new double[]
            {
                 0, -3,  0, 10,
                 2,  0,  0, 20,
                 0,  0,  4, 30,
                 0,  0,  0,  1
            });
        }
    }
}
