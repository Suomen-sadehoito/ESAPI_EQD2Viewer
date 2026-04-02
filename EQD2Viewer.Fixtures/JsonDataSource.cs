using EQD2Viewer.Core.Serialization;
using EQD2Viewer.Core.Interfaces;
using EQD2Viewer.Core.Data;
using System;

namespace EQD2Viewer.Fixtures
{
    /// <summary>
    /// IClinicalDataSource implementation that reads a ClinicalSnapshot from a
    /// directory exported by SnapshotSerializer (via FixtureGenerator's "Export Snapshot" command).
    ///
    /// This enables true end-to-end testing without Eclipse:
    ///   1. Run FixtureGenerator on a clinical workstation  ? exports snapshot directory
    ///   2. Copy directory to any developer machine
    ///   3. Pass it to JsonDataSource ? identical ClinicalSnapshot as EsapiDataSource would produce
    ///
    /// Usage:
    ///   var source = new JsonDataSource(@"C:\Fixtures\PATIENT_C1_Plan1_snapshot");
    ///   var snapshot = source.LoadSnapshot();   // zero ESAPI, works on any machine
    /// </summary>
    public class JsonDataSource : IClinicalDataSource
    {
        private readonly string _snapshotDir;

        /// <param name="snapshotDir">
        /// Directory containing snapshot_meta.json and companion files
        /// written by SnapshotSerializer.Write().
        /// </param>
        public JsonDataSource(string snapshotDir)
        {
            if (string.IsNullOrWhiteSpace(snapshotDir))
                throw new ArgumentNullException(nameof(snapshotDir));
            _snapshotDir = snapshotDir;
        }

        /// <summary>
        /// Deserializes the full ClinicalSnapshot from disk.
        /// Auto-detects JSON+RLE (v2.0) or binary (v3.0) format.
        /// Can be called from any thread (no Eclipse threading constraints).
        /// </summary>
        public ClinicalSnapshot LoadSnapshot()
        {
            return SnapshotSerializer.ReadAuto(_snapshotDir);
        }

        /// <summary>
        /// Checks whether a given directory looks like a valid snapshot directory.
        /// </summary>
        public static bool IsSnapshotDirectory(string dir) =>
      System.IO.File.Exists(
     System.IO.Path.Combine(dir, SnapshotSerializer.MetaFileName));
    }
}
