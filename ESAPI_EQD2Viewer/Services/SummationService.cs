using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using ESAPI_EQD2Viewer.Core.Calculations;
using ESAPI_EQD2Viewer.Core.Extensions;
using ESAPI_EQD2Viewer.Core.Interfaces;
using ESAPI_EQD2Viewer.Core.Models;

namespace ESAPI_EQD2Viewer.Services
{
    /// <summary>
    /// Two-phase dose summation service.
    /// 
    /// PHASE 1 — PrepareData() — UI THREAD (required by ESAPI)
    ///   Reads all ESAPI objects (dose voxels, geometry) into plain arrays/doubles.
    ///   Typically 2-5 seconds. UI is briefly busy but not for minutes.
    /// 
    /// PHASE 2 — ComputeAsync() — BACKGROUND THREAD (no ESAPI calls)
    ///   Pure arithmetic on cached arrays. Reports progress. Can be cancelled.
    ///   Typically 2-10 seconds depending on grid size.
    /// 
    /// PERFORMANCE FIX:
    /// The original code called ESAPI properties (COM interop) inside the inner
    /// voxel loop: _referenceImage.Origin.x, dose.XDirection, etc.
    /// For 512×512×200 slices × 15 calls = ~800M COM calls ≈ 13 minutes.
    /// Now all geometry is pre-cached as plain doubles → 0 COM calls in the loop.
    /// </summary>
    public class SummationService : ISummationService
    {
        // Phase 1 output: cached data ready for background computation
        private List<CachedPlanData> _cachedPlans;
        private CachedRefGeometry _refGeo;
        private SummationConfig _config;
        private int _refW, _refH, _refZ;

        // Phase 2 output: summed dose
        private double[][] _summedSlices;
        private double _summedReferenceDoseGy;
        private bool _hasSummedDose;
        private bool _disposed;

        private readonly Patient _patient;
        private readonly Image _referenceImage;

        public bool HasSummedDose => _hasSummedDose;
        public double SummedReferenceDoseGy => _summedReferenceDoseGy;

        public SummationService(Patient patient, Image referenceImage)
        {
            _patient = patient ?? throw new ArgumentNullException(nameof(patient));
            _referenceImage = referenceImage ?? throw new ArgumentNullException(nameof(referenceImage));
        }

        // ====================================================================
        // PHASE 1: Load ESAPI data — MUST run on UI thread
        // ====================================================================

        public SummationResult PrepareData(SummationConfig config)
        {
            if (config == null || config.Plans.Count == 0)
                return new SummationResult { Success = false, StatusMessage = "No plans configured." };

            if (!config.Plans.Any(p => p.IsReference))
                return new SummationResult { Success = false, StatusMessage = "No reference plan selected." };

            try
            {
                _config = config;
                _refW = _referenceImage.XSize;
                _refH = _referenceImage.YSize;
                _refZ = _referenceImage.ZSize;

                // Cache reference CT geometry (all ESAPI reads happen here)
                _refGeo = new CachedRefGeometry
                {
                    Ox = _referenceImage.Origin.x,
                    Oy = _referenceImage.Origin.y,
                    Oz = _referenceImage.Origin.z,
                    // X direction * XRes = world displacement per pixel step in X
                    Xx = _referenceImage.XDirection.x * _referenceImage.XRes,
                    Xy = _referenceImage.XDirection.y * _referenceImage.XRes,
                    Xz = _referenceImage.XDirection.z * _referenceImage.XRes,
                    // Y direction * YRes
                    Yx = _referenceImage.YDirection.x * _referenceImage.YRes,
                    Yy = _referenceImage.YDirection.y * _referenceImage.YRes,
                    Yz = _referenceImage.YDirection.z * _referenceImage.YRes,
                    // Z direction * ZRes
                    Zx = _referenceImage.ZDirection.x * _referenceImage.ZRes,
                    Zy = _referenceImage.ZDirection.y * _referenceImage.ZRes,
                    Zz = _referenceImage.ZDirection.z * _referenceImage.ZRes,
                };

                // Cache each plan's data
                _cachedPlans = new List<CachedPlanData>();
                foreach (var entry in config.Plans)
                {
                    var cached = CachePlanData(entry, config.Method, config.GlobalAlphaBeta);
                    if (cached == null)
                        return new SummationResult
                        {
                            Success = false,
                            StatusMessage = $"Could not load plan: {entry.DisplayLabel}"
                        };
                    _cachedPlans.Add(cached);
                }

                return new SummationResult
                {
                    Success = true,
                    StatusMessage = $"Data loaded: {config.Plans.Count} plans, {_refZ} slices. Computing..."
                };
            }
            catch (Exception ex)
            {
                return new SummationResult { Success = false, StatusMessage = $"Load error: {ex.Message}" };
            }
        }

