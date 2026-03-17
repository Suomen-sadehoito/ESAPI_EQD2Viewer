using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using ESAPI_EQD2Viewer.Core.Interfaces;
using ESAPI_EQD2Viewer.Core.Extensions;
using ESAPI_EQD2Viewer.Core.Models;
using ESAPI_EQD2Viewer.Core.Calculations;
using ESAPI_EQD2Viewer.Core.Logging;

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

        private int _huOffset;
        private bool _disposed;

        public void Initialize(int width, int height)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            _width = width;
            _height = height;
        }

        public void PreloadData(Image ctImage, Dose dose, double prescriptionDoseGy)
        {
            if (ctImage != null)
            {
                _ctCache = new int[ctImage.ZSize][,];
                for (int z = 0; z < ctImage.ZSize; z++)
                {
                    _ctCache[z] = new int[ctImage.XSize, ctImage.YSize];
                    ctImage.GetVoxels(z, _ctCache[z]);
                }

                int midSlice = ctImage.ZSize / 2;
                _huOffset = ImageUtils.DetermineHuOffset(_ctCache[midSlice], ctImage.XSize, ctImage.YSize);
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
                DoseValue dvRef = dose.VoxelToDoseValue(RenderConstants.DoseCalibrationRawValue);

                _doseRawScale = (dvRef.Dose - dv0.Dose) / (double)RenderConstants.DoseCalibrationRawValue;
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

        // ================================================================
        // Fix #1: Stride assertion helper
        // ================================================================
        private static void AssertBitmapCompatible(WriteableBitmap bmp, int width, int height)
        {
            Debug.Assert(bmp.PixelWidth == width && bmp.PixelHeight == height,
                $"Bitmap size mismatch: expected {width}x{height}, got {bmp.PixelWidth}x{bmp.PixelHeight}");
            Debug.Assert(bmp.BackBufferStride >= width * 4,
                $"Stride too small: {bmp.BackBufferStride} < {width * 4}");
        }

        // ================================================================
        // CT Rendering
        // ================================================================

        public unsafe void RenderCtImage(Image ctImage, WriteableBitmap targetBitmap, int currentSlice,
            double windowLevel, double windowWidth)
        {
            if (_ctCache == null || currentSlice < 0 || currentSlice >= _ctCache.Length) return;

            int[,] currentCtSlice = _ctCache[currentSlice];
            if (currentCtSlice.GetLength(0) != _width || currentCtSlice.GetLength(1) != _height) return;

            AssertBitmapCompatible(targetBitmap, _width, _height);

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
            finally { targetBitmap.Unlock(); }
        }

        // ================================================================
        // Shared: Dose Grid Computation
        // ================================================================

        private DoseGridData PrepareDoseGrid(Image ctImage, Dose dose, int currentSlice,
            double planTotalDoseGy, double planNormalization, EQD2Settings eqd2Settings)
        {
            var result = new DoseGridData();

            if (dose == null || _doseCache == null || !_doseScalingReady)
            { result.StatusText = "No dose available."; return result; }

            double prescriptionGy = planTotalDoseGy;
            double normalization = planNormalization;
            if (double.IsNaN(normalization) || normalization <= 0)
                normalization = 100.0;
            else if (normalization < RenderConstants.NormalizationFractionThreshold)
                normalization *= 100.0;

            double referenceDoseGy = prescriptionGy * (normalization / 100.0);
            if (referenceDoseGy < RenderConstants.MinReferenceDoseGy)
                referenceDoseGy = prescriptionGy;

            bool eqd2Active = eqd2Settings != null && eqd2Settings.IsEnabled
                              && eqd2Settings.NumberOfFractions > 0 && eqd2Settings.AlphaBeta > 0;

            double eqd2QuadFactor = 0, eqd2LinFactor = 1.0;
            if (eqd2Active)
            {
                referenceDoseGy = EQD2Calculator.ToEQD2(referenceDoseGy,
                    eqd2Settings.NumberOfFractions, eqd2Settings.AlphaBeta);
                EQD2Calculator.GetVoxelScalingFactors(eqd2Settings.NumberOfFractions,
                    eqd2Settings.AlphaBeta, out eqd2QuadFactor, out eqd2LinFactor);
            }

            result.ReferenceDoseGy = referenceDoseGy;
            result.IsEQD2 = eqd2Active;

            VVector ctPlaneCenterWorld = ctImage.Origin + ctImage.ZDirection * (currentSlice * ctImage.ZRes);
            VVector relativeToDoseOrigin = ctPlaneCenterWorld - dose.Origin;
            int doseSlice = (int)Math.Round(relativeToDoseOrigin.Dot(dose.ZDirection) / dose.ZRes);

            if (doseSlice < 0 || doseSlice >= dose.ZSize)
            { result.StatusText = $"CT Z: {currentSlice} | Dose Z: {doseSlice} (Out of range)"; return result; }

            result.DoseSlice = doseSlice;
            int dx = dose.XSize, dy = dose.YSize;
            int[,] doseBuffer = _doseCache[doseSlice];

            double maxDose = 0;
            double[,] doseGyGrid = new double[dx, dy];
            for (int y = 0; y < dy; y++)
                for (int x = 0; x < dx; x++)
                {
                    double dGy = (doseBuffer[x, y] * _doseRawScale + _doseRawOffset) * _doseUnitToGyFactor;
                    if (eqd2Active) dGy = EQD2Calculator.ToEQD2Fast(dGy, eqd2QuadFactor, eqd2LinFactor);
                    doseGyGrid[x, y] = dGy;
                    if (dGy > maxDose) maxDose = dGy;
                }

            result.DoseGyGrid = doseGyGrid;
            result.DoseWidth = dx;
            result.DoseHeight = dy;
            result.MaxDoseInSlice = maxDose;

            VVector ctBase = ctImage.Origin + ctImage.ZDirection * (currentSlice * ctImage.ZRes);
            VVector baseDiff = ctBase - dose.Origin;
            result.BaseX = baseDiff.Dot(dose.XDirection) / dose.XRes;
            result.BaseY = baseDiff.Dot(dose.YDirection) / dose.YRes;
            result.DxPerPx = ctImage.XRes * ctImage.XDirection.Dot(dose.XDirection) / dose.XRes;
            result.DxPerPy = ctImage.YRes * ctImage.YDirection.Dot(dose.XDirection) / dose.XRes;
            result.DyPerPx = ctImage.XRes * ctImage.XDirection.Dot(dose.YDirection) / dose.YRes;
            result.DyPerPy = ctImage.YRes * ctImage.YDirection.Dot(dose.YDirection) / dose.YRes;

            result.StatusText = $"CT Z: {currentSlice} | Dose Z: {doseSlice} | " +
                                $"Max: {maxDose:F2} Gy | Ref: {referenceDoseGy:F2} Gy";
            return result;
        }

        private double[] BuildCtResolutionDoseMap(DoseGridData g)
        {
            int w = _width, h = _height;
            double[] map = new double[w * h];
            for (int py = 0; py < h; py++)
            {
                double rxBase = g.BaseX + py * g.DxPerPy;
                double ryBase = g.BaseY + py * g.DyPerPy;
                for (int px = 0; px < w; px++)
                    map[py * w + px] = ImageUtils.BilinearSample(g.DoseGyGrid, g.DoseWidth, g.DoseHeight,
                        rxBase + px * g.DxPerPx, ryBase + px * g.DyPerPx);
            }
            return map;
        }

        private class DoseGridData
        {
            public double[,] DoseGyGrid;
            public int DoseWidth, DoseHeight, DoseSlice;
            public double ReferenceDoseGy, MaxDoseInSlice;
            public double BaseX, BaseY, DxPerPx, DxPerPy, DyPerPx, DyPerPy;
            public bool IsEQD2;
            public string StatusText;
            public bool IsValid => DoseGyGrid != null;
        }

        // ================================================================
        // Dose Rendering
        // ================================================================

        public unsafe string RenderDoseImage(Image ctImage, Dose dose, WriteableBitmap targetBitmap,
            int currentSlice, double planTotalDoseGy, double planNormalization, IsodoseLevel[] levels,
            DoseDisplayMode displayMode, double colorwashOpacity, double colorwashMinPercent,
            EQD2Settings eqd2Settings)
        {
            AssertBitmapCompatible(targetBitmap, _width, _height);

            targetBitmap.Lock();
            try
            {
                int doseStride = targetBitmap.BackBufferStride;
                byte* pDoseBuffer = (byte*)targetBitmap.BackBuffer;
                for (int i = 0; i < _height * doseStride; i++) pDoseBuffer[i] = 0;

                if (displayMode == DoseDisplayMode.Line)
                {
                    targetBitmap.AddDirtyRect(new Int32Rect(0, 0, _width, _height));
                    return "";
                }

                var grid = PrepareDoseGrid(ctImage, dose, currentSlice,
                    planTotalDoseGy, planNormalization, eqd2Settings);

                if (!grid.IsValid)
                { targetBitmap.AddDirtyRect(new Int32Rect(0, 0, _width, _height)); return grid.StatusText; }

                double[] ctDoseMap = BuildCtResolutionDoseMap(grid);

                switch (displayMode)
                {
                    case DoseDisplayMode.Fill:
                        RenderFillMode(pDoseBuffer, doseStride, ctDoseMap, grid.ReferenceDoseGy, levels);
                        break;
                    case DoseDisplayMode.Colorwash:
                        byte cwAlpha = (byte)(Math.Max(0, Math.Min(1, colorwashOpacity)) * 255);
                        RenderColorwashMode(pDoseBuffer, doseStride, ctDoseMap, grid.ReferenceDoseGy, cwAlpha, colorwashMinPercent);
                        break;
                }

                targetBitmap.AddDirtyRect(new Int32Rect(0, 0, _width, _height));
                string label = grid.IsEQD2 ? "EQD2" : "Physical";
                return $"[{label} {displayMode}] {grid.StatusText}";
            }
            finally { targetBitmap.Unlock(); }
        }

        // ================================================================
        // Vector Contours
        // ================================================================

        public ContourGenerationResult GenerateVectorContours(Image ctImage, Dose dose, int currentSlice,
            double planTotalDoseGy, double planNormalization, IsodoseLevel[] levels,
            EQD2Settings eqd2Settings)
        {
            var result = new ContourGenerationResult { Contours = new List<IsodoseContourData>() };

            var grid = PrepareDoseGrid(ctImage, dose, currentSlice,
                planTotalDoseGy, planNormalization, eqd2Settings);

            if (!grid.IsValid || levels == null || levels.Length == 0)
            { result.StatusText = grid.StatusText ?? "No data"; return result; }

            double[] ctDoseMap = BuildCtResolutionDoseMap(grid);
            int w = _width, h = _height;

            for (int i = 0; i < levels.Length; i++)
            {
                if (!levels[i].IsVisible) continue;

                double thresholdGy = grid.ReferenceDoseGy * levels[i].Fraction;
                var polylines = MarchingSquares.GenerateContours(ctDoseMap, w, h, thresholdGy);
                if (polylines.Count == 0) continue;

                var geometry = new StreamGeometry();
                using (var ctx = geometry.Open())
                {
                    foreach (var chain in polylines)
                    {
                        if (chain.Count < 2) continue;
                        ctx.BeginFigure(chain[0], false, false);
                        for (int j = 1; j < chain.Count; j++)
                            ctx.LineTo(chain[j], true, false);
                    }
                }
                geometry.Freeze();

                uint c = levels[i].Color;
                var brush = new SolidColorBrush(Color.FromRgb(
                    (byte)((c >> 16) & 0xFF), (byte)((c >> 8) & 0xFF), (byte)(c & 0xFF)));
                brush.Freeze();

                result.Contours.Add(new IsodoseContourData
                {
                    Geometry = geometry,
                    Stroke = brush,
                    StrokeThickness = 1.0
                });
            }

            string label = grid.IsEQD2 ? "EQD2" : "Physical";
            result.StatusText = $"[{label} Line] {grid.StatusText}";
            return result;
        }

        // ================================================================
        // Structure Contours (NEW)
        // ================================================================

        public List<StructureContourData> GenerateStructureContours(Image ctImage, int currentSlice,
            IEnumerable<Structure> structures)
        {
            var result = new List<StructureContourData>();
            if (structures == null || ctImage == null) return result;

            foreach (var structure in structures)
            {
                try
                {
                    var contours = structure.MeshGeometry; // check if structure has geometry
                    if (contours == null) continue;

                    // Get contour points on this image plane
                    var contourPoints = structure.GetContoursOnImagePlane(currentSlice);
                    if (contourPoints == null || contourPoints.Length == 0) continue;

                    var geometry = new StreamGeometry();
                    using (var ctx = geometry.Open())
                    {
                        foreach (var contour in contourPoints)
                        {
                            if (contour.Length < 3) continue;

                            // Convert DICOM mm to CT pixel coordinates
                            var firstPt = WorldToPixel(contour[0], ctImage);
                            ctx.BeginFigure(firstPt, false, true); // closed contour

                            for (int i = 1; i < contour.Length; i++)
                            {
                                ctx.LineTo(WorldToPixel(contour[i], ctImage), true, false);
                            }
                        }
                    }
                    geometry.Freeze();

                    var brush = new SolidColorBrush(Color.FromArgb(
                        structure.Color.A, structure.Color.R, structure.Color.G, structure.Color.B));
                    brush.Freeze();

                    var contourData = new StructureContourData
                    {
                        Geometry = geometry,
                        Stroke = brush,
                        StrokeThickness = RenderConstants.StructureContourThickness,
                        StructureId = structure.Id
                    };

                    // Dashed line for support structures
                    if (structure.DicomType == "SUPPORT" || structure.DicomType == "EXTERNAL")
                    {
                        contourData.StrokeDashArray = new DoubleCollection { 4, 2 };
                        contourData.StrokeDashArray.Freeze();
                    }

                    result.Add(contourData);
                }
                catch (Exception ex)
                {
                    SimpleLogger.Warning($"Could not render structure '{structure.Id}': {ex.Message}");
                }
            }

            return result;
        }

        private static Point WorldToPixel(VVector worldPoint, Image ctImage)
        {
            VVector diff = worldPoint - ctImage.Origin;
            double px = (diff.x * ctImage.XDirection.x + diff.y * ctImage.XDirection.y + diff.z * ctImage.XDirection.z) / ctImage.XRes;
            double py = (diff.x * ctImage.YDirection.x + diff.y * ctImage.YDirection.y + diff.z * ctImage.YDirection.z) / ctImage.YRes;
            return new Point(px, py);
        }

        // ================================================================
        // GetDoseAtPixel
        // ================================================================

        public double GetDoseAtPixel(Image ctImage, Dose dose, int currentSlice, int pixelX, int pixelY,
            EQD2Settings eqd2Settings)
        {
            if (dose == null || _doseCache == null || !_doseScalingReady) return double.NaN;
            if (pixelX < 0 || pixelX >= _width || pixelY < 0 || pixelY >= _height) return double.NaN;

            VVector worldPos = ctImage.Origin + ctImage.XDirection * (pixelX * ctImage.XRes)
                             + ctImage.YDirection * (pixelY * ctImage.YRes) + ctImage.ZDirection * (currentSlice * ctImage.ZRes);
            VVector diff = worldPos - dose.Origin;
            int dx = (int)Math.Round(diff.Dot(dose.XDirection) / dose.XRes);
            int dy = (int)Math.Round(diff.Dot(dose.YDirection) / dose.YRes);
            int dz = (int)Math.Round(diff.Dot(dose.ZDirection) / dose.ZRes);

            if (dx < 0 || dx >= dose.XSize || dy < 0 || dy >= dose.YSize || dz < 0 || dz >= dose.ZSize)
                return double.NaN;

            double dGy = (_doseCache[dz][dx, dy] * _doseRawScale + _doseRawOffset) * _doseUnitToGyFactor;
            if (eqd2Settings != null && eqd2Settings.IsEnabled && eqd2Settings.NumberOfFractions > 0 && eqd2Settings.AlphaBeta > 0)
                dGy = EQD2Calculator.ToEQD2(dGy, eqd2Settings.NumberOfFractions, eqd2Settings.AlphaBeta);
            return dGy;
        }

        // ================================================================
        // Fill Mode (Fix #7: uses shared ColorMaps)
        // ================================================================

        private unsafe void RenderFillMode(byte* pBuffer, int stride, double[] ctDoseMap,
            double refDoseGy, IsodoseLevel[] levels)
        {
            if (levels == null || levels.Length == 0) return;

            int vc = 0;
            for (int i = 0; i < levels.Length; i++) if (levels[i].IsVisible) vc++;
            if (vc == 0) return;

            double[] thr = new double[vc];
            uint[] col = new uint[vc];
            int vi = 0;
            for (int i = 0; i < levels.Length; i++)
            {
                if (!levels[i].IsVisible) continue;
                thr[vi] = refDoseGy * levels[i].Fraction;
                col[vi] = (levels[i].Color & 0x00FFFFFF) | ((uint)levels[i].Alpha << 24);
                vi++;
            }

            int w = _width, h = _height;
            for (int py = 0; py < h; py++)
            {
                uint* row = (uint*)(pBuffer + py * stride);
                int ro = py * w;
                for (int px = 0; px < w; px++)
                {
                    double d = ctDoseMap[ro + px];
                    if (d <= 0) continue;
                    for (int li = 0; li < vc; li++)
                        if (d >= thr[li]) { row[px] = col[li]; break; }
                }
            }
        }

        // ================================================================
        // Colorwash Mode (Fix #7: uses shared ColorMaps)
        // ================================================================

        private unsafe void RenderColorwashMode(byte* pBuffer, int stride, double[] ctDoseMap,
            double refDoseGy, byte alpha, double minPercent)
        {
            double minGy = refDoseGy * minPercent;
            double maxGy = refDoseGy * RenderConstants.ColorwashMaxFraction;
            double range = maxGy - minGy;
            if (range <= 0) return;

            int w = _width, h = _height;
            for (int py = 0; py < h; py++)
            {
                uint* row = (uint*)(pBuffer + py * stride);
                int ro = py * w;
                for (int px = 0; px < w; px++)
                {
                    double d = ctDoseMap[ro + px];
                    if (d < minGy) continue;
                    double f = (d - minGy) / range;
                    if (f > 1.0) f = 1.0;
                    row[px] = ColorMaps.Jet(f, alpha);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _ctCache = null;
            _doseCache = null;
        }
    }
}
