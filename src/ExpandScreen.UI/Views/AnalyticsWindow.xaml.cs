using System.Windows;
using System.Windows.Input;
using ExpandScreen.UI.ViewModels;

namespace ExpandScreen.UI.Views
{
    public partial class AnalyticsWindow : Window
    {
        private readonly AnalyticsViewModel? _viewModel;

        public AnalyticsWindow()
        {
            InitializeComponent();

            if (Application.Current is not App app)
            {
                return;
            }

            _viewModel = new AnalyticsViewModel(app.AnalyticsService, app.ConfigService);
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

