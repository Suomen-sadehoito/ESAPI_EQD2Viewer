using EQD2Viewer.Core.Serialization;
using EQD2Viewer.Core.Data;
using FluentAssertions;
using System.Collections.Generic;
using System.IO;

namespace EQD2Viewer.Tests.Integration.Models
{
    /// <summary>
    /// End-to-end tests for SnapshotSerializer binary (v3.0) and JSON+RLE (v2.0) formats.
    ///
    /// These tests verify the full serialization round-trip:
    ///   ClinicalSnapshot ? Write ? disk ? Read ? ClinicalSnapshot
    ///
    /// Both formats are tested to ensure:
    ///   - All fields survive the round-trip (patient, plan, CT, dose, structures, DVH, etc.)
    ///   - Binary format produces dramatically smaller files than JSON for volumetric data
    ///   - ReadAuto correctly detects and dispatches to the right reader
    ///   - Voxel values are bit-exact after round-trip
    /// </summary>
    public class SnapshotSerializerTests : IDisposable
    {
        private readonly string _tempDir;

        public SnapshotSerializerTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "EQD2_SnapshotTests_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
            catch { /* best-effort cleanup */ }
        }

        // ========================================================
        // TEST HELPERS
        // ========================================================

        private static ClinicalSnapshot CreateTestSnapshot(int ctX = 8, int ctY = 8, int ctZ = 4,
            int doseX = 4, int doseY = 4, int doseZ = 4)
        {
            var snap = new ClinicalSnapshot
            {
                Patient = new PatientData { Id = "TEST001", LastName = "Test", FirstName = "Patient" },
                ActivePlan = new PlanData
                {
                    Id = "Plan1",
                    CourseId = "C1",
                    TotalDoseGy = 50.0,
                    NumberOfFractions = 25,
                    PlanNormalization = 100.0
                }
            };

            // CT with gradient values (distinct per voxel for verification)
            var ctVoxels = new int[ctZ][,];
            for (int z = 0; z < ctZ; z++)
            {
                ctVoxels[z] = new int[ctX, ctY];
                for (int y = 0; y < ctY; y++)
                    for (int x = 0; x < ctX; x++)
                        ctVoxels[z][x, y] = z * 10000 + y * 100 + x;
            }

            snap.CtImage = new VolumeData
            {
                Geometry = new VolumeGeometry
                {
                    XSize = ctX,
                    YSize = ctY,
                    ZSize = ctZ,
                    XRes = 1.0,
                    YRes = 1.0,
                    ZRes = 2.5,
                    Origin = new Vec3(-100, -100, -50),
                    XDirection = new Vec3(1, 0, 0),
                    YDirection = new Vec3(0, 1, 0),
                    ZDirection = new Vec3(0, 0, 1),
                    FrameOfReference = "1.2.3.4.5",
                    Id = "CT_001"
                },
                Voxels = ctVoxels,
                HuOffset = 32768
            };

            // Dose with distinct values
            var doseVoxels = new int[doseZ][,];
            for (int z = 0; z < doseZ; z++)
            {
                doseVoxels[z] = new int[doseX, doseY];
                for (int y = 0; y < doseY; y++)
                    for (int x = 0; x < doseX; x++)
                        doseVoxels[z][x, y] = (z + 1) * 1000 + y * 10 + x;
            }

            snap.Dose = new DoseVolumeData
            {
                Geometry = new VolumeGeometry
                {
                    XSize = doseX,
                    YSize = doseY,
                    ZSize = doseZ,
                    XRes = 2.5,
                    YRes = 2.5,
                    ZRes = 2.5,
                    Origin = new Vec3(-50, -50, -50),
                    XDirection = new Vec3(1, 0, 0),
                    YDirection = new Vec3(0, 1, 0),
                    ZDirection = new Vec3(0, 0, 1),
                    FrameOfReference = "1.2.3.4.5"
                },
                Voxels = doseVoxels,
                Scaling = new DoseScaling
                {
                    RawScale = 0.001,
                    RawOffset = 0.0,
                    UnitToGy = 1.0,
                    DoseUnit = "Gy"
                }
            };

            // Structures
            snap.Structures = new List<StructureData>
    {
        new StructureData
        {
      Id = "PTV", DicomType = "PTV",
            ColorR = 255, ColorG = 0, ColorB = 0, ColorA = 255,
         IsEmpty = false, HasMesh = true,
  ContoursBySlice = new Dictionary<int, List<double[][]>>
        {
             [1] = new List<double[][]>
  {
     new[]
   {
      new[] { -10.0, -10.0, 0.0 },
         new[] { 10.0, -10.0, 0.0 },
    new[] { 10.0, 10.0, 0.0 },
        new[] { -10.0, 10.0, 0.0 }
         }
           }
       }
       }
        };

            // DVH curves
            snap.DvhCurves = new List<DvhCurveData>
  {
             new DvhCurveData
       {
          StructureId = "PTV", PlanId = "Plan1",
         DMaxGy = 52.5, DMeanGy = 50.1, DMinGy = 48.0, VolumeCc = 125.0,
          Curve = new[] { new[] { 0.0, 100.0 }, new[] { 50.0, 50.0 }, new[] { 55.0, 0.0 } }
     }
            };

            // Registrations
            snap.Registrations = new List<RegistrationData>
 {
            new RegistrationData
     {
     Id = "Reg1", SourceFOR = "1.2.3.4.5", RegisteredFOR = "1.2.3.4.6",
      CreationDateTime = new DateTime(2024, 1, 15, 10, 30, 0),
    Matrix = new double[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 5.0, -3.0, 1.0, 1 }
     }
        };

            // Courses
            snap.AllCourses = new List<CourseData>
 {
          new CourseData
        {
   Id = "C1",
       Plans = new List<PlanSummaryData>
         {
       new PlanSummaryData
  {
     PlanId = "Plan1", CourseId = "C1",
               TotalDoseGy = 50.0, NumberOfFractions = 25,
      PlanNormalization = 100.0, HasDose = true,
                 ImageFOR = "1.2.3.4.5"
        }
          }
         }
     };

            return snap;
        }