        /// <summary>
        /// Reads all ESAPI data for one plan into plain arrays and doubles.
        /// After this, no ESAPI calls are needed for computation.
        /// </summary>
        private CachedPlanData CachePlanData(SummationPlanEntry entry, SummationMethod method, double alphaBeta)
        {
            var course = _patient.Courses.FirstOrDefault(c => c.Id == entry.CourseId);
            if (course == null) return null;
            var plan = course.PlanSetups.FirstOrDefault(p => p.Id == entry.PlanId);
            if (plan?.Dose == null) return null;

            var dose = plan.Dose;
            int dx = dose.XSize, dy = dose.YSize, dz = dose.ZSize;

            // ---- Preload all dose voxels ----
            int[][,] voxels = new int[dz][,];
            for (int z = 0; z < dz; z++)
            {
                voxels[z] = new int[dx, dy];
                dose.GetVoxels(z, voxels[z]);
            }

            // ---- Dose value scaling ----
            DoseValue dv0 = dose.VoxelToDoseValue(0);
            DoseValue dvRef = dose.VoxelToDoseValue(10000);
            double rawScale = (dvRef.Dose - dv0.Dose) / 10000.0;
            double rawOffset = dv0.Dose;
            double unitToGy;
            if (dvRef.Unit == DoseValue.DoseUnit.Percent) unitToGy = entry.TotalDoseGy / 100.0;
            else if (dvRef.Unit == DoseValue.DoseUnit.cGy) unitToGy = 0.01;
            else unitToGy = 1.0;

            // ---- EQD2 factors ----
            double eqd2Q = 0, eqd2L = 1.0;
            bool useEqd2 = method == SummationMethod.EQD2 && entry.NumberOfFractions > 0 && alphaBeta > 0;
            if (useEqd2)
                EQD2Calculator.GetVoxelScalingFactors(entry.NumberOfFractions, alphaBeta, out eqd2Q, out eqd2L);

            // ---- Cache dose grid geometry as plain doubles ----
            var dg = new CachedDoseGeometry
            {
                Ox = dose.Origin.x,
                Oy = dose.Origin.y,
                Oz = dose.Origin.z,
                XDx = dose.XDirection.x,
                XDy = dose.XDirection.y,
                XDz = dose.XDirection.z,
                YDx = dose.YDirection.x,
                YDy = dose.YDirection.y,
                YDz = dose.YDirection.z,
                ZDx = dose.ZDirection.x,
                ZDy = dose.ZDirection.y,
                ZDz = dose.ZDirection.z,
                XRes = dose.XRes,
                YRes = dose.YRes,
                ZRes = dose.ZRes,
                XSize = dx,
                YSize = dy,
                ZSize = dz
            };

            // ---- Registration matrix ----
            double[,] regMatrix = null;
            if (!entry.IsReference && !string.IsNullOrEmpty(entry.RegistrationId))
            {
                var reg = _patient.Registrations?.FirstOrDefault(r => r.Id == entry.RegistrationId);
                if (reg != null)
                    regMatrix = BuildTransformMatrix(reg);
            }

            return new CachedPlanData
            {
                Entry = entry,
                DoseVoxels = voxels,
                DoseGeo = dg,
                RawScale = rawScale,
                RawOffset = rawOffset,
                UnitToGy = unitToGy,
                UseEQD2 = useEqd2,
                EQD2Q = eqd2Q,
                EQD2L = eqd2L,
                Weight = entry.Weight,
                IsReference = entry.IsReference,
                TransformMatrix = regMatrix
            };
        }

