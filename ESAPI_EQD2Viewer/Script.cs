using System;
using System.Windows;
using VMS.TPS.Common.Model.API;
using ESAPI_EQD2Viewer.Core.Interfaces;
using ESAPI_EQD2Viewer.Core.Logging;
using ESAPI_EQD2Viewer.Services;
using ESAPI_EQD2Viewer.UI.ViewModels;
using ESAPI_EQD2Viewer.UI.Views;

[assembly: ESAPIScript(IsWriteable = false)]

namespace VMS.TPS
{
    public class Script
    {
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void Execute(ScriptContext context)
        {
            if (context.Patient == null || context.Image == null)
            {
                MessageBox.Show("Please open a patient with an image before running the script.",
                    "EQD2 Viewer", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                SimpleLogger.EnableFileLogging();

                IImageRenderingService renderingService = new ImageRenderingService();
                IDebugExportService debugService = new DebugExportService();
                IDVHService dvhService = new DVHService();

                var viewModel = new MainViewModel(context, renderingService, debugService, dvhService);
                var window = new MainWindow(viewModel, context);
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                SimpleLogger.Error("Fatal error in Script.Execute", ex);
                MessageBox.Show($"Error:\n\n{ex.Message}\n\nStack Trace:\n{ex.StackTrace}",
                    "EQD2 Viewer Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
