using EQD2Viewer.Core.Models;
using System;

namespace EQD2Viewer.Core.Calculations
{
    /// <summary>
    /// Pure-function DVH binning utilities. The cumulative-DVH histogram
    /// convention is shared by every consumer:
    ///
    ///   numBins   = <see cref="DomainConstants.DvhHistogramBins"/>
    ///   binWidth  = maxDoseGy * 1.1 / numBins   (10% headroom above the peak)
    ///   bin index = floor(dose / binWidth), clamped to numBins - 1
    ///
    /// Voxels with non-positive dose are not added to the histogram, but
    /// they DO count toward <c>totalVoxels</c> — so a structure full of
    /// zero-dose voxels reports 100% volume at the 0 Gy bin and decays
    /// only when dose-bearing voxels appear in higher bins.
    /// </summary>
    public static class DVHCalculator
    {
        /// <summary>
        /// Cumulative-DVH histogram for a structure. Walks every (slice, voxel)
        /// pair where <paramref name="structureMasks"/>[z][i] is true, accumulates
        /// the corresponding dose from <paramref name="doseSlices"/>[z][i] into
        /// the histogram, and returns one <see cref="DoseVolumePoint"/> per bin.
        ///
        /// Returns an empty array when:
        ///   * either input is null;
        ///   * <paramref name="maxDoseGy"/> ≤ 0;
        ///   * no voxel inside the structure mask was found.
        /// </summary>
        public static DoseVolumePoint[] BinToHistogram(
            double[][] doseSlices, bool[][] structureMasks, double maxDoseGy)
        {
            if (doseSlices == null || structureMasks == null || maxDoseGy <= 0)
                return Array.Empty<DoseVolumePoint>();

            int numBins = DomainConstants.DvhHistogramBins;
            double binWidth = maxDoseGy * 1.1 / numBins;
            long[] histogram = new long[numBins];
            long totalVoxels = 0;
            int sliceCount = Math.Min(doseSlices.Length, structureMasks.Length);

            for (int z = 0; z < sliceCount; z++)
            {
                double[] doseSlice = doseSlices[z];
                bool[] mask = structureMasks[z];
                if (doseSlice == null || mask == null) continue;

                int len = Math.Min(doseSlice.Length, mask.Length);
                for (int i = 0; i < len; i++)
                {
                    if (!mask[i]) continue;
                    totalVoxels++;
                    if (doseSlice[i] <= 0) continue;
                    int bin = (int)(doseSlice[i] / binWidth);
                    if (bin >= numBins) bin = numBins - 1;
                    histogram[bin]++;
                }
            }

            if (totalVoxels == 0) return Array.Empty<DoseVolumePoint>();

            var points = new DoseVolumePoint[numBins];
            long cumulative = totalVoxels;
            for (int i = 0; i < numBins; i++)
            {
                points[i] = new DoseVolumePoint(i * binWidth, cumulative * 100.0 / totalVoxels);
                cumulative -= histogram[i];
            }
            return points;
        }
    }
}