        private double[,] BuildTransformMatrix(Registration registration)
        {
            try
            {
                VVector origin = new VVector(0, 0, 0);
                VVector tO = registration.TransformPoint(origin);
                VVector tX = registration.TransformPoint(new VVector(1, 0, 0));
                VVector tY = registration.TransformPoint(new VVector(0, 1, 0));
                VVector tZ = registration.TransformPoint(new VVector(0, 0, 1));

                var M = new double[4, 4];
                M[0, 0] = tX.x - tO.x; M[0, 1] = tY.x - tO.x; M[0, 2] = tZ.x - tO.x; M[0, 3] = tO.x;
                M[1, 0] = tX.y - tO.y; M[1, 1] = tY.y - tO.y; M[1, 2] = tZ.y - tO.y; M[1, 3] = tO.y;
                M[2, 0] = tX.z - tO.z; M[2, 1] = tY.z - tO.z; M[2, 2] = tZ.z - tO.z; M[2, 3] = tO.z;
                M[3, 0] = 0; M[3, 1] = 0; M[3, 2] = 0; M[3, 3] = 1;
                return M;
            }
            catch { return null; }
        }

        // ====================================================================
        // PHASE 2: Compute — runs on BACKGROUND THREAD (no ESAPI calls)
        // ====================================================================

        public Task<SummationResult> ComputeAsync(IProgress<int> progress, CancellationToken ct)
        {
            return Task.Run(() => ComputeCore(progress, ct), ct);
        }

        private SummationResult ComputeCore(IProgress<int> progress, CancellationToken ct)
        {
            try
            {
                int refW = _refW, refH = _refH, refZ = _refZ;
                _summedSlices = new double[refZ][];
                double globalMax = 0;

                for (int z = 0; z < refZ; z++)
                {
                    ct.ThrowIfCancellationRequested();

                    double[] sliceData = new double[refW * refH];

                    foreach (var cp in _cachedPlans)
                    {
                        if (cp.IsReference || cp.TransformMatrix == null)
                            AccumulateDirect(cp, z, refW, refH, sliceData);
                        else
                            AccumulateRegistered(cp, z, refW, refH, sliceData);
                    }

                    for (int i = 0; i < sliceData.Length; i++)
                        if (sliceData[i] > globalMax) globalMax = sliceData[i];

                    _summedSlices[z] = sliceData;

                    // Report progress every 4 slices to avoid overhead
                    if (z % 4 == 0)
                        progress?.Report((int)((z + 1) * 100.0 / refZ));
                }

                progress?.Report(100);

                // Compute summed reference dose
                _summedReferenceDoseGy = 0;
                foreach (var entry in _config.Plans)
                {
                    double refDose = entry.TotalDoseGy * (entry.PlanNormalization / 100.0);
                    if (_config.Method == SummationMethod.EQD2)
                        refDose = EQD2Calculator.ToEQD2(refDose, entry.NumberOfFractions, _config.GlobalAlphaBeta);
                    _summedReferenceDoseGy += refDose * entry.Weight;
                }

                _hasSummedDose = true;

                string label = _config.Method == SummationMethod.EQD2 ? "EQD2" : "Physical";
                return new SummationResult
                {
                    Success = true,
                    MaxDoseGy = globalMax,
                    TotalReferenceDoseGy = _summedReferenceDoseGy,
                    SliceCount = refZ,
                    StatusMessage = $"[{label} Sum] {_config.Plans.Count} plans | " +
                                    $"Max: {globalMax:F2} Gy | Ref: {_summedReferenceDoseGy:F2} Gy"
                };
            }
            catch (OperationCanceledException)
            {
                _hasSummedDose = false;
                return new SummationResult { Success = false, StatusMessage = "Summation cancelled." };
            }
            catch (Exception ex)
            {
                _hasSummedDose = false;
                return new SummationResult { Success = false, StatusMessage = $"Compute error: {ex.Message}" };
            }
        }

        // ====================================================================
        // SAME CT / NO REGISTRATION — fast affine path
        // Pre-computes per-slice constants, then simple loop with 0 ESAPI calls.
        // ====================================================================

