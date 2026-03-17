using CommunityToolkit.Mvvm.Input;
using ESAPI_EQD2Viewer.Core.Calculations;
using ESAPI_EQD2Viewer.Core.Interfaces;
using ESAPI_EQD2Viewer.Core.Models;
using ESAPI_EQD2Viewer.Core.Logging;
using ESAPI_EQD2Viewer.Services;
using ESAPI_EQD2Viewer.UI.Views;
using OxyPlot;
using OxyPlot.Series;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace ESAPI_EQD2Viewer.UI.ViewModels
{
    public partial class MainViewModel
    {
        private ISummationService _summationService;
        private SummationConfig _activeSummationConfig;
        private CancellationTokenSource _summationCts;
        private DispatcherTimer _alphaBetaDebounce;

        private bool _isSummationActive;
        public bool IsSummationActive
        {
            get => _isSummationActive;
            set { if (SetProperty(ref _isSummationActive, value)) { OnPropertyChanged(nameof(SummationStatusLabel)); RequestRender(); } }
        }

        private bool _isSummationComputing;
        public bool IsSummationComputing { get => _isSummationComputing; set => SetProperty(ref _isSummationComputing, value); }

        private int _summationProgress;
        public int SummationProgress { get => _summationProgress; set => SetProperty(ref _summationProgress, value); }

        private string _summationInfo = "No summation active";
        public string SummationInfo { get => _summationInfo; set => SetProperty(ref _summationInfo, value); }

        public string SummationStatusLabel => _isSummationActive ? "Summation active" : "";

        [RelayCommand]
        private async Task OpenSummationDialog()
        {
            var dialog = new PlanSummationDialog(_context.Patient, _plan);
            dialog.Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
            if (dialog.ShowDialog() == true && dialog.ResultConfig != null)
                await ExecuteSummationAsync(dialog.ResultConfig);
        }

        [RelayCommand]
        private void CancelSummation() => _summationCts?.Cancel();

        [RelayCommand]
        private void ClearSummation()
        {
            _summationCts?.Cancel();
            _summationService?.Dispose();
            _summationService = null;
            _activeSummationConfig = null;
            IsSummationActive = false;
            IsSummationComputing = false;
            SummationProgress = 0;
            SummationInfo = "No summation active";
            CurrentOverlayMode = OverlayMode.Off;
            OverlayPlanOptions.Clear();
            ClearSummationDVH();
            if (_isodoseMode == IsodoseMode.Absolute) LoadIsodosePreset("Eclipse");
            RequestRender();
        }

        private async Task ExecuteSummationAsync(SummationConfig config)
        {
            _summationCts?.Cancel();
            _summationCts = new CancellationTokenSource();
            var ct = _summationCts.Token;
            IsSummationComputing = true;
            SummationProgress = 0;
            SummationInfo = "Loading plan data...";

            try
            {
                _summationService?.Dispose();
                _summationService = new SummationService(_context.Patient, _context.Image);
                var prepResult = _summationService.PrepareData(config);
                if (!prepResult.Success)
                {
                    MessageBox.Show($"Failed:\n{prepResult.StatusMessage}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    IsSummationComputing = false; return;
                }

                StatusText = prepResult.StatusMessage;
                SummationInfo = "Computing...";
                var progress = new Progress<int>(pct => { SummationProgress = pct; SummationInfo = $"Computing... {pct}%"; });
                ct.ThrowIfCancellationRequested();
                var result = await _summationService.ComputeAsync(progress, ct);

                if (result.Success)
                {
                    _activeSummationConfig = config;
                    IsSummationActive = true;
                    string ml = config.Method == SummationMethod.EQD2 ? "EQD2" : "Physical";
                    SummationInfo = $"{ml} sum: {config.Plans.Count} plans | Max: {result.MaxDoseGy:F2} Gy | Ref: {result.TotalReferenceDoseGy:F2} Gy";
                    StatusText = result.StatusMessage;
                    _globalAlphaBeta = config.GlobalAlphaBeta;
                    OnPropertyChanged(nameof(GlobalAlphaBeta));
                    if (_isodoseMode != IsodoseMode.Absolute) LoadIsodosePreset("ReIrradiation");

                    OverlayPlanOptions.Clear();
                    foreach (var plan in config.Plans.Where(p => !p.IsReference)) OverlayPlanOptions.Add(plan.DisplayLabel);
                    if (OverlayPlanOptions.Count > 0) SelectedOverlayPlanLabel = OverlayPlanOptions[0];

                    CalculateSummationDVH(result.MaxDoseGy);
                    RequestRender();
                }
                else
                {
                    SummationInfo = result.StatusMessage;
                    if (!ct.IsCancellationRequested)
                        MessageBox.Show($"Failed:\n{result.StatusMessage}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (OperationCanceledException) { SummationInfo = "Cancelled."; }
            catch (Exception ex) { SimpleLogger.Error("Summation failed", ex); MessageBox.Show($"Error:\n{ex.Message}"); }
            finally { IsSummationComputing = false; }
        }

        internal void ResummatIfActive()
        {
            if (!_isSummationActive || _activeSummationConfig == null) return;
            if (_alphaBetaDebounce == null)
            {
                _alphaBetaDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RenderConstants.AlphaBetaDebounceMs) };
                _alphaBetaDebounce.Tick += async (s, e) =>
                {
                    _alphaBetaDebounce.Stop();
                    if (_activeSummationConfig != null && _isSummationActive)
                    { _activeSummationConfig.GlobalAlphaBeta = _globalAlphaBeta; await ExecuteSummationAsync(_activeSummationConfig); }
                };
            }
            _alphaBetaDebounce.Stop();
            _alphaBetaDebounce.Start();
        }

        // ═══ SUMMATION DVH ═══

        private void CalculateSummationDVH(double maxDoseGy)
        {
            if (_summationService == null || !_summationService.HasSummedDose) return;
            var structureIds = _summationService.GetCachedStructureIds();
            if (structureIds == null || structureIds.Count == 0) return;

            var selectedIds = _dvhCache.Select(c => c.Structure.Id).ToHashSet();
            int sliceCount = _summationService.SliceCount;
            double[][] summedSlices = new double[sliceCount][];
            for (int z = 0; z < sliceCount; z++) summedSlices[z] = _summationService.GetSummedSlice(z);
            double voxelVolCc = _summationService.GetVoxelVolumeCc();
            string methodLabel = _activeSummationConfig?.Method == SummationMethod.EQD2 ? "EQD2 Sum" : "Physical Sum";

            ClearSummationDVH();

            foreach (var structureId in structureIds)
            {
                if (!selectedIds.Contains(structureId)) continue;

                bool[][] masks = new bool[sliceCount][];
                for (int z = 0; z < sliceCount; z++) masks[z] = _summationService.GetStructureMask(structureId, z);

                DoseVolumePoint[] dvhPoints = _dvhService.CalculateDVHFromSummedDose(summedSlices, masks, voxelVolCc, maxDoseGy);
                if (dvhPoints == null || dvhPoints.Length == 0) continue;

                long totalVoxels = 0;
                for (int z = 0; z < sliceCount; z++)
                    if (masks[z] != null) for (int i = 0; i < masks[z].Length; i++) if (masks[z][i]) totalVoxels++;

                SummaryData.Add(_dvhService.BuildSummaryFromCurve(structureId, "Summation", methodLabel, dvhPoints, totalVoxels * voxelVolCc));

                var cached = _dvhCache.FirstOrDefault(c => c.Structure.Id == structureId);
                OxyColor color = cached != null
                    ? OxyColor.FromArgb(cached.Structure.Color.A, cached.Structure.Color.R, cached.Structure.Color.G, cached.Structure.Color.B)
                    : OxyColors.White;

                var series = new LineSeries
                {
                    Title = $"{structureId} {methodLabel}", Tag = $"Summation_{structureId}",
                    Color = color, StrokeThickness = 2.5, LineStyle = LineStyle.DashDot
                };
                series.Points.AddRange(dvhPoints.Select(p => new DataPoint(p.DoseGy, p.VolumePercent)));
                PlotModel.Series.Add(series);
            }
            RefreshPlot();
        }

        private void ClearSummationDVH()
        {
            foreach (var s in PlotModel.Series.Where(s => (s.Tag as string)?.StartsWith("Summation_") ?? false).ToList())
                PlotModel.Series.Remove(s);
            foreach (var s in SummaryData.Where(s => s.PlanId == "Summation").ToList())
                SummaryData.Remove(s);
        }

        // ═══ SUMMATION RENDERING ═══

        private void RenderSummationScene()
        {
            double[] summedSlice = _summationService.GetSummedSlice(CurrentSlice);
            if (summedSlice == null) return;

            double refDose = _summationService.SummedReferenceDoseGy;
            if (refDose < RenderConstants.MinReferenceDoseGy) refDose = 1.0;

            if (_doseDisplayMode == DoseDisplayMode.Line)
            {
                ClearDoseBitmap();
                int w = _context.Image.XSize, h = _context.Image.YSize;
                var contours = new ObservableCollection<IsodoseContourData>();

                foreach (var level in _isodoseLevelArray)
                {
                    if (!level.IsVisible) continue;
                    double thr = GetThresholdGy(level, refDose);
                    if (thr <= 0) continue;
                    var polylines = MarchingSquares.GenerateContours(summedSlice, w, h, thr);
                    if (polylines.Count == 0) continue;

                    var geo = new System.Windows.Media.StreamGeometry();
                    using (var ctx = geo.Open())
                        foreach (var chain in polylines)
                        {
                            if (chain.Count < 2) continue;
                            ctx.BeginFigure(chain[0], false, false);
                            for (int j = 1; j < chain.Count; j++) ctx.LineTo(chain[j], true, false);
                        }
                    geo.Freeze();

                    uint c = level.Color;
                    var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(
                        (byte)((c >> 16) & 0xFF), (byte)((c >> 8) & 0xFF), (byte)(c & 0xFF)));
                    brush.Freeze();
                    contours.Add(new IsodoseContourData { Geometry = geo, Stroke = brush, StrokeThickness = 1.0 });
                }
                ContourLines = contours;
                StatusText = $"[Summation · Line] Slice {CurrentSlice} | Ref: {refDose:F2} Gy";
            }
            else
            {
                if (_contourLines?.Count > 0) ContourLines = new ObservableCollection<IsodoseContourData>();
                RenderSummedDoseBitmap(summedSlice, refDose);
            }
        }

        private unsafe void ClearDoseBitmap()
        {
            int w = _context.Image.XSize, h = _context.Image.YSize;
            DoseImageSource.Lock();
            try
            {
                byte* p = (byte*)DoseImageSource.BackBuffer;
                for (int i = 0; i < h * DoseImageSource.BackBufferStride; i++) p[i] = 0;
                DoseImageSource.AddDirtyRect(new System.Windows.Int32Rect(0, 0, w, h));
            }
            finally { DoseImageSource.Unlock(); }
        }

        private unsafe void RenderSummedDoseBitmap(double[] slice, double refDose)
        {
            int w = _context.Image.XSize, h = _context.Image.YSize;
            DoseImageSource.Lock();
            try
            {
                byte* pBuf = (byte*)DoseImageSource.BackBuffer;
                int stride = DoseImageSource.BackBufferStride;
                for (int i = 0; i < h * stride; i++) pBuf[i] = 0;

                if (_doseDisplayMode == DoseDisplayMode.Fill)
                {
                    int vc = 0;
                    for (int i = 0; i < _isodoseLevelArray.Length; i++) if (_isodoseLevelArray[i].IsVisible) vc++;
                    if (vc > 0)
                    {
                        double[] thr = new double[vc]; uint[] col = new uint[vc]; int vi = 0;
                        for (int i = 0; i < _isodoseLevelArray.Length; i++)
                        {
                            if (!_isodoseLevelArray[i].IsVisible) continue;
                            thr[vi] = GetThresholdGy(_isodoseLevelArray[i], refDose);
                            col[vi] = (_isodoseLevelArray[i].Color & 0x00FFFFFF) | ((uint)_isodoseLevelArray[i].Alpha << 24);
                            vi++;
                        }
                        for (int py = 0; py < h; py++)
                        {
                            uint* row = (uint*)(pBuf + py * stride); int ro = py * w;
                            for (int px = 0; px < w; px++)
                            {
                                double d = slice[ro + px]; if (d <= 0) continue;
                                for (int li = 0; li < vc; li++) if (d >= thr[li]) { row[px] = col[li]; break; }
                            }
                        }
                    }
                }
                else if (_doseDisplayMode == DoseDisplayMode.Colorwash)
                {
                    byte cwA = (byte)(System.Math.Max(0, System.Math.Min(1, _colorwashOpacity)) * 255);
                    double minGy = refDose * _colorwashMinPercent, maxGy = refDose * RenderConstants.ColorwashMaxFraction;
                    double range = maxGy - minGy;
                    if (range > 0)
                        for (int py = 0; py < h; py++)
                        {
                            uint* row = (uint*)(pBuf + py * stride); int ro = py * w;
                            for (int px = 0; px < w; px++)
                            {
                                double d = slice[ro + px]; if (d < minGy) continue;
                                row[px] = ColorMaps.Jet(System.Math.Min(1.0, (d - minGy) / range), cwA);
                            }
                        }
                }

                string ml = _doseDisplayMode == DoseDisplayMode.Fill ? "Fill" : "Colorwash";
                StatusText = $"[Summation · {ml}] Slice {CurrentSlice} | Ref: {refDose:F2} Gy";
                DoseImageSource.AddDirtyRect(new System.Windows.Int32Rect(0, 0, w, h));
            }
            finally { DoseImageSource.Unlock(); }
        }
    }
}
