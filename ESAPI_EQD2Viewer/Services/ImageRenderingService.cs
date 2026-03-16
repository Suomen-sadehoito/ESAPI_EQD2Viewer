using System;
using System.Windows;
using System.Windows.Media.Imaging;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using ESAPI_EQD2Viewer.Core.Interfaces;
using ESAPI_EQD2Viewer.Core.Extensions;
using ESAPI_EQD2Viewer.Core.Models;
using ESAPI_EQD2Viewer.Core.Calculations;

namespace ESAPI_EQD2Viewer.Services
{
    public class ImageRenderingService : IImageRenderingService
    {
        private int _width;
        private int _height;

        private int[][,] _ctCache;
        private int[][,] _doseCache;

        private double _doseRawScale;
        private double _doseRawOffset;
        private double _doseUnitToGyFactor;
        private bool _doseScalingReady;

        // Dose grid geometry (cached for GetDoseAtPixel)
        private Dose _cachedDose;
        private Image _cachedCtImage;

        private int _huOffset;
        private bool _disposed;

        public void Initialize(int width, int height)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive.");
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive.");

            _width = width;
            _height = height;
        }

        public void PreloadData(Image ctImage, Dose dose, double prescriptionDoseGy)
        {
            _cachedCtImage = ctImage;
            _cachedDose = dose;

            if (ctImage != null)
            {
                _ctCache = new int[ctImage.ZSize][,];
                for (int z = 0; z < ctImage.ZSize; z++)
                {
                    _ctCache[z] = new int[ctImage.XSize, ctImage.YSize];
                    ctImage.GetVoxels(z, _ctCache[z]);
                }
                _huOffset = DetermineHuOffset(ctImage);
            }

            if (dose != null)
            {
                _doseCache = new int[dose.ZSize][,];
                for (int z = 0; z < dose.ZSize; z++)
                {
                    _doseCache[z] = new int[dose.XSize, dose.YSize];
                    dose.GetVoxels(z, _doseCache[z]);
                }

                DoseValue dv0 = dose.VoxelToDoseValue(0);
                DoseValue dvRef = dose.VoxelToDoseValue(10000);

                _doseRawScale = (dvRef.Dose - dv0.Dose) / 10000.0;
                _doseRawOffset = dv0.Dose;

                if (dvRef.Unit == DoseValue.DoseUnit.Percent)
                    _doseUnitToGyFactor = prescriptionDoseGy / 100.0;
                else if (dvRef.Unit == DoseValue.DoseUnit.cGy)
                    _doseUnitToGyFactor = 0.01;
                else
                    _doseUnitToGyFactor = 1.0;

                _doseScalingReady = true;
            }
        }

        private int DetermineHuOffset(Image ctImage)
        {
            int midSlice = ctImage.ZSize / 2;
            if (_ctCache == null || midSlice < 0 || midSlice >= _ctCache.Length)
                return 0;

            int[,] slice = _ctCache[midSlice];
            int xSize = ctImage.XSize;
            int ySize = ctImage.YSize;
            int step = 8;
            int countAboveThreshold = 0;
            int totalSamples = 0;

            for (int y = 0; y < ySize; y += step)
            {
                for (int x = 0; x < xSize; x += step)
                {
                    totalSamples++;
                    if (slice[x, y] > 30000)
                        countAboveThreshold++;
                }
            }

            return (totalSamples > 0 && countAboveThreshold > totalSamples / 2) ? 32768 : 0;
        }