        // ========================================================
        // BINARY FORMAT ROUND-TRIP
        // ========================================================

        [Fact]
        public void WriteBinary_ReadBinary_RoundTrip_PreservesAllData()
        {
            var original = CreateTestSnapshot();
            string dir = Path.Combine(_tempDir, "binary_roundtrip");

            SnapshotSerializer.WriteBinary(original, dir);
            var loaded = SnapshotSerializer.ReadBinary(dir);

            // Patient
            loaded.Patient.Should().NotBeNull();
            loaded.Patient.Id.Should().Be("TEST001");
            loaded.Patient.LastName.Should().Be("Test");
            loaded.Patient.FirstName.Should().Be("Patient");

            // Plan
            loaded.ActivePlan.Should().NotBeNull();
            loaded.ActivePlan.Id.Should().Be("Plan1");
            loaded.ActivePlan.CourseId.Should().Be("C1");
            loaded.ActivePlan.TotalDoseGy.Should().Be(50.0);
            loaded.ActivePlan.NumberOfFractions.Should().Be(25);

            // CT geometry
            loaded.CtImage.Should().NotBeNull();
            loaded.CtImage.XSize.Should().Be(8);
            loaded.CtImage.YSize.Should().Be(8);
            loaded.CtImage.ZSize.Should().Be(4);
            loaded.CtImage.HuOffset.Should().Be(32768);

            // Dose geometry + scaling
            loaded.Dose.Should().NotBeNull();
            loaded.Dose.XSize.Should().Be(4);
            loaded.Dose.Scaling.RawScale.Should().Be(0.001);
            loaded.Dose.Scaling.DoseUnit.Should().Be("Gy");
        }

        [Fact]
        public void WriteBinary_ReadBinary_CTVoxels_AreBitExact()
        {
            var original = CreateTestSnapshot();
            string dir = Path.Combine(_tempDir, "ct_bitexact");

            SnapshotSerializer.WriteBinary(original, dir);
            var loaded = SnapshotSerializer.ReadBinary(dir);

            for (int z = 0; z < original.CtImage.ZSize; z++)
                for (int y = 0; y < original.CtImage.YSize; y++)
                    for (int x = 0; x < original.CtImage.XSize; x++)
                        loaded.CtImage.Voxels[z][x, y].Should().Be(
                                 original.CtImage.Voxels[z][x, y],
                        $"CT voxel mismatch at ({x},{y},{z})");
        }

        [Fact]
        public void WriteBinary_ReadBinary_DoseVoxels_AreBitExact()
        {
            var original = CreateTestSnapshot();
            string dir = Path.Combine(_tempDir, "dose_bitexact");

            SnapshotSerializer.WriteBinary(original, dir);
            var loaded = SnapshotSerializer.ReadBinary(dir);

            for (int z = 0; z < original.Dose.Geometry.ZSize; z++)
                for (int y = 0; y < original.Dose.Geometry.YSize; y++)
                    for (int x = 0; x < original.Dose.Geometry.XSize; x++)
                        loaded.Dose.Voxels[z][x, y].Should().Be(
                        original.Dose.Voxels[z][x, y],
                    $"dose voxel mismatch at ({x},{y},{z})");
        }

