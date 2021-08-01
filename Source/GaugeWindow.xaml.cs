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
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
        }
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }
    }
}