        private void AccumulateDirect(CachedPlanData cp, int sliceZ, int refW, int refH, double[] sliceData)
        {
            var dg = cp.DoseGeo;
            var rg = _refGeo;

            // CT slice plane base point in world coordinates
            double baseWx = rg.Ox + sliceZ * rg.Zx;
            double baseWy = rg.Oy + sliceZ * rg.Zy;
            double baseWz = rg.Oz + sliceZ * rg.Zz;

            // Difference from dose origin
            double diffX = baseWx - dg.Ox;
            double diffY = baseWy - dg.Oy;
            double diffZ = baseWz - dg.Oz;

            // Dose Z index for this slice
            double zDose = (diffX * dg.ZDx + diffY * dg.ZDy + diffZ * dg.ZDz) / dg.ZRes;
            int doseSliceZ = (int)Math.Round(zDose);
            if (doseSliceZ < 0 || doseSliceZ >= dg.ZSize) return;

            // Base dose grid position (at pixel 0,0 of this CT slice)
            double baseDx = (diffX * dg.XDx + diffY * dg.XDy + diffZ * dg.XDz) / dg.XRes;
            double baseDy = (diffX * dg.YDx + diffY * dg.YDy + diffZ * dg.YDz) / dg.YRes;

            // Per-pixel increments in dose grid space
            // CT X step → world = (rg.Xx, rg.Xy, rg.Xz), project onto dose axes
            double dxPerPx = (rg.Xx * dg.XDx + rg.Xy * dg.XDy + rg.Xz * dg.XDz) / dg.XRes;
            double dyPerPx = (rg.Xx * dg.YDx + rg.Xy * dg.YDy + rg.Xz * dg.YDz) / dg.YRes;
            // CT Y step → world = (rg.Yx, rg.Yy, rg.Yz), project onto dose axes
            double dxPerPy = (rg.Yx * dg.XDx + rg.Yy * dg.XDy + rg.Yz * dg.XDz) / dg.XRes;
            double dyPerPy = (rg.Yx * dg.YDx + rg.Yy * dg.YDy + rg.Yz * dg.YDz) / dg.YRes;

            int dxSize = dg.XSize, dySize = dg.YSize;
            int[,] doseSlice = cp.DoseVoxels[doseSliceZ];
            double weight = cp.Weight;
            double rawScale = cp.RawScale, rawOffset = cp.RawOffset, unitToGy = cp.UnitToGy;
            bool useEqd2 = cp.UseEQD2;
            double eq = cp.EQD2Q, el = cp.EQD2L;

            for (int py = 0; py < refH; py++)
            {
                double rx = baseDx + py * dxPerPy;
                double ry = baseDy + py * dyPerPy;
                int ro = py * refW;

                for (int px = 0; px < refW; px++)
                {
                    double fx = rx + px * dxPerPx;
                    double fy = ry + px * dyPerPx;

                    double dGy = BilinearSample(doseSlice, dxSize, dySize, fx, fy, rawScale, rawOffset, unitToGy);
                    if (dGy <= 0) continue;

                    if (useEqd2)
                        dGy = dGy * dGy * eq + dGy * el; // EQD2Fast inlined

                    sliceData[ro + px] += dGy * weight;
                }
            }
        }

        // ====================================================================
        // DIFFERENT CT WITH REGISTRATION
        // 
        // CRITICAL FIX: The original code called ESAPI properties (COM interop)
        // ~15 times PER VOXEL inside the inner loop. For 512×512×200 slices:
        //   52M iterations × 15 COM calls × ~1µs = ~13 minutes
        //
        // Fixed: Pre-compose the full affine chain (CT pixel → world → 
        // registered → dose grid) into per-slice constants. Inner loop has
        // ZERO COM calls — just 6 multiply-adds per voxel for dose grid coords.
        // ====================================================================

