using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using ESAPI_EQD2Viewer.Core.Models;

namespace ESAPI_EQD2Viewer.UI.Views
{
    public partial class PlanSummationDialog : Window
    {
        private readonly Patient _patient;
        private readonly PlanSetup _currentPlan;

        /// <summary>
        /// All patient registrations indexed once at startup.
        /// </summary>
        private List<RegistrationInfo> _allRegistrations;

        public ObservableCollection<PlanRowItem> PlanRows { get; } = new ObservableCollection<PlanRowItem>();

        public SummationConfig ResultConfig { get; private set; }

        public PlanSummationDialog(Patient patient, PlanSetup currentPlan)
        {
            InitializeComponent();

            _patient = patient ?? throw new ArgumentNullException(nameof(patient));
            _currentPlan = currentPlan;
            _allRegistrations = IndexAllRegistrations();

            PopulatePlans();

            PlanGrid.ItemsSource = PlanRows;
        }

        // ====================================================================
        // REGISTRATION INDEXING
        // ====================================================================

        private List<RegistrationInfo> IndexAllRegistrations()
        {
            var list = new List<RegistrationInfo>();
            if (_patient.Registrations == null) return list;

            foreach (var reg in _patient.Registrations)
            {
                try
                {
                    list.Add(new RegistrationInfo
                    {
                        Id = reg.Id,
                        SourceFOR = reg.SourceFOR ?? "",
                        RegisteredFOR = reg.RegisteredFOR ?? "",
                        DateStr = reg.CreationDateTime?.ToString("d") ?? ""
                    });
                }
                catch { }
            }
            return list;
        }

        // ====================================================================
        // REGISTRATION FILTERING — CORE LOGIC
        //
        // RULE: For a non-reference plan, only show registrations where:
        //   BOTH the plan's FOR and the reference FOR participate.
        //
        // Valid combinations:
        //   SourceFOR == planFOR  AND  RegisteredFOR == refFOR
        //   SourceFOR == refFOR   AND  RegisteredFOR == planFOR
        //
        // Everything else is filtered OUT — it connects to a third CT
        // that has nothing to do with this pair.
        // ====================================================================

        private List<RegistrationOption> FilterRegistrationsForPair(
            string planFOR, string referenceFOR)
        {
            var options = new List<RegistrationOption>
            {
                new RegistrationOption { Id = "", DisplayName = "None — same CT as reference" }
            };

            if (string.IsNullOrEmpty(planFOR) || string.IsNullOrEmpty(referenceFOR))
                return options;

            if (string.Equals(planFOR, referenceFOR, StringComparison.OrdinalIgnoreCase))
                return options;

            foreach (var reg in _allRegistrations)
            {
                bool planIsSource = string.Equals(reg.SourceFOR, planFOR, StringComparison.OrdinalIgnoreCase);
                bool planIsTarget = string.Equals(reg.RegisteredFOR, planFOR, StringComparison.OrdinalIgnoreCase);
                bool refIsSource = string.Equals(reg.SourceFOR, referenceFOR, StringComparison.OrdinalIgnoreCase);
                bool refIsTarget = string.Equals(reg.RegisteredFOR, referenceFOR, StringComparison.OrdinalIgnoreCase);

                if (planIsSource && refIsTarget)
                {
                    options.Add(new RegistrationOption
                    {
                        Id = reg.Id,
                        DisplayName = $"{reg.Id}  [plan \u2192 ref]  ({reg.DateStr})"
                    });
                }
                else if (refIsSource && planIsTarget)
                {
                    options.Add(new RegistrationOption
                    {
                        Id = reg.Id,
                        DisplayName = $"{reg.Id}  [ref \u2192 plan]  ({reg.DateStr})"
                    });
                }
            }

            return options;
        }

        // ====================================================================
        // REBUILD ALL REGISTRATION LISTS
        // Called when reference plan changes.
        // ====================================================================

        private string _lastDebugReport = "";

