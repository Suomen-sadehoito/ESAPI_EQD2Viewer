using EQD2Viewer.Services.Rendering;
using EQD2Viewer.Services;
using EQD2Viewer.Core.Serialization;
using EQD2Viewer.Core.Models;
using EQD2Viewer.Core.Data;
using FluentAssertions;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EQD2Viewer.Tests.Integration.Models
{
    /// <summary>
    /// End-to-end rendering pipeline tests using synthetic ClinicalSnapshots.
    ///
    /// These tests exercise the full chain that runs in production:
    ///   ClinicalSnapshot -> ImageRenderingService -> WriteableBitmap
    ///
    /// The pipeline is tested at three levels:
    ///   1. CT rendering -- windowing, HU offset, bitmap output
    ///   2. Dose rendering -- isodose contours, colorwash, fill modes
    ///   3. Full round-trip -- serialize snapshot -> read -> render -> verify pixels
    ///
    /// Visual regression: rendered bitmaps can be saved as PNG and compared
    /// against Eclipse screenshots stored in the TestFixtures directory.
    /// Use <see cref="SaveBitmapForVisualInspection"/> to export reference images.
    /// </summary>
    public class RenderingPipelineTests : IDisposable
    {
        private readonly string _tempDir;

        public RenderingPipelineTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(),
        "EQD2_RenderTests_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
            catch { }
        }

        // ========================================================
        // TEST HELPERS
        // ========================================================

        /// <summary>
        /// Creates a synthetic snapshot with a simple gradient CT and uniform dose.
        /// CT: gradient from -1000 HU (air) to +1000 HU (bone) left-to-right.
        /// Dose: uniform 2.0 Gy across the central region.
        /// </summary>
        private static ClinicalSnapshot CreateRenderingTestSnapshot(int size = 64)
        {
            int ctX = size, ctY = size, ctZ = 10;
            int doseX = size, doseY = size, doseZ = 10;
            int huOffset = 32768;

            // CT: horizontal HU gradient
            var ctVoxels = new int[ctZ][,];
            for (int z = 0; z < ctZ; z++)
            {
                ctVoxels[z] = new int[ctX, ctY];
                for (int y = 0; y < ctY; y++)
                    for (int x = 0; x < ctX; x++)
                    {
                        // HU range: -1000 to +1000 left-to-right
                        double hu = -1000.0 + (2000.0 * x / (ctX - 1));
                        ctVoxels[z][x, y] = (int)(hu + huOffset);
                    }
            }

            // Dose: uniform in central 50% region, zero outside
            // Raw value 1000 with rawScale=0.001, unitToGy=1.0 -> 1.0 Gy
            var doseVoxels = new int[doseZ][,];
            for (int z = 0; z < doseZ; z++)
            {
                doseVoxels[z] = new int[doseX, doseY];
                for (int y = 0; y < doseY; y++)
                    for (int x = 0; x < doseX; x++)
                    {
                        bool inRegion = x >= doseX / 4 && x < 3 * doseX / 4
                             && y >= doseY / 4 && y < 3 * doseY / 4;
                        doseVoxels[z][x, y] = inRegion ? 2000 : 0;
                    }
            }

            return new ClinicalSnapshot
            {
                Patient = new PatientData { Id = "RENDER_TEST", LastName = "Render", FirstName = "Test" },
                ActivePlan = new PlanData
                {
                    Id = "RenderPlan",
                    CourseId = "C1",
                    TotalDoseGy = 50.0,
                    NumberOfFractions = 25,
                    PlanNormalization = 100.0
                },
                CtImage = new VolumeData
                {
                    Geometry = new VolumeGeometry
                    {
                        XSize = ctX,
                        YSize = ctY,
                        ZSize = ctZ,
                        XRes = 1.0,
                        YRes = 1.0,
                        ZRes = 2.5,
                        Origin = new Vec3(0, 0, 0),
                        XDirection = new Vec3(1, 0, 0),
                        YDirection = new Vec3(0, 1, 0),
                        ZDirection = new Vec3(0, 0, 1),
                        FrameOfReference = "1.2.3.4.5"
                    },
                    Voxels = ctVoxels,
                    HuOffset = huOffset
                },
                Dose = new DoseVolumeData
                {
                    Geometry = new VolumeGeometry
                    {
                        XSize = doseX,
                        YSize = doseY,
                        ZSize = doseZ,
                        XRes = 1.0,
                        YRes = 1.0,
                        ZRes = 2.5,
                        Origin = new Vec3(0, 0, 0),
                        XDirection = new Vec3(1, 0, 0),
                        YDirection = new Vec3(0, 1, 0),
                        ZDirection = new Vec3(0, 0, 1),
                        FrameOfReference = "1.2.3.4.5"
                    },
                    Voxels = doseVoxels,
                    Scaling = new DoseScaling { RawScale = 0.001, RawOffset = 0, UnitToGy = 1.0, DoseUnit = "Gy" }
                },
                Structures = new System.Collections.Generic.List<StructureData>(),
                DvhCurves = new System.Collections.Generic.List<DvhCurveData>(),
                Registrations = new System.Collections.Generic.List<RegistrationData>(),
                AllCourses = new System.Collections.Generic.List<CourseData>()
            };
        }

        /// <summary>
        /// Reads a pixel's BGRA values from a WriteableBitmap.
        /// </summary>
        private static (byte B, byte G, byte R, byte A) ReadPixel(WriteableBitmap bmp, int x, int y)
        {
            int stride = bmp.BackBufferStride;
            byte[] pixel = new byte[4];
            bmp.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), pixel, stride, 0);
            return (pixel[0], pixel[1], pixel[2], pixel[3]);
        }

        /// <summary>
        /// Saves a WriteableBitmap as PNG for visual inspection.
        /// Call this during test development to generate reference images.
        /// </summary>
        private void SaveBitmapForVisualInspection(WriteableBitmap bmp, string name)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            string path = Path.Combine(_tempDir, $"{name}.png");
            using (var fs = File.Create(path))
                encoder.Save(fs);
        }

        // ========================================================
        // CT RENDERING
        // ========================================================

        [Fact]
        public void RenderCt_GradientPhantom_ShouldProduceGradientBitmap()
        {
            var snap = CreateRenderingTestSnapshot();
            var svc = new ImageRenderingService();
            svc.Initialize(snap.CtImage.XSize, snap.CtImage.YSize);
            svc.PreloadData(snap.CtImage, snap.Dose);

            var bmp = new WriteableBitmap(snap.CtImage.XSize, snap.CtImage.YSize,
             96, 96, PixelFormats.Bgra32, null);

            // Soft tissue window: Level=40, Width=400 -> range [-160, 240] HU
            svc.RenderCtImage(bmp, snap.CtImage.ZSize / 2, 40, 400);

            // Left edge: HU = -1000 -> below window -> should be black (or near-black)
            var leftPixel = ReadPixel(bmp, 0, snap.CtImage.YSize / 2);
            leftPixel.R.Should().BeLessThan(10, "far-left should be dark (HU=-1000, below window)");

            // Right edge: HU = +1000 -> above window -> should be white (or near-white)
            var rightPixel = ReadPixel(bmp, snap.CtImage.XSize - 1, snap.CtImage.YSize / 2);
            rightPixel.R.Should().BeGreaterThan(245, "far-right should be bright (HU=+1000, above window)");

            // Middle: should be intermediate
            var midPixel = ReadPixel(bmp, snap.CtImage.XSize / 2, snap.CtImage.YSize / 2);
            midPixel.R.Should().BeInRange(80, 200, "center should be mid-gray");

            svc.Dispose();
        }

        [Fact]
        public void RenderCt_DifferentWindows_ShouldProduceDifferentResults()
        {
            var snap = CreateRenderingTestSnapshot();
            var svc = new ImageRenderingService();
            svc.Initialize(snap.CtImage.XSize, snap.CtImage.YSize);
            svc.PreloadData(snap.CtImage, snap.Dose);

            int midSlice = snap.CtImage.ZSize / 2;
            int midX = snap.CtImage.XSize / 2;
            int midY = snap.CtImage.YSize / 2;

            var bmpSoft = new WriteableBitmap(snap.CtImage.XSize, snap.CtImage.YSize, 96, 96, PixelFormats.Bgra32, null);
            svc.RenderCtImage(bmpSoft, midSlice, 40, 400);
            var softPixel = ReadPixel(bmpSoft, midX, midY);

            var bmpLung = new WriteableBitmap(snap.CtImage.XSize, snap.CtImage.YSize, 96, 96, PixelFormats.Bgra32, null);
            svc.RenderCtImage(bmpLung, midSlice, -600, 1600);
            var lungPixel = ReadPixel(bmpLung, midX, midY);

            // Same pixel, different windowing -> different brightness
            softPixel.R.Should().NotBe(lungPixel.R,
           "different window settings should produce different pixel values");

            svc.Dispose();
        }

        // ========================================================
        // DOSE RENDERING
        // ========================================================

        [Fact]
        public void RenderDose_Colorwash_DoseRegionShouldHaveColor()
        {
            var snap = CreateRenderingTestSnapshot();
            var svc = new ImageRenderingService();
            svc.Initialize(snap.CtImage.XSize, snap.CtImage.YSize);
            svc.PreloadData(snap.CtImage, snap.Dose);

            int midSlice = snap.CtImage.ZSize / 2;
            var levels = IsodoseLevel.GetEclipseDefaults();

            var doseBmp = new WriteableBitmap(snap.CtImage.XSize, snap.CtImage.YSize,
      96, 96, PixelFormats.Bgra32, null);

            svc.RenderDoseImage(doseBmp, midSlice, 50.0, 100.0, levels,
                  DoseDisplayMode.Colorwash, 0.8, 0.01, null);

            // Center of image (inside dose region): should have non-zero color
            int cx = snap.CtImage.XSize / 2;
            int cy = snap.CtImage.YSize / 2;
            var centerPixel = ReadPixel(doseBmp, cx, cy);
            centerPixel.A.Should().BeGreaterThan(0,
           "center pixel should have dose overlay (inside dose region)");

            // Corner (outside dose region): should be transparent
            var cornerPixel = ReadPixel(doseBmp, 0, 0);
            cornerPixel.A.Should().Be(0,
         "corner pixel should be transparent (no dose)");

            svc.Dispose();
        }

        // ========================================================
        // FULL ROUND-TRIP: Serialize -> Read -> Render
        // ========================================================

        [Fact]
        public void FullRoundTrip_BinarySnapshot_RendersCt_SameAsOriginal()
        {
            var original = CreateRenderingTestSnapshot();
            string dir = Path.Combine(_tempDir, "full_roundtrip");

            // Write to disk as binary snapshot
            SnapshotSerializer.WriteBinary(original, dir);

            // Read back
            var loaded = SnapshotSerializer.ReadBinary(dir);

            // Render from original
            var svc1 = new ImageRenderingService();
            svc1.Initialize(original.CtImage.XSize, original.CtImage.YSize);
            svc1.PreloadData(original.CtImage, original.Dose);
            var bmp1 = new WriteableBitmap(original.CtImage.XSize, original.CtImage.YSize, 96, 96, PixelFormats.Bgra32, null);
            svc1.RenderCtImage(bmp1, original.CtImage.ZSize / 2, 40, 400);

            // Render from loaded
            var svc2 = new ImageRenderingService();
            svc2.Initialize(loaded.CtImage.XSize, loaded.CtImage.YSize);
            svc2.PreloadData(loaded.CtImage, loaded.Dose);
            var bmp2 = new WriteableBitmap(loaded.CtImage.XSize, loaded.CtImage.YSize, 96, 96, PixelFormats.Bgra32, null);
            svc2.RenderCtImage(bmp2, loaded.CtImage.ZSize / 2, 40, 400);

            // Pixel-by-pixel comparison
            int w = original.CtImage.XSize, h = original.CtImage.YSize;
            byte[] pixels1 = new byte[w * h * 4];
            byte[] pixels2 = new byte[w * h * 4];
            bmp1.CopyPixels(pixels1, w * 4, 0);
            bmp2.CopyPixels(pixels2, w * 4, 0);

            for (int i = 0; i < pixels1.Length; i++)
            {
                pixels1[i].Should().Be(pixels2[i],
                    $"pixel byte {i} should match after binary round-trip");
            }

            svc1.Dispose();
            svc2.Dispose();
        }

        [Fact]
        public void FullRoundTrip_JsonSnapshot_RendersCt_SameAsOriginal()
        {
            var original = CreateRenderingTestSnapshot();
            string dir = Path.Combine(_tempDir, "json_roundtrip_render");

            SnapshotSerializer.Write(original, dir);
            var loaded = SnapshotSerializer.Read(dir);

            var svc1 = new ImageRenderingService();
            svc1.Initialize(original.CtImage.XSize, original.CtImage.YSize);
            svc1.PreloadData(original.CtImage, original.Dose);
            var bmp1 = new WriteableBitmap(original.CtImage.XSize, original.CtImage.YSize, 96, 96, PixelFormats.Bgra32, null);
            svc1.RenderCtImage(bmp1, original.CtImage.ZSize / 2, 40, 400);

            var svc2 = new ImageRenderingService();
            svc2.Initialize(loaded.CtImage.XSize, loaded.CtImage.YSize);
            svc2.PreloadData(loaded.CtImage, loaded.Dose);
            var bmp2 = new WriteableBitmap(loaded.CtImage.XSize, loaded.CtImage.YSize, 96, 96, PixelFormats.Bgra32, null);
            svc2.RenderCtImage(bmp2, loaded.CtImage.ZSize / 2, 40, 400);

            int w = original.CtImage.XSize, h = original.CtImage.YSize;
            byte[] pixels1 = new byte[w * h * 4];
            byte[] pixels2 = new byte[w * h * 4];
            bmp1.CopyPixels(pixels1, w * 4, 0);
            bmp2.CopyPixels(pixels2, w * 4, 0);

            for (int i = 0; i < pixels1.Length; i++)
                pixels1[i].Should().Be(pixels2[i]);

            svc1.Dispose();
            svc2.Dispose();
        }

        // ========================================================
        // VISUAL REGRESSION INFRASTRUCTURE
        // ========================================================

        /// <summary>
        /// Compares two bitmaps pixel-by-pixel with a per-channel tolerance.
        /// Returns the count of mismatched pixels.
        ///
        /// Usage in visual regression tests:
        ///   1. Run once to generate reference PNGs (SaveBitmapForVisualInspection)
        ///   2. Store reference PNGs in TestFixtures/
        ///   3. Compare rendered output against reference in subsequent runs
        /// </summary>
        internal static int CompareImages(WriteableBitmap actual, WriteableBitmap expected,
         int tolerancePerChannel = 5)
        {
            if (actual.PixelWidth != expected.PixelWidth ||
       actual.PixelHeight != expected.PixelHeight)
                return int.MaxValue;

            int w = actual.PixelWidth, h = actual.PixelHeight;
            byte[] a = new byte[w * h * 4];
            byte[] e = new byte[w * h * 4];
            actual.CopyPixels(a, w * 4, 0);
            expected.CopyPixels(e, w * 4, 0);

            int mismatches = 0;
            for (int i = 0; i < a.Length; i++)
            {
                if (Math.Abs(a[i] - e[i]) > tolerancePerChannel)
                    mismatches++;
            }
            return mismatches;
        }

        [Fact]
        public void CompareImages_IdenticalBitmaps_ShouldReturnZeroMismatches()
        {
            var bmp = new WriteableBitmap(16, 16, 96, 96, PixelFormats.Bgra32, null);
            CompareImages(bmp, bmp).Should().Be(0);
        }
    }
}
