namespace EQD2Viewer.Fixtures.Models
{
    /// <summary>
    /// C# models matching the JSON fixture schema produced by FixtureGenerator.
    /// Used by FixtureLoader to deserialize Eclipse-exported test data.
    /// </summary>

    public class PlanMetadata
    {
        public string patientId { get; set; } = "";
        public string courseId { get; set; } = "";
        public string planId { get; set; } = "";
        public string planType { get; set; } = "";   // "PlanSetup" or "PlanSum"
        public double totalDoseGy { get; set; }
        public int numberOfFractions { get; set; }
        public double planNormalization { get; set; }
        public double dosePerFractionGy { get; set; }
        public string generatedAt { get; set; } = "";
        public string generatorVersion { get; set; } = "";
    }

    public class DoseScaling
    {
        public double rawScale { get; set; }
        public double rawOffset { get; set; }
        public string doseUnit { get; set; } = "";
        public double unitToGy { get; set; }
        public int calibrationRawValue { get; set; }
        public double calibrationDoseValue { get; set; }
        public string calibrationDoseUnit { get; set; } = "";
    }

    public class GridGeometry
    {
        public int xSize { get; set; }
        public int ySize { get; set; }
        public int zSize { get; set; }
        public double xRes { get; set; }
        public double yRes { get; set; }
        public double zRes { get; set; }
        public double[] origin { get; set; } = null!;
        public double[] xDirection { get; set; } = null!;
        public double[] yDirection { get; set; } = null!;
        public double[] zDirection { get; set; } = null!;
        public string frameOfReference { get; set; } = "";
    }

    public class DoseSlice
    {
        public int sliceIndex { get; set; }
        public int width { get; set; }
        public int height { get; set; }
        public double maxDoseGy { get; set; }
        public double meanDoseGy { get; set; }
        public double[] valuesGy { get; set; } = null!;
        public int[] rawValues { get; set; } = null!;
    }

    public class CtSubsample
    {
        public int originalSliceIndex { get; set; }
        public int originalWidth { get; set; }
        public int originalHeight { get; set; }
        public int subsampleStep { get; set; }
        public int width { get; set; }
        public int height { get; set; }
        public int detectedHuOffset { get; set; }
        public int[] values { get; set; } = null!;
    }

    public class StructureFixture
    {
        public string id { get; set; } = "";
        public string dicomType { get; set; } = "";
        public int[] color { get; set; } = null!;
        public StructureSlice[] slices { get; set; } = null!;
    }

    public class StructureSlice
    {
        public int sliceIndex { get; set; }
        public ContourData[] contours { get; set; } = null!;
    }

    public class ContourData
    {
        public double[][] points { get; set; } = null!;
    }

    public class DvhFixture
    {
        public string structureId { get; set; } = "";
        public string planId { get; set; } = "";
        public double dmaxGy { get; set; }
        public double dmeanGy { get; set; }
        public double dminGy { get; set; }
        public double volumeCc { get; set; }
        public int curvePointCount { get; set; }
        public double[][] curve { get; set; } = null!;
    }

    public class ReferenceDosePoints
    {
        public int ctSliceIndex { get; set; }
        public DoseTestPoint[] points { get; set; } = null!;
    }

    public class DoseTestPoint
    {
        public int ctPixelX { get; set; }
        public int ctPixelY { get; set; }
        public int ctSlice { get; set; }
        public int doseVoxelX { get; set; }
        public int doseVoxelY { get; set; }
        public int doseVoxelZ { get; set; }
        public double doseGy { get; set; }
        public bool isInsideDoseGrid { get; set; }
    }

    public class RegistrationsFile
    {
        public string referenceImageFOR { get; set; } = "";
        public RegistrationEntry[] registrations { get; set; } = null!;
    }

    public class RegistrationEntry
    {
        public string id { get; set; } = "";
        public string sourceFOR { get; set; } = "";
        public string registeredFOR { get; set; } = "";
        public string date { get; set; } = "";
        public double[] matrix { get; set; } = null!;
    }
}
