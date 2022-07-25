using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

// See https://nicksnettravels.builttoroam.com/xaml-user-controls/

namespace HandOnMouse.Source
{
    public partial class Scale : UserControl
    {
        public Scale()
        {
            InitializeComponent();
            for (byte i = 0; i < 4; i++)
            {
                //_scale.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(i, GridUnitType.Star) });
                //var area = new Rectangle { Fill = new SolidColorBrush { Color = Color.FromArgb((byte)(i * 64), (byte)(i * 64), (byte)(i * 64), (byte)(i * 64)) } };
                //_scale.Children.Add(area);
                //Grid.SetColumn(area, i);
            }
            SetValue(NegProperty, 20);
            SetValue(PosProperty, 80);
            SetValue(MaxProperty, 20);
        }

        public double Neg
        {
            get => (double)GetValue(NegProperty);
            set { SetValue(NegProperty, value); }
        }
        public static readonly DependencyProperty NegProperty =
            DependencyProperty.Register("Neg", typeof(double), typeof(Scale), new PropertyMetadata(0));
        public double Pos
        {
            get => (double)GetValue(PosProperty);
            set { SetValue(PosProperty, value); }
        }
        public static readonly DependencyProperty PosProperty =
            DependencyProperty.Register("Pos", typeof(double), typeof(Scale), new PropertyMetadata(0));
        public double Max
        {
            get => (double)GetValue(MaxProperty);
            set { SetValue(MaxProperty, value); }
        }
        public static readonly DependencyProperty MaxProperty =
            DependencyProperty.Register("Max", typeof(double), typeof(Scale), new PropertyMetadata(0));
    }
}
