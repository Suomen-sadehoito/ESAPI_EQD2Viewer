using EQD2Viewer.Core.Data;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace EQD2Viewer.Core.Serialization
{
    /// <summary>
    /// Serializes and deserializes a ClinicalSnapshot to/from a directory of JSON files.
    ///
    /// File layout (one directory per snapshot):
    ///   snapshot_meta.json        -- version, export timestamp, patient/plan IDs
    ///   patient.json              -- PatientData
    ///   plan.json                 -- PlanData (active plan)
    ///   ct_geometry.json          -- VolumeGeometry for CT
    ///   ct_voxels_{z:D4}.json    -- one file per CT slice  (raw int values, RLE-compressed)
    ///   dose_geometry.json        -- VolumeGeometry for dose
    ///   dose_scaling.json         -- DoseScaling
    ///   dose_voxels_{z:D4}.json  -- one file per dose slice (raw int values, RLE-compressed)
    ///   structures.json           -- all StructureData (metadata + contours, no voxels)
    ///   dvh_curves.json           -- all DvhCurveData
    ///   registrations.json        -- all RegistrationData
    ///   courses.json              -- AllCourses (for summation dialog)
    ///
    /// Design rationale:
    ///   - Splitting CT/dose voxels into per-slice files lets large datasets load slice-by-slice
    ///     and keeps individual file sizes manageable (&lt;10 MB each for typical CT).
    ///   - RLE (run-length encoding) on CT reduces file size ~50-70% (large air/background regions).
    ///   - No external JSON library -- manual formatting for .NET 4.8 / single-DLL deployment.
    ///   - The same SnapshotSerializer is used by both FixtureGenerator (write) and
    ///     JsonDataSource (read), guaranteeing schema consistency.
    /// </summary>
    public static class SnapshotSerializer
    {
        public const string MetaFileName = "snapshot_meta.json";
        public const string FormatVersion = "2.0";
        public const string BinaryFormatVersion = "3.0";

        private static readonly Encoding UTF8NoBom = new UTF8Encoding(false);
        private static readonly CultureInfo INV = CultureInfo.InvariantCulture;

        // ========================================================
        // WRITE -- JSON+RLE (original, kept for backward compatibility)
        // ========================================================

        // WRITE
        // ========================================================

        /// <summary>
        /// Serializes the entire snapshot to <paramref name="outputDir"/>.
        /// The directory is created if it does not exist.
        /// Returns a human-readable export summary.
        /// </summary>
        public static string Write(ClinicalSnapshot snap, string outputDir)
        {
            Directory.CreateDirectory(outputDir);
            var sb = new StringBuilder();

            // 1. Meta
            WriteMeta(snap, outputDir);
            sb.AppendLine("  snapshot_meta.json");

            // 2. Patient
            WritePatient(snap.Patient, outputDir);
            sb.AppendLine("  patient.json");

            // 3. Active plan
            if (snap.ActivePlan != null)
            {
                WritePlan(snap.ActivePlan, outputDir);
                sb.AppendLine("  plan.json");
            }

            // 4. CT geometry
            if (snap.CtImage != null)
            {
                WriteGeometry(snap.CtImage.Geometry, Path.Combine(outputDir, "ct_geometry.json"));
                sb.AppendLine("  ct_geometry.json");

                // 5. CT voxels -- one file per slice
                int ctSlices = WriteVoxelSlices(snap.CtImage.Voxels, "ct_voxels_", outputDir);
                WriteJson(Path.Combine(outputDir, "ct_huoffset.json"),
                 $"{{\"huOffset\":{snap.CtImage.HuOffset}}}");
                sb.AppendLine($"  ct_voxels_*.json ({ctSlices} slices, HU offset={snap.CtImage.HuOffset})");
            }

            // 6. Dose geometry + scaling
            if (snap.Dose != null)
            {
                WriteGeometry(snap.Dose.Geometry, Path.Combine(outputDir, "dose_geometry.json"));
                WriteScaling(snap.Dose.Scaling, outputDir);
                sb.AppendLine("  dose_geometry.json + dose_scaling.json");

                // 7. Dose voxels -- one file per slice
                int doseSlices = WriteVoxelSlices(snap.Dose.Voxels, "dose_voxels_", outputDir);
                sb.AppendLine($"  dose_voxels_*.json ({doseSlices} slices)");
            }

            // 8. Structures
            WriteStructures(snap.Structures, outputDir);
            sb.AppendLine($"  structures.json ({snap.Structures?.Count ?? 0} structures)");

            // 9. DVH curves
            WriteDvhCurves(snap.DvhCurves, outputDir);
            sb.AppendLine($"  dvh_curves.json ({snap.DvhCurves?.Count ?? 0} curves)");

            // 10. Registrations
            WriteRegistrations(snap.Registrations, outputDir);
            sb.AppendLine($"  registrations.json ({snap.Registrations?.Count ?? 0} registrations)");

            // 11. Courses
            WriteCourses(snap.AllCourses, outputDir);
            sb.AppendLine($"  courses.json ({snap.AllCourses?.Count ?? 0} courses)");

            if (snap.RenderSettings != null)
            {
                WriteRenderSettings(snap.RenderSettings, outputDir);
                sb.AppendLine($"  render_settings.json ({snap.RenderSettings.ReferenceDosePoints?.Count ?? 0} ref points)");
            }

            return sb.ToString();
        }

        // ========================================================
        // READ -- JSON+RLE (original v2.0 format)
        // ========================================================

        /// <summary>
        /// Deserializes a ClinicalSnapshot from a directory written by <see cref="Write"/> (v2.0 format).
        /// </summary>
        public static ClinicalSnapshot Read(string dir)
        {
            string metaPath = Path.Combine(dir, MetaFileName);
            if (!File.Exists(metaPath))
                throw new FileNotFoundException($"snapshot_meta.json not found in: {dir}");

            string metaJson = ReadJson(metaPath);
            string version = ExtractString(metaJson, "formatVersion");
            if (version != FormatVersion)
                throw new InvalidOperationException(
               $"Snapshot format version mismatch: got '{version}', expected '{FormatVersion}'. " +
                      "Re-export the snapshot with the current FixtureGenerator.");

            var snap = new ClinicalSnapshot();

            string patientPath = Path.Combine(dir, "patient.json");
            if (File.Exists(patientPath))
                snap.Patient = ReadPatient(ReadJson(patientPath));

            string planPath = Path.Combine(dir, "plan.json");
            if (File.Exists(planPath))
                snap.ActivePlan = ReadPlan(ReadJson(planPath));

            string ctGeoPath = Path.Combine(dir, "ct_geometry.json");
            if (File.Exists(ctGeoPath))
            {
                var ctGeo = ReadGeometry(ReadJson(ctGeoPath));
                var ctVoxels = ReadVoxelSlices("ct_voxels_", dir, ctGeo.ZSize);
                int huOffset = 0;
                string huPath = Path.Combine(dir, "ct_huoffset.json");
                if (File.Exists(huPath))
                    huOffset = (int)ExtractDouble(ReadJson(huPath), "huOffset");

                snap.CtImage = new VolumeData
                {
                    Geometry = ctGeo,
                    Voxels = ctVoxels,
                    HuOffset = huOffset
                };
            }

            string doseGeoPath = Path.Combine(dir, "dose_geometry.json");
            if (File.Exists(doseGeoPath))
            {
                var doseGeo = ReadGeometry(ReadJson(doseGeoPath));
                string scalingPath = Path.Combine(dir, "dose_scaling.json");
                var scaling = File.Exists(scalingPath)
             ? ReadScaling(ReadJson(scalingPath))
                 : new DoseScaling();
                var doseVoxels = ReadVoxelSlices("dose_voxels_", dir, doseGeo.ZSize);

                snap.Dose = new DoseVolumeData
                {
                    Geometry = doseGeo,
                    Voxels = doseVoxels,
                    Scaling = scaling
                };
            }

            string structPath = Path.Combine(dir, "structures.json");
            if (File.Exists(structPath))
                snap.Structures = ReadStructures(ReadJson(structPath));

            string dvhPath = Path.Combine(dir, "dvh_curves.json");
            if (File.Exists(dvhPath))
                snap.DvhCurves = ReadDvhCurves(ReadJson(dvhPath));

            string regPath = Path.Combine(dir, "registrations.json");
            if (File.Exists(regPath))
                snap.Registrations = ReadRegistrations(ReadJson(regPath));

            string coursesPath = Path.Combine(dir, "courses.json");
            if (File.Exists(coursesPath))
                snap.AllCourses = ReadCourses(ReadJson(coursesPath));

            string renderPath = Path.Combine(dir, "render_settings.json");
            if (File.Exists(renderPath))
                snap.RenderSettings = ReadRenderSettings(ReadJson(renderPath));

            return snap;
        }

        // ========================================================
        // WRITE -- Binary GZip volumes (for full end-to-end snapshots)
        // ========================================================

        /// <summary>
        /// Serializes the entire snapshot using GZip-compressed binary for volume data.
        /// CT and dose voxels are stored as .bin.gz files instead of per-slice JSON.
        /// </summary>
        public static string WriteBinary(ClinicalSnapshot snap, string outputDir)
        {
            Directory.CreateDirectory(outputDir);
            var sb = new StringBuilder();

            WriteBinaryMeta(snap, outputDir);
            sb.AppendLine("wrote snapshot_meta.json (binary v3.0)");

            WritePatient(snap.Patient, outputDir);
            sb.AppendLine("wrote patient.json");

            if (snap.ActivePlan != null)
            {
                WritePlan(snap.ActivePlan, outputDir);
                sb.AppendLine("wrote plan.json");
            }

            if (snap.CtImage != null)
            {
                WriteGeometry(snap.CtImage.Geometry, Path.Combine(outputDir, "ct_geometry.json"));
                WriteJson(Path.Combine(outputDir, "ct_huoffset.json"),
                         $"{{\"huOffset\":{snap.CtImage.HuOffset}}}");
                WriteVolumeBinary(snap.CtImage.Voxels, Path.Combine(outputDir, "ct_volume.bin.gz"));
                var g = snap.CtImage.Geometry;
                sb.AppendLine($"wrote ct_volume.bin.gz ({g.XSize}x{g.YSize}x{g.ZSize})");
            }

            if (snap.Dose != null)
            {
                WriteGeometry(snap.Dose.Geometry, Path.Combine(outputDir, "dose_geometry.json"));
                WriteScaling(snap.Dose.Scaling, outputDir);
                WriteVolumeBinary(snap.Dose.Voxels, Path.Combine(outputDir, "dose_volume.bin.gz"));
                var g = snap.Dose.Geometry;
                sb.AppendLine($"wrote dose_volume.bin.gz ({g.XSize}x{g.YSize}x{g.ZSize})");
            }

            WriteStructures(snap.Structures, outputDir);
            sb.AppendLine($"wrote structures.json ({snap.Structures?.Count ?? 0})");

            WriteDvhCurves(snap.DvhCurves, outputDir);
            sb.AppendLine($"wrote dvh_curves.json ({snap.DvhCurves?.Count ?? 0})");

            WriteRegistrations(snap.Registrations, outputDir);
            sb.AppendLine($"wrote registrations.json ({snap.Registrations?.Count ?? 0})");

            WriteCourses(snap.AllCourses, outputDir);
            sb.AppendLine($"wrote courses.json ({snap.AllCourses?.Count ?? 0})");

            if (snap.RenderSettings != null)
            {
                WriteRenderSettings(snap.RenderSettings, outputDir);
                sb.AppendLine($"wrote render_settings.json ({snap.RenderSettings.ReferenceDosePoints?.Count ?? 0} ref points)");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Reads a snapshot from a directory, auto-detecting binary (v3.0) vs JSON+RLE (v2.0) format.
        /// </summary>
        public static ClinicalSnapshot ReadAuto(string dir)
        {
            string metaPath = Path.Combine(dir, MetaFileName);
            if (!File.Exists(metaPath))
                throw new FileNotFoundException($"snapshot_meta.json not found in: {dir}");

            string metaJson = ReadJson(metaPath);
            string version = ExtractString(metaJson, "formatVersion");

            if (version == BinaryFormatVersion)
                return ReadBinary(dir);
            if (version == FormatVersion)
                return Read(dir);

            throw new InvalidOperationException(
                $"Unsupported snapshot format version '{version}'. Expected '{FormatVersion}' or '{BinaryFormatVersion}'.");
        }

        /// <summary>
        /// Reads a binary-format (v3.0) snapshot directory.
        /// </summary>
        public static ClinicalSnapshot ReadBinary(string dir)
        {
            string metaPath = Path.Combine(dir, MetaFileName);
            if (!File.Exists(metaPath))
                throw new FileNotFoundException($"snapshot_meta.json not found in: {dir}");

            var snap = new ClinicalSnapshot();

            string patientPath = Path.Combine(dir, "patient.json");
            if (File.Exists(patientPath))
                snap.Patient = ReadPatient(ReadJson(patientPath));

            string planPath = Path.Combine(dir, "plan.json");
            if (File.Exists(planPath))
                snap.ActivePlan = ReadPlan(ReadJson(planPath));

            string ctGeoPath = Path.Combine(dir, "ct_geometry.json");
            string ctBinPath = Path.Combine(dir, "ct_volume.bin.gz");
            if (File.Exists(ctGeoPath) && File.Exists(ctBinPath))
            {
                var ctGeo = ReadGeometry(ReadJson(ctGeoPath));
                int huOffset = 0;
                string huPath = Path.Combine(dir, "ct_huoffset.json");
                if (File.Exists(huPath))
                    huOffset = (int)ExtractDouble(ReadJson(huPath), "huOffset");

                snap.CtImage = new VolumeData
                {
                    Geometry = ctGeo,
                    Voxels = ReadVolumeBinary(ctBinPath),
                    HuOffset = huOffset
                };
            }

            string doseGeoPath = Path.Combine(dir, "dose_geometry.json");
            string doseBinPath = Path.Combine(dir, "dose_volume.bin.gz");
            if (File.Exists(doseGeoPath) && File.Exists(doseBinPath))
            {
                var doseGeo = ReadGeometry(ReadJson(doseGeoPath));
                string scalingPath = Path.Combine(dir, "dose_scaling.json");
                var scaling = File.Exists(scalingPath)
                    ? ReadScaling(ReadJson(scalingPath))
                : new DoseScaling();

                snap.Dose = new DoseVolumeData
                {
                    Geometry = doseGeo,
                    Voxels = ReadVolumeBinary(doseBinPath),
                    Scaling = scaling
                };
            }

            string structPath = Path.Combine(dir, "structures.json");
            if (File.Exists(structPath))
                snap.Structures = ReadStructures(ReadJson(structPath));

            string dvhPath = Path.Combine(dir, "dvh_curves.json");
            if (File.Exists(dvhPath))
                snap.DvhCurves = ReadDvhCurves(ReadJson(dvhPath));

            string regPath = Path.Combine(dir, "registrations.json");
            if (File.Exists(regPath))
                snap.Registrations = ReadRegistrations(ReadJson(regPath));

            string coursesPath = Path.Combine(dir, "courses.json");
            if (File.Exists(coursesPath))
                snap.AllCourses = ReadCourses(ReadJson(coursesPath));

            string renderPath = Path.Combine(dir, "render_settings.json");
            if (File.Exists(renderPath))
                snap.RenderSettings = ReadRenderSettings(ReadJson(renderPath));

            return snap;
        }

        // ========================================================
        // BINARY VOLUME I/O
        // ========================================================

        /// <summary>
        /// Writes a 3D voxel volume as a GZip-compressed binary file.
        /// Header: xSize(int32), ySize(int32), zSize(int32)
        /// Body: z outer, y middle, x inner (row-major per slice).
        /// </summary>
        public static void WriteVolumeBinary(int[][,] voxels, string path)
        {
            if (voxels == null || voxels.Length == 0) return;

            int xSize = voxels[0].GetLength(0);
            int ySize = voxels[0].GetLength(1);
            int zSize = voxels.Length;

            using (var fs = File.Create(path))
            using (var gz = new GZipStream(fs, CompressionLevel.Fastest))
            using (var bw = new BinaryWriter(gz))
            {
                bw.Write(xSize);
                bw.Write(ySize);
                bw.Write(zSize);

                for (int z = 0; z < zSize; z++)
                {
                    var slice = voxels[z];
                    for (int y = 0; y < ySize; y++)
                        for (int x = 0; x < xSize; x++)
                            bw.Write(slice[x, y]);
                }
            }
        }

        /// <summary>
        /// Reads a 3D voxel volume from a GZip-compressed binary file.
        /// </summary>
        public static int[][,] ReadVolumeBinary(string path)
        {
            using (var fs = File.OpenRead(path))
            using (var gz = new GZipStream(fs, CompressionMode.Decompress))
            using (var br = new BinaryReader(gz))
            {
                int xSize = br.ReadInt32();
                int ySize = br.ReadInt32();
                int zSize = br.ReadInt32();

                var voxels = new int[zSize][,];
                for (int z = 0; z < zSize; z++)
                {
                    voxels[z] = new int[xSize, ySize];
                    for (int y = 0; y < ySize; y++)
                        for (int x = 0; x < xSize; x++)
                            voxels[z][x, y] = br.ReadInt32();
                }
                return voxels;
            }
        }

        private static void WriteBinaryMeta(ClinicalSnapshot snap, string dir)
        {
            var w = new JW();
            w.OB();
            w.S("formatVersion", BinaryFormatVersion);
            w.S("exportedAt", DateTime.Now.ToString("o"));
            w.S("patientId", snap.Patient?.Id ?? "");
            w.S("planId", snap.ActivePlan?.Id ?? "");
            w.S("courseId", snap.ActivePlan?.CourseId ?? "");
            w.EB();
            WriteJson(Path.Combine(dir, MetaFileName), w.Build());
        }

        // ========================================================
        // WRITE HELPERS
        // ========================================================

        private static void WriteMeta(ClinicalSnapshot snap, string dir)
        {
            var w = new JW();
            w.OB();
            w.S("formatVersion", FormatVersion);
            w.S("exportedAt", DateTime.Now.ToString("o"));
            w.S("patientId", snap.Patient?.Id ?? "");
            w.S("planId", snap.ActivePlan?.Id ?? "");
            w.S("courseId", snap.ActivePlan?.CourseId ?? "");
            w.EB();
            WriteJson(Path.Combine(dir, MetaFileName), w.Build());
        }

        private static void WritePatient(PatientData p, string dir)
        {
            if (p == null) return;
            var w = new JW();
            w.OB(); w.S("id", p.Id); w.S("lastName", p.LastName); w.S("firstName", p.FirstName); w.EB();
            WriteJson(Path.Combine(dir, "patient.json"), w.Build());
        }

        private static void WritePlan(PlanData p, string dir)
        {
            var w = new JW();
            w.OB();
            w.S("id", p.Id);
            w.S("courseId", p.CourseId);
            w.N("totalDoseGy", p.TotalDoseGy);
            w.N("numberOfFractions", p.NumberOfFractions);
            w.N("planNormalization", p.PlanNormalization);
            w.EB();
            WriteJson(Path.Combine(dir, "plan.json"), w.Build());
        }

        private static void WriteGeometry(VolumeGeometry g, string path)
        {
            var w = new JW();
            w.OB();
            w.N("xSize", g.XSize); w.N("ySize", g.YSize); w.N("zSize", g.ZSize);
            w.N("xRes", g.XRes); w.N("yRes", g.YRes); w.N("zRes", g.ZRes);
            w.NA("origin", new[] { g.Origin.X, g.Origin.Y, g.Origin.Z });
            w.NA("xDirection", new[] { g.XDirection.X, g.XDirection.Y, g.XDirection.Z });
            w.NA("yDirection", new[] { g.YDirection.X, g.YDirection.Y, g.YDirection.Z });
            w.NA("zDirection", new[] { g.ZDirection.X, g.ZDirection.Y, g.ZDirection.Z });
            w.S("frameOfReference", g.FrameOfReference ?? "");
            w.S("id", g.Id ?? "");
            w.EB();
            WriteJson(path, w.Build());
        }

        private static void WriteScaling(DoseScaling s, string dir)
        {
            var w = new JW();
            w.OB();
            w.N("rawScale", s.RawScale);
            w.N("rawOffset", s.RawOffset);
            w.N("unitToGy", s.UnitToGy);
            w.S("doseUnit", s.DoseUnit ?? "Gy");
            w.EB();
            WriteJson(Path.Combine(dir, "dose_scaling.json"), w.Build());
        }

        /// <summary>
        /// Writes voxel slices to per-slice JSON files using RLE compression.
        /// Returns the number of slices written.
        /// </summary>
        private static int WriteVoxelSlices(int[][,] voxels, string prefix, string dir)
        {
            if (voxels == null) return 0;
            for (int z = 0; z < voxels.Length; z++)
            {
                string path = Path.Combine(dir, $"{prefix}{z:D4}.json");
                WriteJson(path, SerializeSliceRle(voxels[z]));
            }
            return voxels.Length;
        }

        private static void WriteStructures(List<StructureData> structures, string dir)
        {
            var sb = new StringBuilder();
            sb.Append("[");
            bool first = true;
            foreach (var s in structures ?? new List<StructureData>())
            {
                if (!first) sb.Append(",");
                first = false;
                sb.Append("\n{");
                sb.Append($"\"id\":{Str(s.Id)},");
                sb.Append($"\"dicomType\":{Str(s.DicomType)},");
                sb.Append($"\"colorR\":{s.ColorR},\"colorG\":{s.ColorG},\"colorB\":{s.ColorB},\"colorA\":{s.ColorA},");
                sb.Append($"\"isEmpty\":{Bool(s.IsEmpty)},\"hasMesh\":{Bool(s.HasMesh)},");

                // Contours: {"sliceIndex": [[point,...],...], ...}
                sb.Append("\"contoursBySlice\":{");
                bool firstSlice = true;
                foreach (var kv in s.ContoursBySlice ?? new Dictionary<int, List<double[][]>>())
                {
                    if (!firstSlice) sb.Append(",");
                    firstSlice = false;
                    sb.Append($"\"{kv.Key}\":[");
                    bool firstPoly = true;
                    foreach (var polygon in kv.Value)
                    {
                        if (!firstPoly) sb.Append(",");
                        firstPoly = false;
                        sb.Append("[");
                        for (int i = 0; i < polygon.Length; i++)
                        {
                            if (i > 0) sb.Append(",");
                            var pt = polygon[i];
                            sb.Append($"[{F(pt[0])},{F(pt[1])},{F(pt[2])}]");
                        }
                        sb.Append("]");
                    }
                    sb.Append("]");
                }
                sb.Append("}}");
            }
            sb.Append("\n]");
            WriteJson(Path.Combine(dir, "structures.json"), sb.ToString());
        }

        private static void WriteDvhCurves(List<DvhCurveData> curves, string dir)
        {
            var sb = new StringBuilder();
            sb.Append("[");
            bool first = true;
            foreach (var d in curves ?? new List<DvhCurveData>())
            {
                if (!first) sb.Append(",");
                first = false;
                sb.Append($"\n{{\"structureId\":{Str(d.StructureId)},");
                sb.Append($"\"planId\":{Str(d.PlanId)},");
                sb.Append($"\"dmaxGy\":{F(d.DMaxGy)},\"dmeanGy\":{F(d.DMeanGy)},\"dminGy\":{F(d.DMinGy)},");
                sb.Append($"\"volumeCc\":{F(d.VolumeCc)},");
                sb.Append("\"curve\":[");
                if (d.Curve != null)
                {
                    for (int i = 0; i < d.Curve.Length; i++)
                    {
                        if (i > 0) sb.Append(",");
                        sb.Append($"[{F(d.Curve[i][0])},{F(d.Curve[i][1])}]");
                    }
                }
                sb.Append("]}");
            }
            sb.Append("\n]");
            WriteJson(Path.Combine(dir, "dvh_curves.json"), sb.ToString());
        }

        private static void WriteRegistrations(List<RegistrationData> regs, string dir)
        {
            var sb = new StringBuilder();
            sb.Append("[");
            bool first = true;
            foreach (var r in regs ?? new List<RegistrationData>())
            {
                if (!first) sb.Append(",");
                first = false;
                sb.Append($"\n{{\"id\":{Str(r.Id)},");
                sb.Append($"\"sourceFOR\":{Str(r.SourceFOR)},");
                sb.Append($"\"registeredFOR\":{Str(r.RegisteredFOR)},");
                sb.Append($"\"date\":{Str(r.CreationDateTime?.ToString("o") ?? "")},");
                sb.Append("\"matrix\":[");
                if (r.Matrix != null)
                    for (int i = 0; i < r.Matrix.Length; i++)
                    {
                        if (i > 0) sb.Append(",");
                        sb.Append(F(r.Matrix[i]));
                    }
                sb.Append("]}");
            }
            sb.Append("\n]");
            WriteJson(Path.Combine(dir, "registrations.json"), sb.ToString());
        }

        private static void WriteCourses(List<CourseData> courses, string dir)
        {
            var sb = new StringBuilder();
            sb.Append("[");
            bool first = true;
            foreach (var c in courses ?? new List<CourseData>())
            {
                if (!first) sb.Append(",");
                first = false;
                sb.Append($"\n{{\"id\":{Str(c.Id)},\"plans\":[");
                bool firstPlan = true;
                foreach (var p in c.Plans ?? new List<PlanSummaryData>())
                {
                    if (!firstPlan) sb.Append(",");
                    firstPlan = false;
                    sb.Append($"{{\"planId\":{Str(p.PlanId)},");
                    sb.Append($"\"courseId\":{Str(p.CourseId)},");
                    sb.Append($"\"imageId\":{Str(p.ImageId)},");
                    sb.Append($"\"imageFOR\":{Str(p.ImageFOR)},");
                    sb.Append($"\"totalDoseGy\":{F(p.TotalDoseGy)},");
                    sb.Append($"\"numberOfFractions\":{p.NumberOfFractions},");
                    sb.Append($"\"planNormalization\":{F(p.PlanNormalization)},");
                    sb.Append($"\"hasDose\":{Bool(p.HasDose)}}}");
                }
                sb.Append("]}");
            }
            sb.Append("\n]");
            WriteJson(Path.Combine(dir, "courses.json"), sb.ToString());
        }

        // ========================================================
        // RENDER SETTINGS I/O
        // ========================================================

        private static void WriteRenderSettings(RenderSettings settings, string dir)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"windowLevel\":{F(settings.WindowLevel)},");
            sb.Append($"\"windowWidth\":{F(settings.WindowWidth)}");

            if (settings.IsodoseLevels != null && settings.IsodoseLevels.Count > 0)
            {
                sb.Append(",\"isodoseLevels\":[");
                for (int i = 0; i < settings.IsodoseLevels.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    var l = settings.IsodoseLevels[i];
                    sb.Append($"{{\"fraction\":{F(l.Fraction)},\"absoluteDoseGy\":{F(l.AbsoluteDoseGy)},");
                    sb.Append($"\"color\":{l.Color},\"isVisible\":{Bool(l.IsVisible)}}}");
                }
                sb.Append("]");
            }

            if (settings.ReferenceDosePoints != null && settings.ReferenceDosePoints.Count > 0)
            {
                sb.Append(",\"referenceDosePoints\":[");
                for (int i = 0; i < settings.ReferenceDosePoints.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    var p = settings.ReferenceDosePoints[i];
                    sb.Append($"{{\"ctPixelX\":{p.CtPixelX},\"ctPixelY\":{p.CtPixelY},");
                    sb.Append($"\"ctSlice\":{p.CtSlice},\"expectedDoseGy\":{F(p.ExpectedDoseGy)},");
                    sb.Append($"\"isInsideDoseGrid\":{Bool(p.IsInsideDoseGrid)}}}");
                }
                sb.Append("]");
            }

            sb.Append("}");
            WriteJson(Path.Combine(dir, "render_settings.json"), sb.ToString());
        }

        private static RenderSettings ReadRenderSettings(string json)
        {
            var rs = new RenderSettings
            {
                WindowLevel = ExtractDouble(json, "windowLevel"),
                WindowWidth = ExtractDouble(json, "windowWidth")
            };

            // Parse isodose levels
            int isoStart = json.IndexOf("\"isodoseLevels\":[", StringComparison.Ordinal);
            if (isoStart >= 0)
            {
                int arrStart = json.IndexOf('[', isoStart + 16);
                int arrEnd = FindMatchingBracket(json, arrStart, '[', ']');
                if (arrEnd > arrStart)
                {
                    rs.IsodoseLevels = new List<IsodoseLevelSetting>();
                    var items = SplitTopLevelObjects(json.Substring(arrStart, arrEnd - arrStart + 1));
                    foreach (var item in items)
                    {
                        rs.IsodoseLevels.Add(new IsodoseLevelSetting
                        {
                            Fraction = ExtractDouble(item, "fraction"),
                            AbsoluteDoseGy = ExtractDouble(item, "absoluteDoseGy"),
                            Color = (uint)ExtractDouble(item, "color"),
                            IsVisible = ExtractBool(item, "isVisible")
                        });
                    }
                }
            }

            // Parse reference dose points
            int refStart = json.IndexOf("\"referenceDosePoints\":[", StringComparison.Ordinal);
            if (refStart >= 0)
            {
                int arrStart = json.IndexOf('[', refStart + 22);
                int arrEnd = FindMatchingBracket(json, arrStart, '[', ']');
                if (arrEnd > arrStart)
                {
                    rs.ReferenceDosePoints = new List<ReferenceDosePoint>();
                    var items = SplitTopLevelObjects(json.Substring(arrStart, arrEnd - arrStart + 1));
                    foreach (var item in items)
                    {
                        rs.ReferenceDosePoints.Add(new ReferenceDosePoint
                        {
                            CtPixelX = (int)ExtractDouble(item, "ctPixelX"),
                            CtPixelY = (int)ExtractDouble(item, "ctPixelY"),
                            CtSlice = (int)ExtractDouble(item, "ctSlice"),
                            ExpectedDoseGy = ExtractDouble(item, "expectedDoseGy"),
                            IsInsideDoseGrid = ExtractBool(item, "isInsideDoseGrid")
                        });
                    }
                }
            }

            return rs;
        }

        // ========================================================
        // RLE VOXEL SERIALIZATION
        // ========================================================

        /// <summary>
        /// Serializes a 2D voxel slice with run-length encoding.
        /// Format: {"w":W,"h":H,"rle":[[value,count],...]}
        /// Iterates in row-major order (y outer, x inner) matching ESAPI GetVoxels convention.
        /// </summary>
        private static string SerializeSliceRle(int[,] slice)
        {
            if (slice == null) return "{\"w\":0,\"h\":0,\"rle\":[]}";
            int w = slice.GetLength(0);
            int h = slice.GetLength(1);

            var sb = new StringBuilder();
            sb.Append($"{{\"w\":{w},\"h\":{h},\"rle\":[");

            int? runVal = null;
            int runLen = 0;
            bool first = true;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int v = slice[x, y];
                    if (runVal == null)
                    {
                        runVal = v; runLen = 1;
                    }
                    else if (v == runVal)
                    {
                        runLen++;
                    }
                    else
                    {
                        if (!first) sb.Append(",");
                        first = false;
                        sb.Append($"[{runVal},{runLen}]");
                        runVal = v; runLen = 1;
                    }
                }
            }
            if (runVal != null)
            {
                if (!first) sb.Append(",");
                sb.Append($"[{runVal},{runLen}]");
            }

            sb.Append("]}");
            return sb.ToString();
        }

        // ========================================================
        // READ HELPERS
        // ========================================================

        private static PatientData ReadPatient(string json) => new PatientData
        {
            Id = ExtractString(json, "id"),
            LastName = ExtractString(json, "lastName"),
            FirstName = ExtractString(json, "firstName")
        };

        private static PlanData ReadPlan(string json) => new PlanData
        {
            Id = ExtractString(json, "id"),
            CourseId = ExtractString(json, "courseId"),
            TotalDoseGy = ExtractDouble(json, "totalDoseGy"),
            NumberOfFractions = (int)ExtractDouble(json, "numberOfFractions"),
            PlanNormalization = ExtractDouble(json, "planNormalization")
        };

        private static VolumeGeometry ReadGeometry(string json) => new VolumeGeometry
        {
            XSize = (int)ExtractDouble(json, "xSize"),
            YSize = (int)ExtractDouble(json, "ySize"),
            ZSize = (int)ExtractDouble(json, "zSize"),
            XRes = ExtractDouble(json, "xRes"),
            YRes = ExtractDouble(json, "yRes"),
            ZRes = ExtractDouble(json, "zRes"),
            Origin = Vec3.FromArray(ExtractDoubleArray(json, "origin")),
            XDirection = Vec3.FromArray(ExtractDoubleArray(json, "xDirection")),
            YDirection = Vec3.FromArray(ExtractDoubleArray(json, "yDirection")),
            ZDirection = Vec3.FromArray(ExtractDoubleArray(json, "zDirection")),
            FrameOfReference = ExtractString(json, "frameOfReference"),
            Id = ExtractString(json, "id")
        };

        private static DoseScaling ReadScaling(string json) => new DoseScaling
        {
            RawScale = ExtractDouble(json, "rawScale"),
            RawOffset = ExtractDouble(json, "rawOffset"),
            UnitToGy = ExtractDouble(json, "unitToGy"),
            DoseUnit = ExtractString(json, "doseUnit")
        };

        private static int[][,] ReadVoxelSlices(string prefix, string dir, int zSize)
        {
            var voxels = new int[zSize][,];
            for (int z = 0; z < zSize; z++)
            {
                string path = Path.Combine(dir, $"{prefix}{z:D4}.json");
                if (!File.Exists(path))
                    throw new FileNotFoundException($"Missing voxel slice file: {path}");
                voxels[z] = DeserializeSliceRle(ReadJson(path));
            }
            return voxels;
        }

        private static int[,] DeserializeSliceRle(string json)
        {
            int w = (int)ExtractDouble(json, "w");
            int h = (int)ExtractDouble(json, "h");
            var grid = new int[w, h];

            // Find "rle": [ ... ]
            int rleStart = json.IndexOf("\"rle\":", StringComparison.Ordinal);
            if (rleStart < 0) return grid;
            int arrayStart = json.IndexOf('[', rleStart + 6);
            if (arrayStart < 0) return grid;
            int arrayEnd = json.LastIndexOf(']');
            if (arrayEnd <= arrayStart) return grid;

            string rleContent = json.Substring(arrayStart + 1, arrayEnd - arrayStart - 1);
            // Parse pairs [value,count]
            int x = 0, y = 0;
            int pos = 0;
            while (pos < rleContent.Length)
            {
                int lb = rleContent.IndexOf('[', pos);
                if (lb < 0) break;
                int rb = rleContent.IndexOf(']', lb);
                if (rb < 0) break;
                string pair = rleContent.Substring(lb + 1, rb - lb - 1);
                int comma = pair.IndexOf(',');
                if (comma < 0) { pos = rb + 1; continue; }
                int value = int.Parse(pair.Substring(0, comma).Trim(), INV);
                int count = int.Parse(pair.Substring(comma + 1).Trim(), INV);
                for (int i = 0; i < count; i++)
                {
                    if (x < w && y < h) grid[x, y] = value;
                    x++;
                    if (x >= w) { x = 0; y++; }
                }
                pos = rb + 1;
            }
            return grid;
        }

        private static List<StructureData> ReadStructures(string json)
        {
            var result = new List<StructureData>();
            // Each structure starts with a '{' at top-level array element
            var items = SplitTopLevelObjects(json);
            foreach (var item in items)
            {
                var sd = new StructureData
                {
                    Id = ExtractString(item, "id"),
                    DicomType = ExtractString(item, "dicomType"),
                    ColorR = (byte)ExtractDouble(item, "colorR"),
                    ColorG = (byte)ExtractDouble(item, "colorG"),
                    ColorB = (byte)ExtractDouble(item, "colorB"),
                    ColorA = (byte)ExtractDouble(item, "colorA"),
                    IsEmpty = ExtractBool(item, "isEmpty"),
                    HasMesh = ExtractBool(item, "hasMesh"),
                    ContoursBySlice = ReadContoursBySlice(item)
                };
                result.Add(sd);
            }
            return result;
        }

        private static Dictionary<int, List<double[][]>> ReadContoursBySlice(string structJson)
        {
            var result = new Dictionary<int, List<double[][]>>();
            int start = structJson.IndexOf("\"contoursBySlice\":{", StringComparison.Ordinal);
            if (start < 0) return result;
            start += "\"contoursBySlice\":{".Length - 1; // position at '{'

            // Extract the contoursBySlice object body
            string? body = ExtractObjectBody(structJson, start);
            if (string.IsNullOrEmpty(body)) return result;

            // Parse: "sliceIndex": [[points], ...]
            int pos = 0;
            while (pos < body!.Length)
            {
                int keyStart = body.IndexOf('"', pos);
                if (keyStart < 0) break;
                int keyEnd = body.IndexOf('"', keyStart + 1);
                if (keyEnd < 0) break;
                string sliceKey = body.Substring(keyStart + 1, keyEnd - keyStart - 1);
                if (!int.TryParse(sliceKey, out int sliceIndex)) { pos = keyEnd + 1; continue; }

                int arrStart = body.IndexOf('[', keyEnd);
                if (arrStart < 0) break;

                // Find matching ']' at top level of this array
                int arrEnd = FindMatchingBracket(body, arrStart, '[', ']');
                if (arrEnd < 0) break;

                string polygonsJson = body.Substring(arrStart, arrEnd - arrStart + 1);
                var polygons = ReadPolygonArray(polygonsJson);
                if (polygons.Count > 0) result[sliceIndex] = polygons;
                pos = arrEnd + 1;
            }
            return result;
        }

        private static List<double[][]> ReadPolygonArray(string json)
        {
            var result = new List<double[][]>();
            // json is [[points...],[points...],...]
            // outer array contains inner arrays (polygons), each of which contains point arrays
            int pos = 1; // skip outer '['
            while (pos < json.Length - 1)
            {
                int lb = json.IndexOf('[', pos);
                if (lb < 0) break;
                int rb = FindMatchingBracket(json, lb, '[', ']');
                if (rb < 0) break;
                string polyJson = json.Substring(lb, rb - lb + 1);
                var points = ReadPointArray(polyJson);
                if (points.Length > 0) result.Add(points);
                pos = rb + 1;
            }
            return result;
        }

        private static double[][] ReadPointArray(string json)
        {
            // json is [[x,y,z],[x,y,z],...]
            var pts = new List<double[]>();
            int pos = 1;
            while (pos < json.Length - 1)
            {
                int lb = json.IndexOf('[', pos);
                if (lb < 0) break;
                int rb = json.IndexOf(']', lb);
                if (rb < 0) break;
                string pt = json.Substring(lb + 1, rb - lb - 1);
                var coords = pt.Split(',');
                if (coords.Length >= 3)
                {
                    pts.Add(new[]
                        {
     double.Parse(coords[0].Trim(), INV),
               double.Parse(coords[1].Trim(), INV),
     double.Parse(coords[2].Trim(), INV)
       });
                }
                pos = rb + 1;
            }
            return pts.ToArray();
        }

        private static List<DvhCurveData> ReadDvhCurves(string json)
        {
            var result = new List<DvhCurveData>();
            var items = SplitTopLevelObjects(json);
            foreach (var item in items)
            {
                // Parse curve array [[d,v],...]
                int curveStart = item.IndexOf("\"curve\":[", StringComparison.Ordinal);
                double[][]? curve = null;
                if (curveStart >= 0)
                {
                    int arrStart = item.IndexOf('[', curveStart + 8);
                    int arrEnd = FindMatchingBracket(item, arrStart, '[', ']');
                    if (arrEnd > arrStart)
                        curve = ParseCurveArray(item.Substring(arrStart, arrEnd - arrStart + 1));
                }

                result.Add(new DvhCurveData
                {
                    StructureId = ExtractString(item, "structureId"),
                    PlanId = ExtractString(item, "planId"),
                    DMaxGy = ExtractDouble(item, "dmaxGy"),
                    DMeanGy = ExtractDouble(item, "dmeanGy"),
                    DMinGy = ExtractDouble(item, "dminGy"),
                    VolumeCc = ExtractDouble(item, "volumeCc"),
                    Curve = curve ?? new double[0][]
                });
            }
            return result;
        }

        private static double[][] ParseCurveArray(string json)
        {
            var pts = new List<double[]>();
            int pos = 1;
            while (pos < json.Length - 1)
            {
                int lb = json.IndexOf('[', pos);
                if (lb < 0) break;
                int rb = json.IndexOf(']', lb);
                if (rb < 0) break;
                var parts = json.Substring(lb + 1, rb - lb - 1).Split(',');
                if (parts.Length >= 2)
                    pts.Add(new[] { double.Parse(parts[0].Trim(), INV), double.Parse(parts[1].Trim(), INV) });
                pos = rb + 1;
            }
            return pts.ToArray();
        }

        private static List<RegistrationData> ReadRegistrations(string json)
        {
            var result = new List<RegistrationData>();
            var items = SplitTopLevelObjects(json);
            foreach (var item in items)
            {
                double[]? matrix = null;
                int mStart = item.IndexOf("\"matrix\":[", StringComparison.Ordinal);
                if (mStart >= 0)
                {
                    int arrStart = item.IndexOf('[', mStart + 9);
                    int arrEnd = FindMatchingBracket(item, arrStart, '[', ']');
                    if (arrEnd > arrStart)
                    {
                        var parts = item.Substring(arrStart + 1, arrEnd - arrStart - 1).Split(',');
                        matrix = new double[parts.Length];
                        for (int i = 0; i < parts.Length; i++)
                            double.TryParse(parts[i].Trim(), NumberStyles.Any, INV, out matrix[i]);
                    }
                }
                string dateStr = ExtractString(item, "date");
                DateTime? dt = null;
                if (!string.IsNullOrEmpty(dateStr) && DateTime.TryParse(dateStr, null,
            DateTimeStyles.RoundtripKind, out DateTime parsed))
                    dt = parsed;

                result.Add(new RegistrationData
                {
                    Id = ExtractString(item, "id"),
                    SourceFOR = ExtractString(item, "sourceFOR"),
                    RegisteredFOR = ExtractString(item, "registeredFOR"),
                    CreationDateTime = dt,
                    Matrix = matrix!
                });
            }
            return result;
        }

        private static List<CourseData> ReadCourses(string json)
        {
            var result = new List<CourseData>();
            var items = SplitTopLevelObjects(json);
            foreach (var item in items)
            {
                var cd = new CourseData { Id = ExtractString(item, "id") };

                int plansStart = item.IndexOf("\"plans\":[", StringComparison.Ordinal);
                if (plansStart >= 0)
                {
                    int arrStart = item.IndexOf('[', plansStart + 8);
                    int arrEnd = FindMatchingBracket(item, arrStart, '[', ']');
                    if (arrEnd > arrStart)
                    {
                        string plansJson = item.Substring(arrStart, arrEnd - arrStart + 1);
                        var planItems = SplitTopLevelObjects(plansJson);
                        foreach (var pi in planItems)
                        {
                            cd.Plans.Add(new PlanSummaryData
                            {
                                PlanId = ExtractString(pi, "planId"),
                                CourseId = ExtractString(pi, "courseId"),
                                ImageId = ExtractString(pi, "imageId"),
                                ImageFOR = ExtractString(pi, "imageFOR"),
                                TotalDoseGy = ExtractDouble(pi, "totalDoseGy"),
                                NumberOfFractions = (int)ExtractDouble(pi, "numberOfFractions"),
                                PlanNormalization = ExtractDouble(pi, "planNormalization"),
                                HasDose = ExtractBool(pi, "hasDose")
                            });
                        }
                    }
                }
                result.Add(cd);
            }
            return result;
        }

        // ========================================================
        // MINI JSON PRIMITIVES
        // ========================================================

        private static string ExtractString(string json, string key)
        {
            string search = $"\"{key}\":\"";
            int start = json.IndexOf(search, StringComparison.Ordinal);
            if (start < 0) return "";
            start += search.Length;
            int end = start;
            while (end < json.Length)
            {
                if (json[end] == '"' && (end == 0 || json[end - 1] != '\\')) break;
                end++;
            }
            return json.Substring(start, end - start)
                .Replace("\\\"", "\"").Replace("\\\\", "\\");
        }

        private static double ExtractDouble(string json, string key)
        {
            string search = $"\"{key}\":";
            int start = json.IndexOf(search, StringComparison.Ordinal);
            if (start < 0) return 0;
            start += search.Length;
            while (start < json.Length && json[start] == ' ') start++;
            int end = start;
            while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '-'
                  || json[end] == '.' || json[end] == 'E' || json[end] == 'e'
                || json[end] == '+'))
                end++;
            if (end == start) return 0;
            double.TryParse(json.Substring(start, end - start), NumberStyles.Any, INV, out double v);
            return v;
        }

        private static bool ExtractBool(string json, string key)
        {
            string search = $"\"{key}\":";
            int start = json.IndexOf(search, StringComparison.Ordinal);
            if (start < 0) return false;
            start += search.Length;
            while (start < json.Length && json[start] == ' ') start++;
            return start + 4 <= json.Length && json.Substring(start, 4) == "true";
        }

        private static double[] ExtractDoubleArray(string json, string key)
        {
            string search = $"\"{key}\":[";
            int start = json.IndexOf(search, StringComparison.Ordinal);
            if (start < 0) return new double[0];
            int arrStart = json.IndexOf('[', start + search.Length - 1);
            int arrEnd = json.IndexOf(']', arrStart);
            if (arrEnd < 0) return new double[0];
            var parts = json.Substring(arrStart + 1, arrEnd - arrStart - 1).Split(',');
            var result = new double[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                double.TryParse(parts[i].Trim(), NumberStyles.Any, INV, out result[i]);
            return result;
        }

        /// <summary>Splits a JSON array of objects into individual object strings.</summary>
        private static List<string> SplitTopLevelObjects(string json)
        {
            var result = new List<string>();
            int pos = 0;
            while (pos < json.Length)
            {
                int ob = json.IndexOf('{', pos);
                if (ob < 0) break;
                int cb = FindMatchingBracket(json, ob, '{', '}');
                if (cb < 0) break;
                result.Add(json.Substring(ob, cb - ob + 1));
                pos = cb + 1;
            }
            return result;
        }

        private static string? ExtractObjectBody(string json, int openBracePos)
        {
            int close = FindMatchingBracket(json, openBracePos, '{', '}');
            if (close < 0) return null;
            return json.Substring(openBracePos + 1, close - openBracePos - 1);
        }

        private static int FindMatchingBracket(string s, int open, char openChar, char closeChar)
        {
            int depth = 0;
            bool inString = false;
            for (int i = open; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '"' && (i == 0 || s[i - 1] != '\\')) inString = !inString;
                if (inString) continue;
                if (c == openChar) depth++;
                else if (c == closeChar) { depth--; if (depth == 0) return i; }
            }
            return -1;
        }

        private static void WriteJson(string path, string content) =>
               File.WriteAllText(path, content, UTF8NoBom);

        private static string ReadJson(string path)
        {
            string text = File.ReadAllText(path, Encoding.UTF8);
            if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);
            return text;
        }

        private static string F(double v) => double.IsNaN(v) ? "null" : v.ToString("G10", INV);
        private static string Bool(bool v) => v ? "true" : "false";
        private static string Str(string s) =>
  "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        // ========================================================
        // MINIMAL JSON WRITER HELPER (for structured output)
        // ========================================================

        private class JW
        {
            private readonly StringBuilder _sb = new StringBuilder();
            private bool _comma;
            public void OB() { _sb.Append("{"); _comma = false; }
            public void EB() { _sb.Append("}"); _comma = true; }
            public void S(string k, string v) { Sep(); _sb.Append($"\"{k}\":{Str(v)}"); _comma = true; }
            public void N(string k, double v) { Sep(); _sb.Append($"\"{k}\":{F(v)}"); _comma = true; }
            public void N(string k, int v) { Sep(); _sb.Append($"\"{k}\":{v}"); _comma = true; }
            public void NA(string k, double[] v)
            {
                Sep(); _sb.Append($"\"{k}\":[");
                for (int i = 0; i < v.Length; i++) { if (i > 0) _sb.Append(","); _sb.Append(F(v[i])); }
                _sb.Append("]"); _comma = true;
            }
            private void Sep() { if (_comma) _sb.Append(","); }
            public string Build() => _sb.ToString();
        }
    }
}
