using EQD2Viewer.Core.Data;
using itk.simple;
using System;

namespace EQD2Viewer.Registration.ITK.Converters
{
    /// <summary>
    /// Converts between EQD2Viewer domain objects (VolumeData, DeformationField)
    /// and SimpleITK Image objects.
    /// </summary>
    internal static class ItkImageConverter
    {
        internal static Image VolumeToImage(VolumeData vol)
        {
            var size = new VectorUInt32(new uint[] { (uint)vol.XSize, (uint)vol.YSize, (uint)vol.ZSize });
            var img = new Image(size, PixelIDValueEnum.sitkInt16);

            img.SetSpacing(new VectorDouble(new double[] { vol.XRes, vol.YRes, vol.ZRes }));
            img.SetOrigin(new VectorDouble(new double[] { vol.Origin.X, vol.Origin.Y, vol.Origin.Z }));
            img.SetDirection(new VectorDouble(new double[]
            {
                vol.XDirection.X, vol.XDirection.Y, vol.XDirection.Z,
                vol.YDirection.X, vol.YDirection.Y, vol.YDirection.Z,
                vol.ZDirection.X, vol.ZDirection.Y, vol.ZDirection.Z
            }));

            for (int z = 0; z < vol.ZSize; z++)
                for (int y = 0; y < vol.YSize; y++)
                    for (int x = 0; x < vol.XSize; x++)
                        img.SetPixelAsInt16(new VectorUInt32(new uint[] { (uint)x, (uint)y, (uint)z }),
                            (short)(vol.Voxels[z][x, y] - vol.HuOffset));

            return img;
        }

        internal static DeformationField DisplacementImageToField(Image dvfImage, VolumeData referenceVol)
        {
            var sz = dvfImage.GetSize();
            int xSize = (int)sz[0], ySize = (int)sz[1], zSize = (int)sz[2];
            var sp = dvfImage.GetSpacing();
            var orig = dvfImage.GetOrigin();
            var dir = dvfImage.GetDirection();

            var vectors = new Vec3[zSize][,];
            for (int z = 0; z < zSize; z++)
            {
                vectors[z] = new Vec3[xSize, ySize];
                for (int y = 0; y < ySize; y++)
                    for (int x = 0; x < xSize; x++)
                    {
                        var idx = new VectorUInt32(new uint[] { (uint)x, (uint)y, (uint)z });
                        var v = dvfImage.GetPixelAsVectorFloat64(idx);
                        vectors[z][x, y] = new Vec3(v[0], v[1], v[2]);
                    }
            }

            return new DeformationField
            {
                XSize = xSize, YSize = ySize, ZSize = zSize,
                XRes = sp[0], YRes = sp[1], ZRes = sp[2],
                Origin = new Vec3(orig[0], orig[1], orig[2]),
                XDirection = new Vec3(dir[0], dir[1], dir[2]),
                YDirection = new Vec3(dir[3], dir[4], dir[5]),
                ZDirection = new Vec3(dir[6], dir[7], dir[8]),
                SourceFOR = referenceVol.FOR,
                Vectors = vectors
            };
        }
    }
}
