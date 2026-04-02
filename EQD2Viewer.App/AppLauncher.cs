using EQD2Viewer.Services.Rendering;
using EQD2Viewer.Services;
using EQD2Viewer.Core.Interfaces;
using EQD2Viewer.Core.Data;
using EQD2Viewer.App.UI.ViewModels;
using EQD2Viewer.App.UI.Views;
using System;

namespace EQD2Viewer.App
{
    /// <summary>
    /// Composition root for launching the EQD2 Viewer UI.
    /// Called from both the ESAPI Script.cs and the DevRunner.
    /// 
    /// This class owns the wiring of services ? ViewModel ? Window.
    /// Neither EQD2Viewer.Esapi nor EQD2Viewer.DevRunner need to know
    /// about internal service types -- they just provide the data and call Launch().
    /// </summary>
    public static class AppLauncher
    {
        /// <summary>
        /// Creates all services, initializes rendering, builds the ViewModel, and shows the window.
        /// </summary>
        /// <param name="snapshot">Fully loaded clinical data (from ESAPI or JSON fixtures).</param>
        /// <param name="summationLoader">
        /// Optional: provides on-demand plan loading for multi-plan summation.
        /// Null when summation is not available (e.g., DevRunner without full data).
        /// </param>
        /// <param name="windowTitle">Optional title suffix (e.g., "[DEV MODE]").</param>
        /// <param name="useShowDialog">
        /// True to call ShowDialog() (ESAPI scripts must block the calling thread).
        /// False to call Show() (standalone WPF apps manage their own message loop).
        /// </param>
        public static void Launch(
          ClinicalSnapshot snapshot,
         ISummationDataLoader? summationLoader = null,
     string? windowTitle = null,
                bool useShowDialog = true)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            // -- Create ESAPI-free services --
            IImageRenderingService renderingService = new ImageRenderingService();
            IDebugExportService debugService = new DebugExportService();
            IDVHCalculation dvhService = new DVHService();

            // -- Initialize rendering pipeline from snapshot dimensions --
            int width = snapshot.CtImage.XSize;
            int height = snapshot.CtImage.YSize;
            renderingService.Initialize(width, height);
            renderingService.PreloadData(snapshot.CtImage, snapshot.Dose);

            // -- Build ViewModel --
            var viewModel = new MainViewModel(
            snapshot,
              renderingService,
         debugService,
                    dvhService,
                summationLoader,
                        summationLoader != null ? new SummationServiceFactory() : null);

            // -- Launch window --
            var window = new MainWindow(viewModel);
            if (!string.IsNullOrEmpty(windowTitle))
                window.Title += $" {windowTitle}";

            if (useShowDialog)
                window.ShowDialog();
            else
                window.Show();
        }
    }
}
