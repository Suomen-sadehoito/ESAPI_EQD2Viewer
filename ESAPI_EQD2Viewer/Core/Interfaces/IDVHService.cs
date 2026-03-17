using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using ESAPI_EQD2Viewer.Core.Models;

namespace ESAPI_EQD2Viewer.Core.Interfaces
{
    public interface IDVHService
    {
        DVHData GetDVH(PlanSetup plan, Structure structure);
        DVHSummary BuildPhysicalSummary(PlanSetup plan, Structure structure, DVHData dvhData);
        DVHSummary BuildEQD2Summary(PlanSetup plan, Structure structure, DVHData dvhData,
            int numberOfFractions, double alphaBeta, EQD2MeanMethod meanMethod);
        DoseVolumePoint[] CalculateDVHFromSummedDose(double[][] summedSlices, bool[][] structureMasks,
            double voxelVolumeCc, double maxDoseGy);
        DVHSummary BuildSummaryFromCurve(string structureId, string label, string type,
            DoseVolumePoint[] curve, double totalVolumeCc);
    }
}
