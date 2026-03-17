using System;
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
        /// Returns the secondary plan's CT voxels (as HU values) mapped onto the
        /// reference CT grid for registration verification overlay.
        /// Returns a flat array [y * width + x] of HU values at CT resolution,
        /// or null if the plan is not found or has no CT data.
        /// </summary>
        /// <param name="planDisplayLabel">Plan's DisplayLabel from SummationConfig</param>
        /// <param name="sliceIndex">Reference CT slice index</param>
        int[] GetRegisteredCtSlice(string planDisplayLabel, int sliceIndex);
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