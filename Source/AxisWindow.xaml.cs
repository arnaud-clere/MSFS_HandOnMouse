using HandOnMouse.Properties;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using winuser;

namespace HandOnMouse
{
    public class AxisViewModel : INotifyPropertyChanged
    {
        public Axis Axis { get => _axis; set { if (_axis != value) { _axis = value; NotifyPropertyChanged(); } } }
        public string SimAircraftTitle { get => _simAircraftTitle; set { if (_simAircraftTitle != value) { _simAircraftTitle = value; NotifyPropertyChanged(); } } }
        public bool IsSimAircraftKnown => SimAircraftTitle != null && SimAircraftTitle.Length > 0;
        public bool IsForAircraft { get => _isForAircraft; set { if (_isForAircraft != value) { _isForAircraft = value; NotifyPropertyChanged(); } } }
        public string TriggerMoveHint { get => _triggerMoveHint; set { if (_triggerMoveHint != value) { _triggerMoveHint = value; NotifyPropertyChanged(); } } }

        public event PropertyChangedEventHandler PropertyChanged;

        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "") => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        
        private Axis _axis = null;
        private bool _isForAircraft = false;
        private string _simAircraftTitle = "";
        private string _triggerMoveHint = "or just set options below:";
    }

    public partial class AxisWindow : Window
    {
        public AxisWindow(Axis axis, string simAircraftName)
        {
            DataContext = new AxisViewModel { Axis = axis, SimAircraftTitle = simAircraftName };
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

                d.TriggerMoveHint = "1. PRESS desired trigger + MOVE mouse";
            }
            else
            {
                d.TriggerMoveHint = "or just set options below:";
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
                        d.TriggerMoveHint = "1. PRESS desired trigger + MOVE mouse";
                    }
                    else
                    {
                        d.TriggerMoveHint = "2. MOVE mouse firmly in increase direction";

                        Mouse.Device.StartDrag(move);
                    }
                }
                else
                {
                    var v = Mouse.Device.Drag;
                    if (v.Length < Settings.Default.SetMinimumMouseMove)
                    {
                        d.TriggerMoveHint = "2. MOVE mouse firmly in increase direction";
                    }
                    else if (!(Math.Abs(v.X) > 2*Math.Abs(v.Y) || Math.Abs(v.Y) > 2* Math.Abs(v.X)))
                    {
                        d.TriggerMoveHint = "2. MOVE mouse firmly in ←↑↓→ direction";
                    }
                    else
                    {
                        d.TriggerMoveHint = "3. RELEASE the trigger + MOVE mouse";

                        if ((m.MouseButtonsFilter != RAWMOUSE.RI_MOUSE.Reserved &&
                            !Mouse.Device.ButtonsPressed.HasFlag(m.MouseButtonsFilter)) ||
                            (m.KeyboardKeyDownFilter != Key.None &&
                            !Keyboard.IsKeyDown(m.KeyboardKeyDownFilter)) ||
                            (_controllerTrigger != null &&
                            !_controllerTrigger.ButtonsPressed.HasFlag(m.ControllerButtonsFilter)))
                        {
                            d.TriggerMoveHint = "or just set options below:";

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
            var d = (AxisViewModel)DataContext;
            var errors = d.Axis.Reset(d.IsForAircraft ? d.SimAircraftTitle : "");
            if (errors.Length > 0)
            {
                Trace.WriteLine(errors);
                MessageBox.Show(errors, "HandOnMouse");
            }
        }
        private void Button_Save(object sender, RoutedEventArgs e)
        {
            var d = (AxisViewModel)DataContext;
            var errors = d.Axis.Save(d.IsForAircraft ? d.SimAircraftTitle : "");
            if (errors.Length > 0)
            {
                Trace.WriteLine(errors);
                MessageBox.Show(errors, "HandOnMouse");
            }
            Close();
        }

        // Implementation

        private Controller _controllerTrigger = null;
        private Key _keyboardLastKeyDown = Key.None;
    }
}
