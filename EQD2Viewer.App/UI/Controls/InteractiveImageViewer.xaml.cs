using EQD2Viewer.Core.Models;
using EQD2Viewer.App.UI.Rendering;
using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace EQD2Viewer.App.UI.Controls
{
    public partial class InteractiveImageViewer : UserControl
    {
        private bool _isPanning;
        private bool _isWindowing;
        private Point _panStartPoint;
        private Point _windowingStartPoint;
        private double _initialWindowLevel;
        private double _initialWindowWidth;
        private DateTime _lastRenderTime = DateTime.MinValue;

        #region Dependency Properties

        public static readonly DependencyProperty CtImageSourceProperty =
            DependencyProperty.Register(nameof(CtImageSource), typeof(ImageSource), typeof(InteractiveImageViewer));
        public ImageSource CtImageSource
        {
            get => (ImageSource)GetValue(CtImageSourceProperty);
            set => SetValue(CtImageSourceProperty, value);
        }

        public static readonly DependencyProperty DoseImageSourceProperty =
            DependencyProperty.Register(nameof(DoseImageSource), typeof(ImageSource), typeof(InteractiveImageViewer));
        public ImageSource DoseImageSource
        {
            get => (ImageSource)GetValue(DoseImageSourceProperty);
            set => SetValue(DoseImageSourceProperty, value);
        }

        public static readonly DependencyProperty OverlayImageSourceProperty =
            DependencyProperty.Register(nameof(OverlayImageSource), typeof(ImageSource), typeof(InteractiveImageViewer));
        public ImageSource OverlayImageSource
        {
            get => (ImageSource)GetValue(OverlayImageSourceProperty);
            set => SetValue(OverlayImageSourceProperty, value);
        }

        public static readonly DependencyProperty CurrentSliceProperty =
            DependencyProperty.Register(nameof(CurrentSlice), typeof(int), typeof(InteractiveImageViewer),
                new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
        public int CurrentSlice
        {
            get => (int)GetValue(CurrentSliceProperty);
            set => SetValue(CurrentSliceProperty, value);
        }

        public static readonly DependencyProperty MaxSliceProperty =
            DependencyProperty.Register(nameof(MaxSlice), typeof(int), typeof(InteractiveImageViewer));
        public int MaxSlice
        {
            get => (int)GetValue(MaxSliceProperty);
            set => SetValue(MaxSliceProperty, value);
        }

        public static readonly DependencyProperty WindowLevelProperty =
            DependencyProperty.Register(nameof(WindowLevel), typeof(double), typeof(InteractiveImageViewer),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
        public double WindowLevel
        {
            get => (double)GetValue(WindowLevelProperty);
            set => SetValue(WindowLevelProperty, value);
        }

        public static readonly DependencyProperty WindowWidthProperty =
            DependencyProperty.Register(nameof(WindowWidth), typeof(double), typeof(InteractiveImageViewer),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
        public double WindowWidth
        {
            get => (double)GetValue(WindowWidthProperty);
            set => SetValue(WindowWidthProperty, value);
        }

        public static readonly DependencyProperty ContourLinesProperty =
            DependencyProperty.Register(nameof(ContourLines),
                typeof(ObservableCollection<IsodoseContourData>),
                typeof(InteractiveImageViewer));
        public ObservableCollection<IsodoseContourData> ContourLines
        {
            get => (ObservableCollection<IsodoseContourData>)GetValue(ContourLinesProperty);
            set => SetValue(ContourLinesProperty, value);
        }

        /// <summary>Structure contours for the current slice.</summary>
        public static readonly DependencyProperty StructureContourLinesProperty =
            DependencyProperty.Register(nameof(StructureContourLines),
                typeof(ObservableCollection<StructureContourData>),
                typeof(InteractiveImageViewer));
        public ObservableCollection<StructureContourData> StructureContourLines
        {
            get => (ObservableCollection<StructureContourData>)GetValue(StructureContourLinesProperty);
            set => SetValue(StructureContourLinesProperty, value);
        }

        /// <summary>Dose readout text at cursor position.</summary>
        public static readonly DependencyProperty DoseCursorTextProperty =
            DependencyProperty.Register(nameof(DoseCursorText), typeof(string), typeof(InteractiveImageViewer),
                new PropertyMetadata("", OnDoseCursorTextChanged));
        public string DoseCursorText
        {
            get => (string)GetValue(DoseCursorTextProperty);
            set => SetValue(DoseCursorTextProperty, value);
        }

        public static readonly DependencyProperty DoseCursorVisibilityProperty =
            DependencyProperty.Register(nameof(DoseCursorVisibility), typeof(Visibility),
                typeof(InteractiveImageViewer), new PropertyMetadata(Visibility.Collapsed));
        public Visibility DoseCursorVisibility
        {
            get => (Visibility)GetValue(DoseCursorVisibilityProperty);
            set => SetValue(DoseCursorVisibilityProperty, value);
        }

        private static void OnDoseCursorTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is InteractiveImageViewer viewer)
            {
                viewer.DoseCursorVisibility = string.IsNullOrEmpty(e.NewValue as string)
                    ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        #endregion

        public InteractiveImageViewer()
        {
            InitializeComponent();
        }

        private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                double zoomFactor = e.Delta > 0 ? RenderConstants.ZoomStepFactor : 1.0 / RenderConstants.ZoomStepFactor;
                double newScale = Math.Max(RenderConstants.MinZoom,
                    Math.Min(RenderConstants.MaxZoom, ImageScale.ScaleX * zoomFactor));
                ImageScale.ScaleX = newScale;
                ImageScale.ScaleY = newScale;
            }
            else
            {
                int sliceDelta = e.Delta > 0 ? 1 : -1;
                int newSlice = CurrentSlice + sliceDelta;
                if (newSlice >= 0 && newSlice <= MaxSlice)
                    CurrentSlice = newSlice;
            }
            e.Handled = true;
        }

        private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.MiddleButton == MouseButtonState.Pressed)
            {
                _isPanning = true;
                _panStartPoint = e.GetPosition(this);
                e.Handled = true;
            }
            else if (e.LeftButton == MouseButtonState.Pressed)
            {
                _isWindowing = true;
                _windowingStartPoint = e.GetPosition(this);
                _initialWindowLevel = WindowLevel;
                _initialWindowWidth = WindowWidth;
                e.Handled = true;
            }
        }

        private void OnPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_isPanning && e.MiddleButton == MouseButtonState.Pressed)
            {
                Point currentPoint = e.GetPosition(this);
                Vector diff = currentPoint - _panStartPoint;
                ImageTranslate.X += diff.X;
                ImageTranslate.Y += diff.Y;
                _panStartPoint = currentPoint;
                e.Handled = true;
            }
            else if (_isWindowing && e.LeftButton == MouseButtonState.Pressed)
            {
                if ((DateTime.Now - _lastRenderTime).TotalMilliseconds > RenderConstants.WindowingThrottleMs)
                {
                    Point currentPoint = e.GetPosition(this);
                    Vector diff = currentPoint - _windowingStartPoint;
                    WindowWidth = Math.Max(1.0, _initialWindowWidth + (diff.X * RenderConstants.WindowingSensitivity));
                    WindowLevel = _initialWindowLevel - (diff.Y * RenderConstants.WindowingSensitivity);
                    _lastRenderTime = DateTime.Now;
                }
                e.Handled = true;
            }
            else
            {
                _isPanning = false;
                _isWindowing = false;

                // Dose cursor: compute pixel coordinates in CT space
                UpdateDoseCursorFromMouse(e);
            }
        }

        private void OnPreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isWindowing && e.ChangedButton == MouseButton.Left)
            {
                Point currentPoint = e.GetPosition(this);
                Vector diff = currentPoint - _windowingStartPoint;
                WindowWidth = Math.Max(1.0, _initialWindowWidth + (diff.X * RenderConstants.WindowingSensitivity));
                WindowLevel = _initialWindowLevel - (diff.Y * RenderConstants.WindowingSensitivity);
            }
            _isPanning = false;
            _isWindowing = false;
        }

        /// <summary>
        /// Converts mouse position to CT pixel coordinates and fires dose cursor update.
        /// </summary>
        private void UpdateDoseCursorFromMouse(MouseEventArgs e)
        {
            try
            {
                // Get mouse position relative to the ImageContainer
                Point posInContainer = e.GetPosition(ImageContainer);

                // The image is rendered at native CT resolution (Stretch="None"),
                // so posInContainer directly maps to CT pixel coordinates
                int pixelX = (int)Math.Floor(posInContainer.X);
                int pixelY = (int)Math.Floor(posInContainer.Y);

                // Raise a routed event or use the DataContext to update
                if (DataContext is UI.ViewModels.MainViewModel vm)
                {
                    vm.UpdateDoseCursor(pixelX, pixelY);
                    DoseCursorText = vm.DoseCursorText;
                }
            }
            catch
            {
                // Don't crash on cursor tracking
            }
        }
    }
}