        public unsafe void RenderCtImage(Image ctImage, WriteableBitmap targetBitmap, int currentSlice,
            double windowLevel, double windowWidth)
        {
            if (_ctCache == null || currentSlice < 0 || currentSlice >= _ctCache.Length)
                return;

            int[,] currentCtSlice = _ctCache[currentSlice];
            int sliceWidth = currentCtSlice.GetLength(0);
            int sliceHeight = currentCtSlice.GetLength(1);
            if (sliceWidth != _width || sliceHeight != _height)
                return;

            targetBitmap.Lock();
            try
            {
                byte* pBackBuffer = (byte*)targetBitmap.BackBuffer;
                int stride = targetBitmap.BackBufferStride;

                double huMin = windowLevel - (windowWidth / 2.0);
                double factor = (windowWidth > 0) ? 255.0 / windowWidth : 0;
                int huOffset = _huOffset;

                for (int y = 0; y < _height; y++)
                {
                    uint* pRow = (uint*)(pBackBuffer + y * stride);
                    for (int x = 0; x < _width; x++)
                    {
                        int hu = currentCtSlice[x, y] - huOffset;
                        double valDouble = (hu - huMin) * factor;
                        byte val = (byte)(valDouble < 0 ? 0 : (valDouble > 255 ? 255 : valDouble));
                        pRow[x] = (0xFFu << 24) | ((uint)val << 16) | ((uint)val << 8) | val;
                    }
                }

                targetBitmap.AddDirtyRect(new Int32Rect(0, 0, _width, _height));
            }
            finally
            {
                targetBitmap.Unlock();
            }
        }

        public unsafe string RenderDoseImage(Image ctImage, Dose dose, WriteableBitmap targetBitmap,
            int currentSlice, double planTotalDoseGy, double planNormalization, IsodoseLevel[] levels,
            DoseDisplayMode displayMode = DoseDisplayMode.Line,
            double colorwashOpacity = 0.5, double colorwashMinPercent = 0.1,
            EQD2Settings eqd2Settings = null)
        {
            targetBitmap.Lock();
            try
            {
                int doseStride = targetBitmap.BackBufferStride;
                byte* pDoseBuffer = (byte*)targetBitmap.BackBuffer;

                // Clear to transparent
                for (int i = 0; i < _height * doseStride; i++)
                    pDoseBuffer[i] = 0;

                if (dose == null || _doseCache == null || !_doseScalingReady)
                {
                    targetBitmap.AddDirtyRect(new Int32Rect(0, 0, _width, _height));
                    return "No dose available.";
                }

                // === Reference dose calculation ===
                double prescriptionGy = planTotalDoseGy;
                double normalization = planNormalization;
                if (double.IsNaN(normalization) || normalization <= 0)
                    normalization = 100.0;
                else if (normalization < 5.0)
                    normalization *= 100.0;

                double referenceDoseGy = prescriptionGy * (normalization / 100.0);
                if (referenceDoseGy < 0.1)
                    referenceDoseGy = prescriptionGy;

                // === EQD2 setup ===
                bool eqd2Active = eqd2Settings != null && eqd2Settings.IsEnabled
                                  && eqd2Settings.NumberOfFractions > 0 && eqd2Settings.AlphaBeta > 0;

                double eqd2QuadFactor = 0;
                double eqd2LinFactor = 1.0;

                if (eqd2Active)
                {
                    referenceDoseGy = EQD2Calculator.ToEQD2(referenceDoseGy,
                        eqd2Settings.NumberOfFractions, eqd2Settings.AlphaBeta);
                    EQD2Calculator.GetVoxelScalingFactors(eqd2Settings.NumberOfFractions,
                        eqd2Settings.AlphaBeta, out eqd2QuadFactor, out eqd2LinFactor);
                }

                // === Dose slice Z lookup ===
                VVector ctPlaneCenterWorld = ctImage.Origin + ctImage.ZDirection * (currentSlice * ctImage.ZRes);
                VVector relativeToDoseOrigin = ctPlaneCenterWorld - dose.Origin;
                int doseSlice = (int)Math.Round(relativeToDoseOrigin.Dot(dose.ZDirection) / dose.ZRes);

                if (doseSlice < 0 || doseSlice >= dose.ZSize)
                {
                    targetBitmap.AddDirtyRect(new Int32Rect(0, 0, _width, _height));
                    return $"CT Z: {currentSlice} | Dose Z: {doseSlice} (Out of range)";
                }

                int dx = dose.XSize;
                int dy = dose.YSize;
                int[,] doseBuffer = _doseCache[doseSlice];

                double rawScale = _doseRawScale;
                double rawOffset = _doseRawOffset;
                double unitToGyFactor = _doseUnitToGyFactor;

                string modeLabel = eqd2Active ? "EQD2" : "Physical";
                double maxDoseInSlice = 0;

                // ============================================================
                // Build dose-in-Gy grid for the dose slice (used by all modes)
                // ============================================================
                double[,] doseGyGrid = new double[dx, dy];
                for (int y = 0; y < dy; y++)
                {
                    for (int x = 0; x < dx; x++)
                    {
                        double valInUnits = doseBuffer[x, y] * rawScale + rawOffset;
                        double dGy = valInUnits * unitToGyFactor;
                        if (eqd2Active)
                            dGy = EQD2Calculator.ToEQD2Fast(dGy, eqd2QuadFactor, eqd2LinFactor);
                        doseGyGrid[x, y] = dGy;
                        if (dGy > maxDoseInSlice) maxDoseInSlice = dGy;
                    }
                }

                // ============================================================
                // Dispatch to rendering mode
                // ============================================================
                switch (displayMode)
                {
                    case DoseDisplayMode.Line:
                        RenderLineMode(pDoseBuffer, doseStride, ctImage, dose, currentSlice,
                            doseGyGrid, dx, dy, referenceDoseGy, levels);
                        break;

                    case DoseDisplayMode.Fill:
                        RenderFillMode(pDoseBuffer, doseStride, ctImage, dose, doseSlice,
                            doseGyGrid, dx, dy, referenceDoseGy, levels);
                        break;

                    case DoseDisplayMode.Colorwash:
                        byte cwAlpha = (byte)(Math.Max(0, Math.Min(1, colorwashOpacity)) * 255);
                        RenderColorwashMode(pDoseBuffer, doseStride, ctImage, dose, doseSlice,
                            doseGyGrid, dx, dy, referenceDoseGy, cwAlpha, colorwashMinPercent);
                        break;
                }

                targetBitmap.AddDirtyRect(new Int32Rect(0, 0, _width, _height));

                string modeName = displayMode.ToString();
                return $"[{modeLabel} {modeName}] CT Z: {currentSlice} | Dose Z: {doseSlice} | " +
                       $"Max: {maxDoseInSlice:F2} Gy | Ref: {referenceDoseGy:F2} Gy";
            }
            finally
            {
                targetBitmap.Unlock();
            }
        }

