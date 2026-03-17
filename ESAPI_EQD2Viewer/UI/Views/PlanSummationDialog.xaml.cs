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

        public ObservableCollection<PlanRowItem> PlanRows { get; } = new ObservableCollection<PlanRowItem>();

        public SummationConfig ResultConfig { get; private set; }

        public PlanSummationDialog(Patient patient, PlanSetup currentPlan)
        {
            InitializeComponent();

            _patient = patient ?? throw new ArgumentNullException(nameof(patient));
            _currentPlan = currentPlan;

            PopulatePlans();

            PlanGrid.ItemsSource = PlanRows;
            // NOTE: No RegistrationColumn.ItemsSource — each row's ComboBox
            // is bound to its own RelevantRegistrations list via XAML binding.
        }

        /// <summary>
        /// Indexes all patient registrations with their source/target FOR UIDs.
        /// </summary>
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
                catch
                {
                    // Skip unreadable registrations
                }
            }
            return list;
        }

        /// <summary>
        /// Returns only registrations where planFOR participates as source or target.
        /// Always includes "None (same CT)" as first option.
        /// </summary>
        private List<RegistrationOption> FilterRegistrationsForFOR(
            string planFOR, List<RegistrationInfo> allRegs)
        {
            var options = new List<RegistrationOption>
            {
                new RegistrationOption { Id = "", DisplayName = "None (same CT)" }
            };

            if (string.IsNullOrEmpty(planFOR))
                return options;

            foreach (var reg in allRegs)
            {
                bool isSource = string.Equals(reg.SourceFOR, planFOR, StringComparison.OrdinalIgnoreCase);
                bool isTarget = string.Equals(reg.RegisteredFOR, planFOR, StringComparison.OrdinalIgnoreCase);

                if (isSource || isTarget)
                {
                    string direction = isSource ? "→ target" : "← source";
                    options.Add(new RegistrationOption
                    {
                        Id = reg.Id,
                        DisplayName = $"{reg.Id} ({direction}, {reg.DateStr})"
                    });
                }
            }

            return options;
        }

        private void PopulatePlans()
        {
            if (_patient.Courses == null) return;

            var allRegs = IndexAllRegistrations();

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

                    // Get image info for FOR filtering and display
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
                    catch { /* Image access can fail in some edge cases */ }

                    // Per-row filtered registration list
                    var relevantRegs = FilterRegistrationsForFOR(imageFOR, allRegs);

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
                        RelevantRegistrations = relevantRegs
                    };

                    PlanRows.Add(row);
                }
            }
        }

        private void Compute_Click(object sender, RoutedEventArgs e)
        {
            var includedPlans = PlanRows.Where(p => p.IsIncluded).ToList();

            if (includedPlans.Count < 2)
            {
                MessageBox.Show("Select at least 2 plans for summation.",
                    "Summation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!includedPlans.Any(p => p.IsReference))
            {
                MessageBox.Show("Set one plan as the Reference (Ref column).",
                    "Summation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            foreach (var p in includedPlans)
            {
                if (p.NumberOfFractions <= 0)
                {
                    MessageBox.Show($"Plan {p.CourseId}/{p.PlanId} has invalid fraction count.",
                        "Summation", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            // Smart warning: only warn if FORs actually differ and no registration selected
            var refPlan = includedPlans.First(p => p.IsReference);
            foreach (var p in includedPlans.Where(p => !p.IsReference))
            {
                bool sameFOR = !string.IsNullOrEmpty(p.ImageFOR)
                    && !string.IsNullOrEmpty(refPlan.ImageFOR)
                    && string.Equals(p.ImageFOR, refPlan.ImageFOR, StringComparison.OrdinalIgnoreCase);

                if (!sameFOR && string.IsNullOrEmpty(p.SelectedRegistrationId))
                {
                    var result = MessageBox.Show(
                        $"Plan {p.CourseId}/{p.PlanId} (Image: {p.ImageId}) is on a different CT " +
                        $"than the reference but has no registration selected.\n\n" +
                        "Dose mapping will be incorrect. Continue anyway?",
                        "Registration Warning", MessageBoxButton.YesNo, MessageBoxImage.Warning);
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
                    RegistrationId = p.SelectedRegistrationId,
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

        public string CourseId { get; set; }
        public string PlanId { get; set; }

        /// <summary>
        /// Image ID for display in the grid. Helps user identify which CT each plan uses.
        /// </summary>
        public string ImageId { get; set; }

        /// <summary>
        /// Frame of Reference UID. Used to filter registrations.
        /// Not displayed in grid, only used internally.
        /// </summary>
        public string ImageFOR { get; set; }

        public double TotalDoseGy { get; set; }
        public double PlanNormalization { get; set; }

        /// <summary>
        /// Per-row filtered registration list. ComboBox binds to this.
        /// Only contains registrations where this plan's FOR participates.
        /// </summary>
        public List<RegistrationOption> RelevantRegistrations { get; set; }
            = new List<RegistrationOption>();

        public bool IsIncluded
        {
            get => _isIncluded;
            set { _isIncluded = value; OnPropertyChanged(); }
        }

        public bool IsReference
        {
            get => _isReference;
            set { _isReference = value; OnPropertyChanged(); }
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