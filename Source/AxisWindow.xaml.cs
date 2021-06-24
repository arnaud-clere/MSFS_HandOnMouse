using HandOnMouse.Properties;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using winuser;

namespace HandOnMouse
{
    public class AxisViewModel : INotifyPropertyChanged
    {
        public AxisViewModel(Axis axis)
        {
            _axis = axis;
        }
        public Axis Axis { get { return _axis; } set { if (_axis != value) { _axis = value; NotifyPropertyChanged(); } } }
        public string TriggerMoveHint { get { return _triggerMoveHint; } set { if (_triggerMoveHint != value) { _triggerMoveHint = value; NotifyPropertyChanged(); } } }

        public event PropertyChangedEventHandler PropertyChanged;

        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "") => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        private Axis _axis = null;
        private string _triggerMoveHint = null;
    }

    public partial class AxisWindow : Window
    {
        public AxisWindow(Axis axis)
        {
            DataContext = new AxisViewModel(axis);
            InitializeComponent();
            Mouse.Device.RawMouseMove += new Mouse.RawMouseMoveHandler(Mouse_Move);
            Keyboard.AddKeyDownHandler(this, new KeyEventHandler(Keyboard_KeyDown));
        }

        private void Keyboard_KeyDown(object sender, KeyEventArgs e)
        {
            _keyboardLastKeyDown = e.Key;
        }

        private void SetTriggerButton_Click(object sender, RoutedEventArgs e)
        {
            var b = (ToggleButton)sender;
            var d = (AxisViewModel)DataContext;
            var m = d.Axis;
            if (b.IsChecked ?? false)
            {
                m.MouseButtonsFilter = RAWMOUSE.RI_MOUSE.Reserved;
                m.KeyboardKeyDownFilter = Key.None;
                m.ControllerButtonsFilter = Controller.Buttons.None;
                Controller.UpdateDevices();

                d.TriggerMoveHint = "MOVE mouse + PRESS desired trigger";
            }
            else
            {
                d.TriggerMoveHint = "";
            }
        }

        private void Mouse_Move(Vector move)
        {
            var b = (ToggleButton)FindName("SetTriggerButton");
            if (b.IsChecked ?? false)
            {
                var d = (AxisViewModel)DataContext;
                var m = d.Axis;

                if (m.MouseButtonsFilter == RAWMOUSE.RI_MOUSE.Reserved &&
                    m.ControllerButtonsFilter == Controller.Buttons.None &&
                    m.KeyboardKeyDownFilter == Key.None)
                {
                    if (Mouse.Device.ButtonsPressed != RAWMOUSE.RI_MOUSE.None)
                    {
                        m.MouseButtonsFilter = Mouse.Device.ButtonsPressed;
                        m.KeyboardKeyDownFilter = Key.None;
                        m.ControllerButtonsFilter = Controller.Buttons.None;
                    }
                    else if (_keyboardLastKeyDown != Key.None && Keyboard.IsKeyDown(_keyboardLastKeyDown))
                    {
                        m.MouseButtonsFilter = RAWMOUSE.RI_MOUSE.Reserved;
                        m.KeyboardKeyDownFilter = _keyboardLastKeyDown;
                        m.ControllerButtonsFilter = Controller.Buttons.None;
                    }
                    else
                    {
                        foreach (var c in Controller.Devices)
                        {
                            if (c.ButtonsPressed != Controller.Buttons.None)
                            {
                                m.MouseButtonsFilter = RAWMOUSE.RI_MOUSE.Reserved;
                                m.KeyboardKeyDownFilter = Key.None;
                                _controllerTrigger = c;
                                m.ControllerManufacturerId = c.ManufacturerId;
                                m.ControllerProductId      = c.ProductId;
                                m.ControllerButtonsFilter  = c.ButtonsPressed;
                                break;
                            }
                        }
                    }

                    if (m.MouseButtonsFilter == RAWMOUSE.RI_MOUSE.Reserved &&
                        m.ControllerButtonsFilter == Controller.Buttons.None &&
                        m.KeyboardKeyDownFilter == Key.None)
                    {
                        d.TriggerMoveHint = "MOVE mouse + PRESS desired trigger";
                    }
                    else
                    {
                        d.TriggerMoveHint = "MOVE mouse firmly in increase direction";

                        Mouse.Device.StartDrag(move);
                    }
                }
                else
                {
                    var v = Mouse.Device.Drag;
                    if (v.Length < Settings.Default.SetMinimumMouseMove)
                    {
                        d.TriggerMoveHint = "MOVE mouse firmly in increase direction";
                    }
                    else if (!(Math.Abs(v.X) > 2*Math.Abs(v.Y) || Math.Abs(v.Y) > 2* Math.Abs(v.X)))
                    {
                        d.TriggerMoveHint = "MOVE mouse firmly in F/B/L/R direction";
                    }
                    else
                    {
                        d.TriggerMoveHint = "RELEASE the trigger";

                        if ((m.MouseButtonsFilter != RAWMOUSE.RI_MOUSE.Reserved &&
                            !Mouse.Device.ButtonsPressed.HasFlag(m.MouseButtonsFilter)) ||
                            (m.KeyboardKeyDownFilter != Key.None &&
                            !Keyboard.IsKeyDown(m.KeyboardKeyDownFilter)) ||
                            (_controllerTrigger != null &&
                            !_controllerTrigger.ButtonsPressed.HasFlag(m.ControllerButtonsFilter)))
                        {
                            d.TriggerMoveHint = "";

                            _controllerTrigger = null;
                            m.IncreaseDirection =
                                Math.Abs(v.Y) > Math.Abs(v.X) && v.Y < 0 ? Axis.Direction.Push :
                                Math.Abs(v.Y) > Math.Abs(v.X) && v.Y > 0 ? Axis.Direction.Draw :
                                Math.Abs(v.X) > Math.Abs(v.Y) && v.X > 0 ? Axis.Direction.Right : Axis.Direction.Left;

                            Mouse.Device.StopDrag();
                            b.IsChecked = false;
                        }
                    }
                }
            }
        }

        private void Button_Reset(object sender, RoutedEventArgs e)
        {
            Axis.MappingsReset(MainWindow.MappingFile(), ((AxisViewModel)DataContext).Axis);
        }
        private void Button_Save(object sender, RoutedEventArgs e)
        {
            Axis.MappingsSaveCustom(MainWindow.MappingFile(), ((AxisViewModel)DataContext).Axis);
            Close();
        }

        // Implementation

        private Controller _controllerTrigger = null;
        private Key _keyboardLastKeyDown = Key.None;
    }
}
