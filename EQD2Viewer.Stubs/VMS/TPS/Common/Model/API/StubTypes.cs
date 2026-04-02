namespace VMS.TPS.Common.Model.API
{
    using VMS.TPS.Common.Model.Types;

    // ── Minimal stubs: just enough to compile ──

    public class Patient
    {
        public string Id { get; set; }
        public string LastName { get; set; }
        public string FirstName { get; set; }
        public System.Collections.Generic.IEnumerable<Course> Courses { get; set; }
        public System.Collections.Generic.IEnumerable<Registration> Registrations { get; set; }
    }

    public class Course
    {
        public string Id { get; set; }
        public System.Collections.Generic.IEnumerable<PlanSetup> PlanSetups { get; set; }
    }

    /// <summary>Stub for PlanningItem (base of PlanSetup and PlanSum).</summary>
    public abstract class PlanningItem
    {
        public string Id { get; set; }
        public Dose Dose { get; set; }
        public DVHData GetDVHCumulativeData(Structure s, DoseValuePresentation dp,
       VolumePresentation vp, double res) => null;
    }

    public class PlanSetup : PlanningItem
    {
        public DoseValue TotalDose { get; set; }
        public int? NumberOfFractions { get; set; }
        public double PlanNormalizationValue { get; set; }
        public new Dose Dose { get; set; }
        public StructureSet StructureSet { get; set; }
        public Course Course { get; set; }
        public new DVHData GetDVHCumulativeData(Structure s, DoseValuePresentation dp,
               VolumePresentation vp, double res) => null;
    }

    public class PlanSum : PlanningItem
    {
        public Course Course { get; set; }
        public System.Collections.Generic.IEnumerable<PlanSetup> PlanSetups { get; set; }
        = new System.Collections.Generic.List<PlanSetup>();
    }

    public class ExternalPlanSetup : PlanSetup { }

    public class Image
    {
        public int XSize { get; set; }
        public int YSize { get; set; }
        public int ZSize { get; set; }
        public double XRes { get; set; }
        public double YRes { get; set; }
        public double ZRes { get; set; }
        public VVector Origin { get; set; }
        public VVector XDirection { get; set; }
        public VVector YDirection { get; set; }
        public VVector ZDirection { get; set; }
        public string FOR { get; set; }
        public string Id { get; set; }
        public void GetVoxels(int sliceIndex, int[,] buffer) { }
    }

    public class Dose
    {
        public int XSize { get; set; }
        public int YSize { get; set; }
        public int ZSize { get; set; }
        public double XRes { get; set; }
        public double YRes { get; set; }
        public double ZRes { get; set; }
        public VVector Origin { get; set; }
        public VVector XDirection { get; set; }
        public VVector YDirection { get; set; }
        public VVector ZDirection { get; set; }
        public DoseValue VoxelToDoseValue(int rawValue) => new DoseValue(0, DoseValue.DoseUnit.Gy);
        public void GetVoxels(int sliceIndex, int[,] buffer) { }
    }

    public class Structure
    {
        public string Id { get; set; }
        public string DicomType { get; set; }
        public bool IsEmpty { get; set; }
        public System.Windows.Media.Color Color { get; set; }
        public System.Windows.Media.Media3D.MeshGeometry3D MeshGeometry { get; set; }
        public VVector[][] GetContoursOnImagePlane(int sliceIndex) => null;
    }

    public class StructureSet
    {
        public System.Collections.Generic.IEnumerable<Structure> Structures { get; set; }
        public Image Image { get; set; }
    }

    public class Registration
    {
        public string Id { get; set; }
        public string SourceFOR { get; set; }
        public string RegisteredFOR { get; set; }
        public System.DateTime? CreationDateTime { get; set; }
        public VVector TransformPoint(VVector input) => input;
    }

    public class DVHData
    {
        public DVHPoint[] CurveData { get; set; }
        public DoseValue MaxDose { get; set; }
        public DoseValue MeanDose { get; set; }
        public DoseValue MinDose { get; set; }
        public double Volume { get; set; }
    }

    public class ScriptContext
    {
        public Patient Patient { get; set; }
        public Image Image { get; set; }
        public ExternalPlanSetup ExternalPlanSetup { get; set; }
        public System.Collections.Generic.IEnumerable<PlanSum> PlanSumsInScope { get; set; }
    }

    public class ESAPIScriptAttribute : System.Attribute
    {
        public bool IsWriteable { get; set; }
    }
}