        private void RebuildAllRegistrationLists()
        {
            var refRow = PlanRows.FirstOrDefault(r => r.IsReference);
            string referenceFOR = refRow?.ImageFOR ?? "";

            var dbg = new System.Text.StringBuilder();
            dbg.AppendLine("══════════════════════════════════════════════════");
            dbg.AppendLine("  EQD2 VIEWER — REGISTRATION DEBUG REPORT");
            dbg.AppendLine($"  Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            dbg.AppendLine("══════════════════════════════════════════════════");

            // Section 1: Reference plan
            dbg.AppendLine();
            dbg.AppendLine("── REFERENCE PLAN ──");
            if (refRow != null)
            {
                dbg.AppendLine($"  Course:    {refRow.CourseId}");
                dbg.AppendLine($"  Plan:      {refRow.PlanId}");
                dbg.AppendLine($"  Image:     {refRow.ImageId}");
                dbg.AppendLine($"  FOR (full): {refRow.ImageFOR}");
            }
            else
            {
                dbg.AppendLine("  (no reference selected)");
            }

            // Section 2: All plans with full FOR
            dbg.AppendLine();
            dbg.AppendLine("── ALL PLANS ──");
            foreach (var row in PlanRows)
            {
                string marker = row.IsReference ? " [REF]" : "";
                string included = row.IsIncluded ? "YES" : "no";
                dbg.AppendLine($"  {row.CourseId}/{row.PlanId}{marker}");
                dbg.AppendLine($"    Image:    {row.ImageId}");
                dbg.AppendLine($"    FOR:      {row.ImageFOR}");
                dbg.AppendLine($"    Included: {included}  Dose: {row.TotalDoseGy:F1} Gy  Fx: {row.NumberOfFractions}");

                bool sameFOR = !string.IsNullOrEmpty(row.ImageFOR)
                    && !string.IsNullOrEmpty(referenceFOR)
                    && string.Equals(row.ImageFOR, referenceFOR, StringComparison.OrdinalIgnoreCase);
                dbg.AppendLine($"    Same CT as ref: {sameFOR}");
                dbg.AppendLine();
            }

            // Section 3: All registrations with full FOR UIDs
            dbg.AppendLine("── ALL PATIENT REGISTRATIONS ──");
            dbg.AppendLine($"  Count: {_allRegistrations.Count}");
            if (_allRegistrations.Count == 0)
            {
                dbg.AppendLine("  (none found)");
            }
            foreach (var reg in _allRegistrations)
            {
                dbg.AppendLine($"  Registration: {reg.Id}  ({reg.DateStr})");
                dbg.AppendLine($"    SourceFOR:     {reg.SourceFOR}");
                dbg.AppendLine($"    RegisteredFOR: {reg.RegisteredFOR}");
            }

            // Section 4: Filtering logic per plan
            dbg.AppendLine();
            dbg.AppendLine("── FILTERING RESULTS ──");
            dbg.AppendLine($"  Reference FOR: {referenceFOR}");
            dbg.AppendLine();

            foreach (var row in PlanRows)
            {
                if (row.IsReference)
                {
                    row.RelevantRegistrations = new List<RegistrationOption>();
                    row.SelectedRegistrationId = "";
                    dbg.AppendLine($"  {row.CourseId}/{row.PlanId}: REFERENCE — skipped");
                    continue;
                }

                dbg.AppendLine($"  {row.CourseId}/{row.PlanId}:");
                dbg.AppendLine($"    Plan FOR: {row.ImageFOR}");
                dbg.AppendLine($"    Ref  FOR: {referenceFOR}");

                bool sameFOR = !string.IsNullOrEmpty(row.ImageFOR)
                    && string.Equals(row.ImageFOR, referenceFOR, StringComparison.OrdinalIgnoreCase);

                if (sameFOR)
                {
                    dbg.AppendLine($"    Result: SAME FOR — no registration needed");
                }
                else if (string.IsNullOrEmpty(row.ImageFOR))
                {
                    dbg.AppendLine($"    Result: Plan FOR is EMPTY — cannot filter");
                }
                else if (string.IsNullOrEmpty(referenceFOR))
                {
                    dbg.AppendLine($"    Result: Reference FOR is EMPTY — cannot filter");
                }
                else
                {
                    dbg.AppendLine($"    Checking each registration:");
                    foreach (var reg in _allRegistrations)
                    {
                        bool planIsSource = string.Equals(reg.SourceFOR, row.ImageFOR, StringComparison.OrdinalIgnoreCase);
                        bool planIsTarget = string.Equals(reg.RegisteredFOR, row.ImageFOR, StringComparison.OrdinalIgnoreCase);
                        bool refIsSource = string.Equals(reg.SourceFOR, referenceFOR, StringComparison.OrdinalIgnoreCase);
                        bool refIsTarget = string.Equals(reg.RegisteredFOR, referenceFOR, StringComparison.OrdinalIgnoreCase);

                        string verdict;
                        if (planIsSource && refIsTarget)
                            verdict = "MATCH (plan=Source, ref=Registered)";
                        else if (refIsSource && planIsTarget)
                            verdict = "MATCH (ref=Source, plan=Registered)";
                        else
                        {
                            var reasons = new List<string>();
                            if (!planIsSource && !planIsTarget) reasons.Add("plan FOR not in this reg");
                            else if (planIsSource && !refIsTarget) reasons.Add("plan=Source but ref\u2260Registered");
                            else if (planIsTarget && !refIsSource) reasons.Add("plan=Registered but ref\u2260Source");
                            if (!refIsSource && !refIsTarget) reasons.Add("ref FOR not in this reg");
                            verdict = "SKIP (" + string.Join("; ", reasons) + ")";
                        }

                        dbg.AppendLine($"      {reg.Id}: {verdict}");
                    }
                }

                var newList = FilterRegistrationsForPair(row.ImageFOR, referenceFOR);
                string currentSelection = row.SelectedRegistrationId;
                bool selectionStillValid = newList.Any(o => o.Id == currentSelection);

                row.RelevantRegistrations = newList;
                if (!selectionStillValid)
                    row.SelectedRegistrationId = "";

                int matchCount = newList.Count - 1; // minus "None" entry
                dbg.AppendLine($"    Available registrations: {matchCount}");
                foreach (var opt in newList.Where(o => !string.IsNullOrEmpty(o.Id)))
                    dbg.AppendLine($"      \u2714 {opt.DisplayName}");
                dbg.AppendLine();
            }

            dbg.AppendLine("══════════════════════════════════════════════════");       

            _lastDebugReport = dbg.ToString();

            if (TbDebugInfo != null)
                TbDebugInfo.Text = _lastDebugReport;
        }

        private void CopyDebug_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_lastDebugReport))
            {
                try
                {
                    Clipboard.SetText(_lastDebugReport);
                    MessageBox.Show("Debug report copied to clipboard.\nPaste it to Claude for diagnosis.",
                        "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not copy to clipboard: {ex.Message}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // ====================================================================
        // PLAN POPULATION
        // ====================================================================

        private void PopulatePlans()
        {
            if (_patient.Courses == null) return;

            foreach (var course in _patient.Courses)
            {
                if (course.PlanSetups == null) continue;

                foreach (var plan in course.PlanSetups)
                {
                    if (plan.Dose == null) continue;

                    double totalGy;
                    if (plan.TotalDose.Unit == DoseValue.DoseUnit.cGy)
                        totalGy = plan.TotalDose.Dose / 100.0;
                    else
                        totalGy = plan.TotalDose.Dose;

                    bool isCurrentPlan = _currentPlan != null
                        && plan.Id == _currentPlan.Id
                        && course.Id == _currentPlan.Course?.Id;

                    string imageId = "";
                    string imageFOR = "";
                    try
                    {
                        var img = plan.StructureSet?.Image;
                        if (img != null)
                        {
                            imageId = img.Id ?? "";
                            imageFOR = img.FOR ?? "";
                        }
                    }
                    catch { }

                    var row = new PlanRowItem
                    {
                        CourseId = course.Id,
                        PlanId = plan.Id,
                        ImageId = imageId,
                        ImageFOR = imageFOR,
                        TotalDoseGy = totalGy,
                        NumberOfFractions = plan.NumberOfFractions ?? 1,
                        PlanNormalization = plan.PlanNormalizationValue,
                        IsIncluded = isCurrentPlan,
                        IsReference = isCurrentPlan,
                        SelectedRegistrationId = "",
                        Weight = 1.0,
                        RelevantRegistrations = new List<RegistrationOption>()
                    };

                    row.PropertyChanged += OnRowPropertyChanged;
                    PlanRows.Add(row);
                }
            }

            RebuildAllRegistrationLists();
        }

        private void OnRowPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlanRowItem.IsReference))
            {
                RebuildAllRegistrationLists();
            }
        }

