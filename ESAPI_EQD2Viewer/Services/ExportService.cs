using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using EQD2Viewer.Core.Models;

namespace ESAPI_EQD2Viewer.Services
{
    public static class ExportService
    {
        public static void ExportSummaryToCSV(IEnumerable<DVHSummary> summaries)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "CSV (*.csv)|*.csv",
                Title = "Save DVH Summary",
                FileName = $"EQD2_Summary_{DateTime.Now:yyyyMMdd_HHmm}.csv"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("StructureId,PlanId,Type,DMax_Gy,DMean_Gy,DMin_Gy,Volume_cm3");
                    foreach (var s in summaries)
                        sb.AppendLine($"{s.StructureId},{s.PlanId},{s.Type},{s.DMax:F2},{s.DMean:F2},{s.DMin:F2},{s.Volume:F2}");

                    File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                    MessageBox.Show("File saved successfully.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error saving file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
