using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls.Primitives;

using winuser;

namespace HandOnMouse
{
    public class AxisViewModel : INotifyPropertyChanged
    {
        public AxisViewModel(Axis axis)
        {
            _axis = axis;
        }
        public Axis Axis
        {
            get { return _axis; }
            set
            {
                if (_axis != value)
                {
                    _axis = value;
                    NotifyPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "") => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        private Axis _axis = null;
    }

    public partial class AxisWindow : Window
    {
        public AxisWindow(Axis axis)
        {
            DataContext = new AxisViewModel(axis);
            InitializeComponent();
            Mouse.Device.RawMouseMove += new Mouse.RawMouseMoveHandler(Mouse_Move);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (Owner != null)
            {
                // FIXME
                Top  = Owner.Top;
                Left = Owner.Left + Width;
            }
        }

        private void Mouse_Move(Vector move)
        {
            var setTriggerButton = (ToggleButton)FindName("SetTriggerButton");
            if (setTriggerButton.IsChecked ?? false && !move.IsZero)
            {
                if (mouseTriggerButtons == RAWMOUSE.RI_MOUSE.None &&
                    controllerTrigger == null)
                {
                    if (Mouse.Device.ButtonsPressed != RAWMOUSE.RI_MOUSE.None)
                    {
                        mouseTriggerButtons = Mouse.Device.ButtonsPressed;
                    }
                    else
                    {
                        foreach (var c in Controller.Devices)
                        {
                            if (c.ButtonsPressed != Controller.Buttons.None)
                            {
                                controllerTrigger = c;
                                controllerTriggerButtons = c.ButtonsPressed;
                            }
                        }
                    }
                }
                else
                {
                    if ((mouseTriggerButtons != RAWMOUSE.RI_MOUSE.None &&
                        !Mouse.Device.ButtonsPressed.HasFlag(mouseTriggerButtons)) ||
                        (controllerTrigger != null &&
                        !controllerTrigger.ButtonsPressed.HasFlag(controllerTriggerButtons)))
                    {
                        var m = ((AxisViewModel)DataContext).Axis;
                        m.MouseButtonsFilter      = mouseTriggerButtons;
                        m.Controller              = controllerTrigger;
                        m.ControllerButtonsFilter = controllerTriggerButtons;
                        setTriggerButton.IsChecked = false;
                    }
                }
            }
        }

        private void Button_Reset(object sender, RoutedEventArgs e)
        {
            Axis.MappingsReset(MainWindow.MappingFile(), ((AxisViewModel)DataContext).Axis);
        }
        private void Button_Cancel(object sender, RoutedEventArgs e)
        {
            Close();
        }
        private void Button_Save(object sender, RoutedEventArgs e)
        {
            Axis.MappingsSaveCustom(MainWindow.MappingFile(), ((AxisViewModel)DataContext).Axis);
            Close();
        }

        // Implementation

        RAWMOUSE.RI_MOUSE mouseTriggerButtons = RAWMOUSE.RI_MOUSE.None;
        Controller controllerTrigger = null;
        Controller.Buttons controllerTriggerButtons = Controller.Buttons.None;
    }
}
