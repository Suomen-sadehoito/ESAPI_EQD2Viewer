using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EQD2Viewer.Core.Data;
using EQD2Viewer.Core.Models;
using EQD2Viewer.Core.Calculations;
using EQD2Viewer.Core.Logging;
using EQD2Viewer.Core.Models;

namespace EQD2Viewer.Core.Interfaces
{
    /// <summary>
    /// Two-phase dose summation service for multi-plan re-irradiation assessment.
    /// 
    /// Architecture:
    ///   Phase 1 (UI thread):  Load plan data through ISummationDataLoader into plain arrays.
    ///   Phase 2 (any thread): Accumulate per-plan physical dose + compute EQD2 display sum.
    /// 
    /// After Phase 2 completes, the service retains per-plan physical dose arrays,
    /// enabling:
    ///   - Fast EQD2 recomputation with a different display Î±/Î² (no data reloading).
    ///   - Per-structure DVH calculation with structure-specific Î±/Î² values.
    /// </summary>
    public interface ISummationService : IDisposable
    {
        /// <summary>
        /// Phase 1: Load plan data into plain arrays via ISummationDataLoader.
        /// MUST run on UI thread when using ESAPI data loader.
        /// </summary>
        SummationResult PrepareData(SummationConfig config);

        /// <summary>
        /// Phase 2: Heavy voxel computation. Runs on ANY thread (no ESAPI calls).
        /// Stores per-plan physical dose AND computes EQD2 display sum.
        /// </summary>
        Task<SummationResult> ComputeAsync(IProgress<int> progress, CancellationToken ct);

        /// <summary>
        /// Recomputes the EQD2 display sum from stored per-plan physical doses.
        /// Much faster than full re-summation â€” skips Phase 1 entirely.
        /// Called when the user changes the display Î±/Î² slider.
        /// </summary>
        /// <param name="displayAlphaBeta">New Î±/Î² value for isodose visualization [Gy].</param>
        /// <param name="progress">Optional progress reporter.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Updated summation result with new max dose and reference dose.</returns>
        Task<SummationResult> RecomputeEQD2DisplayAsync(double displayAlphaBeta,
            IProgress<int> progress, CancellationToken ct);

        /// <summary>
        /// Computes a cumulative DVH for a specific structure using that structure's own Î±/Î².
        /// Iterates per-plan physical doses, converts each to EQD2 with the given Î±/Î²
        /// and each plan's own fractionation, then sums and bins into a histogram.
        /// 
        /// This is the correct physics: EQD2_total = Î£_i D_iÂ·(d_i + Î±/Î²)/(2 + Î±/Î²)
        /// where d_i = D_i/n_i uses each plan's individual fraction count.
        /// </summary>
        /// <param name="structureId">Structure ID for mask lookup.</param>
        /// <param name="structureAlphaBeta">Structure-specific Î±/Î² [Gy].</param>
        /// <param name="maxDoseGy">Maximum dose for histogram range.</param>
        /// <returns>Cumulative DVH curve, or empty array if structure has no mask.</returns>
        DoseVolumePoint[] ComputeStructureEQD2DVH(string structureId,
            double structureAlphaBeta, double maxDoseGy);

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
        /// Gets the voxel volume in cmÂ³ for the reference CT grid.
        /// </summary>
        double GetVoxelVolumeCc();

        /// <summary>
        /// Gets the total number of slices.
        /// </summary>
        int SliceCount { get; }

        /// <summary>
        /// Gets the maximum dose in the current EQD2 display sum [Gy].
        /// Updated after ComputeAsync and RecomputeEQD2DisplayAsync.
        /// </summary>
        double MaxDoseGy { get; }
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