using EQD2Viewer.Core.Data;
using EQD2Viewer.Core.Interfaces;
using EQD2Viewer.Core.Models;
using EQD2Viewer.Services;
using FluentAssertions;
using Moq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EQD2Viewer.Tests.Services
{
    /// <summary>
    /// Core tests for SummationService — covers the direct-sample reference path,
    /// the affine-registration path, EQD2 recompute, and per-structure DVH.
    ///
    /// Test strategy:
    ///   * Build a tiny 4x4x2 reference CT with identity orientation.
    ///   * Mock ISummationDataLoader to return deterministic dose voxels.
    ///   * Assert voxel-level dose placement matches the expected sampling.
    /// </summary>
    public class SummationServiceTests
    {
        private const int RefX = 4, RefY = 4, RefZ = 2;

        // ── Test fixtures ─────────────────────────────────────────────────

        private static VolumeData MakeReferenceCt()
        {
            var vox = new int[RefZ][,];
            for (int z = 0; z < RefZ; z++) vox[z] = new int[RefX, RefY];
            return new VolumeData
            {
                Geometry = new VolumeGeometry
                {
                    XSize = RefX, YSize = RefY, ZSize = RefZ,
                    XRes = 1.0, YRes = 1.0, ZRes = 1.0,
                    Origin = new Vec3(0, 0, 0),
                    XDirection = new Vec3(1, 0, 0),
                    YDirection = new Vec3(0, 1, 0),
                    ZDirection = new Vec3(0, 0, 1),
                    FrameOfReference = "FOR_REF"
                },
                Voxels = vox,
                HuOffset = 0
            };
        }

        /// <summary>
        /// Builds a dose grid co-located with the reference CT, with the specified per-voxel
        /// dose in Gy stored in raw voxels (RawScale=1, offset=0, unit=Gy).
        /// </summary>
        private static SummationPlanDoseData MakeDoseData(int[][,] doseGy)
            => new SummationPlanDoseData
            {
                DoseVoxels = doseGy,
                DoseGeometry = new VolumeGeometry
                {
                    XSize = RefX, YSize = RefY, ZSize = RefZ,
                    XRes = 1.0, YRes = 1.0, ZRes = 1.0,
                    Origin = new Vec3(0, 0, 0),
                    XDirection = new Vec3(1, 0, 0),
                    YDirection = new Vec3(0, 1, 0),
                    ZDirection = new Vec3(0, 0, 1)
                },
                Scaling = new DoseScaling { RawScale = 1.0, RawOffset = 0, UnitToGy = 1.0, DoseUnit = "Gy" }
            };

        private static int[][,] FillDose(int value)
        {
            var data = new int[RefZ][,];
            for (int z = 0; z < RefZ; z++)
            {
                data[z] = new int[RefX, RefY];
                for (int y = 0; y < RefY; y++)
                    for (int x = 0; x < RefX; x++)
                        data[z][x, y] = value;
            }
            return data;
        }

        private static Mock<ISummationDataLoader> MakeLoader(
            int refDoseGy,
            int movingDoseGy,
            string movingFor = "FOR_MOV")
        {
            var loader = new Mock<ISummationDataLoader>(MockBehavior.Strict);
            loader.Setup(l => l.LoadPlanDose("C1", "PlanRef", It.IsAny<double>()))
                  .Returns(MakeDoseData(FillDose(refDoseGy)));
            loader.Setup(l => l.LoadPlanDose("C1", "PlanMov", It.IsAny<double>()))
                  .Returns(MakeDoseData(FillDose(movingDoseGy)));
            loader.Setup(l => l.LoadStructureContours(It.IsAny<string>(), It.IsAny<string>()))
                  .Returns(new List<StructureData>());
            loader.Setup(l => l.GetPlanImageFOR("C1", "PlanRef")).Returns("FOR_REF");
            loader.Setup(l => l.GetPlanImageFOR("C1", "PlanMov")).Returns(movingFor);
            return loader;
        }

        private static SummationConfig MakeConfig()
            => new SummationConfig
            {
                Method = SummationMethod.Physical,
                GlobalAlphaBeta = 3.0,
                Plans = new List<SummationPlanEntry>
                {
                    new SummationPlanEntry { CourseId = "C1", PlanId = "PlanRef", DisplayLabel = "Ref",
                        NumberOfFractions = 25, TotalDoseGy = 50, Weight = 1.0, IsReference = true },
                    new SummationPlanEntry { CourseId = "C1", PlanId = "PlanMov", DisplayLabel = "Mov",
                        NumberOfFractions = 25, TotalDoseGy = 50, Weight = 1.0, IsReference = false }
                }
            };

        // ── PrepareData validation ─────────────────────────────────────────

        [Fact]
        public void PrepareData_EmptyConfig_ReturnsFailure()
        {
            var svc = new SummationService(MakeReferenceCt(),
                new Mock<ISummationDataLoader>().Object, new List<RegistrationData>());
            var result = svc.PrepareData(new SummationConfig { Plans = new List<SummationPlanEntry>() });
            result.Success.Should().BeFalse();
            result.StatusMessage.Should().Contain("No plans");
        }

        [Fact]
        public void PrepareData_NoReferencePlan_ReturnsFailure()
        {
            var svc = new SummationService(MakeReferenceCt(),
                new Mock<ISummationDataLoader>().Object, new List<RegistrationData>());
            var config = new SummationConfig
            {
                Plans = new List<SummationPlanEntry>
                {
                    new SummationPlanEntry { CourseId = "C", PlanId = "P", IsReference = false }
                }
            };
            var result = svc.PrepareData(config);
            result.Success.Should().BeFalse();
            result.StatusMessage.Should().Contain("reference");
        }

        // ── ComputeAsync: reference-only pathway ───────────────────────────

        [Fact]
        public async Task ComputeAsync_ReferenceOnlyPlan_AccumulatesDirectDose()
        {
            var loader = MakeLoader(refDoseGy: 5, movingDoseGy: 0);
            var svc = new SummationService(MakeReferenceCt(), loader.Object, new List<RegistrationData>());
            var config = new SummationConfig
            {
                Method = SummationMethod.Physical,
                GlobalAlphaBeta = 3.0,
                Plans = new List<SummationPlanEntry>
                {
                    new SummationPlanEntry { CourseId = "C1", PlanId = "PlanRef", DisplayLabel = "Ref",
                        NumberOfFractions = 25, TotalDoseGy = 50, Weight = 1.0, IsReference = true }
                }
            };
            svc.PrepareData(config).Success.Should().BeTrue();
            var r = await svc.ComputeAsync(null, CancellationToken.None);
            r.Success.Should().BeTrue();
            r.MaxDoseGy.Should().BeApproximately(5, 1e-6);
        }

        // ── ComputeAsync: cancellation ─────────────────────────────────────

        [Fact]
        public async Task ComputeAsync_WithCancelledToken_PropagatesCancellation()
        {
            var loader = MakeLoader(refDoseGy: 1, movingDoseGy: 1);
            var svc = new SummationService(MakeReferenceCt(), loader.Object, new List<RegistrationData>());
            svc.PrepareData(MakeConfig()).Success.Should().BeTrue();

            using var cts = new CancellationTokenSource();
            cts.Cancel();
            // Task.Run with a pre-cancelled token schedules a canceled Task; awaiting surfaces
            // TaskCanceledException. That is the contract the UI layer expects — it catches
            // OperationCanceledException to distinguish user-cancel from server error.
            System.Func<Task> act = async () => await svc.ComputeAsync(null, cts.Token);
            await act.Should().ThrowAsync<System.Threading.Tasks.TaskCanceledException>();
        }

        [Fact]
        public void Dispose_ClearsInternalState()
        {
            var loader = MakeLoader(refDoseGy: 1, movingDoseGy: 1);
            var svc = new SummationService(MakeReferenceCt(), loader.Object, new List<RegistrationData>());
            svc.PrepareData(MakeConfig()).Success.Should().BeTrue();
            svc.Dispose();
            svc.HasSummedDose.Should().BeFalse();
        }

        [Fact]
        public void SliceCount_MatchesReferenceZSize()
        {
            var loader = MakeLoader(refDoseGy: 0, movingDoseGy: 0);
            var svc = new SummationService(MakeReferenceCt(), loader.Object, new List<RegistrationData>());
            svc.PrepareData(MakeConfig());
            svc.SliceCount.Should().Be(RefZ);
        }

        [Fact]
        public void VoxelVolume_ComputedFromSpacing_1mm3IsOneNanoLiter()
        {
            var loader = MakeLoader(refDoseGy: 0, movingDoseGy: 0);
            var svc = new SummationService(MakeReferenceCt(), loader.Object, new List<RegistrationData>());
            svc.PrepareData(MakeConfig());
            // 1mm × 1mm × 1mm = 0.001 cm³ = 1 µL
            svc.GetVoxelVolumeCc().Should().BeApproximately(0.001, 1e-9);
        }

        // ── Affine (registered) pathway ────────────────────────────────────

        /// <summary>
        /// Affine path (RegistrationId set) with identity matrix must behave exactly
        /// like the direct path — covers AccumulatePhysicalRegistered.
        /// </summary>
        [Fact]
        public async Task ComputeAsync_AffineIdentityMatrix_SamplesAtSamePosition()
        {
            // Moving dose varies by X so we can verify per-voxel correctness.
            var movingDose = new int[RefZ][,];
            for (int z = 0; z < RefZ; z++)
            {
                movingDose[z] = new int[RefX, RefY];
                for (int y = 0; y < RefY; y++)
                    for (int x = 0; x < RefX; x++)
                        movingDose[z][x, y] = x * 10;
            }

            var loader = new Mock<ISummationDataLoader>(MockBehavior.Strict);
            loader.Setup(l => l.LoadPlanDose("C1", "PlanRef", It.IsAny<double>())).Returns(MakeDoseData(FillDose(0)));
            loader.Setup(l => l.LoadPlanDose("C1", "PlanMov", It.IsAny<double>())).Returns(MakeDoseData(movingDose));
            loader.Setup(l => l.LoadStructureContours(It.IsAny<string>(), It.IsAny<string>()))
                  .Returns(new List<StructureData>());
            loader.Setup(l => l.GetPlanImageFOR("C1", "PlanRef")).Returns("FOR_REF");
            loader.Setup(l => l.GetPlanImageFOR("C1", "PlanMov")).Returns("FOR_REF"); // same FOR

            var identityMatrix = new RegistrationData
            {
                Id = "REG_ID",
                SourceFOR = "FOR_REF",
                RegisteredFOR = "FOR_REF",
                Matrix = new double[]
                {
                    1, 0, 0, 0,
                    0, 1, 0, 0,
                    0, 0, 1, 0,
                    0, 0, 0, 1
                }
            };

            var svc = new SummationService(MakeReferenceCt(), loader.Object, new List<RegistrationData> { identityMatrix });
            var config = new SummationConfig
            {
                Method = SummationMethod.Physical,
                GlobalAlphaBeta = 3.0,
                Plans = new List<SummationPlanEntry>
                {
                    new SummationPlanEntry { CourseId = "C1", PlanId = "PlanRef", DisplayLabel = "Ref",
                        NumberOfFractions = 25, TotalDoseGy = 50, Weight = 1.0, IsReference = true },
                    new SummationPlanEntry { CourseId = "C1", PlanId = "PlanMov", DisplayLabel = "Mov",
                        NumberOfFractions = 25, TotalDoseGy = 50, Weight = 1.0, IsReference = false,
                        RegistrationId = "REG_ID" }
                }
            };
            svc.PrepareData(config).Success.Should().BeTrue();
            var r = await svc.ComputeAsync(null, CancellationToken.None);
            r.Success.Should().BeTrue();

            var slice = svc.GetSummedSlice(0);
            slice.Should().NotBeNull();
            // Identity transform → each ref voxel gets moving dose at same index
            // ref (0, 0, 0) → moving (0, 0, 0) → 0*10 = 0
            slice![0 + 0 * RefX].Should().BeApproximately(0, 1e-4);
            // ref (2, 0, 0) → moving (2, 0, 0) → 2*10 = 20
            slice[2 + 0 * RefX].Should().BeApproximately(20, 1e-4);
        }

        // ── Structure DVH ──────────────────────────────────────────────────

        [Fact]
        public async Task ComputeStructureEQD2DVH_NonexistentStructure_ReturnsEmpty()
        {
            var loader = MakeLoader(refDoseGy: 5, movingDoseGy: 0);
            var svc = new SummationService(MakeReferenceCt(), loader.Object, new List<RegistrationData>());
            svc.PrepareData(MakeConfig()).Success.Should().BeTrue();
            await svc.ComputeAsync(null, CancellationToken.None);

            var dvh = svc.ComputeStructureEQD2DVH("NonExistent", structureAlphaBeta: 3.0, maxDoseGy: 10);
            dvh.Should().BeEmpty();
        }

        [Fact]
        public async Task ComputeStructureEQD2DVH_ZeroMaxDose_ReturnsEmpty()
        {
            var loader = MakeLoader(refDoseGy: 5, movingDoseGy: 0);
            var svc = new SummationService(MakeReferenceCt(), loader.Object, new List<RegistrationData>());
            svc.PrepareData(MakeConfig()).Success.Should().BeTrue();
            await svc.ComputeAsync(null, CancellationToken.None);

            var dvh = svc.ComputeStructureEQD2DVH("AnyStruct", structureAlphaBeta: 3.0, maxDoseGy: 0);
            dvh.Should().BeEmpty();
        }

        // ── Display α/β recompute ──────────────────────────────────────────

        [Fact]
        public async Task RecomputeEQD2DisplayAsync_WithoutPriorCompute_ReturnsFailure()
        {
            var loader = MakeLoader(refDoseGy: 5, movingDoseGy: 0);
            var svc = new SummationService(MakeReferenceCt(), loader.Object, new List<RegistrationData>());
            svc.PrepareData(MakeConfig()).Success.Should().BeTrue();
            // Note: no ComputeAsync call → no cached physical slices
            var r = await svc.RecomputeEQD2DisplayAsync(3.0, null, CancellationToken.None);
            r.Success.Should().BeFalse();
            r.StatusMessage.Should().Contain("No");
        }

        [Fact]
        public async Task RecomputeEQD2DisplayAsync_PhysicalMode_ReproducesOriginalMaxDose()
        {
            // In Physical mode, changing α/β should NOT change the summed dose — it's a no-op.
            var loader = MakeLoader(refDoseGy: 3, movingDoseGy: 7);
            var svc = new SummationService(MakeReferenceCt(), loader.Object, new List<RegistrationData>());
            svc.PrepareData(MakeConfig()).Success.Should().BeTrue();
            var original = await svc.ComputeAsync(null, CancellationToken.None);
            double originalMax = original.MaxDoseGy;

            var recomputed = await svc.RecomputeEQD2DisplayAsync(10.0, null, CancellationToken.None);
            recomputed.Success.Should().BeTrue();
            recomputed.MaxDoseGy.Should().BeApproximately(originalMax, 1e-6,
                "Physical-mode summation is α/β-independent");
        }

        [Fact]
        public async Task RecomputeEQD2DisplayAsync_AlphaBetaZero_FallsBackToPhysicalDose()
        {
            // α/β = 0 is a hypo-fractionation edge case that must not produce NaN/Inf.
            // EQD2Calculator treats α/β ≤ 0 as "no EQD2 transform" and returns identity
            // factors (Q=0, L=1 → eqd2 = 0·D² + 1·D = D). Verify this propagates through
            // the recompute path without crashing.
            var loader = MakeLoader(refDoseGy: 5, movingDoseGy: 3);
            var svc = new SummationService(MakeReferenceCt(), loader.Object, new List<RegistrationData>());
            svc.PrepareData(MakeConfig()).Success.Should().BeTrue();
            var first = await svc.ComputeAsync(null, CancellationToken.None);
            first.Success.Should().BeTrue();

            // Recompute with α/β = 0: must not crash, must return finite values.
            var r = await svc.RecomputeEQD2DisplayAsync(0.0, null, CancellationToken.None);
            r.Success.Should().BeTrue("α/β = 0 path must fall back gracefully, not throw");
            double.IsNaN(r.MaxDoseGy).Should().BeFalse();
            double.IsInfinity(r.MaxDoseGy).Should().BeFalse();
        }

        [Fact]
        public async Task RecomputeEQD2DisplayAsync_VeryHighAlphaBeta_StaysFinite()
        {
            // α/β = 1e6 → EQD2 formula → (d + 1e6) / (2 + 1e6) ≈ 1. Essentially physical dose.
            var loader = MakeLoader(refDoseGy: 2, movingDoseGy: 2);
            var svc = new SummationService(MakeReferenceCt(), loader.Object, new List<RegistrationData>());
            svc.PrepareData(MakeConfig()).Success.Should().BeTrue();
            await svc.ComputeAsync(null, CancellationToken.None);
            var r = await svc.RecomputeEQD2DisplayAsync(1e6, null, CancellationToken.None);
            r.Success.Should().BeTrue();
            double.IsNaN(r.MaxDoseGy).Should().BeFalse();
            double.IsInfinity(r.MaxDoseGy).Should().BeFalse();
        }

        // ── Affine path: FOR-flip (inversion branch) ─────────────────────

        /// <summary>
        /// When the registration is stored as plan→ref (SourceFOR == planFOR), CachePlanData
        /// must invert the matrix to get ref→plan. This exercises MatrixMath.Invert4x4 through
        /// the affine summation path. Uses translation-by-one-in-X so the effect is observable
        /// in the sampled dose row.
        /// </summary>
        [Fact]
        public async Task ComputeAsync_AffineSourceIsPlan_InvertsMatrix()
        {
            // Moving dose gradient: dose at (x, y, z) = x * 10
            var movingDose = new int[RefZ][,];
            for (int z = 0; z < RefZ; z++)
            {
                movingDose[z] = new int[RefX, RefY];
                for (int y = 0; y < RefY; y++)
                    for (int x = 0; x < RefX; x++)
                        movingDose[z][x, y] = x * 10;
            }

            var loader = new Mock<ISummationDataLoader>(MockBehavior.Strict);
            loader.Setup(l => l.LoadPlanDose("C1", "PlanRef", It.IsAny<double>())).Returns(MakeDoseData(FillDose(0)));
            loader.Setup(l => l.LoadPlanDose("C1", "PlanMov", It.IsAny<double>())).Returns(MakeDoseData(movingDose));
            loader.Setup(l => l.LoadStructureContours(It.IsAny<string>(), It.IsAny<string>()))
                  .Returns(new List<StructureData>());
            loader.Setup(l => l.GetPlanImageFOR("C1", "PlanRef")).Returns("FOR_REF");
            loader.Setup(l => l.GetPlanImageFOR("C1", "PlanMov")).Returns("FOR_MOV");

            // Plan→ref translation: shift +1 in X. Matrix is SourceFOR=FOR_MOV (the plan).
            // CachePlanData must invert → ref→plan = shift -1 in X.
            // Effect on sampling: ref voxel (x, 0, 0) samples plan at (x - 1, 0, 0) → dose (x-1)*10.
            var reg = new RegistrationData
            {
                Id = "REG_PLAN_TO_REF",
                SourceFOR = "FOR_MOV",   // plan side — triggers inversion branch
                RegisteredFOR = "FOR_REF",
                Matrix = new double[]
                {
                    1, 0, 0, 1,  // translate +1 in X (plan → ref)
                    0, 1, 0, 0,
                    0, 0, 1, 0,
                    0, 0, 0, 1
                }
            };

            var svc = new SummationService(MakeReferenceCt(), loader.Object, new List<RegistrationData> { reg });
            var config = new SummationConfig
            {
                Method = SummationMethod.Physical,
                GlobalAlphaBeta = 3.0,
                Plans = new List<SummationPlanEntry>
                {
                    new SummationPlanEntry { CourseId = "C1", PlanId = "PlanRef", DisplayLabel = "Ref",
                        NumberOfFractions = 25, TotalDoseGy = 50, Weight = 1.0, IsReference = true },
                    new SummationPlanEntry { CourseId = "C1", PlanId = "PlanMov", DisplayLabel = "Mov",
                        NumberOfFractions = 25, TotalDoseGy = 50, Weight = 1.0, IsReference = false,
                        RegistrationId = "REG_PLAN_TO_REF" }
                }
            };
            svc.PrepareData(config).Success.Should().BeTrue();
            var r = await svc.ComputeAsync(null, CancellationToken.None);
            r.Success.Should().BeTrue();

            var slice = svc.GetSummedSlice(0);
            slice.Should().NotBeNull();
            // ref (1, 0, 0) → inverted transform → plan (0, 0, 0) → dose 0
            slice![1 + 0 * RefX].Should().BeApproximately(0, 1e-4);
            // ref (2, 0, 0) → plan (1, 0, 0) → dose 10
            slice[2 + 0 * RefX].Should().BeApproximately(10, 1e-4);
            // ref (3, 0, 0) → plan (2, 0, 0) → dose 20
            slice[3 + 0 * RefX].Should().BeApproximately(20, 1e-4);
        }

        // ── Structure-specific EQD2 DVH ──────────────────────────────────

        /// <summary>
        /// Constructs a minimal structure mask around the known-hot region, computes the DVH,
        /// and verifies monotonic cumulative volume. Covers the successful-path of
        /// ComputeStructureEQD2DVH which was previously only tested for negative cases.
        /// </summary>
        [Fact]
        public async Task ComputeStructureEQD2DVH_NonEmptyStructure_ProducesMonotonicCurve()
        {
            // Reference plan dose = 10 Gy everywhere. Structure: the whole volume.
            var loader = new Mock<ISummationDataLoader>(MockBehavior.Strict);
            loader.Setup(l => l.LoadPlanDose("C1", "PlanRef", It.IsAny<double>())).Returns(MakeDoseData(FillDose(10)));
            loader.Setup(l => l.GetPlanImageFOR("C1", "PlanRef")).Returns("FOR_REF");
            loader.Setup(l => l.LoadStructureContours(It.IsAny<string>(), It.IsAny<string>()))
                  .Returns(new List<StructureData>
                  {
                      new StructureData
                      {
                          Id = "BODY",
                          DicomType = "EXTERNAL",
                          // Polygon that covers the entire slice for all Z
                          ContoursBySlice = BuildWholeVolumeStructure()
                      }
                  });

            var svc = new SummationService(MakeReferenceCt(), loader.Object, new List<RegistrationData>());
            var config = new SummationConfig
            {
                Method = SummationMethod.Physical,
                GlobalAlphaBeta = 3.0,
                Plans = new List<SummationPlanEntry>
                {
                    new SummationPlanEntry { CourseId = "C1", PlanId = "PlanRef", DisplayLabel = "Ref",
                        NumberOfFractions = 25, TotalDoseGy = 50, Weight = 1.0, IsReference = true }
                }
            };
            svc.PrepareData(config).Success.Should().BeTrue();
            await svc.ComputeAsync(null, CancellationToken.None);

            var dvh = svc.ComputeStructureEQD2DVH("BODY", structureAlphaBeta: 3.0, maxDoseGy: 15);

            dvh.Should().NotBeEmpty();
            // Cumulative DVH must be monotonically non-increasing.
            for (int i = 1; i < dvh.Length; i++)
                dvh[i].VolumePercent.Should().BeLessOrEqualTo(dvh[i - 1].VolumePercent + 0.01,
                    $"cumulative DVH must not grow at bin {i}");
            // First bin (dose 0) should be ~100% volume.
            dvh[0].VolumePercent.Should().BeApproximately(100.0, 1.0);
        }

        // ── Round-trip: GetSummedSlice / GetStructureMask ──────────────────

        [Fact]
        public async Task GetSummedSlice_OutOfRangeIndex_ReturnsNull()
        {
            var loader = MakeLoader(refDoseGy: 1, movingDoseGy: 0);
            var svc = new SummationService(MakeReferenceCt(), loader.Object, new List<RegistrationData>());
            svc.PrepareData(MakeConfig()).Success.Should().BeTrue();
            await svc.ComputeAsync(null, CancellationToken.None);

            svc.GetSummedSlice(-1).Should().BeNull();
            svc.GetSummedSlice(RefZ).Should().BeNull();
            svc.GetSummedSlice(RefZ + 10).Should().BeNull();
        }

        [Fact]
        public void GetSummedSlice_BeforeCompute_ReturnsNull()
        {
            var loader = MakeLoader(refDoseGy: 1, movingDoseGy: 0);
            var svc = new SummationService(MakeReferenceCt(), loader.Object, new List<RegistrationData>());
            svc.PrepareData(MakeConfig());
            svc.GetSummedSlice(0).Should().BeNull("no compute → no data");
        }

        /// <summary>Builds a single polygon covering the entire reference slice, repeated for all Z.</summary>
        private static Dictionary<int, List<double[][]>> BuildWholeVolumeStructure()
        {
            var dict = new Dictionary<int, List<double[][]>>();
            // Reference CT origin at (0,0,0), spacing 1×1×1, size 4×4×2 → slice extent 0..3 in x,y.
            // Build a closed square polygon that covers the full slice. Contour points are world mm.
            for (int z = 0; z < RefZ; z++)
            {
                double zMm = z; // identity direction → z index == z mm
                var polygon = new double[][]
                {
                    new double[] { -0.5, -0.5, zMm },
                    new double[] {  3.5, -0.5, zMm },
                    new double[] {  3.5,  3.5, zMm },
                    new double[] { -0.5,  3.5, zMm },
                };
                dict[z] = new List<double[][]> { polygon };
            }
            return dict;
        }
    }
}
