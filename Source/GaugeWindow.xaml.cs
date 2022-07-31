using System;
using System.Windows;
using System.Windows.Input;

namespace HandOnMouse
{
    public partial class GaugeWindow : Window
    {
        public GaugeWindow()
        {
            InitializeComponent();
        }
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        private void Window_MouseEnter(object sender, MouseEventArgs e)
        {
            ((ViewModel)DataContext).GaugeOpacity = 0.1;
        }

        private void Window_MouseLeave(object sender, MouseEventArgs e)
        {
            ((ViewModel)DataContext).GaugeOpacity = 0;
        }
    }
}