        #region Line Mode — SMOOTH (bilinear interpolation at CT resolution)

        /// <summary>
        /// Renders smooth isodose contour lines by interpolating the dose grid
        /// to CT pixel resolution using bilinear interpolation, then detecting
        /// boundaries at the fine CT pixel grid.
        /// 
        /// This eliminates the blocky "pixel mess" caused by the original method
        /// which operated at coarse dose grid resolution.
        /// </summary>
        private unsafe void RenderLineMode(byte* pBuffer, int stride, Image ctImage, Dose dose,
            int currentSlice, double[,] doseGyGrid, int dx, int dy, double refDoseGy, IsodoseLevel[] levels)
        {
            if (levels == null || levels.Length == 0) return;

            // Build filtered level list (visible only)
            int visibleCount = 0;
            for (int i = 0; i < levels.Length; i++)
                if (levels[i].IsVisible) visibleCount++;
            if (visibleCount == 0) return;

            var visLevels = new IsodoseLevel[visibleCount];
            int vi = 0;
            for (int i = 0; i < levels.Length; i++)
                if (levels[i].IsVisible) visLevels[vi++] = levels[i];

            // Precompute absolute dose thresholds (Gy) for each visible level
            double[] thresholds = new double[visibleCount];
            for (int i = 0; i < visibleCount; i++)
                thresholds[i] = refDoseGy * visLevels[i].Fraction;

            // ================================================================
            // Precompute CT pixel → dose grid coordinate mapping coefficients.
            // For CT pixel (px, py) on the current slice:
            //   world = ctBase + ctXDir * (px * ctXRes) + ctYDir * (py * ctYRes)
            //   diff  = world - doseOrigin
            //   doseXf = diff · doseXDir / doseXRes
            //   doseYf = diff · doseYDir / doseYRes
            //
            // This simplifies to linear functions of (px, py) with constant coefficients.
            // ================================================================
            VVector ctBase = ctImage.Origin + ctImage.ZDirection * (currentSlice * ctImage.ZRes);
            VVector baseDiff = ctBase - dose.Origin;

            double baseX = baseDiff.Dot(dose.XDirection) / dose.XRes;
            double baseY = baseDiff.Dot(dose.YDirection) / dose.YRes;

            double dxPerPx = ctImage.XRes * ctImage.XDirection.Dot(dose.XDirection) / dose.XRes;
            double dxPerPy = ctImage.YRes * ctImage.YDirection.Dot(dose.XDirection) / dose.XRes;
            double dyPerPx = ctImage.XRes * ctImage.XDirection.Dot(dose.YDirection) / dose.YRes;
            double dyPerPy = ctImage.YRes * ctImage.YDirection.Dot(dose.YDirection) / dose.YRes;

            // ================================================================
            // Pass 1: Build CT-resolution dose map via bilinear interpolation
            // ================================================================
            int w = _width;
            int h = _height;
            double[] ctDoseMap = new double[w * h]; // flat array for cache performance

            for (int py = 0; py < h; py++)
            {
                double rowDoseXBase = baseX + py * dxPerPy;
                double rowDoseYBase = baseY + py * dyPerPy;

                for (int px = 0; px < w; px++)
                {
                    double fx = rowDoseXBase + px * dxPerPx;
                    double fy = rowDoseYBase + px * dyPerPx;

                    ctDoseMap[py * w + px] = BilinearSample(doseGyGrid, dx, dy, fx, fy);
                }
            }

            // ================================================================
            // Pass 2: Classify each CT pixel to its highest matching isodose level
            // ================================================================
            int[] levelMap = new int[w * h];
            for (int i = 0; i < w * h; i++)
            {
                double dGy = ctDoseMap[i];
                levelMap[i] = -1;
                for (int li = 0; li < visibleCount; li++)
                {
                    if (dGy >= thresholds[li])
                    {
                        levelMap[i] = li;
                        break;
                    }
                }
            }

            // ================================================================
            // Pass 3: Find boundary pixels and draw contour lines
            // A pixel is a boundary if any 4-connected neighbor has a different
            // level classification. This produces 1-CT-pixel-wide contour lines
            // that are smooth because the underlying dose was interpolated.
            // ================================================================
            for (int py = 0; py < h; py++)
            {
                uint* row = (uint*)(pBuffer + py * stride);
                for (int px = 0; px < w; px++)
                {
                    int idx = py * w + px;
                    int myLevel = levelMap[idx];
                    if (myLevel < 0) continue;

                    bool isBoundary = false;

                    if (px == 0 || levelMap[idx - 1] != myLevel) isBoundary = true;
                    else if (px == w - 1 || levelMap[idx + 1] != myLevel) isBoundary = true;
                    else if (py == 0 || levelMap[idx - w] != myLevel) isBoundary = true;
                    else if (py == h - 1 || levelMap[idx + w] != myLevel) isBoundary = true;

                    if (!isBoundary) continue;

                    // Full opacity for crisp contour lines
                    uint color = (visLevels[myLevel].Color & 0x00FFFFFF) | 0xFF000000;
                    row[px] = color;
                }
            }
        }