        [Fact]
        public void WriteBinary_ReadBinary_Structures_PreserveContours()
        {
            var original = CreateTestSnapshot();
            string dir = Path.Combine(_tempDir, "structures");

            SnapshotSerializer.WriteBinary(original, dir);
            var loaded = SnapshotSerializer.ReadBinary(dir);

            loaded.Structures.Should().HaveCount(1);
            loaded.Structures[0].Id.Should().Be("PTV");
            loaded.Structures[0].DicomType.Should().Be("PTV");
            loaded.Structures[0].ContoursBySlice.Should().ContainKey(1);
            loaded.Structures[0].ContoursBySlice[1].Should().HaveCount(1);
            loaded.Structures[0].ContoursBySlice[1][0].Should().HaveCount(4);
        }

        [Fact]
        public void WriteBinary_ReadBinary_DVHCurves_PreserveValues()
        {
            var original = CreateTestSnapshot();
            string dir = Path.Combine(_tempDir, "dvh");

            SnapshotSerializer.WriteBinary(original, dir);
            var loaded = SnapshotSerializer.ReadBinary(dir);

            loaded.DvhCurves.Should().HaveCount(1);
            loaded.DvhCurves[0].StructureId.Should().Be("PTV");
            loaded.DvhCurves[0].DMaxGy.Should().BeApproximately(52.5, 0.01);
            loaded.DvhCurves[0].Curve.Should().HaveCount(3);
        }

        [Fact]
        public void WriteBinary_ReadBinary_Registrations_PreserveMatrix()
        {
            var original = CreateTestSnapshot();
            string dir = Path.Combine(_tempDir, "regs");

            SnapshotSerializer.WriteBinary(original, dir);
            var loaded = SnapshotSerializer.ReadBinary(dir);

            loaded.Registrations.Should().HaveCount(1);
            loaded.Registrations[0].Id.Should().Be("Reg1");
            loaded.Registrations[0].Matrix.Should().HaveCount(16);
            loaded.Registrations[0].Matrix[12].Should().BeApproximately(5.0, 0.001);
        }

        // ========================================================
        // JSON+RLE FORMAT ROUND-TRIP
        // ========================================================

        [Fact]
        public void WriteJson_ReadJson_RoundTrip_PreservesAllData()
        {
            var original = CreateTestSnapshot();
            string dir = Path.Combine(_tempDir, "json_roundtrip");

            SnapshotSerializer.Write(original, dir);
            var loaded = SnapshotSerializer.Read(dir);

            loaded.Patient.Id.Should().Be("TEST001");
            loaded.ActivePlan.Id.Should().Be("Plan1");
            loaded.CtImage.XSize.Should().Be(8);
            loaded.Dose.XSize.Should().Be(4);
            loaded.Structures.Should().HaveCount(1);
            loaded.DvhCurves.Should().HaveCount(1);
        }

        [Fact]
        public void WriteJson_ReadJson_CTVoxels_AreBitExact()
        {
            var original = CreateTestSnapshot();
            string dir = Path.Combine(_tempDir, "json_ct");

            SnapshotSerializer.Write(original, dir);
            var loaded = SnapshotSerializer.Read(dir);

            for (int z = 0; z < original.CtImage.ZSize; z++)
                for (int y = 0; y < original.CtImage.YSize; y++)
                    for (int x = 0; x < original.CtImage.XSize; x++)
                        loaded.CtImage.Voxels[z][x, y].Should().Be(
                             original.CtImage.Voxels[z][x, y]);
        }

        // ========================================================
        // AUTO-DETECTION
        // ========================================================

        [Fact]
        public void ReadAuto_BinaryFormat_DetectsAndLoadsCorrectly()
        {
            var original = CreateTestSnapshot();
            string dir = Path.Combine(_tempDir, "auto_binary");

            SnapshotSerializer.WriteBinary(original, dir);
            var loaded = SnapshotSerializer.ReadAuto(dir);

            loaded.Patient.Id.Should().Be("TEST001");
            loaded.CtImage.Should().NotBeNull();
        }

        [Fact]
        public void ReadAuto_JsonFormat_DetectsAndLoadsCorrectly()
        {
            var original = CreateTestSnapshot();
            string dir = Path.Combine(_tempDir, "auto_json");

            SnapshotSerializer.Write(original, dir);
            var loaded = SnapshotSerializer.ReadAuto(dir);

            loaded.Patient.Id.Should().Be("TEST001");
            loaded.CtImage.Should().NotBeNull();
        }

        // ========================================================
        // BINARY VOLUME I/O UNIT TESTS
        // ========================================================