        private void AccumulateRegistered(CachedPlanData cp, int sliceZ, int refW, int refH, double[] sliceData)
        {
            var dg = cp.DoseGeo;
            var rg = _refGeo;
            var M = cp.TransformMatrix;

            // === Pre-compose: CT pixel (px, py, fixed z) → registered world ===
            //
            // World position:
            //   w = ctOrigin + px * ctX + py * ctY + z * ctZ
            //
            // Registered position:
            //   r = M * w = M * (ctOrigin + px*ctX + py*ctY + z*ctZ)
            //     = M*ctOrigin + M*z*ctZ + px*(M*ctX) + py*(M*ctY)
            //     = R_base(z) + px * R_px + py * R_py
            //
            // All M* terms are constant per-slice — compute once.

            // M * ctX (per-pixel-X offset in registered space)
            double rpxX = M[0, 0] * rg.Xx + M[0, 1] * rg.Xy + M[0, 2] * rg.Xz;
            double rpxY = M[1, 0] * rg.Xx + M[1, 1] * rg.Xy + M[1, 2] * rg.Xz;
            double rpxZ = M[2, 0] * rg.Xx + M[2, 1] * rg.Xy + M[2, 2] * rg.Xz;

            // M * ctY (per-pixel-Y offset)
            double rpyX = M[0, 0] * rg.Yx + M[0, 1] * rg.Yy + M[0, 2] * rg.Yz;
            double rpyY = M[1, 0] * rg.Yx + M[1, 1] * rg.Yy + M[1, 2] * rg.Yz;
            double rpyZ = M[2, 0] * rg.Yx + M[2, 1] * rg.Yy + M[2, 2] * rg.Yz;

            // M * (ctOrigin + z * ctZ) = base registered position for this slice
            double bwx = rg.Ox + sliceZ * rg.Zx;
            double bwy = rg.Oy + sliceZ * rg.Zy;
            double bwz = rg.Oz + sliceZ * rg.Zz;
            double rbX = M[0, 0] * bwx + M[0, 1] * bwy + M[0, 2] * bwz + M[0, 3];
            double rbY = M[1, 0] * bwx + M[1, 1] * bwy + M[1, 2] * bwz + M[1, 3];
            double rbZ = M[2, 0] * bwx + M[2, 1] * bwy + M[2, 2] * bwz + M[2, 3];

            // === Pre-compose: registered world → dose grid index ===
            //
            // fdx = ((r - doseOrigin) · doseXDir) / doseXRes
            //     = (r · doseXDir - doseOrigin · doseXDir) / doseXRes
            //
            // Since r = rb + px*rpx + py*rpy, this is linear in px,py:
            //   fdx = baseFdx + px * fdxPerPx + py * fdxPerPy

            // Dose origin dot products (constants)
            double dOrigDotX = (dg.Ox * dg.XDx + dg.Oy * dg.XDy + dg.Oz * dg.XDz);
            double dOrigDotY = (dg.Ox * dg.YDx + dg.Oy * dg.YDy + dg.Oz * dg.YDz);
            double dOrigDotZ = (dg.Ox * dg.ZDx + dg.Oy * dg.ZDy + dg.Oz * dg.ZDz);

            // Base dose grid position (at px=0, py=0 for this slice)
            double baseFdx = ((rbX * dg.XDx + rbY * dg.XDy + rbZ * dg.XDz) - dOrigDotX) / dg.XRes;
            double baseFdy = ((rbX * dg.YDx + rbY * dg.YDy + rbZ * dg.YDz) - dOrigDotY) / dg.YRes;
            double baseFdz = ((rbX * dg.ZDx + rbY * dg.ZDy + rbZ * dg.ZDz) - dOrigDotZ) / dg.ZRes;

            // Per-pixel-X increments in dose grid
            double fdxPerPx = (rpxX * dg.XDx + rpxY * dg.XDy + rpxZ * dg.XDz) / dg.XRes;
            double fdyPerPx = (rpxX * dg.YDx + rpxY * dg.YDy + rpxZ * dg.YDz) / dg.YRes;
            double fdzPerPx = (rpxX * dg.ZDx + rpxY * dg.ZDy + rpxZ * dg.ZDz) / dg.ZRes;

            // Per-pixel-Y increments in dose grid
            double fdxPerPy = (rpyX * dg.XDx + rpyY * dg.XDy + rpyZ * dg.XDz) / dg.XRes;
            double fdyPerPy = (rpyX * dg.YDx + rpyY * dg.YDy + rpyZ * dg.YDz) / dg.YRes;
            double fdzPerPy = (rpyX * dg.ZDx + rpyY * dg.ZDy + rpyZ * dg.ZDz) / dg.ZRes;

            int dxSize = dg.XSize, dySize = dg.YSize, dzSize = dg.ZSize;
            double weight = cp.Weight;
            double rawScale = cp.RawScale, rawOffset = cp.RawOffset, unitToGy = cp.UnitToGy;
            bool useEqd2 = cp.UseEQD2;
            double eq = cp.EQD2Q, el = cp.EQD2L;

            // === Inner loop: ZERO COM calls, just arithmetic ===
            for (int py = 0; py < refH; py++)
            {
                double rowFdx = baseFdx + py * fdxPerPy;
                double rowFdy = baseFdy + py * fdyPerPy;
                double rowFdz = baseFdz + py * fdzPerPy;
                int ro = py * refW;

                for (int px = 0; px < refW; px++)
                {
                    double fx = rowFdx + px * fdxPerPx;
                    double fy = rowFdy + px * fdyPerPx;
                    double fz = rowFdz + px * fdzPerPx;

                    int iz = (int)Math.Round(fz);
                    if (iz < 0 || iz >= dzSize) continue;

                    int[,] doseSlice = cp.DoseVoxels[iz];
                    double dGy = BilinearSample(doseSlice, dxSize, dySize, fx, fy, rawScale, rawOffset, unitToGy);
                    if (dGy <= 0) continue;

                    if (useEqd2)
                        dGy = dGy * dGy * eq + dGy * el;

                    sliceData[ro + px] += dGy * weight;
                }
            }
        }

