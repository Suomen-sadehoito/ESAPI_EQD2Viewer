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
        /// Fast (~2-5 s for typical plans).
        /// </summary>
        SummationResult PrepareData(SummationConfig config);

        /// <summary>
        /// Phase 2: Heavy voxel computation. Runs on ANY thread (no ESAPI calls).
        /// Reports progress 0-100 and supports cancellation.
        /// </summary>
        Task<SummationResult> ComputeAsync(IProgress<int> progress, CancellationToken ct);

        bool HasSummedDose { get; }
        double[] GetSummedSlice(int sliceIndex);
        double SummedReferenceDoseGy { get; }
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