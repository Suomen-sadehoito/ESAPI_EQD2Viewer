using System;

namespace EQD2Viewer.Core.Data
{
    /// <summary>
    /// A deformation vector field (DVF) produced by deformable image registration.
    /// Maps each voxel in the fixed image to a displacement vector in patient coordinates (mm).
    /// Defined on the same spatial grid as the fixed (reference) CT.
    /// </summary>
    public class DeformationField
    {
        public int XSize { get; set; }
        public int YSize { get; set; }
        public int ZSize { get; set; }
        public double XRes { get; set; }
        public double YRes { get; set; }
        public double ZRes { get; set; }
        public Vec3 Origin { get; set; }
        public Vec3 XDirection { get; set; }
        public Vec3 YDirection { get; set; }
        public Vec3 ZDirection { get; set; }

        public string SourceFOR { get; set; } = "";
        public string TargetFOR { get; set; } = "";

        /// <summary>
        /// Per-voxel displacement vectors in millimeters, indexed as [z][x, y].
        /// Adding Vectors[z][x,y] to the world-space position of voxel (x,y,z)
        /// gives the corresponding position in the moving image (forward mapping).
        /// </summary>
        public Vec3[][,] Vectors { get; set; } = null!;
    }
}
