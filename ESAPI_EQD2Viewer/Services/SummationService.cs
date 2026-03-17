using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using ESAPI_EQD2Viewer.Core.Calculations;
using ESAPI_EQD2Viewer.Core.Extensions;
using ESAPI_EQD2Viewer.Core.Interfaces;
using ESAPI_EQD2Viewer.Core.Logging;
using ESAPI_EQD2Viewer.Core.Models;

namespace ESAPI_EQD2Viewer.Services
{
    /// <summary>
    /// Two-phase dose summation with structure mask caching for DVH.
    /// 
    /// Phase 1 — PrepareData() — UI thread: reads ESAPI data into plain arrays,
    ///   also rasterizes structure contours into bitmasks for DVH.
    /// Phase 2 — ComputeAsync() — background: pure arithmetic.
    /// </summary>
    public class SummationService : ISummationService
    {
        // Phase 1 output
        private List<CachedPlanData> _cachedPlans;
        private CachedRefGeometry _refGeo;
        private SummationConfig _config;
        private int _refW, _refH, _refZ;
        private int _refHuOffset;
        private string _referenceFOR;
        private double _voxelVolumeCc;

        // Structure masks: structureId → [sliceIndex] → bool[w*h]
        private Dictionary<string, bool[][]> _structureMasks;
        private List<string> _cachedStructureIds;

        // Phase 2 output
        private double[][] _summedSlices;
        private double _summedReferenceDoseGy;
        private volatile bool _hasSummedDose;
        private bool _disposed;

        private readonly Patient _patient;
        private readonly Image _referenceImage;

        public bool HasSummedDose => _hasSummedDose;
        public double SummedReferenceDoseGy => _summedReferenceDoseGy;
        public int SliceCount => _refZ;

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

                // Voxel volume in cm³ for DVH
                _voxelVolumeCc = (_referenceImage.XRes * _referenceImage.YRes * _referenceImage.ZRes) / 1000.0;

                _refGeo = new CachedRefGeometry
                {
                    Ox = _referenceImage.Origin.x,
                    Oy = _referenceImage.Origin.y,
                    Oz = _referenceImage.Origin.z,
                    Xx = _referenceImage.XDirection.x * _referenceImage.XRes,
                    Xy = _referenceImage.XDirection.y * _referenceImage.XRes,
                    Xz = _referenceImage.XDirection.z * _referenceImage.XRes,
                    Yx = _referenceImage.YDirection.x * _referenceImage.YRes,
                    Yy = _referenceImage.YDirection.y * _referenceImage.YRes,
                    Yz = _referenceImage.YDirection.z * _referenceImage.YRes,
                    Zx = _referenceImage.ZDirection.x * _referenceImage.ZRes,
                    Zy = _referenceImage.ZDirection.y * _referenceImage.ZRes,
                    Zz = _referenceImage.ZDirection.z * _referenceImage.ZRes,
                };

                _refHuOffset = DetermineHuOffset(_referenceImage);
                _referenceFOR = _referenceImage.FOR ?? "";

                _cachedPlans = new List<CachedPlanData>();
                foreach (var entry in config.Plans)
                {
                    var cached = CachePlanData(entry, config.Method, config.GlobalAlphaBeta, _referenceFOR);
                    if (cached == null)
                        return new SummationResult
                        {
                            Success = false,
                            StatusMessage = $"Could not load plan: {entry.DisplayLabel}"
                        };
                    _cachedPlans.Add(cached);
                }

                // Cache structure masks from the reference plan for DVH calculation
                CacheStructureMasks();

