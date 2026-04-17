using System;
using System.Collections.Generic;

namespace EQD2Viewer.Core.Data
{
    /// <summary>
    /// Represents an immutable snapshot of all clinical data required to run the application.
    /// </summary>
    /// <remarks>
    /// Populated by an IClinicalDataSource (e.g., Eclipse API or JSON fixtures).
    /// Once instantiated, the application operates entirely on this data, eliminating direct ESAPI dependencies.
    /// </remarks>
    public class ClinicalSnapshot
    {
        /// <summary>
        /// Gets or sets the associated patient data.
        /// </summary>
        public PatientData Patient { get; set; } = null!;

        /// <summary>
        /// Gets or sets the currently active plan data.
        /// </summary>
        public PlanData ActivePlan { get; set; } = null!;

        /// <summary>
        /// Gets or sets the primary CT image volume.
        /// </summary>
        public VolumeData CtImage { get; set; } = null!;

        /// <summary>
        /// Gets or sets the calculated dose grid data.
        /// </summary>
        public DoseVolumeData Dose { get; set; } = null!;

        /// <summary>
        /// Gets or sets the collection of structures associated with the active plan's structure set.
        /// </summary>
        public List<StructureData> Structures { get; set; } = new List<StructureData>();

        /// <summary>
        /// Gets or sets pre-computed Dose-Volume Histogram (DVH) curves.
        /// </summary>
        public List<DvhCurveData> DvhCurves { get; set; } = new List<DvhCurveData>();

        /// <summary>
        /// Gets or sets spatial registrations used for dose summation.
        /// </summary>
        public List<RegistrationData> Registrations { get; set; } = new List<RegistrationData>();

        /// <summary>
        /// Gets or sets the collection of all available courses and plans, primarily used for the summation dialog.
        /// </summary>
        public List<CourseData> AllCourses { get; set; } = new List<CourseData>();

        /// <summary>
        /// Gets or sets optional display parameters captured at export time, such as window/level, isodose levels, and reference points.
        /// </summary>
        /// <remarks>
        /// May be null for older snapshots or synthetic test data. Primarily used for end-to-end verification.
        /// </remarks>
        public RenderSettings? RenderSettings { get; set; }
    }

    /// <summary>
    /// Contains essential demographic and identification data for a patient.
    /// </summary>
    public class PatientData
    {
        public string Id { get; set; } = "";
        public string LastName { get; set; } = "";
        public string FirstName { get; set; } = "";
    }

    /// <summary>
    /// Contains core dosimetric and fractionation details for a treatment plan.
    /// </summary>
    public class PlanData
    {
        public string Id { get; set; } = "";
        public string CourseId { get; set; } = "";
        public double TotalDoseGy { get; set; }
        public int NumberOfFractions { get; set; } = 1;
        public double PlanNormalization { get; set; } = 100.0;
    }

    /// <summary>
    /// Defines the 3D spatial geometry, including dimensions, resolution, and orientation.
    /// Applied to both CT images and dose grids.
    /// </summary>
    public class VolumeGeometry
    {
        public int XSize { get; set; }
        public int YSize { get; set; }
        public int ZSize { get; set; }
        public double XRes { get; set; }
        public double YRes { get; set; }
        public double ZRes { get; set; }
        public Vec3 Origin { get; set; }
        public Vec3 XDirection { get; set; }
        public Vec3 YDirection { get; set; }
        public Vec3 ZDirection { get; set; }
        public string FrameOfReference { get; set; } = "";
        public string Id { get; set; } = "";
    }

    /// <summary>
    /// Represents a complete CT image volume, encapsulating geometry, voxel data, and Hounsfield Unit (HU) offsets.
    /// </summary>
    /// <remarks>
    /// Voxel data is pre-loaded into memory to facilitate highly performant rendering.
    /// </remarks>
    public class VolumeData
    {
        public VolumeGeometry Geometry { get; set; } = new VolumeGeometry();

        /// <summary>
        /// 3D array of CT voxel data, indexed by [sliceZ][x, y].
        /// Contains raw DICOM stored values; HU offset subtraction may be required depending on storage format.
        /// </summary>
        public int[][,] Voxels { get; set; } = null!;

        /// <summary>
        /// The Hounsfield Unit (HU) offset detected from the underlying voxel data.
        /// Typically 0 for signed storage and 32768 for unsigned storage.
        /// </summary>
        public int HuOffset { get; set; }

        // ESAPI-compatible convenience accessors mapping to the underlying geometry.
        public int XSize => Geometry.XSize;
        public int YSize => Geometry.YSize;
        public int ZSize => Geometry.ZSize;
        public double XRes => Geometry.XRes;
        public double YRes => Geometry.YRes;
        public double ZRes => Geometry.ZRes;
        public Vec3 Origin => Geometry.Origin;
        public Vec3 XDirection => Geometry.XDirection;
        public Vec3 YDirection => Geometry.YDirection;
        public Vec3 ZDirection => Geometry.ZDirection;
        public string FOR => Geometry.FrameOfReference;
    }

    /// <summary>
    /// Defines the calibration factors required to convert raw dose voxel values into Gray (Gy).
    /// </summary>
    public class DoseScaling
    {
        /// <summary>
        /// The linear scaling factor applied to raw voxel values.
        /// Calculation: doseUnit = (rawVoxel * RawScale) + RawOffset
        /// </summary>
        public double RawScale { get; set; }

        /// <summary>
        /// The constant offset applied in the original dose unit space.
        /// </summary>
        public double RawOffset { get; set; }