        [Fact]
        public void WriteVolumeBinary_ReadVolumeBinary_SmallVolume_IsExact()
        {
            string path = Path.Combine(_tempDir, "test_vol.bin.gz");
            var voxels = new int[2][,];
            voxels[0] = new int[3, 2] { { 100, 200 }, { 300, 400 }, { 500, 600 } };
            voxels[1] = new int[3, 2] { { -100, -200 }, { int.MaxValue, int.MinValue }, { 0, 1 } };

            SnapshotSerializer.WriteVolumeBinary(voxels, path);
            var loaded = SnapshotSerializer.ReadVolumeBinary(path);

            loaded.Length.Should().Be(2);
            loaded[0].GetLength(0).Should().Be(3);
            loaded[0].GetLength(1).Should().Be(2);

            for (int z = 0; z < 2; z++)
                for (int x = 0; x < 3; x++)
                    for (int y = 0; y < 2; y++)
                        loaded[z][x, y].Should().Be(voxels[z][x, y]);
        }

        [Fact]
        public void WriteVolumeBinary_ProducesCompactFile()
        {
            // 64x64x64 = 262,144 voxels * 4 bytes = 1 MB uncompressed
            // With all-zero data, GZip should compress to < 10 KB
            string path = Path.Combine(_tempDir, "compact.bin.gz");
            var voxels = new int[64][,];
            for (int z = 0; z < 64; z++)
                voxels[z] = new int[64, 64]; // all zeros

            SnapshotSerializer.WriteVolumeBinary(voxels, path);

            new FileInfo(path).Length.Should().BeLessThan(10 * 1024,
       "uniform zero volume should compress to < 10 KB");
        }

        [Fact]
        public void BinaryFormat_ShouldBeSmallerThanJson_ForRealisticVolume()
        {
            var snap = CreateTestSnapshot(ctX: 32, ctY: 32, ctZ: 16,
               doseX: 16, doseY: 16, doseZ: 16);

            string jsonDir = Path.Combine(_tempDir, "size_json");
            string binDir = Path.Combine(_tempDir, "size_binary");

            SnapshotSerializer.Write(snap, jsonDir);
            SnapshotSerializer.WriteBinary(snap, binDir);

            long jsonSize = GetDirectorySize(jsonDir);
            long binSize = GetDirectorySize(binDir);

            binSize.Should().BeLessThan(jsonSize,
               "binary format should be smaller than JSON+RLE for volumetric data");
        }

        // ========================================================
        // CROSS-FORMAT COMPATIBILITY
        // ========================================================

        [Fact]
        public void BinaryAndJson_ShouldProduceIdenticalSnapshots()
        {
            var original = CreateTestSnapshot();
            string jsonDir = Path.Combine(_tempDir, "cross_json");
            string binDir = Path.Combine(_tempDir, "cross_binary");

            SnapshotSerializer.Write(original, jsonDir);
            SnapshotSerializer.WriteBinary(original, binDir);

            var fromJson = SnapshotSerializer.Read(jsonDir);
            var fromBin = SnapshotSerializer.ReadBinary(binDir);

            // Verify both produce identical data
            fromJson.Patient.Id.Should().Be(fromBin.Patient.Id);
            fromJson.ActivePlan.TotalDoseGy.Should().Be(fromBin.ActivePlan.TotalDoseGy);
            fromJson.CtImage.XSize.Should().Be(fromBin.CtImage.XSize);
            fromJson.CtImage.HuOffset.Should().Be(fromBin.CtImage.HuOffset);
            fromJson.Structures.Count.Should().Be(fromBin.Structures.Count);

            // Voxel-level comparison
            for (int z = 0; z < original.CtImage.ZSize; z++)
                for (int y = 0; y < original.CtImage.YSize; y++)
                    for (int x = 0; x < original.CtImage.XSize; x++)
                        fromJson.CtImage.Voxels[z][x, y].Should().Be(
                           fromBin.CtImage.Voxels[z][x, y],
                                    $"cross-format CT mismatch at ({x},{y},{z})");
        }

        // ========================================================
        // ERROR HANDLING
        // ========================================================

        [Fact]
        public void Read_MissingDirectory_ShouldThrow()
        {
            Action act = () => SnapshotSerializer.Read(Path.Combine(_tempDir, "nonexistent"));
            act.Should().Throw<FileNotFoundException>();
        }

        [Fact]
        public void ReadAuto_UnknownVersion_ShouldThrow()
        {
            string dir = Path.Combine(_tempDir, "bad_version");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "snapshot_meta.json"),
  "{\"formatVersion\":\"99.0\"}");

            Action act = () => SnapshotSerializer.ReadAuto(dir);
            act.Should().Throw<InvalidOperationException>()
             .WithMessage("*99.0*");
        }

        private static long GetDirectorySize(string dir)
        {
            long size = 0;
            foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                size += new FileInfo(file).Length;
            return size;
        }
    }
}