                return new SummationResult
                {
                    Success = true,
                    StatusMessage = $"Loaded {config.Plans.Count} plans, {_refZ} slices. Computing..."
                };
            }
            catch (Exception ex)
            {
                SimpleLogger.Error("PrepareData failed", ex);
                return new SummationResult { Success = false, StatusMessage = $"Load error: {ex.Message}" };
            }
        }

        /// <summary>
        /// Rasterizes all non-empty structures from the reference plan's structure set
        /// into per-slice bitmasks for DVH computation.
        /// </summary>
        private void CacheStructureMasks()
        {
            _structureMasks = new Dictionary<string, bool[][]>();
            _cachedStructureIds = new List<string>();

            // Find the reference plan to get its structure set
            var refEntry = _config.Plans.FirstOrDefault(p => p.IsReference);
            if (refEntry == null) return;

            var course = _patient.Courses.FirstOrDefault(c => c.Id == refEntry.CourseId);
            var plan = course?.PlanSetups.FirstOrDefault(p => p.Id == refEntry.PlanId);
            if (plan?.StructureSet == null) return;

            double imgOx = _referenceImage.Origin.x;
            double imgOy = _referenceImage.Origin.y;
            double imgOz = _referenceImage.Origin.z;
            double xDirX = _referenceImage.XDirection.x;
            double xDirY = _referenceImage.XDirection.y;
            double xDirZ = _referenceImage.XDirection.z;
            double yDirX = _referenceImage.YDirection.x;
            double yDirY = _referenceImage.YDirection.y;
            double yDirZ = _referenceImage.YDirection.z;
            double xRes = _referenceImage.XRes;
            double yRes = _referenceImage.YRes;

            foreach (var structure in plan.StructureSet.Structures)
            {
                if (structure.IsEmpty) continue;

                try
                {
                    bool[][] sliceMasks = new bool[_refZ][];
                    bool hasAnyContour = false;

                    for (int z = 0; z < _refZ; z++)
                    {
                        var contours = structure.GetContoursOnImagePlane(z);
                        if (contours == null || contours.Length == 0)
                        {
                            sliceMasks[z] = null; // No contour on this slice
                            continue;
                        }

                        hasAnyContour = true;
                        var masks = new List<bool[]>();

                        foreach (var contour in contours)
                        {
                            if (contour.Length < 3) continue;

                            // Convert world coordinates to CT pixel coordinates
                            var pixelPoints = new Point[contour.Length];
                            for (int i = 0; i < contour.Length; i++)
                            {
                                double dx = contour[i].x - imgOx;
                                double dy = contour[i].y - imgOy;
                                double dz = contour[i].z - imgOz;
                                double px = (dx * xDirX + dy * xDirY + dz * xDirZ) / xRes;
                                double py = (dx * yDirX + dy * yDirY + dz * yDirZ) / yRes;
                                pixelPoints[i] = new Point(px, py);
                            }

                            masks.Add(StructureRasterizer.RasterizePolygon(pixelPoints, _refW, _refH));
                        }

                        sliceMasks[z] = StructureRasterizer.CombineContourMasks(masks, _refW, _refH);
                    }

                    if (hasAnyContour)
                    {
                        _structureMasks[structure.Id] = sliceMasks;
                        _cachedStructureIds.Add(structure.Id);
                    }
                }
                catch (Exception ex)
                {
                    SimpleLogger.Warning($"Could not rasterize structure '{structure.Id}': {ex.Message}");
                }
            }

            SimpleLogger.Info($"Cached {_cachedStructureIds.Count} structure masks for DVH");
        }

        private int DetermineHuOffset(Image ctImage)
        {
            int midSlice = ctImage.ZSize / 2;
            var slice = new int[ctImage.XSize, ctImage.YSize];
            ctImage.GetVoxels(midSlice, slice);
            return ImageUtils.DetermineHuOffset(slice, ctImage.XSize, ctImage.YSize);
        }

        /// <summary>
        /// Reads all ESAPI data for one plan into plain arrays.
        /// Fix #3: proper logging instead of silent catches.
        /// Fix #6: uses MatrixMath.Invert4x4 for proper inversion.
        /// </summary>
        private CachedPlanData CachePlanData(SummationPlanEntry entry, SummationMethod method,
            double alphaBeta, string referenceFOR)
        {
            var course = _patient.Courses.FirstOrDefault(c => c.Id == entry.CourseId);
            if (course == null)
            {
                SimpleLogger.Error($"Course not found: {entry.CourseId}");
                return null;
            }

            var plan = course.PlanSetups.FirstOrDefault(p => p.Id == entry.PlanId);
            if (plan?.Dose == null)
            {
                SimpleLogger.Error($"Plan or dose not found: {entry.PlanId}");
                return null;
            }

            var dose = plan.Dose;
            int dx = dose.XSize, dy = dose.YSize, dz = dose.ZSize;

            int[][,] doseVoxels = new int[dz][,];
            for (int z = 0; z < dz; z++)
            {
                doseVoxels[z] = new int[dx, dy];
                dose.GetVoxels(z, doseVoxels[z]);
            }

            DoseValue dv0 = dose.VoxelToDoseValue(0);
            DoseValue dvRef = dose.VoxelToDoseValue(RenderConstants.DoseCalibrationRawValue);
            double rawScale = (dvRef.Dose - dv0.Dose) / (double)RenderConstants.DoseCalibrationRawValue;
            double rawOffset = dv0.Dose;
            double unitToGy;
            if (dvRef.Unit == DoseValue.DoseUnit.Percent) unitToGy = entry.TotalDoseGy / 100.0;
            else if (dvRef.Unit == DoseValue.DoseUnit.cGy) unitToGy = 0.01;
            else unitToGy = 1.0;

            double eqd2Q = 0, eqd2L = 1.0;
            bool useEqd2 = method == SummationMethod.EQD2 && entry.NumberOfFractions > 0 && alphaBeta > 0;
            if (useEqd2)
                EQD2Calculator.GetVoxelScalingFactors(entry.NumberOfFractions, alphaBeta, out eqd2Q, out eqd2L);

            var dg = new CachedDoseGeometry
            {
                Ox = dose.Origin.x, Oy = dose.Origin.y, Oz = dose.Origin.z,
                XDx = dose.XDirection.x, XDy = dose.XDirection.y, XDz = dose.XDirection.z,
                YDx = dose.YDirection.x, YDy = dose.YDirection.y, YDz = dose.YDirection.z,
                ZDx = dose.ZDirection.x, ZDy = dose.ZDirection.y, ZDz = dose.ZDirection.z,
                XRes = dose.XRes, YRes = dose.YRes, ZRes = dose.ZRes,
                XSize = dx, YSize = dy, ZSize = dz
            };

            // Registration (Fix #6: uses MatrixMath.Invert4x4)
            double[,] regMatrix = null;
            if (!entry.IsReference && !string.IsNullOrEmpty(entry.RegistrationId))
            {
                var reg = _patient.Registrations?.FirstOrDefault(r => r.Id == entry.RegistrationId);
                if (reg != null)
                {
                    var forwardMatrix = BuildTransformMatrix(reg);
                    if (forwardMatrix != null)
                    {
                        string planFOR = "";
                        try { planFOR = plan.StructureSet?.Image?.FOR ?? ""; }
                        catch (Exception ex) { SimpleLogger.Warning($"Could not get plan FOR: {ex.Message}"); }

                        bool sourceIsRef = string.Equals(reg.SourceFOR, referenceFOR, StringComparison.OrdinalIgnoreCase);
                        bool registeredIsPlan = string.Equals(reg.RegisteredFOR, planFOR, StringComparison.OrdinalIgnoreCase);
                        bool sourceIsPlan = string.Equals(reg.SourceFOR, planFOR, StringComparison.OrdinalIgnoreCase);
                        bool registeredIsRef = string.Equals(reg.RegisteredFOR, referenceFOR, StringComparison.OrdinalIgnoreCase);

                        if (sourceIsRef && registeredIsPlan)
                        {
                            regMatrix = forwardMatrix;
                            SimpleLogger.Info($"Registration {reg.Id}: ref→plan (direct)");
                        }
                        else if (sourceIsPlan && registeredIsRef)
                        {
                            regMatrix = MatrixMath.Invert4x4(forwardMatrix);
                            if (regMatrix == null)
                                SimpleLogger.Error($"Registration {reg.Id}: matrix inversion failed (singular)");
                            else
                                SimpleLogger.Info($"Registration {reg.Id}: plan→ref (inverted)");
                        }
                        else
                        {
                            regMatrix = forwardMatrix;
                            SimpleLogger.Warning($"Registration {reg.Id}: unexpected FOR direction, using forward as fallback");
                        }
                    }
                }
            }

            // Cache CT voxels for non-reference plans (registration overlay)
            int[][,] ctVoxels = null;
            CachedCtGeometry ctGeo = null;
            int ctHuOffset = 0;

            if (!entry.IsReference)
            {
                try
                {
                    var img = plan.StructureSet?.Image;
                    if (img != null)
                    {
                        int cx = img.XSize, cy = img.YSize, cz = img.ZSize;
                        ctVoxels = new int[cz][,];
                        for (int z = 0; z < cz; z++)
                        {
                            ctVoxels[z] = new int[cx, cy];
                            img.GetVoxels(z, ctVoxels[z]);
                        }

                        ctGeo = new CachedCtGeometry
                        {
                            Ox = img.Origin.x, Oy = img.Origin.y, Oz = img.Origin.z,
                            XDx = img.XDirection.x, XDy = img.XDirection.y, XDz = img.XDirection.z,
                            YDx = img.YDirection.x, YDy = img.YDirection.y, YDz = img.YDirection.z,
                            ZDx = img.ZDirection.x, ZDy = img.ZDirection.y, ZDz = img.ZDirection.z,
                            XRes = img.XRes, YRes = img.YRes, ZRes = img.ZRes,
                            XSize = cx, YSize = cy, ZSize = cz
                        };

                        int midZ = cz / 2;
                        ctHuOffset = ImageUtils.DetermineHuOffset(ctVoxels[midZ], cx, cy);
                    }
                }
                catch (Exception ex)
                {
                    SimpleLogger.Warning($"CT loading failed for {entry.DisplayLabel}: {ex.Message}");
                }
            }

            return new CachedPlanData
            {
                Entry = entry,
                DoseVoxels = doseVoxels, DoseGeo = dg,
                RawScale = rawScale, RawOffset = rawOffset, UnitToGy = unitToGy,
                UseEQD2 = useEqd2, EQD2Q = eqd2Q, EQD2L = eqd2L,
                Weight = entry.Weight, IsReference = entry.IsReference,
                TransformMatrix = regMatrix,
                CtVoxels = ctVoxels, CtGeo = ctGeo, CtHuOffset = ctHuOffset
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
            catch (Exception ex)
            {
                SimpleLogger.Error($"BuildTransformMatrix failed for {registration.Id}", ex);
                return null;
            }
        }

        // ====================================================================
        // PHASE 2: Compute — BACKGROUND THREAD
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

                    if (z % RenderConstants.SummationProgressInterval == 0)
                        progress?.Report((int)((z + 1) * 100.0 / refZ));
                }

                progress?.Report(100);

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
                SimpleLogger.Error("ComputeCore failed", ex);
                return new SummationResult { Success = false, StatusMessage = $"Compute error: {ex.Message}" };
            }
        }

        // ====================================================================
        // Structure mask API
        // ====================================================================

        public bool[] GetStructureMask(string structureId, int sliceIndex)
        {
            if (_structureMasks == null || string.IsNullOrEmpty(structureId))
                return null;

            if (!_structureMasks.TryGetValue(structureId, out var masks))
                return null;

            if (sliceIndex < 0 || sliceIndex >= masks.Length)
                return null;

            return masks[sliceIndex];
        }

        public IReadOnlyList<string> GetCachedStructureIds()
        {
            return _cachedStructureIds ?? (IReadOnlyList<string>)new string[0];
        }

        public double GetVoxelVolumeCc()
        {
            return _voxelVolumeCc;
        }

        // ====================================================================
        // CT Overlay
        // ====================================================================

        public int[] GetRegisteredCtSlice(string planDisplayLabel, int sliceIndex)
        {
            if (_cachedPlans == null || string.IsNullOrEmpty(planDisplayLabel))
                return null;

            var cp = _cachedPlans.FirstOrDefault(p =>
                p.Entry.DisplayLabel == planDisplayLabel && !p.IsReference);

            if (cp == null || cp.CtVoxels == null || cp.CtGeo == null)
                return null;

            if (sliceIndex < 0 || sliceIndex >= _refZ)
                return null;

            int refW = _refW, refH = _refH;
            int[] result = new int[refW * refH];

            var cg = cp.CtGeo;
            var rg = _refGeo;

            if (cp.TransformMatrix == null)
            {
                double baseWx = rg.Ox + sliceIndex * rg.Zx;
                double baseWy = rg.Oy + sliceIndex * rg.Zy;
                double baseWz = rg.Oz + sliceIndex * rg.Zz;

                double diffX = baseWx - cg.Ox, diffY = baseWy - cg.Oy, diffZ = baseWz - cg.Oz;
                double zCt = (diffX * cg.ZDx + diffY * cg.ZDy + diffZ * cg.ZDz) / cg.ZRes;
                int ctSliceZ = (int)Math.Round(zCt);
                if (ctSliceZ < 0 || ctSliceZ >= cg.ZSize) return result;

                double baseCx = (diffX * cg.XDx + diffY * cg.XDy + diffZ * cg.XDz) / cg.XRes;
                double baseCy = (diffX * cg.YDx + diffY * cg.YDy + diffZ * cg.YDz) / cg.YRes;

                double dxPerPx = (rg.Xx * cg.XDx + rg.Xy * cg.XDy + rg.Xz * cg.XDz) / cg.XRes;
                double dyPerPx = (rg.Xx * cg.YDx + rg.Xy * cg.YDy + rg.Xz * cg.YDz) / cg.YRes;
                double dxPerPy = (rg.Yx * cg.XDx + rg.Yy * cg.XDy + rg.Yz * cg.XDz) / cg.XRes;
                double dyPerPy = (rg.Yx * cg.YDx + rg.Yy * cg.YDy + rg.Yz * cg.YDz) / cg.YRes;

                int[,] ctSlice = cp.CtVoxels[ctSliceZ];
                int cxSize = cg.XSize, cySize = cg.YSize;
                int huOff = cp.CtHuOffset;

                for (int py = 0; py < refH; py++)
                {
                    double rx = baseCx + py * dxPerPy;
                    double ry = baseCy + py * dyPerPy;
                    int ro = py * refW;
                    for (int px = 0; px < refW; px++)
                    {
                        int ix = (int)Math.Round(rx + px * dxPerPx);
                        int iy = (int)Math.Round(ry + px * dyPerPx);
                        if (ix >= 0 && ix < cxSize && iy >= 0 && iy < cySize)
                            result[ro + px] = ctSlice[ix, iy] - huOff;
                    }
                }
            }
            else
            {
                var M = cp.TransformMatrix;

                double rpxX = M[0, 0] * rg.Xx + M[0, 1] * rg.Xy + M[0, 2] * rg.Xz;
                double rpxY = M[1, 0] * rg.Xx + M[1, 1] * rg.Xy + M[1, 2] * rg.Xz;
                double rpxZ = M[2, 0] * rg.Xx + M[2, 1] * rg.Xy + M[2, 2] * rg.Xz;

                double rpyX = M[0, 0] * rg.Yx + M[0, 1] * rg.Yy + M[0, 2] * rg.Yz;
                double rpyY = M[1, 0] * rg.Yx + M[1, 1] * rg.Yy + M[1, 2] * rg.Yz;
                double rpyZ = M[2, 0] * rg.Yx + M[2, 1] * rg.Yy + M[2, 2] * rg.Yz;

                double bwx = rg.Ox + sliceIndex * rg.Zx;
                double bwy = rg.Oy + sliceIndex * rg.Zy;
                double bwz = rg.Oz + sliceIndex * rg.Zz;
                double rbX = M[0, 0] * bwx + M[0, 1] * bwy + M[0, 2] * bwz + M[0, 3];
                double rbY = M[1, 0] * bwx + M[1, 1] * bwy + M[1, 2] * bwz + M[1, 3];
                double rbZ = M[2, 0] * bwx + M[2, 1] * bwy + M[2, 2] * bwz + M[2, 3];

                double cOrigDotX = cg.Ox * cg.XDx + cg.Oy * cg.XDy + cg.Oz * cg.XDz;
                double cOrigDotY = cg.Ox * cg.YDx + cg.Oy * cg.YDy + cg.Oz * cg.YDz;
                double cOrigDotZ = cg.Ox * cg.ZDx + cg.Oy * cg.ZDy + cg.Oz * cg.ZDz;

                double baseFcx = ((rbX * cg.XDx + rbY * cg.XDy + rbZ * cg.XDz) - cOrigDotX) / cg.XRes;
                double baseFcy = ((rbX * cg.YDx + rbY * cg.YDy + rbZ * cg.YDz) - cOrigDotY) / cg.YRes;
                double baseFcz = ((rbX * cg.ZDx + rbY * cg.ZDy + rbZ * cg.ZDz) - cOrigDotZ) / cg.ZRes;

                double fcxPerPx = (rpxX * cg.XDx + rpxY * cg.XDy + rpxZ * cg.XDz) / cg.XRes;
                double fcyPerPx = (rpxX * cg.YDx + rpxY * cg.YDy + rpxZ * cg.YDz) / cg.YRes;
                double fczPerPx = (rpxX * cg.ZDx + rpxY * cg.ZDy + rpxZ * cg.ZDz) / cg.ZRes;

                double fcxPerPy = (rpyX * cg.XDx + rpyY * cg.XDy + rpyZ * cg.XDz) / cg.XRes;
                double fcyPerPy = (rpyX * cg.YDx + rpyY * cg.YDy + rpyZ * cg.YDz) / cg.YRes;
                double fczPerPy = (rpyX * cg.ZDx + rpyY * cg.ZDy + rpyZ * cg.ZDz) / cg.ZRes;

                int cxSize = cg.XSize, cySize = cg.YSize, czSize = cg.ZSize;
                int huOff = cp.CtHuOffset;

                for (int py = 0; py < refH; py++)
                {
                    double rowFcx = baseFcx + py * fcxPerPy;
                    double rowFcy = baseFcy + py * fcyPerPy;
                    double rowFcz = baseFcz + py * fczPerPy;
                    int ro = py * refW;

                    for (int px = 0; px < refW; px++)
                    {
                        double fz = rowFcz + px * fczPerPx;
                        int iz = (int)Math.Round(fz);
                        if (iz < 0 || iz >= czSize) continue;

                        int ix = (int)Math.Round(rowFcx + px * fcxPerPx);
                        int iy = (int)Math.Round(rowFcy + px * fcyPerPx);
                        if (ix >= 0 && ix < cxSize && iy >= 0 && iy < cySize)
                            result[ro + px] = cp.CtVoxels[iz][ix, iy] - huOff;
                    }
                }
            }

            return result;
        }

        // ====================================================================
        // Dose accumulation (Fix #7: uses shared ImageUtils.BilinearSampleRaw)
        // ====================================================================

        private void AccumulateDirect(CachedPlanData cp, int sliceZ, int refW, int refH, double[] sliceData)
        {
            var dg = cp.DoseGeo;
            var rg = _refGeo;

            double baseWx = rg.Ox + sliceZ * rg.Zx;
            double baseWy = rg.Oy + sliceZ * rg.Zy;
            double baseWz = rg.Oz + sliceZ * rg.Zz;

            double diffX = baseWx - dg.Ox, diffY = baseWy - dg.Oy, diffZ = baseWz - dg.Oz;

            double zDose = (diffX * dg.ZDx + diffY * dg.ZDy + diffZ * dg.ZDz) / dg.ZRes;
            int doseSliceZ = (int)Math.Round(zDose);
            if (doseSliceZ < 0 || doseSliceZ >= dg.ZSize) return;

            double baseDx = (diffX * dg.XDx + diffY * dg.XDy + diffZ * dg.XDz) / dg.XRes;
            double baseDy = (diffX * dg.YDx + diffY * dg.YDy + diffZ * dg.YDz) / dg.YRes;

            double dxPerPx = (rg.Xx * dg.XDx + rg.Xy * dg.XDy + rg.Xz * dg.XDz) / dg.XRes;
            double dyPerPx = (rg.Xx * dg.YDx + rg.Xy * dg.YDy + rg.Xz * dg.YDz) / dg.YRes;
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

                    double dGy = ImageUtils.BilinearSampleRaw(doseSlice, dxSize, dySize, fx, fy, rawScale, rawOffset, unitToGy);
                    if (dGy <= 0) continue;

                    if (useEqd2)
                        dGy = dGy * dGy * eq + dGy * el;

                    sliceData[ro + px] += dGy * weight;
                }
            }
        }

        private void AccumulateRegistered(CachedPlanData cp, int sliceZ, int refW, int refH, double[] sliceData)
        {
            var dg = cp.DoseGeo;
            var rg = _refGeo;
            var M = cp.TransformMatrix;

            double rpxX = M[0, 0] * rg.Xx + M[0, 1] * rg.Xy + M[0, 2] * rg.Xz;
            double rpxY = M[1, 0] * rg.Xx + M[1, 1] * rg.Xy + M[1, 2] * rg.Xz;
            double rpxZ = M[2, 0] * rg.Xx + M[2, 1] * rg.Xy + M[2, 2] * rg.Xz;

            double rpyX = M[0, 0] * rg.Yx + M[0, 1] * rg.Yy + M[0, 2] * rg.Yz;
            double rpyY = M[1, 0] * rg.Yx + M[1, 1] * rg.Yy + M[1, 2] * rg.Yz;
            double rpyZ = M[2, 0] * rg.Yx + M[2, 1] * rg.Yy + M[2, 2] * rg.Yz;

            double bwx = rg.Ox + sliceZ * rg.Zx;
            double bwy = rg.Oy + sliceZ * rg.Zy;
            double bwz = rg.Oz + sliceZ * rg.Zz;
            double rbX = M[0, 0] * bwx + M[0, 1] * bwy + M[0, 2] * bwz + M[0, 3];
            double rbY = M[1, 0] * bwx + M[1, 1] * bwy + M[1, 2] * bwz + M[1, 3];
            double rbZ = M[2, 0] * bwx + M[2, 1] * bwy + M[2, 2] * bwz + M[2, 3];

            double dOrigDotX = dg.Ox * dg.XDx + dg.Oy * dg.XDy + dg.Oz * dg.XDz;
            double dOrigDotY = dg.Ox * dg.YDx + dg.Oy * dg.YDy + dg.Oz * dg.YDz;
            double dOrigDotZ = dg.Ox * dg.ZDx + dg.Oy * dg.ZDy + dg.Oz * dg.ZDz;

            double baseFdx = ((rbX * dg.XDx + rbY * dg.XDy + rbZ * dg.XDz) - dOrigDotX) / dg.XRes;
            double baseFdy = ((rbX * dg.YDx + rbY * dg.YDy + rbZ * dg.YDz) - dOrigDotY) / dg.YRes;
            double baseFdz = ((rbX * dg.ZDx + rbY * dg.ZDy + rbZ * dg.ZDz) - dOrigDotZ) / dg.ZRes;

            double fdxPerPx = (rpxX * dg.XDx + rpxY * dg.XDy + rpxZ * dg.XDz) / dg.XRes;
            double fdyPerPx = (rpxX * dg.YDx + rpxY * dg.YDy + rpxZ * dg.YDz) / dg.YRes;
            double fdzPerPx = (rpxX * dg.ZDx + rpxY * dg.ZDy + rpxZ * dg.ZDz) / dg.ZRes;

            double fdxPerPy = (rpyX * dg.XDx + rpyY * dg.XDy + rpyZ * dg.XDz) / dg.XRes;
            double fdyPerPy = (rpyX * dg.YDx + rpyY * dg.YDy + rpyZ * dg.YDz) / dg.YRes;
            double fdzPerPy = (rpyX * dg.ZDx + rpyY * dg.ZDy + rpyZ * dg.ZDz) / dg.ZRes;

            int dxSize = dg.XSize, dySize = dg.YSize, dzSize = dg.ZSize;
            double weight = cp.Weight;
            double rawScale = cp.RawScale, rawOffset = cp.RawOffset, unitToGy = cp.UnitToGy;
            bool useEqd2 = cp.UseEQD2;
            double eq = cp.EQD2Q, el = cp.EQD2L;

            for (int py = 0; py < refH; py++)
            {
                double rowFdx = baseFdx + py * fdxPerPy;
                double rowFdy = baseFdy + py * fdyPerPy;
                double rowFdz = baseFdz + py * fdzPerPy;
                int ro = py * refW;

                for (int px = 0; px < refW; px++)
                {
                    double fz = rowFdz + px * fdzPerPx;
                    int iz = (int)Math.Round(fz);
                    if (iz < 0 || iz >= dzSize) continue;

                    double fx = rowFdx + px * fdxPerPx;
                    double fy = rowFdy + px * fdyPerPx;

                    int[,] doseSlice = cp.DoseVoxels[iz];
                    double dGy = ImageUtils.BilinearSampleRaw(doseSlice, dxSize, dySize, fx, fy, rawScale, rawOffset, unitToGy);
                    if (dGy <= 0) continue;

                    if (useEqd2) dGy = dGy * dGy * eq + dGy * el;
                    sliceData[ro + px] += dGy * weight;
                }
            }
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
            _structureMasks = null;
        }

        // ====================================================================
        // Cached data types
        // ====================================================================

        private class CachedRefGeometry
        {
            public double Ox, Oy, Oz;
            public double Xx, Xy, Xz;
            public double Yx, Yy, Yz;
            public double Zx, Zy, Zz;
        }

        private class CachedDoseGeometry
        {
            public double Ox, Oy, Oz;
            public double XDx, XDy, XDz;
            public double YDx, YDy, YDz;
            public double ZDx, ZDy, ZDz;
            public double XRes, YRes, ZRes;
            public int XSize, YSize, ZSize;
        }

        private class CachedCtGeometry
        {
            public double Ox, Oy, Oz;
            public double XDx, XDy, XDz;
            public double YDx, YDy, YDz;
            public double ZDx, ZDy, ZDz;
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
            public double[,] TransformMatrix;
            public int[][,] CtVoxels;
            public CachedCtGeometry CtGeo;
            public int CtHuOffset;
        }
    }
}
