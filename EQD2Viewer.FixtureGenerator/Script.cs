using VMS.TPS.Common.Model.API;
using EQD2Viewer.Core.Logging;
using EQD2Viewer.Core.Data;
using EQD2Viewer.Esapi.Adapters;
using System;
using System.IO;
using System.Linq;
using System.Windows;

[assembly: ESAPIScript(IsWriteable = false)]

namespace VMS.TPS
{
    /// <summary>
    /// ESAPI script with two export modes:
    ///
    ///   A) Testifixturit  — FixtureExporter: selective fixture files for unit/integration tests.
    ///      Lightweight (~1 MB). Copy to EQD2Viewer.Tests\TestFixtures\.
    ///
    ///   B) Koko snapshot  — SnapshotExporter: full ClinicalSnapshot for end-to-end QA.
    ///      Complete CT + dose voxels (~25-65 MB). Open with JsonDataSource on any machine.
    ///      Use this to verify that the app shows identical results from JSON vs. live Eclipse.
    ///
    /// Plan Sum support: if a Plan Sum is open and active in Eclipse, it is used automatically.
    /// CT image must always be selected in Eclipse before running.
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
                    "Avaa potilas ja kuva ennen skriptin ajoa.",
                    "Fixture Generator", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            PlanningItem planningItem = ResolvePlanningItem(context, out string planType);

            if (planningItem == null || planningItem.Dose == null)
            {
                MessageBox.Show(
                    "Valitse suunnitelma tai Plan Sum, jossa on laskettu annos.\n\n" +
                    "• Yksittäinen suunnitelma: valitse se aktiiviseksi Eclipsessä\n" +
                    "• Plan Sum: avaa Plan Sum ja valitse se aktiiviseksi",
                    "Fixture Generator", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Ask user which export mode to use
            var choice = MessageBox.Show(
                $"Potilas: {context.Patient.Id}  |  Suunnitelma: {planningItem.Id}  ({planType})\n\n" +
                "Valitse vientitapa:\n\n" +
                "[Kyllä]  →  Koko snapshot  (CT + annos täydelliset vokselit, ~25–65 MB)\n" +
                "       Käytä end-to-end laadunvalvontaan toisella koneella\n\n" +
                "[Ei]     →  Testifixturit  (valikoitu data testeille, ~1 MB)\n" +
                "            Kopioi EQD2Viewer.Tests\\TestFixtures\\\n\n" +
                "[Peruuta] → Keskeytä",
                "Fixture Generator — Valitse vientitapa",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (choice == MessageBoxResult.Cancel) return;

            try
            {
                if (choice == MessageBoxResult.Yes)
                    RunSnapshotExport(context);
                else
                    RunFixtureExport(context, planningItem, planType);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Virhe:\n{ex.Message}\n\n{ex.StackTrace}",
                    "Fixture Generator — Virhe",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ────────────────────────────────────────────────────────
        // MODE A: Full snapshot (end-to-end QA)
        // ────────────────────────────────────────────────────────

        private static void RunSnapshotExport(ScriptContext context)
        {
            string patId = context.Patient?.Id ?? "UNKNOWN";
            string planId = context.ExternalPlanSetup?.Id ?? "NOPLAN";
            string courseId = context.ExternalPlanSetup?.Course?.Id ?? "NOCOURSE";

            string outputDir = EQD2Viewer.FixtureGenerator.SnapshotExporter
                .BuildOutputDirName(patId, courseId, planId);
            Directory.CreateDirectory(outputDir);

            // Load via the same EsapiDataSource the live application uses —
            // this guarantees the snapshot is byte-for-byte equivalent to what the app sees.
            var dataSource = new EsapiDataSource(context);
            var snapshot = dataSource.LoadSnapshot();

            var exporter = new EQD2Viewer.FixtureGenerator.SnapshotExporter();
            string report = exporter.ExportSnapshot(snapshot, outputDir);

            MessageBox.Show(
       $"Snapshot tallennettu:\n{outputDir}\n\n{report}\n\n" +
       "Avaa toisella koneella:\n" +
       "  var source = new JsonDataSource(@\"<polku>\");\n" +
               "  var snapshot = source.LoadSnapshot();",
       "Fixture Generator — Snapshot valmis",
             MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ────────────────────────────────────────────────────────
        // MODE B: Selective test fixtures
        // ────────────────────────────────────────────────────────

        private static void RunFixtureExport(ScriptContext context,
        PlanningItem planningItem, string planType)
        {
            string courseId = GetCourseId(planningItem);
            string planLabel = SanitizePath(
         $"{context.Patient.Id}_{courseId}_{planningItem.Id}");
            string outputDir = Path.Combine(
           Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
      "EQD2_Fixtures", planLabel);
            Directory.CreateDirectory(outputDir);

            var exporter = new EQD2Viewer.FixtureGenerator.FixtureExporter();
            string report = exporter.ExportAll(context, planningItem, planType, outputDir);

            MessageBox.Show(
                  $"Fixtures tallennettu ({planType}):\n{outputDir}\n\n{report}\n\n" +
             "Kopioi kansio projektiin:\n" +
                        "EQD2Viewer.Tests\\TestFixtures\\",
         "Fixture Generator — Testifixturit valmis",
               MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ────────────────────────────────────────────────────────
        // HELPERS
        // ────────────────────────────────────────────────────────

        private static PlanningItem ResolvePlanningItem(ScriptContext context, out string planType)
        {
            if (context.PlanSumsInScope != null)
            {
                var planSum = context.PlanSumsInScope.FirstOrDefault(ps => ps.Dose != null);
                if (planSum != null) { planType = "PlanSum"; return planSum; }
            }

            var plan = context.ExternalPlanSetup;
            if (plan != null && plan.Dose != null) { planType = "PlanSetup"; return plan; }

            planType = "Unknown";
            return null;
        }

        private static string GetCourseId(PlanningItem item)
        {
            if (item is PlanSetup ps) return ps.Course?.Id ?? "NoC";
            if (item is PlanSum sum) return sum.Course?.Id ?? "NoC";
            return "NoC";
        }

        private static string SanitizePath(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}
