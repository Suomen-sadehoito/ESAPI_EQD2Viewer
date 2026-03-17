using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ESAPI_EQD2Viewer.Core.Models;

namespace ESAPI_EQD2Viewer.Core.Interfaces
{
    public interface ISummationService : IDisposable
    {
        /// <summary>
        /// Phase 1: Load ESAPI data into plain arrays. MUST run on UI thread.
        /// </summary>
        SummationResult PrepareData(SummationConfig config);

        /// <summary>
        /// Phase 2: Heavy voxel computation. Runs on ANY thread (no ESAPI calls).
        /// </summary>
        Task<SummationResult> ComputeAsync(IProgress<int> progress, CancellationToken ct);

        bool HasSummedDose { get; }
        double[] GetSummedSlice(int sliceIndex);
        double SummedReferenceDoseGy { get; }

        /// <summary>
        /// Returns the secondary plan's CT voxels mapped onto the reference CT grid.
        /// </summary>
        int[] GetRegisteredCtSlice(string planDisplayLabel, int sliceIndex);

        /// <summary>
        /// Gets the pre-rasterized structure mask for a specific structure and slice.
        /// Returns null if the structure was not cached.
        /// </summary>
        bool[] GetStructureMask(string structureId, int sliceIndex);

        /// <summary>
        /// Gets all structure IDs that have cached masks.
        /// </summary>
        IReadOnlyList<string> GetCachedStructureIds();

        /// <summary>
        /// Gets the voxel volume in cm³ for the reference CT grid.
        /// </summary>
        double GetVoxelVolumeCc();

        /// <summary>
        /// Gets the total number of slices.
        /// </summary>
        int SliceCount { get; }
    }

    public class SummationResult
    {
        public bool Success { get; set; }
        public string StatusMessage { get; set; }
        public double MaxDoseGy { get; set; }
        public double TotalReferenceDoseGy { get; set; }
        public int SliceCount { get; set; }
    }
}