        // ====================================================================
        // COMPUTE
        // ====================================================================

        private void Compute_Click(object sender, RoutedEventArgs e)
        {
            var includedPlans = PlanRows.Where(p => p.IsIncluded).ToList();

            if (includedPlans.Count < 2)
            {
                MessageBox.Show("Select at least two plans for summation.",
                    "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!includedPlans.Any(p => p.IsReference))
            {
                MessageBox.Show("Mark one plan as the reference (Ref column).\n" +
                    "The reference plan's CT is used as the spatial grid for summation.",
                    "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int refCount = includedPlans.Count(p => p.IsReference);
            if (refCount > 1)
            {
                MessageBox.Show("Only one plan can be the reference. Please select exactly one.",
                    "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            foreach (var p in includedPlans)
            {
                if (p.NumberOfFractions <= 0)
                {
                    MessageBox.Show($"Plan {p.CourseId}/{p.PlanId}: fraction count must be \u2265 1.",
                        "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            var refPlan = includedPlans.First(p => p.IsReference);
            foreach (var p in includedPlans.Where(p => !p.IsReference))
            {
                bool sameFOR = !string.IsNullOrEmpty(p.ImageFOR)
                    && !string.IsNullOrEmpty(refPlan.ImageFOR)
                    && string.Equals(p.ImageFOR, refPlan.ImageFOR, StringComparison.OrdinalIgnoreCase);

                if (!sameFOR && string.IsNullOrEmpty(p.SelectedRegistrationId))
                {
                    var result = MessageBox.Show(
                        $"Plan \"{p.CourseId} / {p.PlanId}\" uses a different CT ({p.ImageId}) " +
                        "than the reference, but no registration is selected.\n\n" +
                        "Without registration, dose mapping will be spatially incorrect.\n\n" +
                        "Continue anyway?",
                        "Missing Registration", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (result == MessageBoxResult.No) return;
                }
            }

            double alphaBeta;
            if (!double.TryParse(TbAlphaBeta.Text, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out alphaBeta) || alphaBeta <= 0)
                alphaBeta = 3.0;

            ResultConfig = new SummationConfig
            {
                Method = RbEQD2.IsChecked == true ? SummationMethod.EQD2 : SummationMethod.Physical,
                GlobalAlphaBeta = alphaBeta,
                Plans = includedPlans.Select(p => new SummationPlanEntry
                {
                    DisplayLabel = $"{p.CourseId} / {p.PlanId}",
                    CourseId = p.CourseId,
                    PlanId = p.PlanId,
                    NumberOfFractions = p.NumberOfFractions,
                    TotalDoseGy = p.TotalDoseGy,
                    PlanNormalization = double.IsNaN(p.PlanNormalization) || p.PlanNormalization <= 0
                        ? 100.0 : p.PlanNormalization,
                    IsReference = p.IsReference,
                    RegistrationId = p.IsReference ? null : p.SelectedRegistrationId,
                    Weight = p.Weight
                }).ToList()
            };

            DialogResult = true;
        }
    }

    // ======================================================================
    // VIEW MODEL FOR ONE ROW IN THE PLAN GRID
    // ======================================================================

    public class PlanRowItem : INotifyPropertyChanged
    {
        private bool _isIncluded;
        private bool _isReference;
        private int _numberOfFractions;
        private double _weight = 1.0;
        private string _selectedRegistrationId = "";
        private List<RegistrationOption> _relevantRegistrations = new List<RegistrationOption>();

        public string CourseId { get; set; }
        public string PlanId { get; set; }
        public string ImageId { get; set; }
        public string ImageFOR { get; set; }
        public double TotalDoseGy { get; set; }
        public double PlanNormalization { get; set; }

        /// <summary>
        /// Shortened FOR UID for display in the grid.
        /// Shows last 8 characters which are typically unique.
        /// Full FOR is available as tooltip.
        /// </summary>
        public string ShortFOR
        {
            get
            {
                if (string.IsNullOrEmpty(ImageFOR)) return "—";
                return ImageFOR.Length > 8
                    ? ".." + ImageFOR.Substring(ImageFOR.Length - 8)
                    : ImageFOR;
            }
        }

        public List<RegistrationOption> RelevantRegistrations
        {
            get => _relevantRegistrations;
            set { _relevantRegistrations = value; OnPropertyChanged(); }
        }

        public bool IsIncluded
        {
            get => _isIncluded;
            set { _isIncluded = value; OnPropertyChanged(); }
        }

        public bool IsReference
        {
            get => _isReference;
            set
            {
                if (_isReference != value)
                {
                    _isReference = value;
                    OnPropertyChanged();
                    if (value) SelectedRegistrationId = "";
                }
            }
        }

        public int NumberOfFractions
        {
            get => _numberOfFractions;
            set { _numberOfFractions = value; OnPropertyChanged(); }
        }

        public double Weight
        {
            get => _weight;
            set { _weight = value; OnPropertyChanged(); }
        }

        public string SelectedRegistrationId
        {
            get => _selectedRegistrationId;
            set { _selectedRegistrationId = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // ======================================================================
    // HELPER TYPES
    // ======================================================================

    public class RegistrationOption
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
    }

    internal class RegistrationInfo
    {
        public string Id { get; set; }
        public string SourceFOR { get; set; }
        public string RegisteredFOR { get; set; }
        public string DateStr { get; set; }
    }
}