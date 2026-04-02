using EQD2Viewer.Services.Rendering;
using EQD2Viewer.App.UI.ViewModels;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Shapes;

namespace EQD2Viewer.App.UI.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = viewModel;
            Closed += (s, e) => viewModel?.Dispose();
        }

        private void SelectStructures_Click(object sender, RoutedEventArgs e)
        {
            var structures = _viewModel.AvailableStructures;
            if (structures == null || structures.Count == 0)
            {
                MessageBox.Show("No structures available.",
                    "EQD2 Viewer", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new StructureSelectionDialog(structures);
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