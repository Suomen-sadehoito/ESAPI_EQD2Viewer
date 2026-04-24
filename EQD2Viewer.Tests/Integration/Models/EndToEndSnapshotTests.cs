using EQD2Viewer.App.UI.Rendering;
using EQD2Viewer.Services;
using EQD2Viewer.Core.Serialization;
using EQD2Viewer.Core.Models;
using EQD2Viewer.Core.Data;
using FluentAssertions;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EQD2Viewer.Tests.Integration.Models
{
    /// <summary>
    /// End-to-end tests that verify the full pipeline:
    ///   ClinicalSnapshot -> ImageRenderingService -> pixel/dose values
    ///
    /// Three tiers of tests:
    ///
    /// 1. SYNTHETIC PHANTOM TESTS (always run, no Eclipse needed)
    ///    Uses mathematically defined phantoms where expected values are known analytically.
    ///    Verifies: CT windowing math, dose scaling, coordinate mapping, EQD2 conversion.
    ///
    /// 2. SELF-CONSISTENCY TESTS (always run)
    ///    Verifies that serialize -> deserialize -> render produces identical pixels.
    ///    Catches serialization bugs and rendering non-determinism.
    ///
    /// 3. ECLIPSE REFERENCE TESTS (run when real snapshot is available)
    ///    Loads a full binary snapshot exported from Eclipse (via FixtureGenerator).
    ///    Compares app-computed dose values against Eclipse-computed reference points.
    ///    These tests are skipped when the snapshot directory does not exist.
    ///
    /// WHY NUMERICAL VERIFICATION OVER VISUAL (SSIM):
    ///   - Eclipse and this app use different rendering backends (DirectX vs WPF).
    ///   - Pixel-level comparison would fail on anti-aliasing, font rendering, color space.
    ///   - Numerical dose comparison (Gy at reference points) tests what actually matters:
    ///     does the clinical data pipeline produce correct dose values?
    ///   - DVH statistics comparison validates the full chain end-to-end.
    /// </summary>
    public class EndToEndSnapshotTests : IDisposable
    {
        private readonly string _tempDir;

        public EndToEndSnapshotTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(),
                "EQD2_E2E_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
            catch { /* best-effort cleanup */ }
        }

        // ========================================================
        // TIER 1: SYNTHETIC PHANTOM -- analytical verification
        // ========================================================

        /// <summary>
        /// Verifies dose value computation at specific pixel locations
        /// using a synthetic phantom where dose values are known analytically.
        /// 
        /// Phantom: uniform 2.0 Gy in central 50% region, 0 Gy outside.
        /// CT and dose grids are co-registered (same origin, same resolution).
        /// </summary>
        [Fact]
        public void SyntheticPhantom_DoseAtPixel_ShouldMatchAnalyticalValues()
        {
            var snap = CreateCoregisteredPhantom(size: 64, doseGyInRegion: 2.0);
            var svc = new ImageRenderingService();
            svc.Initialize(snap.CtImage.XSize, snap.CtImage.YSize);
            svc.PreloadData(snap.CtImage, snap.Dose);

            int midSlice = snap.CtImage.ZSize / 2;
            int center = snap.CtImage.XSize / 2;
            int quarter = snap.CtImage.XSize / 4;

            // Center (inside dose region): should be 2.0 Gy
            double doseCenter = svc.GetDoseAtPixel(midSlice, center, center, null);
            doseCenter.Should().BeApproximately(2.0, 0.01,
                "center pixel should be 2.0 Gy (inside uniform dose region)");

            // Just inside the boundary
            double doseInside = svc.GetDoseAtPixel(midSlice, quarter, quarter, null);
            doseInside.Should().BeApproximately(2.0, 0.01,
                "quarter point should be 2.0 Gy (at dose region boundary)");

            // Corner (outside dose region): should be 0 or NaN
            double doseCorner = svc.GetDoseAtPixel(midSlice, 0, 0, null);
            if (!double.IsNaN(doseCorner))
                doseCorner.Should().BeApproximately(0.0, 0.01,
                    "corner pixel should be 0 Gy (outside dose region)");

            svc.Dispose();
        }

        /// <summary>
        /// Verifies per-voxel EQD2 conversion using the linear-quadratic (LQ) model.
        ///
        /// Phantom voxel dose = 2.0 Gy, fractions = 25, alpha/beta = 3.0 Gy.
        /// Formula: EQD2 = D * (d + alpha/beta) / (2 + alpha/beta), where d = D / n.
        /// Expected: d = 2.0 / 25 = 0.08, EQD2 = 2.0 * (0.08 + 3.0) / (2.0 + 3.0) = 1.232 Gy.
        /// </summary>
        [Fact]
        public void SyntheticPhantom_EQD2AtPixel_ShouldMatchFormula()
        {
            var snap = CreateCoregisteredPhantom(size: 64, doseGyInRegion: 2.0);
            var svc = new ImageRenderingService();
            svc.Initialize(snap.CtImage.XSize, snap.CtImage.YSize);
            svc.PreloadData(snap.CtImage, snap.Dose);

            int midSlice = snap.CtImage.ZSize / 2;
            int center = snap.CtImage.XSize / 2;

            // Raw voxel dose = 2.0 Gy, converted via EQD2Calculator.ToEQD2(doseGy, n, alphaBeta)
            var eqd2Settings = new EQD2Settings
            {
                IsEnabled = true,
                NumberOfFractions = 25,
                AlphaBeta = 3.0
            };

            double eqd2Dose = svc.GetDoseAtPixel(midSlice, center, center, eqd2Settings);

            // ToEQD2(2.0, 25, 3.0):
            //   dPerFraction = 2.0 / 25 = 0.08
            //   EQD2 = 2.0 * (0.08 + 3.0) / (2.0 + 3.0) = 2.0 * 3.08 / 5.0 = 1.232
            double dPerFraction = 2.0 / 25.0;
            double expectedEqd2 = 2.0 * (dPerFraction + 3.0) / (2.0 + 3.0);

            eqd2Dose.Should().BeApproximately(expectedEqd2, 0.001,
                "EQD2 dose should match the LQ model formula");

            svc.Dispose();
        }

        // ========================================================
        // TIER 2: SELF-CONSISTENCY -- serialize -> render round-trip
        // ========================================================

        /// <summary>
        /// Verifies that dose values survive the full serialize -> deserialize pipeline.
        /// Renders dose from original and from round-tripped snapshot, compares pixel-by-pixel.
        /// </summary>
        [Fact]
        public void BinaryRoundTrip_DoseValues_ShouldBeIdentical()
        {
            var original = CreateCoregisteredPhantom(size: 32, doseGyInRegion: 1.5);
            string dir = Path.Combine(_tempDir, "dose_roundtrip");

            SnapshotSerializer.WriteBinary(original, dir);
            var loaded = SnapshotSerializer.ReadBinary(dir);

            var svc1 = new ImageRenderingService();
            svc1.Initialize(original.CtImage.XSize, original.CtImage.YSize);
            svc1.PreloadData(original.CtImage, original.Dose);

            var svc2 = new ImageRenderingService();
            svc2.Initialize(loaded.CtImage.XSize, loaded.CtImage.YSize);
            svc2.PreloadData(loaded.CtImage, loaded.Dose);

            int midSlice = original.CtImage.ZSize / 2;

            // Compare dose at every 4th pixel
            for (int y = 0; y < original.CtImage.YSize; y += 4)
                for (int x = 0; x < original.CtImage.XSize; x += 4)
                {
                    double d1 = svc1.GetDoseAtPixel(midSlice, x, y, null);
                    double d2 = svc2.GetDoseAtPixel(midSlice, x, y, null);

                    if (double.IsNaN(d1))
                        d2.Should().Be(double.NaN, $"both should be NaN at ({x},{y})");
                    else
                        d2.Should().BeApproximately(d1, 0.0001,
                                $"dose mismatch at ({x},{y}) after round-trip");
                }

            svc1.Dispose();
            svc2.Dispose();
        }

        /// <summary>
        /// Verifies that RenderSettings survive serialization round-trip.
        /// </summary>
        [Fact]
        public void RenderSettings_ShouldSurviveRoundTrip()
        {
            var snap = CreateCoregisteredPhantom(size: 16, doseGyInRegion: 1.0);
            snap.RenderSettings = new RenderSettings
            {
                WindowLevel = 40,
                WindowWidth = 400,
                IsodoseLevels = new List<IsodoseLevelSetting>
                {
                    new IsodoseLevelSetting { Fraction = 0.95, Color = 0xFFFFFF00, IsVisible = true },
                    new IsodoseLevelSetting { Fraction = 0.50, Color = 0xFF0000FF, IsVisible = true },
                },
                ReferenceDosePoints = new List<ReferenceDosePoint>
                {
                    new ReferenceDosePoint { CtPixelX = 8, CtPixelY = 8, CtSlice = 2, ExpectedDoseGy = 1.0, IsInsideDoseGrid = true },
                    new ReferenceDosePoint { CtPixelX = 0, CtPixelY = 0, CtSlice = 2, ExpectedDoseGy = 0.0, IsInsideDoseGrid = true },
                }
            };

            string dir = Path.Combine(_tempDir, "rendersettings_rt");
            SnapshotSerializer.WriteBinary(snap, dir);
            var loaded = SnapshotSerializer.ReadBinary(dir);

            loaded.RenderSettings.Should().NotBeNull();
            loaded.RenderSettings!.WindowLevel.Should().Be(40);
            loaded.RenderSettings.WindowWidth.Should().Be(400);
            loaded.RenderSettings.IsodoseLevels.Should().HaveCount(2);
            loaded.RenderSettings.IsodoseLevels[0].Fraction.Should().Be(0.95);
            loaded.RenderSettings.IsodoseLevels[1].Color.Should().Be(0xFF0000FF);
            loaded.RenderSettings.ReferenceDosePoints.Should().HaveCount(2);
            loaded.RenderSettings.ReferenceDosePoints[0].ExpectedDoseGy.Should().Be(1.0);
            loaded.RenderSettings.ReferenceDosePoints[1].IsInsideDoseGrid.Should().BeTrue();
        }

        // ========================================================
        // TIER 3: ECLIPSE REFERENCE -- requires real snapshot
        // ========================================================

        /// <summary>
        /// Loads a real clinical snapshot exported from Eclipse and verifies
        /// that the app computes the same dose values at reference points.
        ///
        /// This test is SKIPPED when no snapshot directory is found.
        /// To enable: export a snapshot from Eclipse using FixtureGenerator
        /// and place it in TestFixtures/snapshot/ or set the path via environment variable.
        /// </summary>
        [Fact]
        public void EclipseSnapshot_DoseAtReferencePoints_ShouldMatchEclipseValues()
        {
            string? snapshotDir = FindEclipseSnapshotDir();
            if (snapshotDir == null)
            {
                // No Eclipse snapshot available (expected in CI)
                return;
            }

            var snap = SnapshotSerializer.ReadAuto(snapshotDir);
            snap.Should().NotBeNull("snapshot should load");
            snap.CtImage.Should().NotBeNull("CT image should exist");
            snap.Dose.Should().NotBeNull("dose grid should exist");

            if (snap.RenderSettings?.ReferenceDosePoints == null ||
                snap.RenderSettings.ReferenceDosePoints.Count == 0)
            {
                // Snapshot has no reference points; re-export with the latest generator
                return;
            }

            var svc = new ImageRenderingService();
            svc.Initialize(snap.CtImage.XSize, snap.CtImage.YSize);
            svc.PreloadData(snap.CtImage, snap.Dose);

            int passed = 0, total = 0;
            foreach (var refPoint in snap.RenderSettings.ReferenceDosePoints)
            {
                total++;
                double appDose = svc.GetDoseAtPixel(refPoint.CtSlice, refPoint.CtPixelX, refPoint.CtPixelY, null);

                if (!refPoint.IsInsideDoseGrid)
                {
                    // Outside dose grid -- app should return NaN
                    if (double.IsNaN(appDose)) passed++;
                    continue;
                }

                if (double.IsNaN(appDose))
                {
                    // App thinks we're outside the grid but Eclipse said we're inside -- investigate
                    continue;
                }

                // Tolerance: 0.5% of expected dose or 0.01 Gy absolute, whichever is larger
                double tolerance = Math.Max(0.01, Math.Abs(refPoint.ExpectedDoseGy) * 0.005);
                appDose.Should().BeApproximately(refPoint.ExpectedDoseGy, tolerance,
                    $"dose mismatch at CT pixel ({refPoint.CtPixelX},{refPoint.CtPixelY}) slice {refPoint.CtSlice}");
                passed++;
            }

            passed.Should().BeGreaterOrEqualTo(total * 8 / 10,
                $"at least 80% of reference points should match (passed {passed}/{total})");

            svc.Dispose();
        }

        /// <summary>
        /// Loads a real snapshot and verifies DVH statistics against Eclipse values.
        /// Compares DMax, DMean for each structure that has pre-computed DVH curves.
        /// </summary>
        [Fact]
        public void EclipseSnapshot_DVHStatistics_ShouldMatchEclipseValues()
        {
            string? snapshotDir = FindEclipseSnapshotDir();
            if (snapshotDir == null) return;

            var snap = SnapshotSerializer.ReadAuto(snapshotDir);
            if (snap?.DvhCurves == null || snap.DvhCurves.Count == 0) return;

            var dvhService = new DVHService();

            foreach (var dvh in snap.DvhCurves)
            {
                if (dvh.Curve == null || dvh.Curve.Length < 3) continue;

                // Build summary from the serialized curve
                var curve = dvh.Curve.Select(p => new DoseVolumePoint(p[0], p[1])).ToArray();
                var summary = dvhService.BuildSummaryFromCurve(
                    dvh.StructureId, dvh.PlanId, "Physical", curve, dvh.VolumeCc);

                // DMax from our summary should be close to Eclipse's DMax
                double dmaxTolerance = Math.Max(0.1, dvh.DMaxGy * 0.02); // 2% or 0.1 Gy
                summary.DMax.Should().BeApproximately(dvh.DMaxGy, dmaxTolerance,
                    $"{dvh.StructureId}: DMax mismatch");

                // Volume should match
                if (dvh.VolumeCc > 0)
                    summary.Volume.Should().BeApproximately(dvh.VolumeCc, dvh.VolumeCc * 0.01,
                        $"{dvh.StructureId}: volume mismatch");
            }
        }

        // ========================================================
        // HELPERS
        // ========================================================

        /// <summary>
        /// Creates a synthetic co-registered phantom where CT and dose share
        /// the same grid (same origin, same resolution, same size).
        /// Dose is uniform in the central 50% region.
        /// </summary>
        /// <param name="size">Width and height of the square CT and dose grids (in pixels).</param>
        /// <param name="doseGyInRegion">Uniform dose value in Gy applied to the central 50% region.</param>
        private static ClinicalSnapshot CreateCoregisteredPhantom(int size, double doseGyInRegion)
        {
            int slices = 5;
            int huOffset = 32768;

            // Uniform raw dose value that maps to doseGyInRegion Gy
            // rawScale=0.001, rawOffset=0, unitToGy=1.0 -> raw = doseGy / 0.001
            int rawDose = (int)(doseGyInRegion / 0.001);

            var ctVoxels = new int[slices][,];
            for (int z = 0; z < slices; z++)
            {
                ctVoxels[z] = new int[size, size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                        ctVoxels[z][x, y] = huOffset; // 0 HU everywhere
            }

            var doseVoxels = new int[slices][,];
            for (int z = 0; z < slices; z++)
            {
                doseVoxels[z] = new int[size, size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        bool inRegion = x >= size / 4 && x < 3 * size / 4
                            && y >= size / 4 && y < 3 * size / 4;
                        doseVoxels[z][x, y] = inRegion ? rawDose : 0;
                    }
            }

            var geo = new VolumeGeometry
            {
                XSize = size,
                YSize = size,
                ZSize = slices,
                XRes = 1.0,
                YRes = 1.0,
                ZRes = 1.0,
                Origin = new Vec3(0, 0, 0),
                XDirection = new Vec3(1, 0, 0),
                YDirection = new Vec3(0, 1, 0),
                ZDirection = new Vec3(0, 0, 1),
                FrameOfReference = "1.2.3.4.5"
            };

            return new ClinicalSnapshot
            {
                Patient = new PatientData { Id = "PHANTOM", LastName = "Test", FirstName = "E2E" },
                ActivePlan = new PlanData
                {
                    Id = "E2E_Plan",
                    CourseId = "C1",
                    TotalDoseGy = 50.0,
                    NumberOfFractions = 25,
                    PlanNormalization = 100.0
                },
                CtImage = new VolumeData
                {
                    Geometry = geo,
                    Voxels = ctVoxels,
                    HuOffset = huOffset
                },
                Dose = new DoseVolumeData
                {
                    Geometry = geo,
                    Voxels = doseVoxels,
                    Scaling = new DoseScaling
                    {
                        RawScale = 0.001,
                        RawOffset = 0,
                        UnitToGy = 1.0,
                        DoseUnit = "Gy"
                    }
                },
                Structures = new List<StructureData>(),
                DvhCurves = new List<DvhCurveData>(),
                Registrations = new List<RegistrationData>(),
                AllCourses = new List<CourseData>()
            };
        }

        /// <summary>
        /// Searches for an Eclipse snapshot directory in standard locations.
        /// Checks the <c>EQD2_SNAPSHOT_DIR</c> environment variable first, then
        /// walks up from the output directory looking for TestFixtures/snapshot/.
        /// </summary>
        /// <returns>Full path to the snapshot directory, or <c>null</c> if not found.</returns>
        private static string? FindEclipseSnapshotDir()
        {
            // 1. Environment variable (CI/CD can set this)
            string envPath = Environment.GetEnvironmentVariable("EQD2_SNAPSHOT_DIR");
            if (!string.IsNullOrEmpty(envPath) && Directory.Exists(envPath)
                && File.Exists(Path.Combine(envPath, SnapshotSerializer.MetaFileName)))
                return envPath;

            // 2. TestFixtures/snapshot/ in test project
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string dir = baseDir;
            for (int i = 0; i < 8; i++)
            {
                string candidate = Path.Combine(dir, "EQD2Viewer.Tests", "TestFixtures", "snapshot");
                if (Directory.Exists(candidate)
                    && File.Exists(Path.Combine(candidate, SnapshotSerializer.MetaFileName)))
                    return candidate;

                // Also check for any subdirectory with snapshot_meta.json
                string fixturesDir = Path.Combine(dir, "EQD2Viewer.Tests", "TestFixtures");
                if (Directory.Exists(fixturesDir))
                {
                    foreach (var subDir in Directory.GetDirectories(fixturesDir))
                    {
                        if (File.Exists(Path.Combine(subDir, SnapshotSerializer.MetaFileName)))
                            return subDir;
                    }
                }

                dir = Path.GetDirectoryName(dir);
                if (dir == null) break;
            }

            return null;
        }
    }
}
