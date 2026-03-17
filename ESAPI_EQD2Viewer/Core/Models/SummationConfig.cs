using System.Collections.Generic;

namespace ESAPI_EQD2Viewer.Core.Models
{
    /// <summary>
    /// Configuration for multi-plan dose summation.
    /// 
    /// MATHEMATICAL BASIS FOR RE-IRRADIATION SUMMATION
    /// ================================================
    /// 
    /// Physical dose summation (D_total = ΣDᵢ) is biologically incorrect when
    /// plans have different fractionation schedules. The correct approach uses
    /// the EQD2 formalism:
    /// 
    ///   EQD2_total(x,y,z) = Σᵢ [ Dᵢ(x,y,z) × (dᵢ + α/β) / (2 + α/β) ]
    /// 
    /// where:
    ///   Dᵢ = total physical dose from plan i at voxel (x,y,z)
    ///   dᵢ = Dᵢ/nᵢ = dose per fraction for plan i at that voxel
    ///   nᵢ = number of fractions for plan i
    ///   α/β = tissue-specific radiobiological parameter
    /// 
    /// KEY INSIGHT: The per-fraction dose dᵢ varies spatially because dose is
    /// not uniform. A voxel receiving 70 Gy in 35 fractions (d=2.0 Gy) has a
    /// different biological effect than one receiving 35 Gy in 35 fractions
    /// (d=1.0 Gy), even though both have the same fractionation schedule.
    /// This is why EQD2 must be computed voxel-by-voxel.
    /// 
    /// REGISTRATION REQUIREMENT
    /// ========================
    /// Different treatment courses typically use different CT scans. To sum
    /// doses at corresponding anatomical locations, a spatial registration
    /// (rigid or deformable) maps coordinates between image sets.
    /// 
    /// For each voxel in the reference CT:
    ///   1. Get world coordinate P_ref
    ///   2. For each secondary plan:
    ///      a. Transform P_ref → P_sec using the selected registration
    ///      b. Sample dose from secondary plan at P_sec (bilinear interpolation)
    ///      c. Convert to EQD2 using that plan's fractionation
    ///   3. Sum all EQD2 contributions
    /// </summary>
    public class SummationConfig
    {
        /// <summary>
        /// All plans participating in summation, including the primary (reference) plan.
        /// </summary>
        public List<SummationPlanEntry> Plans { get; set; } = new List<SummationPlanEntry>();

        /// <summary>
        /// Summation method.
        /// </summary>
        public SummationMethod Method { get; set; } = SummationMethod.EQD2;

        /// <summary>
        /// Global α/β for isodose rendering of the summed dose.
        /// Structure-specific α/β is used separately for DVH calculations.
        /// </summary>
        public double GlobalAlphaBeta { get; set; } = 3.0;
    }

    /// <summary>
    /// One plan's entry in the summation configuration.
    /// </summary>
    public class SummationPlanEntry
    {
        /// <summary>
        /// Display label: "Course / Plan" for UI identification.
        /// </summary>
        public string DisplayLabel { get; set; }

        /// <summary>
        /// Course ID this plan belongs to.
        /// </summary>
        public string CourseId { get; set; }

        /// <summary>
        /// Plan ID within the course.
        /// </summary>
        public string PlanId { get; set; }

        /// <summary>
        /// Number of fractions for this plan. Read from PlanSetup but user-overridable.
        /// Critical for correct EQD2 calculation.
        /// </summary>
        public int NumberOfFractions { get; set; } = 1;

        /// <summary>
        /// Total prescribed dose in Gy. Used as reference for relative dose display.
        /// </summary>
        public double TotalDoseGy { get; set; }

        /// <summary>
        /// Plan normalization value (%).
        /// </summary>
        public double PlanNormalization { get; set; } = 100.0;

        /// <summary>
        /// Whether this is the reference (primary) plan whose CT is used as the base grid.
        /// Exactly one plan must be the reference.
        /// </summary>
        public bool IsReference { get; set; }

        /// <summary>
        /// Selected registration ID for mapping this plan's dose to the reference CT.
        /// Null/empty for the reference plan itself (no transformation needed).
        /// </summary>
        public string RegistrationId { get; set; }

        /// <summary>
        /// Weight factor for this plan (default 1.0). Allows fractional contribution,
        /// e.g. 0.5 for partial-course re-planning scenarios.
        /// </summary>
        public double Weight { get; set; } = 1.0;
    }

    /// <summary>
    /// How to sum doses from multiple plans.
    /// </summary>
    public enum SummationMethod
    {
        /// <summary>
        /// Sum physical doses directly. Biologically incorrect for different fractionations
        /// but useful as a quick reference.
        /// </summary>
        Physical,

        /// <summary>
        /// Convert each plan's dose to EQD2 independently, then sum.
        /// Biologically correct for re-irradiation assessment.
        /// 
        /// EQD2_total = Σ [ Dᵢ × (Dᵢ/nᵢ + α/β) / (2 + α/β) ]
        /// </summary>
        EQD2
    }
}