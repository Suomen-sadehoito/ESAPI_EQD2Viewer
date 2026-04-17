using EQD2Viewer.Core.Data;
using EQD2Viewer.Core.Models;

namespace EQD2Viewer.Core.Interfaces
{
    /// <summary>
    /// DVH calculation service interface.
    /// 
    /// Provides methods for:
    ///   - Computing DVH from summed dose arrays (summation workflow)
    ///   - Building summary statistics from pre-computed DVH curves
    ///   - Building physical and EQD2 summaries from Eclipse DVH data
    /// </summary>
    public interface IDVHCalculation
    {
        DoseVolumePoint[] CalculateDVHFromSummedDose(
            double[][] summedSlices, bool[][] structureMasks,
            double voxelVolumeCc, double maxDoseGy);

        DVHSummary BuildSummaryFromCurve(string structureId, string label,
            string type, DoseVolumePoint[] curve, double totalVolumeCc);

        /// <summary>
        /// Builds a physical dose summary from a pre-computed DVH curve.
        /// </summary>
        DVHSummary BuildPhysicalSummaryFromCurve(DvhCurveData dvh, string planId);

        /// <summary>
        /// Builds an EQD2-converted summary from a pre-computed DVH curve.
        /// </summary>
        DVHSummary BuildEQD2SummaryFromCurve(DvhCurveData dvh, string planId,
            int numberOfFractions, double alphaBeta, EQD2MeanMethod meanMethod);
    }
}