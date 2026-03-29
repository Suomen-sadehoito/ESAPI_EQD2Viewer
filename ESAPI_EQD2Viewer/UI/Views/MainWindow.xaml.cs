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

        /// <summary>
        /// DevRunner constructor — no ScriptContext needed.
        /// Structure selection uses snapshot data instead of ESAPI.
        /// </summary>
        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            _context = null;  // Not available in dev mode
            DataContext = viewModel;
            Closed += (s, e) => viewModel?.Dispose();
        }

        private void SelectStructures_Click(object sender, RoutedEventArgs e)
        {
            if (_context == null)
            {
                // Dev mode: structures come from snapshot
                // TODO: Create a StructureSelectionDialog that works with StructureData DTOs
                MessageBox.Show("Structure selection not yet available in dev mode.\n" +
                                "Structures are loaded automatically from fixture data.",
                                "EQD2 Viewer", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

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