        // ====================================================================
        // Bilinear sampling — static, no ESAPI references
        // ====================================================================

        private static double BilinearSample(int[,] grid, int gw, int gh,
            double fx, double fy, double rawScale, double rawOffset, double unitToGy)
        {
            if (fx < 0 || fy < 0 || fx >= gw - 1 || fy >= gh - 1)
            {
                int nx = (int)Math.Round(fx), ny = (int)Math.Round(fy);
                return (nx >= 0 && nx < gw && ny >= 0 && ny < gh)
                    ? (grid[nx, ny] * rawScale + rawOffset) * unitToGy : 0;
            }
            int x0 = (int)fx, y0 = (int)fy;
            double tx = fx - x0, ty = fy - y0;
            double raw = grid[x0, y0] * (1 - tx) * (1 - ty)
                       + grid[x0 + 1, y0] * tx * (1 - ty)
                       + grid[x0, y0 + 1] * (1 - tx) * ty
                       + grid[x0 + 1, y0 + 1] * tx * ty;
            return (raw * rawScale + rawOffset) * unitToGy;
        }

        public double[] GetSummedSlice(int sliceIndex)
        {
            if (!_hasSummedDose || _summedSlices == null) return null;
            if (sliceIndex < 0 || sliceIndex >= _summedSlices.Length) return null;
            return _summedSlices[sliceIndex];
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _summedSlices = null;
            _cachedPlans = null;
        }

        // ====================================================================
        // Cached data types — plain doubles, no ESAPI references
        // ====================================================================

        private class CachedRefGeometry
        {
            public double Ox, Oy, Oz;      // CT origin
            public double Xx, Xy, Xz;      // XDirection * XRes
            public double Yx, Yy, Yz;      // YDirection * YRes
            public double Zx, Zy, Zz;      // ZDirection * ZRes
        }

        private class CachedDoseGeometry
        {
            public double Ox, Oy, Oz;      // Dose origin
            public double XDx, XDy, XDz;   // Dose X direction (unit vector)
            public double YDx, YDy, YDz;   // Dose Y direction
            public double ZDx, ZDy, ZDz;   // Dose Z direction
            public double XRes, YRes, ZRes;
            public int XSize, YSize, ZSize;
        }

        private class CachedPlanData
        {
            public SummationPlanEntry Entry;
            public int[][,] DoseVoxels;
            public CachedDoseGeometry DoseGeo;
            public double RawScale, RawOffset, UnitToGy;
            public double Weight;
            public bool IsReference;
            public bool UseEQD2;
            public double EQD2Q, EQD2L;
            public double[,] TransformMatrix;   // 4x4 or null
        }
    }
}