using System.Windows;
using System.Windows.Input;
using ExpandScreen.UI.ViewModels;

namespace ExpandScreen.UI.Views
{
    public partial class PerformanceTestWindow : Window
    {
        private readonly PerformanceTestViewModel _viewModel = new();

        public PerformanceTestWindow()
        {
            InitializeComponent();
            DataContext = _viewModel;
            Closed += (_, _) => _viewModel.Dispose();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

