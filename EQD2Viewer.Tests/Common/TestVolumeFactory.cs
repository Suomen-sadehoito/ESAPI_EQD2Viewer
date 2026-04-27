using EQD2Viewer.Core.Data;
using EQD2Viewer.Core.Interfaces;

namespace EQD2Viewer.Tests.Common
{
    /// <summary>
    /// Shared builders for the tiny reference / dose volumes used by unit tests.
    /// All volumes use identity orientation, 1mm spacing, and origin at (0,0,0)
    /// so that voxel index and world-mm coordinates coincide. Test cases that
    /// need other geometries should construct their own.
    /// </summary>
    internal static class TestVolumeFactory
    {
        /// <summary>
        /// Builds a CT <see cref="VolumeData"/> with all-zero voxels, identity
        /// orientation, 1mm spacing, and the supplied frame-of-reference id.
        /// </summary>
        public static VolumeData MakeCt(int xSize, int ySize, int zSize, string frameOfReference = "FOR_REF")
        {
            var vox = new int[zSize][,];
            for (int z = 0; z < zSize; z++) vox[z] = new int[xSize, ySize];
            return new VolumeData
            {
                Geometry = new VolumeGeometry
                {
                    XSize = xSize, YSize = ySize, ZSize = zSize,
                    XRes = 1.0, YRes = 1.0, ZRes = 1.0,
                    Origin = new Vec3(0, 0, 0),
                    XDirection = new Vec3(1, 0, 0),
                    YDirection = new Vec3(0, 1, 0),
                    ZDirection = new Vec3(0, 0, 1),
                    FrameOfReference = frameOfReference
                },
                Voxels = vox,
                HuOffset = 0
            };
        }

        /// <summary>
        /// Returns an int[zSize][xSize, ySize] grid filled with <paramref name="value"/>.
        /// </summary>
        public static int[][,] FillDose(int xSize, int ySize, int zSize, int value)
        {
            var data = new int[zSize][,];
            for (int z = 0; z < zSize; z++)
            {
                data[z] = new int[xSize, ySize];
                for (int y = 0; y < ySize; y++)
                    for (int x = 0; x < xSize; x++)
                        data[z][x, y] = value;
            }
            return data;
        }

        /// <summary>
        /// Builds a dose <see cref="DoseVolumeData"/> with all voxels set to
        /// <paramref name="value"/>, identity orientation, RawScale=1, no offset.
        /// </summary>
        public static DoseVolumeData MakeDoseVolume(int xSize, int ySize, int zSize, double value)
        {
            return new DoseVolumeData
            {
                Geometry = new VolumeGeometry
                {
                    XSize = xSize, YSize = ySize, ZSize = zSize,
                    XRes = 1, YRes = 1, ZRes = 1,
                    Origin = new Vec3(0, 0, 0),
                    XDirection = new Vec3(1, 0, 0),
                    YDirection = new Vec3(0, 1, 0),
                    ZDirection = new Vec3(0, 0, 1),
                },
                Voxels = FillDose(xSize, ySize, zSize, (int)value),
                Scaling = new DoseScaling { RawScale = 1.0, RawOffset = 0, UnitToGy = 1.0, DoseUnit = "Gy" }
            };
        }

        /// <summary>
        /// Builds a <see cref="SummationPlanDoseData"/> co-located with the standard
        /// test reference grid: identity orientation, origin (0,0,0), 1mm spacing,
        /// RawScale=1, no offset, unitToGy=1.
        /// </summary>
        public static SummationPlanDoseData MakeSummationDoseData(int[][,] doseVoxels, int xSize, int ySize, int zSize)
            => new SummationPlanDoseData
            {
                DoseVoxels = doseVoxels,
                DoseGeometry = new VolumeGeometry
                {
                    XSize = xSize, YSize = ySize, ZSize = zSize,
                    XRes = 1.0, YRes = 1.0, ZRes = 1.0,
                    Origin = new Vec3(0, 0, 0),
                    XDirection = new Vec3(1, 0, 0),
                    YDirection = new Vec3(0, 1, 0),
                    ZDirection = new Vec3(0, 0, 1)
                },
                Scaling = new DoseScaling { RawScale = 1.0, RawOffset = 0, UnitToGy = 1.0, DoseUnit = "Gy" }
            };
    }
}
