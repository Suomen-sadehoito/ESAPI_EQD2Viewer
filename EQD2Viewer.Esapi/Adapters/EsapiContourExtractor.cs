using System.Collections.Generic;
using VMS.TPS.Common.Model.API;

namespace EQD2Viewer.Esapi.Adapters
{
    /// <summary>
    /// Walks an ESAPI <see cref="Structure"/> slice-by-slice and packs its
    /// contour polygons into the domain-side <c>ContoursBySlice</c> map.
    ///
    /// Both <see cref="EsapiDataSource"/> and <see cref="EsapiSummationDataLoader"/>
    /// formerly carried byte-identical copies of this loop. The per-slice
    /// <c>catch { }</c> is intentionally silent because most slices legitimately
    /// throw — a structure does not intersect every slice in the image.
    /// </summary>
    internal static class EsapiContourExtractor
    {
        /// <summary>
        /// Returns a slice-index → contour-polygons map for <paramref name="structure"/>.
        /// Slices on which <see cref="Structure.GetContoursOnImagePlane(int)"/>
        /// throws (the common case for slices the structure does not intersect)
        /// are silently skipped. Empty slices and contours with fewer than 3
        /// points are also dropped.
        /// </summary>
        public static Dictionary<int, List<double[][]>> ExtractContoursBySlice(
            Structure structure, int imageZSize)
        {
            var bySlice = new Dictionary<int, List<double[][]>>();

            for (int z = 0; z < imageZSize; z++)
            {
                try
                {
                    var contours = structure.GetContoursOnImagePlane(z);
                    if (contours == null || contours.Length == 0) continue;

                    var sliceContours = new List<double[][]>();
                    foreach (var contour in contours)
                    {
                        if (contour.Length < 3) continue;
                        var points = new double[contour.Length][];
                        for (int i = 0; i < contour.Length; i++)
                            points[i] = new double[] { contour[i].x, contour[i].y, contour[i].z };
                        sliceContours.Add(points);
                    }

                    if (sliceContours.Count > 0)
                        bySlice[z] = sliceContours;
                }
                catch
                {
                    // Intentionally ignored: structures legitimately lack contours on
                    // most slices (the structure simply does not intersect the plane).
                    // Logging this would be one warning per slice per structure — pure noise.
                }
            }

            return bySlice;
        }
    }
}
