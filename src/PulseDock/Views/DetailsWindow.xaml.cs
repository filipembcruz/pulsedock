using System.Windows;
using System.Windows.Input;

namespace PulseDock.Views
{
    public partial class DetailsWindow : Window
    {
        public DetailsWindow()
        {
            InitializeComponent();
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    Hide();
                }
            };
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }
    }
}
