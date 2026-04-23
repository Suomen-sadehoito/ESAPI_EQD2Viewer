using EQD2Viewer.Core.Data;
using FluentAssertions;
using System;

namespace EQD2Viewer.Tests.Registration
{
    /// <summary>
    /// Sanity tests for the DeformationField data structure.
    /// </summary>
    public class DeformationFieldTests
    {
        [Fact]
        public void DefaultVec3_IsZeroDisplacement()
        {
            var v = default(Vec3);
            v.X.Should().Be(0);
            v.Y.Should().Be(0);
            v.Z.Should().Be(0);
        }

        [Fact]
        public void DeformationField_CanBeAllocated()
        {
            const int xSize = 4, ySize = 4, zSize = 2;
            var vectors = new Vec3[zSize][,];
            for (int z = 0; z < zSize; z++)
                vectors[z] = new Vec3[xSize, ySize];

            var dvf = new DeformationField
            {
                XSize = xSize, YSize = ySize, ZSize = zSize,
                XRes = 2.0, YRes = 2.0, ZRes = 3.0,
                Origin = new Vec3(-10, -10, 0),
                Vectors = vectors,
                SourceFOR = "FOR_A",
                TargetFOR = "FOR_B"
            };

            dvf.XSize.Should().Be(xSize);
            dvf.ZSize.Should().Be(zSize);
            dvf.SourceFOR.Should().Be("FOR_A");
            dvf.Vectors[0][0, 0].X.Should().Be(0);
        }

        [Fact]
        public void DeformationField_VectorAssignment_Roundtrips()
        {
            var vectors = new Vec3[1][,];
            vectors[0] = new Vec3[3, 3];
            vectors[0][1, 2] = new Vec3(1.5, -2.5, 0.1);

            var dvf = new DeformationField
            {
                XSize = 3, YSize = 3, ZSize = 1,
                XRes = 1, YRes = 1, ZRes = 1,
                Origin = new Vec3(0, 0, 0),
                Vectors = vectors
            };

            dvf.Vectors[0][1, 2].X.Should().BeApproximately(1.5, 1e-10);
            dvf.Vectors[0][1, 2].Y.Should().BeApproximately(-2.5, 1e-10);
            dvf.Vectors[0][1, 2].Z.Should().BeApproximately(0.1, 1e-10);
        }
    }
}
