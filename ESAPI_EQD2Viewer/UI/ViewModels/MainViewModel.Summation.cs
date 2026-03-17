using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ESAPI_EQD2Viewer.Core.Calculations;
using ESAPI_EQD2Viewer.Core.Interfaces;
using ESAPI_EQD2Viewer.Core.Models;
using ESAPI_EQD2Viewer.Services;
using ESAPI_EQD2Viewer.UI.Views;
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

        // Debounce timer for α/β slider: waits until slider stops moving
        private DispatcherTimer _alphaBetaDebounce;

        #region Summation Properties

        private bool _isSummationActive;
        public bool IsSummationActive
        {
            get => _isSummationActive;
            set
            {
                if (SetProperty(ref _isSummationActive, value))
                {
                    OnPropertyChanged(nameof(SummationStatusLabel));
                    RequestRender();
                }
            }
        }

        private bool _isSummationComputing;
        public bool IsSummationComputing
        {
            get => _isSummationComputing;
            set => SetProperty(ref _isSummationComputing, value);
        }

        private int _summationProgress;
        public int SummationProgress
        {
            get => _summationProgress;
            set => SetProperty(ref _summationProgress, value);
        }

        private string _summationInfo = "No summation active";
        public string SummationInfo
        {
            get => _summationInfo;
            set => SetProperty(ref _summationInfo, value);
        }

        public string SummationStatusLabel => _isSummationActive
            ? "✓ Summation active"
            : "";

        #endregion

        #region Summation Commands

        [RelayCommand]
        private async Task OpenSummationDialog()
        {
            var dialog = new PlanSummationDialog(_context.Patient, _plan);
            dialog.Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);

            if (dialog.ShowDialog() == true && dialog.ResultConfig != null)
            {
                await ExecuteSummationAsync(dialog.ResultConfig);
            }
        }

        [RelayCommand]
        private void CancelSummation()
        {
            _summationCts?.Cancel();
        }

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
            RequestRender();
        }

        /// <summary>
        /// Two-phase async summation:
        ///   Phase 1 (UI thread): Load ESAPI data → plain arrays (~3 s)
        ///   Phase 2 (background): Voxel computation with progress (~5-10 s)
        /// UI stays responsive throughout Phase 2.
        /// </summary>
        private async Task ExecuteSummationAsync(SummationConfig config)
        {
            // Cancel any running computation
            _summationCts?.Cancel();
            _summationCts = new CancellationTokenSource();
            var ct = _summationCts.Token;

            IsSummationComputing = true;
            SummationProgress = 0;
            SummationInfo = "Loading plan data...";

            try
            {
                // ---- PHASE 1: ESAPI data loading (UI thread, required by ESAPI) ----
                _summationService?.Dispose();
                _summationService = new SummationService(_context.Patient, _context.Image);

                var prepResult = _summationService.PrepareData(config);
                if (!prepResult.Success)
                {
                    MessageBox.Show($"Data loading failed:\n{prepResult.StatusMessage}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    IsSummationComputing = false;
                    return;
                }

                StatusText = prepResult.StatusMessage;
                SummationInfo = "Computing voxel summation...";

                // ---- PHASE 2: Heavy computation (background thread) ----
                var progress = new Progress<int>(pct =>
                {
                    SummationProgress = pct;
                    SummationInfo = $"Computing... {pct}%";
                });

                ct.ThrowIfCancellationRequested();

                var result = await _summationService.ComputeAsync(progress, ct);

                if (result.Success)
                {
                    _activeSummationConfig = config;
                    IsSummationActive = true;
                    // Force label update even if IsSummationActive was already true
                    // (SetProperty won't fire OnPropertyChanged if value didn't change)
                    OnPropertyChanged(nameof(SummationStatusLabel));

                    string methodLabel = config.Method == SummationMethod.EQD2 ? "EQD2" : "Physical";
                    SummationInfo = $"{methodLabel} sum: {config.Plans.Count} plans | " +
                                    $"Max: {result.MaxDoseGy:F2} Gy | Ref: {result.TotalReferenceDoseGy:F2} Gy";
                    StatusText = result.StatusMessage;

                    // Sync global α/β slider to the config value
                    _globalAlphaBeta = config.GlobalAlphaBeta;
                    OnPropertyChanged(nameof(GlobalAlphaBeta));

                    RequestRender();
                }
                else
                {
                    SummationInfo = result.StatusMessage;
                    if (!ct.IsCancellationRequested)
                    {
                        MessageBox.Show($"Summation failed:\n{result.StatusMessage}",
                            "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                SummationInfo = "Summation cancelled.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Summation error:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsSummationComputing = false;
            }
        }

        /// <summary>
        /// Called from GlobalAlphaBeta setter. Debounces: waits 500 ms after last
        /// slider movement before re-computing. Prevents dozens of re-computations
        /// while dragging the slider.
        /// </summary>
        private void ResummatIfActive()
        {
            if (!_isSummationActive || _activeSummationConfig == null) return;

            // Initialize debounce timer on first use
            if (_alphaBetaDebounce == null)
            {
                _alphaBetaDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                _alphaBetaDebounce.Tick += async (s, e) =>
                {
                    _alphaBetaDebounce.Stop();

                    if (_activeSummationConfig != null && _isSummationActive)
                    {
                        _activeSummationConfig.GlobalAlphaBeta = _globalAlphaBeta;
                        await ExecuteSummationAsync(_activeSummationConfig);
                    }
                };
            }

            // Restart timer — each slider tick resets the 500 ms countdown
            _alphaBetaDebounce.Stop();
            _alphaBetaDebounce.Start();
        }

        #endregion

        #region Summation-Aware Rendering

        /// <summary>
        /// Called from RenderScene when summation is active.
        /// </summary>
        private void RenderSummationScene()
        {
            if (_summationService == null || !_summationService.HasSummedDose) return;

            double[] summedSlice = _summationService.GetSummedSlice(CurrentSlice);
            if (summedSlice == null) return;

            double referenceDose = _summationService.SummedReferenceDoseGy;
            if (referenceDose < 0.01) referenceDose = 1.0;

            if (_doseDisplayMode == DoseDisplayMode.Line)
            {
                ClearDoseBitmap();

                int w = _context.Image.XSize, h = _context.Image.YSize;
                var contours = new ObservableCollection<IsodoseContourData>();

                foreach (var level in _isodoseLevelArray)
                {
                    if (!level.IsVisible) continue;

                    double thresholdGy = referenceDose * level.Fraction;
                    var polylines = MarchingSquares.GenerateContours(summedSlice, w, h, thresholdGy);
                    if (polylines.Count == 0) continue;

                    var geometry = new System.Windows.Media.StreamGeometry();
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

                    uint c = level.Color;
                    var brush = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(
                            (byte)((c >> 16) & 0xFF),
                            (byte)((c >> 8) & 0xFF),
                            (byte)(c & 0xFF)));
                    brush.Freeze();

                    contours.Add(new IsodoseContourData
                    {
                        Geometry = geometry,
                        Stroke = brush,
                        StrokeThickness = 1.0
                    });
                }

                ContourLines = contours;
                StatusText = $"[Summation Line] Slice {CurrentSlice} | Ref: {referenceDose:F2} Gy";
            }
            else
            {
                if (_contourLines != null && _contourLines.Count > 0)
                    ContourLines = new ObservableCollection<IsodoseContourData>();

                RenderSummedDoseBitmap(summedSlice, referenceDose);
            }
        }

        private unsafe void ClearDoseBitmap()
        {
            int w = _context.Image.XSize, h = _context.Image.YSize;
            DoseImageSource.Lock();
            try
            {
                byte* p = (byte*)DoseImageSource.BackBuffer;
                int stride = DoseImageSource.BackBufferStride;
                for (int i = 0; i < h * stride; i++) p[i] = 0;
                DoseImageSource.AddDirtyRect(new System.Windows.Int32Rect(0, 0, w, h));
            }
            finally { DoseImageSource.Unlock(); }
        }

        private unsafe void RenderSummedDoseBitmap(double[] summedSlice, double referenceDose)
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
                    for (int i = 0; i < _isodoseLevelArray.Length; i++)
                        if (_isodoseLevelArray[i].IsVisible) vc++;
                    if (vc > 0)
                    {
                        double[] thr = new double[vc]; uint[] col = new uint[vc]; int vi = 0;
                        for (int i = 0; i < _isodoseLevelArray.Length; i++)
                        {
                            if (!_isodoseLevelArray[i].IsVisible) continue;
                            thr[vi] = referenceDose * _isodoseLevelArray[i].Fraction;
                            col[vi] = (_isodoseLevelArray[i].Color & 0x00FFFFFF) | ((uint)_isodoseLevelArray[i].Alpha << 24);
                            vi++;
                        }
                        for (int py = 0; py < h; py++)
                        {
                            uint* row = (uint*)(pBuf + py * stride); int ro = py * w;
                            for (int px = 0; px < w; px++)
                            {
                                double d = summedSlice[ro + px]; if (d <= 0) continue;
                                for (int li = 0; li < vc; li++)
                                    if (d >= thr[li]) { row[px] = col[li]; break; }
                            }
                        }
                    }
                }
                else if (_doseDisplayMode == DoseDisplayMode.Colorwash)
                {
                    byte cwA = (byte)(Math.Max(0, Math.Min(1, _colorwashOpacity)) * 255);
                    double minGy = referenceDose * _colorwashMinPercent, maxGy = referenceDose * 1.15;
                    double range = maxGy - minGy;
                    if (range > 0)
                    {
                        for (int py = 0; py < h; py++)
                        {
                            uint* row = (uint*)(pBuf + py * stride); int ro = py * w;
                            for (int px = 0; px < w; px++)
                            {
                                double d = summedSlice[ro + px]; if (d < minGy) continue;
                                double f = Math.Min(1.0, (d - minGy) / range);
                                row[px] = SumJet(f, cwA);
                            }
                        }
                    }
                }

                string ml = _doseDisplayMode == DoseDisplayMode.Fill ? "Fill" : "Colorwash";
                StatusText = $"[Summation {ml}] Slice {CurrentSlice} | Ref: {referenceDose:F2} Gy";
                DoseImageSource.AddDirtyRect(new System.Windows.Int32Rect(0, 0, w, h));
            }
            finally { DoseImageSource.Unlock(); }
        }

        private static uint SumJet(double t, byte a)
        {
            double r, g, b;
            if (t < 0.125) { r = 0; g = 0; b = 0.5 + t * 4.0; }
            else if (t < 0.375) { r = 0; g = (t - 0.125) * 4.0; b = 1.0; }
            else if (t < 0.625) { r = (t - 0.375) * 4.0; g = 1.0; b = 1.0 - (t - 0.375) * 4.0; }
            else if (t < 0.875) { r = 1.0; g = 1.0 - (t - 0.625) * 4.0; b = 0; }
            else { r = 1.0 - (t - 0.875) * 4.0; g = 0; b = 0; }
            byte R = (byte)(Cl(r) * 255), G = (byte)(Cl(g) * 255), B = (byte)(Cl(b) * 255);
            return ((uint)a << 24) | ((uint)R << 16) | ((uint)G << 8) | B;
        }
        private static double Cl(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);

        #endregion
    }
}