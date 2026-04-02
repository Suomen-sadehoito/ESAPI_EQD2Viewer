using EQD2Viewer.Core.Interfaces;
using VMS.TPS.Common.Model.API;
using EQD2Viewer.App;
using EQD2Viewer.Esapi.Adapters;
using EQD2Viewer.Core.Logging;
using EQD2Viewer.Core.Data;
using System;
using System.Windows;

[assembly: ESAPIScript(IsWriteable = false)]
namespace VMS.TPS
{
    /// <summary>
    /// Eclipse ESAPI script entry point for the EQD2 Viewer.
    /// This is the only file in the solution that carries the [ESAPIScript] attribute.
    /// 
    /// Responsibilities:
    ///   1. Validate that a patient + image are open.
    ///   2. Load the full ClinicalSnapshot via EsapiDataSource (ESAPI -> POCOs).
    ///   3. Create the EsapiSummationDataLoader for on-demand plan loading.
    ///   4. Delegate UI creation to AppLauncher (no WPF type knowledge here).
    /// 
    /// This class is the sole bridge between VMS.TPS and the application.
    /// After LoadSnapshot(), zero ESAPI calls are made by the UI layer.
    /// </summary>
    public class Script
    {
        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void Execute(ScriptContext context)
        {
            if (context.Patient == null || context.Image == null)
            {
                MessageBox.Show(
                    "Please open a patient with an image before running the script.",
                    "EQD2 Viewer",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            try
            {
                SimpleLogger.EnableFileLogging();

                // -- Load the full clinical snapshot via the ESAPI adapter layer --
                var dataSource = new EQD2Viewer.Esapi.Adapters.EsapiDataSource(context);
                var snapshot = dataSource.LoadSnapshot();

                // -- Create the ESAPI summation data loader for on-demand plan loading --
                ISummationDataLoader summationLoader =
                    new EQD2Viewer.Esapi.Adapters.EsapiSummationDataLoader(context.Patient);

                // -- Launch the UI via the composition root (no direct WPF type references here) --
                EQD2Viewer.App.AppLauncher.Launch(
                    snapshot,
                    summationLoader,
                    windowTitle: null,
                    useShowDialog: true);
            }
            catch (Exception ex)
            {
                SimpleLogger.Error("Fatal error in Script.Execute", ex);
                MessageBox.Show(
                    $"Error:\n\n{ex.Message}\n\nStack Trace:\n{ex.StackTrace}",
                    "EQD2 Viewer Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