        /// <summary>
        /// Bilinear interpolation of a 2D grid at continuous coordinates (fx, fy).
        /// Returns 0 if outside the grid bounds (with 0.5-pixel margin for center-of-voxel convention).
        /// </summary>
        private static double BilinearSample(double[,] grid, int gridW, int gridH, double fx, double fy)
        {
            // Clamp to valid interpolation range
            if (fx < 0 || fy < 0 || fx >= gridW - 1 || fy >= gridH - 1)
            {
                // Nearest-neighbor for edge pixels (within grid but outside interpolation range)
                int nx = (int)Math.Round(fx);
                int ny = (int)Math.Round(fy);
                if (nx >= 0 && nx < gridW && ny >= 0 && ny < gridH)
                    return grid[nx, ny];
                return 0;
            }

            int x0 = (int)fx;
            int y0 = (int)fy;
            int x1 = x0 + 1;
            int y1 = y0 + 1;

            double tx = fx - x0;
            double ty = fy - y0;

            return grid[x0, y0] * (1 - tx) * (1 - ty)
                 + grid[x1, y0] * tx * (1 - ty)
                 + grid[x0, y1] * (1 - tx) * ty
                 + grid[x1, y1] * tx * ty;
        }

        #endregion

        #region Fill Mode (original behavior)

