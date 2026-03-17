using ESAPI_EQD2Viewer.UI.ViewModels;
using ESAPI_EQD2Viewer.Core.Models;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Shapes;
using VMS.TPS.Common.Model.API;

namespace ESAPI_EQD2Viewer.UI.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private readonly ScriptContext _context;

        public MainWindow(MainViewModel viewModel, ScriptContext context)
        {
            InitializeComponent();
            _viewModel = viewModel;
            _context = context;
            DataContext = viewModel;
            Closed += (s, e) => viewModel?.Dispose();
        }

        private void SelectStructures_Click(object sender, RoutedEventArgs e)
        {
            var plan = _context.ExternalPlanSetup;
            if (plan?.StructureSet == null)
            {
                MessageBox.Show("No plan or structure set available.",
                    "EQD2 Viewer", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new StructureSelectionDialog(plan);
            dialog.Owner = this;

            if (dialog.ShowDialog() == true && dialog.SelectedStructures.Any())
            {
                _viewModel.AddStructuresForDVH(dialog.SelectedStructures);
            }
        }

        private void ColorSwatch_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Rectangle rect && rect.DataContext is IsodoseLevel level)
            {
                uint[] palette = IsodoseLevel.ColorPalette;
                uint current = level.Color;
                int idx = -1;
                for (int i = 0; i < palette.Length; i++)
                    if (palette[i] == current) { idx = i; break; }
                int next = (idx + 1) % palette.Length;
                level.Color = palette[next];
            }
        }
    }
}
