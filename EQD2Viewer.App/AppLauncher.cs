using EQD2Viewer.App.UI.Rendering;
using EQD2Viewer.Services;
using EQD2Viewer.Core.Interfaces;
using EQD2Viewer.Core.Data;
using EQD2Viewer.Core.Logging;
using EQD2Viewer.App.UI.ViewModels;
using EQD2Viewer.App.UI.Views;
using System;

namespace EQD2Viewer.App
{
    /// <summary>
    /// Composition root for launching the EQD2 Viewer UI.
    /// Called from both the ESAPI Script.cs and the DevRunner.
    /// </summary>
    public static class AppLauncher
    {
        public static void Launch(
            ClinicalSnapshot snapshot,
            ISummationDataLoader? summationLoader = null,
            string? windowTitle = null,
            bool useShowDialog = true)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            LogResearchDisclaimer();

            IImageRenderingService renderingService = new ImageRenderingService();
            IDebugExportService debugService = new DebugExportService();
            IDVHCalculation dvhService = new DVHService();

            int width  = snapshot.CtImage.XSize;
            int height = snapshot.CtImage.YSize;
            renderingService.Initialize(width, height);
            renderingService.PreloadData(snapshot.CtImage, snapshot.Dose);

            var factory = summationLoader != null
                ? new SummationServiceFactory()
                : null;

            var viewModel = new MainViewModel(
                snapshot,
                renderingService,
                debugService,
                dvhService,
                summationLoader,
                factory);

            var window = new MainWindow(viewModel);
            if (!string.IsNullOrEmpty(windowTitle))
                window.Title += $" {windowTitle}";

            if (useShowDialog)
                window.ShowDialog();
            else
                window.Show();
        }

        /// <summary>
        /// Writes a prominent disclaimer banner to the log on every startup, stating
        /// that this software is a research prototype and is not a medical device.
        /// The banner is here so that any log excerpt shared or inspected during QA
        /// carries the disclaimer — regulator, reviewer, or successor developer alike.
        /// </summary>
        private static void LogResearchDisclaimer()
        {
            const string bar = "==============================================================";
            SimpleLogger.Info(bar);
            SimpleLogger.Info("EQD2 Viewer — RESEARCH PROTOTYPE");
            SimpleLogger.Info("Not a medical device (not CE-marked, not FDA-cleared).");
            SimpleLogger.Info("Not validated for clinical use. Outputs must not drive");
            SimpleLogger.Info("clinical decisions without independent verification against");
            SimpleLogger.Info("a validated reference system.");
            SimpleLogger.Info(bar);
        }
    }
}