        /// <summary>
        /// Fills each voxel with the color of its highest matching isodose level.
        /// Uses the per-level alpha for semi-transparency.
        /// </summary>
        private unsafe void RenderFillMode(byte* pBuffer, int stride, Image ctImage, Dose dose,
            int doseSlice, double[,] doseGyGrid, int dx, int dy, double refDoseGy, IsodoseLevel[] levels)
        {
            if (levels == null || levels.Length == 0) return;

            for (int y = 0; y < dy; y++)
            {
                for (int x = 0; x < dx; x++)
                {
                    double dGy = doseGyGrid[x, y];
                    uint color = 0;

                    for (int i = 0; i < levels.Length; i++)
                    {
                        if (!levels[i].IsVisible) continue;
                        if (dGy >= refDoseGy * levels[i].Fraction)
                        {
                            color = (levels[i].Color & 0x00FFFFFF) | ((uint)levels[i].Alpha << 24);
                            break;
                        }
                    }

                    if (color != 0)
                        MapDoseVoxelToCT(pBuffer, stride, ctImage, dose, doseSlice, x, y, color);
                }
            }
        }

        #endregion

        #region Colorwash Mode

        /// <summary>
        /// Continuous jet colormap from dose = minPercent*ref to dose >= ref.
        /// Maps every voxel above the minimum threshold to a smooth blue-cyan-green-yellow-red gradient.
        /// </summary>
        private unsafe void RenderColorwashMode(byte* pBuffer, int stride, Image ctImage, Dose dose,
            int doseSlice, double[,] doseGyGrid, int dx, int dy, double refDoseGy,
            byte alpha, double minPercent)
        {
            double minDoseGy = refDoseGy * minPercent;
            double maxDoseGy = refDoseGy * 1.15;

            for (int y = 0; y < dy; y++)
            {
                for (int x = 0; x < dx; x++)
                {
                    double dGy = doseGyGrid[x, y];
                    if (dGy < minDoseGy) continue;

                    double fraction = (dGy - minDoseGy) / (maxDoseGy - minDoseGy);
                    if (fraction < 0) fraction = 0;
                    if (fraction > 1) fraction = 1;

                    uint color = JetColormap(fraction, alpha);
                    MapDoseVoxelToCT(pBuffer, stride, ctImage, dose, doseSlice, x, y, color);
                }
            }
        }

        /// <summary>
        /// Jet/rainbow colormap. fraction 0.0 = deep blue, 1.0 = dark red.
        /// </summary>
        private static uint JetColormap(double t, byte alpha)
        {
            double r, g, b;

            if (t < 0.125) { r = 0; g = 0; b = 0.5 + t * 4.0; }
            else if (t < 0.375) { r = 0; g = (t - 0.125) * 4.0; b = 1.0; }
            else if (t < 0.625) { r = (t - 0.375) * 4.0; g = 1.0; b = 1.0 - (t - 0.375) * 4.0; }
            else if (t < 0.875) { r = 1.0; g = 1.0 - (t - 0.625) * 4.0; b = 0; }
            else { r = 1.0 - (t - 0.875) * 4.0; g = 0; b = 0; }

            byte rb = (byte)(Clamp01(r) * 255);
            byte gb = (byte)(Clamp01(g) * 255);
            byte bb = (byte)(Clamp01(b) * 255);

            return ((uint)alpha << 24) | ((uint)rb << 16) | ((uint)gb << 8) | bb;
        }

