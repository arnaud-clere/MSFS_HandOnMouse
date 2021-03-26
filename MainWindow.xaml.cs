using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

using winuser;
using Microsoft.FlightSimulator.SimConnect;
using Microsoft.Win32;
using System.IO;

using HandOnMouse.Properties;
using System.Windows.Input;
using System.Collections.ObjectModel;
using System.Windows.Data;

namespace HandOnMouse
{
    public class ViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<Axis> Mappings { get { return Axis.Mappings; } }
        public bool ShowAll
        {
            get { return _showAll; }
            set
            {
                if (_showAll != value)
                {
                    _showAll = value;
                    NotifyPropertyChanged();
                }
            }
        }
        public Brush StatusBrushForText
        {
            get { return _statusBrushForText; }
            set
            {
                if (_statusBrushForText != value)
                {
                    _statusBrushForText = value;
                    NotifyPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "") => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        private bool _showAll = true;
        private Brush _statusBrushForText = new SolidColorBrush(Colors.Gray);
    }
    public class BooleanToWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo _)
        {
            return (bool)value ? 100 : 140;
        }
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo _)
        {
            return false;
        }
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        const int WM_USER_SIMCONNECT = (int)WM.USER + 2;

        /// <summary>SimConnect Requests and Data Definitions are actually defined dynamically for each Axis in Axis.Mappings</summary>
        public enum Definitions { Zero = 0 }

        IntPtr _hwnd;
        SimConnect _simConnect;
        bool _connected = false;
        RAWMOUSE.RI_MOUSE _buttons = RAWMOUSE.RI_MOUSE.None;

        public MainWindow()
        {
            DataContext = new ViewModel();

            InitializeComponent();

            Settings.Default.PropertyChanged += new PropertyChangedEventHandler(Settings_Changed);
            Axis.Read(Path.Combine(MappingsDir(), Settings.Default.MappingFile));
        }
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var source = PresentationSource.FromVisual(this) as HwndSource;
            source.AddHook(WndProc);
            _hwnd = source.Handle;

            var rid = new RAWINPUTDEVICE[1];
            rid[0].UsagePage = HID_USAGE_PAGE.GENERIC;
            rid[0].Usage = HID_USAGE.MOUSE;
            rid[0].Flags = RAWINPUTDEVICE.RIDEV.INPUTSINK;
            rid[0].Target = _hwnd;
            if (!User32.RegisterRawInputDevices(rid, (uint)rid.Length, (uint)Marshal.SizeOf(rid[0])))
            {
                Debug.WriteLine($"User32.RegisterRawInputDevices failed with Marshal.GetLastWin32Error: {Marshal.GetLastWin32Error()}");
            }
        }
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (msg)
            {
                case WM_USER_SIMCONNECT:
                    _simConnect?.ReceiveMessage(); // and dispatch to OnRecv... events
                    break;
                case (int)WM.INPUT:
                    RawInput_Handler(hwnd, msg, wParam, lParam, ref handled);
                    break;
            }
            return hwnd;
        }
        private void Settings_Changed(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Sensitivity")
            {
                ((ViewModel)DataContext).StatusBrushForText = new SolidColorBrush(_connected && Settings.Default.Sensitivity > 0 ? Colors.Black : Colors.Gray);
            }
        }
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }
        private void Window_Close(object sender, RoutedEventArgs e)
        {
            Close();
        }
        private void Window_Compact(object sender, RoutedEventArgs e)
        {
            ((ViewModel)DataContext).ShowAll = !((ViewModel)DataContext).ShowAll;
            Width = ((ViewModel)DataContext).ShowAll ? 160 : 120;
        }
        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.WidthChanged && WindowState == WindowState.Normal) Left += e.PreviousSize.Width - e.NewSize.Width;
        }
        private void Window_Minimize(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized; 
        }
        private void Window_Help(object sender, RoutedEventArgs e)
        {
            Process.Start("https://github.com/arnaud-clere/MSFS_HandOnMouse/blob/main/README.md#version-1");
        }

        public void Connect_Click(object sender, RoutedEventArgs e)
        {
            if (_simConnect == null)
            {
                try
                {
                    _simConnect = new SimConnect("HandOnMouse", _hwnd, WM_USER_SIMCONNECT, null, 0);
                    _simConnect.Text(SIMCONNECT_TEXT_TYPE.PRINT_BLACK, 2, Definitions.Zero, "HandOnMouse connected!");
                    ChangeButtonStatus(false, connectButton, true, "DISCONNECT");

                    _simConnect.OnRecvOpen += new SimConnect.RecvOpenEventHandler(SimConnect_OnRecvOpen);
                    _simConnect.OnRecvQuit += new SimConnect.RecvQuitEventHandler(SimConnect_OnRecvQuit);
                    _simConnect.OnRecvException += new SimConnect.RecvExceptionEventHandler(SimConnect_OnRecvException);
                    _simConnect.OnRecvSimobjectData += new SimConnect.RecvSimobjectDataEventHandler(SimConnect_OnRecvData);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
            else
            {
                Disconnect();
            }
        }
        private void SimConnect_OnRecvOpen(SimConnect sender, SIMCONNECT_RECV_OPEN data)
        {
            _connected = true;
            ((ViewModel)DataContext).StatusBrushForText = new SolidColorBrush(Settings.Default.Sensitivity > 0 ? Colors.Black : Colors.Gray);
            for (int i = 0; i < Axis.Mappings.Count; i++)
            {
                if (Axis.Mappings[i].SimVarName.Length > 0)
                {
                    _simConnect.AddToDataDefinition((Definitions)i, Axis.Mappings[i].SimVarName, Axis.Mappings[i].SimVarUnit, SIMCONNECT_DATATYPE.FLOAT64, 0.1f, SimConnect.SIMCONNECT_UNUSED);
                    _simConnect.RegisterDataDefineStruct<double>((Definitions)i);
                    _simConnect.RequestDataOnSimObject((Definitions)i, (Definitions)i, (uint)SIMCONNECT_SIMOBJECT_TYPE.USER, SIMCONNECT_PERIOD.SIM_FRAME, SIMCONNECT_DATA_REQUEST_FLAG.CHANGED, 0, 0, 0);
                }
            }
        }
        private void SimConnect_OnRecvQuit(SimConnect sender, SIMCONNECT_RECV data)
        {
            Disconnect();
        }
        public void Disconnect()
        {
            if (_simConnect == null) return;

            try
            {
                _simConnect.Text(SIMCONNECT_TEXT_TYPE.PRINT_RED, 2, Definitions.Zero, "HandOnMouse disconnected!");
                _simConnect.Dispose();
                _simConnect = null;
                _connected = false;
                ((ViewModel)DataContext).StatusBrushForText = new SolidColorBrush(Colors.Gray);

                ChangeButtonStatus(true, connectButton, true, "CONNECT FS");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        private void SimConnect_OnRecvException(SimConnect sender, SIMCONNECT_RECV_EXCEPTION data)
        {
            SIMCONNECT_EXCEPTION e = (SIMCONNECT_EXCEPTION)data.dwException;
            Console.WriteLine("SimConnect_OnRecvException: " + e.ToString());
            ((ViewModel)DataContext).StatusBrushForText = new SolidColorBrush(Colors.Red);
        }

        public void ChangeButtonStatus(bool active, Button b, bool enabled, string text)
        {
            b.BorderBrush = new SolidColorBrush(active ? Colors.DarkGreen : Colors.DarkRed);
            b.Foreground  = new SolidColorBrush(active ? Colors.DarkGreen : Colors.DarkRed);

            if (!string.IsNullOrEmpty(text))
                b.Content = text;

            b.IsEnabled = enabled;
        }

        public string MappingsDir() { return Path.Combine(Directory.GetCurrentDirectory(), "Mappings"); }
        public void Window_File(object sender, RoutedEventArgs e)
        {
            if (_simConnect != null && _connected)
            {
                MessageBox.Show("Please DISCONNECT before changing mappings!");
            }
            else
            {
                var openFileDialog = new OpenFileDialog
                {
                    InitialDirectory = MappingsDir(),
                    Filter = "HandOnMouse mappings file (*.ini)|*.ini",
                    FilterIndex = 2
                };
                if (openFileDialog.ShowDialog() == true)
                {
                    var filePath = openFileDialog.FileName.Replace(MappingsDir() + @"\", "");
                    var errors = Axis.Read(filePath);
                    if (errors.Length > 0)
                    {
                        MessageBox.Show(filePath + ":\n" + errors);
                    }
                    ((TextBlock)window.FindName("MappingsFile")).Text = filePath;
                }
            }
        }
        
        private IntPtr RawInput_Handler(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != (int)WM.INPUT) return hwnd;
            {
                RAWINPUT input;
                int inputSize = Marshal.SizeOf(typeof(RAWINPUT));
                int headerSize = Marshal.SizeOf(typeof(RAWINPUTHEADER));
                int outSize = User32.GetRawInputData(lParam, RID.INPUT, out input, ref inputSize, headerSize);
                if (outSize == inputSize)
                {
                    if (input.header.Type == RAWINPUTHEADER.RIM.TYPEMOUSE && input.mouse.Flags == RAWMOUSE.MOUSE.MOVE_RELATIVE)
                    {
                        var mouse = input.mouse;

                        // Coalesce button up/down events into homButtonsDown status
                        var buttons = mouse.ButtonFlags;
                        _buttons |= (buttons & (
                            RAWMOUSE.RI_MOUSE.LEFT_BUTTON_DOWN |
                            RAWMOUSE.RI_MOUSE.MIDDLE_BUTTON_DOWN |
                            RAWMOUSE.RI_MOUSE.RIGHT_BUTTON_DOWN |
                            RAWMOUSE.RI_MOUSE.BUTTON_4_DOWN |
                            RAWMOUSE.RI_MOUSE.BUTTON_5_DOWN));
                        // Check UP after DOWN in case both are true in a single coalesced message
                        if (buttons.HasFlag(RAWMOUSE.RI_MOUSE.LEFT_BUTTON_UP    )) _buttons &= ~RAWMOUSE.RI_MOUSE.LEFT_BUTTON_DOWN  ;
                        if (buttons.HasFlag(RAWMOUSE.RI_MOUSE.MIDDLE_BUTTON_UP  )) _buttons &= ~RAWMOUSE.RI_MOUSE.MIDDLE_BUTTON_DOWN;
                        if (buttons.HasFlag(RAWMOUSE.RI_MOUSE.RIGHT_BUTTON_UP   )) _buttons &= ~RAWMOUSE.RI_MOUSE.RIGHT_BUTTON_DOWN ;
                        if (buttons.HasFlag(RAWMOUSE.RI_MOUSE.BUTTON_4_UP       )) _buttons &= ~RAWMOUSE.RI_MOUSE.BUTTON_4_DOWN     ;
                        if (buttons.HasFlag(RAWMOUSE.RI_MOUSE.BUTTON_5_UP       )) _buttons &= ~RAWMOUSE.RI_MOUSE.BUTTON_5_DOWN     ;
                        // Update complement to enable MouseButtonsFilter with UP requirements
                        if (_buttons.HasFlag(RAWMOUSE.RI_MOUSE.LEFT_BUTTON_DOWN  )) _buttons &= ~RAWMOUSE.RI_MOUSE.LEFT_BUTTON_UP  ; else _buttons |= RAWMOUSE.RI_MOUSE.LEFT_BUTTON_UP  ;
                        if (_buttons.HasFlag(RAWMOUSE.RI_MOUSE.MIDDLE_BUTTON_DOWN)) _buttons &= ~RAWMOUSE.RI_MOUSE.MIDDLE_BUTTON_UP; else _buttons |= RAWMOUSE.RI_MOUSE.MIDDLE_BUTTON_UP;
                        if (_buttons.HasFlag(RAWMOUSE.RI_MOUSE.RIGHT_BUTTON_DOWN )) _buttons &= ~RAWMOUSE.RI_MOUSE.RIGHT_BUTTON_UP ; else _buttons |= RAWMOUSE.RI_MOUSE.RIGHT_BUTTON_UP ;
                        if (_buttons.HasFlag(RAWMOUSE.RI_MOUSE.BUTTON_4_DOWN     )) _buttons &= ~RAWMOUSE.RI_MOUSE.BUTTON_4_UP     ; else _buttons |= RAWMOUSE.RI_MOUSE.BUTTON_4_UP     ;
                        if (_buttons.HasFlag(RAWMOUSE.RI_MOUSE.BUTTON_5_DOWN     )) _buttons &= ~RAWMOUSE.RI_MOUSE.BUTTON_5_UP     ; else _buttons |= RAWMOUSE.RI_MOUSE.BUTTON_5_UP     ;

                        for (int i = 0; i < Axis.Mappings.Count; i++)
                        {
                            var m = Axis.Mappings[i];
                            if (_buttons.HasFlag(m.ButtonsFilter))
                            {
                                m.UpdateChanges(Properties.Settings.Default.Sensitivity, 
                                    m.IncreaseDirection == Axis.Direction.Push ? -mouse.LastY :
                                    m.IncreaseDirection == Axis.Direction.Draw ?  mouse.LastY :
                                    m.IncreaseDirection == Axis.Direction.Right ? mouse.LastX : -mouse.LastX);
                            }
                            if (m.WaitButtonsReleased)
                            {
                                if (!_buttons.HasFlag(m.ButtonsFilter)) // CurrentChange end
                                {
                                    m.CurrentChange = 0;
                                    // Keeping the remainder for the current change would mysteriously:
                                    // - increase a subsequent move in the opposite direction after even a long time
                                    // - decrease a subsequent move in the same direction to potentially insignificant moves
                                    if (m.SimVarChange != 0)
                                    {
                                        if (_connected)
                                            _simConnect?.RequestDataOnSimObject((Definitions)i, (Definitions)i, (uint)SIMCONNECT_SIMOBJECT_TYPE.USER, SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
                                        else
                                            m.UpdateSimVar(m.SimVarValue);
                                    }
                                }
                            }
                            else // !m.WaitButtonsReleased
                            {
                                if (m.SimVarChange != 0) // CurrentChange end
                                {
                                    m.CurrentChange = 0;
                                    // Since SimVarChange is proportional to CurrentChange modulo SimVarIncrement
                                    if (_connected)
                                        _simConnect?.RequestDataOnSimObject((Definitions)i, (Definitions)i, (uint)SIMCONNECT_SIMOBJECT_TYPE.USER, SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
                                    else
                                        m.UpdateSimVar(m.SimVarValue);
                                }
                            }
                            m.ChangeColorForText = m.SimVarChange != 0 && m.CurrentChange == 0 ? Colors.Red : Axis.TextColorFromChange(m.CurrentChange);
                        }
                    }
                    else
                    {
                        Debug.WriteLine("Received unsupported RAWINPUT");
                    }
                }
                else
                {
                    Debug.WriteLine("Received unsupported WM.INPUT");
                }
            }
            return hwnd;
        }
        public void SimConnect_OnRecvData(SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA data)
        {
            // if (data.dwObjectID != 1) return;
            if (data.dwentrynumber != 1 || data.dwoutof != 1 || data.dwDefineCount != 1) return;
            int i = (int)data.dwRequestID;
            if (i >= Axis.Mappings.Count || (uint)data.dwDefineID != i) return;

            try
            {
                var m = Axis.Mappings[i];
                var valueInSim = (double)data.dwData[0];
                m.UpdateSimVar(valueInSim);
                if (m.SimVarValue != valueInSim)
                    _simConnect?.SetDataOnSimObject((Definitions)i, (uint)SIMCONNECT_SIMOBJECT_TYPE.USER, SIMCONNECT_DATA_SET_FLAG.DEFAULT, m.SimVarValue);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }
    }
}
