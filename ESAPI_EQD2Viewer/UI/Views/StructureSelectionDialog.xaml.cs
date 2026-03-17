using System.Collections.Generic;
using System.Linq;
using System.Windows;
using VMS.TPS.Common.Model.API;

namespace ESAPI_EQD2Viewer.UI.Views
{
    public partial class StructureSelectionDialog : Window
    {
        public IEnumerable<Structure> SelectedStructures { get; private set; }

        public StructureSelectionDialog(PlanSetup plan)
        {
            InitializeComponent();
            if (plan?.StructureSet != null)
            {
                StructureListBox.ItemsSource = plan.StructureSet.Structures
                    .Where(s => !s.IsEmpty)
                    .GroupBy(s => s.Id)
                    .Select(g => g.First())
                    .OrderBy(s => s.Id)
                    .ToList();
            }
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            SelectedStructures = StructureListBox.SelectedItems.Cast<Structure>().ToList();
            DialogResult = true;
        }
    }
}