        private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);

        #endregion

        #region Shared: Dose voxel to CT pixel mapping (used by Fill and Colorwash modes)

        /// <summary>
        /// Maps a single dose grid voxel (doseX, doseY) to the CT image pixel space
        /// and writes the given color to all covered CT pixels.
        /// </summary>
        private unsafe void MapDoseVoxelToCT(byte* pBuffer, int stride, Image ctImage, Dose dose,
            int doseSlice, int doseX, int doseY, uint color)
        {
            VVector worldPos = dose.Origin +
                               dose.XDirection * (doseX * dose.XRes) +
                               dose.YDirection * (doseY * dose.YRes) +
                               dose.ZDirection * (doseSlice * dose.ZRes);

            VVector diff = worldPos - ctImage.Origin;
            double px = diff.Dot(ctImage.XDirection) / ctImage.XRes;
            double py = diff.Dot(ctImage.YDirection) / ctImage.YRes;

            double scaleX = dose.XRes / ctImage.XRes;
            double scaleY = dose.YRes / ctImage.YRes;

            int startX = (int)(px - scaleX / 2.0);
            int startY = (int)(py - scaleY / 2.0);
            int endX = (int)(px + scaleX / 2.0);
            int endY = (int)(py + scaleY / 2.0);

            if (startX < 0) startX = 0;
            if (startY < 0) startY = 0;
            if (endX > _width) endX = _width;
            if (endY > _height) endY = _height;

            for (int pyImg = startY; pyImg < endY; pyImg++)
            {
                uint* row = (uint*)(pBuffer + pyImg * stride);
                for (int pxImg = startX; pxImg < endX; pxImg++)
                {
                    row[pxImg] = color;
                }
            }
        }

        #endregion

        #region GetDoseAtPixel

        /// <summary>
        /// Returns the dose in Gy at a given CT pixel coordinate.
        /// </summary>
        public double GetDoseAtPixel(Image ctImage, Dose dose, int currentSlice, int pixelX, int pixelY,
            EQD2Settings eqd2Settings = null)
        {
            if (dose == null || _doseCache == null || !_doseScalingReady)
                return double.NaN;
            if (pixelX < 0 || pixelX >= _width || pixelY < 0 || pixelY >= _height)
                return double.NaN;

            VVector worldPos = ctImage.Origin +
                               ctImage.XDirection * (pixelX * ctImage.XRes) +
                               ctImage.YDirection * (pixelY * ctImage.YRes) +
                               ctImage.ZDirection * (currentSlice * ctImage.ZRes);

            VVector diffToDose = worldPos - dose.Origin;
            int doseX = (int)Math.Round(diffToDose.Dot(dose.XDirection) / dose.XRes);
            int doseY = (int)Math.Round(diffToDose.Dot(dose.YDirection) / dose.YRes);
            int doseZ = (int)Math.Round(diffToDose.Dot(dose.ZDirection) / dose.ZRes);

            if (doseX < 0 || doseX >= dose.XSize ||
                doseY < 0 || doseY >= dose.YSize ||
                doseZ < 0 || doseZ >= dose.ZSize)
                return double.NaN;

            double valInUnits = _doseCache[doseZ][doseX, doseY] * _doseRawScale + _doseRawOffset;
            double dGy = valInUnits * _doseUnitToGyFactor;

            if (eqd2Settings != null && eqd2Settings.IsEnabled &&
                eqd2Settings.NumberOfFractions > 0 && eqd2Settings.AlphaBeta > 0)
            {
                dGy = EQD2Calculator.ToEQD2(dGy, eqd2Settings.NumberOfFractions, eqd2Settings.AlphaBeta);
            }

            return dGy;
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _ctCache = null;
            _doseCache = null;
            _cachedDose = null;
            _cachedCtImage = null;
        }
    }
}