        /// <summary>
        /// The multiplier used to convert from the original dose unit to Gray (Gy).
        /// Examples: 0.01 for cGy, 1.0 for Gy.
        /// </summary>
        public double UnitToGy { get; set; } = 1.0;

        /// <summary>
        /// The original nomenclature of the dose unit as defined in the source system (e.g., Gy, cGy, Percent).
        /// </summary>
        public string DoseUnit { get; set; } = "Gy";
    }

    /// <summary>
    /// Represents a complete dose grid, including spatial geometry, raw voxel matrices, and calibration data.
    /// </summary>
    /// <remarks>
    /// To calculate the absolute dose in Gy at a specific voxel [z][x, y]:
    /// (Voxels[z][x,y] * Scaling.RawScale + Scaling.RawOffset) * Scaling.UnitToGy
    /// </remarks>
    public class DoseVolumeData
    {
        public VolumeGeometry Geometry { get; set; } = new VolumeGeometry();

        /// <summary>
        /// 3D array of raw dose voxels, indexed by [sliceZ][x, y].
        /// Must be calibrated using the associated <see cref="Scaling"/> property.
        /// </summary>
        public int[][,] Voxels { get; set; } = null!;

        /// <summary>
        /// The scaling parameters necessary for converting raw voxel data into absolute dose values (Gy).
        /// </summary>
        public DoseScaling Scaling { get; set; } = new DoseScaling();

        // Convenience accessors mapping to the underlying geometry.
        public int XSize => Geometry.XSize;
        public int YSize => Geometry.YSize;
        public int ZSize => Geometry.ZSize;
        public double XRes => Geometry.XRes;
        public double YRes => Geometry.YRes;
        public double ZRes => Geometry.ZRes;
        public Vec3 Origin => Geometry.Origin;
        public Vec3 XDirection => Geometry.XDirection;
        public Vec3 YDirection => Geometry.YDirection;
        public Vec3 ZDirection => Geometry.ZDirection;
    }

    /// <summary>
    /// Defines an anatomical structure or region of interest, including its contour polygons and visual properties.
    /// </summary>
    public class StructureData
    {
        public string Id { get; set; } = "";
        public string DicomType { get; set; } = "";
        public bool IsEmpty { get; set; }
        public byte ColorR { get; set; }
        public byte ColorG { get; set; }
        public byte ColorB { get; set; }
        public byte ColorA { get; set; } = 255;

        /// <summary>
        /// Indicates whether the structure contains valid 3D mesh geometry.
        /// Primarily utilized for preliminary validation checks.
        /// </summary>
        public bool HasMesh { get; set; }

        /// <summary>
        /// A collection of planar contour polygons mapped by CT slice index.
        /// Each polygon is defined as an array of 3D spatial coordinates [x, y, z] measured in millimeters.
        /// </summary>
        public Dictionary<int, List<double[][]>> ContoursBySlice { get; set; }
            = new Dictionary<int, List<double[][]>>();
    }

    /// <summary>
    /// Represents a pre-computed cumulative Dose-Volume Histogram (DVH) for a specific structure.
    /// </summary>
    public class DvhCurveData
    {
        public string StructureId { get; set; } = "";
        public string PlanId { get; set; } = "";
        public double DMaxGy { get; set; }
        public double DMeanGy { get; set; }
        public double DMinGy { get; set; }
        public double VolumeCc { get; set; }

        /// <summary>
        /// The data points comprising the DVH curve. Each array element is a coordinate pair: [doseGy, volumePercent].
        /// </summary>
        public double[][] Curve { get; set; } = null!;
    }

    /// <summary>
    /// Defines a spatial registration transform between two disparate frames of reference.
    /// </summary>
    public class RegistrationData
    {
        public string Id { get; set; } = "";
        public string SourceFOR { get; set; } = "";
        public string RegisteredFOR { get; set; } = "";
        public DateTime? CreationDateTime { get; set; }

        /// <summary>
        /// A 16-element array representing a 4x4 affine transformation matrix in row-major order.
        /// </summary>
        public double[] Matrix { get; set; } = null!;

        /// <summary>
        /// Projects the flat matrix array into a two-dimensional 4x4 matrix, suitable for matrix mathematics operations.
        /// </summary>
        /// <returns>A 4x4 multidimensional array, or null if the source matrix is invalid.</returns>
        public double[,]? ToMatrix4x4()
        {
            if (Matrix == null || Matrix.Length != 16) return null;
            var m = new double[4, 4];
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 4; c++)
                    m[r, c] = Matrix[r * 4 + c];
            return m;
        }
    }

    /// <summary>
    /// Represents a course of treatment containing multiple plans, utilized within the summation dialog's plan selection hierarchy.
    /// </summary>
    public class CourseData
    {
        public string Id { get; set; } = "";
        public List<PlanSummaryData> Plans { get; set; } = new List<PlanSummaryData>();
    }

    /// <summary>
    /// Provides a lightweight data transfer object for plan summation interfaces.
    /// Defers loading of intensive voxel data until the plan is explicitly selected for operation.
    /// </summary>
    public class PlanSummaryData
    {
        public string PlanId { get; set; } = "";
        public string CourseId { get; set; } = "";
        public string ImageId { get; set; } = "";
        public string ImageFOR { get; set; } = "";
        public double TotalDoseGy { get; set; }
        public int NumberOfFractions { get; set; } = 1;
        public double PlanNormalization { get; set; } = 100.0;
        public bool HasDose { get; set; }
    }
}