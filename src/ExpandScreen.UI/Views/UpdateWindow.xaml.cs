using System.Windows;
using System.Windows.Input;

namespace ExpandScreen.UI.Views
{
    public partial class UpdateWindow : Window
    {
        public UpdateWindow()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                if (DataContext is ViewModels.UpdateViewModel vm)
                {
                    vm.CheckUpdatesCommand.Execute(null);
                }
            };
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

