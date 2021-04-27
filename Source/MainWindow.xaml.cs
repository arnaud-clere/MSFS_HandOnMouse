using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

using Microsoft.FlightSimulator.SimConnect;
using Microsoft.Win32;

using winuser;

using HandOnMouse.Properties;

namespace HandOnMouse
{
    public struct SmartTrimAxis
    {
        public double Trim;
        public double TrimmedAxis;
    }
    public struct SmartEngineAxis
    {
        public double Engine1;
        public double Engine2;
        public double Engine3;
        public double Engine4;
    }

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
        public enum Definitions { None = 0, AircraftLoaded = 1, IndicatedAirSpeedKnots = 2, DesignCruiseSpeedFeetPerSec = 3, EnginesCount = 4, Axis = 5 }

        IntPtr _hwnd;
        RAWMOUSE.RI_MOUSE _buttons = RAWMOUSE.RI_MOUSE.None;
        SimConnect _simConnect;
        bool _connected = false;

        public MainWindow()
        {
            DataContext = new ViewModel();

            InitializeComponent();

            Settings.Default.PropertyChanged += new PropertyChangedEventHandler(Settings_Changed);
            TryReadMappings();
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

            var simFrameTimer = new DispatcherTimer();
            simFrameTimer.Interval = new TimeSpan(0, 0, 0, 0, 1000/36);
            simFrameTimer.Tick += new EventHandler(Timer_Tick);
            simFrameTimer.Start();
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
            Process.Start("https://github.com/arnaud-clere/MSFS_HandOnMouse#version-13");
        }

        public void Connect_Click(object sender, RoutedEventArgs e)
        {
            if (_simConnect == null)
            {
                try
                {
                    _simConnect = new SimConnect("HandOnMouse", _hwnd, WM_USER_SIMCONNECT, null, 0);
                    _simConnect.Text(SIMCONNECT_TEXT_TYPE.PRINT_BLACK, 2, Definitions.None, "HandOnMouse connected!");
                    ChangeButtonStatus(false, connectButton, true, "DISCONNECT");

                    _simConnect.OnRecvOpen += new SimConnect.RecvOpenEventHandler(SimConnect_OnRecvOpen);
                    _simConnect.OnRecvQuit += new SimConnect.RecvQuitEventHandler(SimConnect_OnRecvQuit);
                    _simConnect.OnRecvEventFilename += new SimConnect.RecvEventFilenameEventHandler(SimConnect_OnRecvEventFilename);
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
        private enum RequestType { AxisValue = 0, SmartAxisValue = 1, AxisInfo = 2, Count = AxisInfo+1 }
        private Definitions RequestId(int i, RequestType requestType = RequestType.AxisValue)
        {
            return Definitions.Axis + i + Axis.Mappings.Count * (int)requestType;
        }
        private Definitions ReadAxisValueId(int i) 
        {
            var m = Axis.Mappings[i];
            var requestType =
                m.TrimCounterCenteringMove && Axis.AxisForTrim.ContainsKey(m.SimVarName) ? RequestType.SmartAxisValue :
                RequestType.AxisValue;
            return RequestId(i, requestType);
        }
        private void SimConnect_OnRecvOpen(SimConnect sender, SIMCONNECT_RECV_OPEN data)
        {
            _connected = true;
            ((ViewModel)DataContext).StatusBrushForText = new SolidColorBrush(Settings.Default.Sensitivity > 0 ? Colors.Black : Colors.Gray);
            bool requestEngines = false;
            bool requestSpeeds = false;
            for (int i = 0; i < Axis.Mappings.Count; i++)
            {
                var m = Axis.Mappings[i];
                if (m.Name.Length > 0)
                {
                    if (m.TrimCounterCenteringMove && Axis.AxisForTrim.ContainsKey(m.SimVarName))
                    {
                        _simConnect.AddToDataDefinition(RequestId(i), m.SimVarName, m.SimVarUnit, SIMCONNECT_DATATYPE.FLOAT64, (float)(m.SimVarScale/Settings.Default.ContinuousSimVarIncrements), SimConnect.SIMCONNECT_UNUSED);
                        _simConnect.RegisterDataDefineStruct<double>(RequestId(i));
                        
                        _simConnect.AddToDataDefinition(ReadAxisValueId(i), m.SimVarName, m.SimVarUnit, SIMCONNECT_DATATYPE.FLOAT64, (float)(m.SimVarScale / Settings.Default.ContinuousSimVarIncrements), SimConnect.SIMCONNECT_UNUSED);
                        _simConnect.AddToDataDefinition(ReadAxisValueId(i), Axis.AxisForTrim[m.SimVarName], "Position", SIMCONNECT_DATATYPE.FLOAT64, (float)((1 - -1) / Settings.Default.ContinuousSimVarIncrements), SimConnect.SIMCONNECT_UNUSED);
                        _simConnect.RegisterDataDefineStruct<SmartTrimAxis>(ReadAxisValueId(i));
                        _simConnect.RequestDataOnSimObject(ReadAxisValueId(i), ReadAxisValueId(i), (uint)SIMCONNECT_SIMOBJECT_TYPE.USER, SIMCONNECT_PERIOD.SIM_FRAME, SIMCONNECT_DATA_REQUEST_FLAG.CHANGED, 0, 0, 0);
                    }
                    else if (m.ForAllEngines)
                    {
                        _simConnect.AddToDataDefinition(RequestId(i), m.SimVarName+":1", m.SimVarUnit, SIMCONNECT_DATATYPE.FLOAT64, (float)(m.SimVarScale / Settings.Default.ContinuousSimVarIncrements), SimConnect.SIMCONNECT_UNUSED);
                        _simConnect.RegisterDataDefineStruct<double>(RequestId(i));
                        _simConnect.RequestDataOnSimObject(RequestId(i), RequestId(i), (uint)SIMCONNECT_SIMOBJECT_TYPE.USER, SIMCONNECT_PERIOD.SIM_FRAME, SIMCONNECT_DATA_REQUEST_FLAG.CHANGED, 0, 0, 0);
                        
                        for (uint engineId = 1; engineId < 5; engineId++)
                            _simConnect.AddToDataDefinition(RequestId(i, RequestType.SmartAxisValue), m.SimVarName+":"+engineId, m.SimVarUnit, SIMCONNECT_DATATYPE.FLOAT64, (float)(m.SimVarScale / Settings.Default.ContinuousSimVarIncrements), SimConnect.SIMCONNECT_UNUSED);
                        _simConnect.RegisterDataDefineStruct<SmartEngineAxis>(RequestId(i, RequestType.SmartAxisValue));
                    }
                    else
                    {
                        _simConnect.AddToDataDefinition(RequestId(i), m.SimVarName, m.SimVarUnit, SIMCONNECT_DATATYPE.FLOAT64, (float)(m.SimVarScale / Settings.Default.ContinuousSimVarIncrements), SimConnect.SIMCONNECT_UNUSED);
                        _simConnect.RegisterDataDefineStruct<double>(RequestId(i));
                        _simConnect.RequestDataOnSimObject(RequestId(i), RequestId(i), (uint)SIMCONNECT_SIMOBJECT_TYPE.USER, SIMCONNECT_PERIOD.SIM_FRAME, SIMCONNECT_DATA_REQUEST_FLAG.CHANGED, 0, 0, 0);
                    }
                    var infoId = RequestId(i, RequestType.AxisInfo);
                    if (m.IsThrottle && !m.DisableThrottleReverse)
                    {
                        _simConnect.AddToDataDefinition(infoId, "THROTTLE LOWER LIMIT", "Percent" /* <0 */, SIMCONNECT_DATATYPE.FLOAT64, 1, SimConnect.SIMCONNECT_UNUSED);
                        _simConnect.RegisterDataDefineStruct<double>(infoId);
                        _simConnect.RequestDataOnSimObject(infoId, infoId, (uint)SIMCONNECT_SIMOBJECT_TYPE.USER, SIMCONNECT_PERIOD.SIM_FRAME, SIMCONNECT_DATA_REQUEST_FLAG.CHANGED, 0, 0, 0);
                    }
                    if (m.SimVarName.StartsWith("FLAPS HANDLE INDEX"))
                    {
                        _simConnect.AddToDataDefinition(infoId, "FLAPS NUM HANDLE POSITIONS", "Number", SIMCONNECT_DATATYPE.FLOAT64, 1, SimConnect.SIMCONNECT_UNUSED);
                        _simConnect.RegisterDataDefineStruct<double>(infoId);
                        _simConnect.RequestDataOnSimObject(infoId, infoId, (uint)SIMCONNECT_SIMOBJECT_TYPE.USER, SIMCONNECT_PERIOD.SIM_FRAME, SIMCONNECT_DATA_REQUEST_FLAG.CHANGED, 0, 0, 0);
                    }
                    foreach (var v in Axis.EngineSimVars) if (v == m.SimVarName) requestEngines = true;
                    if (m.SensitivityAtCruiseSpeed) requestSpeeds = true;
                }
            }
            if (requestSpeeds)
            {
                _simConnect.AddToDataDefinition(Definitions.IndicatedAirSpeedKnots, "AIRSPEED INDICATED", "Knots", SIMCONNECT_DATATYPE.FLOAT64, 5, SimConnect.SIMCONNECT_UNUSED);
                _simConnect.RegisterDataDefineStruct<double>(Definitions.IndicatedAirSpeedKnots);
                _simConnect.RequestDataOnSimObject(Definitions.IndicatedAirSpeedKnots, Definitions.IndicatedAirSpeedKnots, (uint)SIMCONNECT_SIMOBJECT_TYPE.USER, SIMCONNECT_PERIOD.SECOND, SIMCONNECT_DATA_REQUEST_FLAG.CHANGED, 0, 0, 0);
                
                _simConnect.AddToDataDefinition(Definitions.DesignCruiseSpeedFeetPerSec, "DESIGN SPEED VC", "Feet per second", SIMCONNECT_DATATYPE.FLOAT64, 5, SimConnect.SIMCONNECT_UNUSED);
                _simConnect.RegisterDataDefineStruct<double>(Definitions.DesignCruiseSpeedFeetPerSec);
                _simConnect.RequestDataOnSimObject(Definitions.DesignCruiseSpeedFeetPerSec, Definitions.DesignCruiseSpeedFeetPerSec, (uint)SIMCONNECT_SIMOBJECT_TYPE.USER, SIMCONNECT_PERIOD.SIM_FRAME, SIMCONNECT_DATA_REQUEST_FLAG.CHANGED, 0, 0, 0);
            }
            if (requestEngines)
            {
                _simConnect.AddToDataDefinition(Definitions.EnginesCount, "NUMBER OF ENGINES", "Number", SIMCONNECT_DATATYPE.FLOAT64, 1, SimConnect.SIMCONNECT_UNUSED);
                _simConnect.RegisterDataDefineStruct<double>(Definitions.EnginesCount);
                _simConnect.RequestDataOnSimObject(Definitions.EnginesCount, Definitions.EnginesCount, (uint)SIMCONNECT_SIMOBJECT_TYPE.USER, SIMCONNECT_PERIOD.SIM_FRAME, SIMCONNECT_DATA_REQUEST_FLAG.CHANGED, 0, 0, 0);
            }
            _simConnect.SubscribeToSystemEvent(Definitions.AircraftLoaded, "AircraftLoaded");
        }
        private void SimConnect_OnRecvEventFilename(SimConnect sender, SIMCONNECT_RECV_EVENT_FILENAME data)
        {
            if (data.uEventID == (uint)Definitions.AircraftLoaded)
            {
                Axis.MappingsRead(MappingFile());
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
                _simConnect.Text(SIMCONNECT_TEXT_TYPE.PRINT_RED, 2, Definitions.None, "HandOnMouse disconnected!");
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
        public string MappingFile() { return Path.Combine(MappingsDir(), Settings.Default.MappingFile); }
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
                    TryReadMappings(openFileDialog.FileName);
                }
            }
        }
        private void TryReadMappings(string filePath = null)
        {
            if (filePath != null && !Path.IsPathRooted(filePath))
            {
                filePath = Path.Combine(MappingsDir(), filePath);
            }
            if (filePath == null || !File.Exists(filePath))
            {
                filePath = MappingFile();
            }
            var errors = Axis.MappingsRead(filePath);
            if (errors.Length > 0)
            {
                MessageBox.Show(filePath + ":\n" + errors);
            }
            if (Axis.Mappings.Count == 0 && filePath != MappingFile())
            {
                Axis.MappingsRead(MappingFile());
            }
            else
            {
                Settings.Default.MappingFile = filePath.Replace(MappingsDir() + @"\", "");
                Settings.Default.Save();
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
                        _buttons |= buttons & (
                            RAWMOUSE.RI_MOUSE.LEFT_BUTTON_DOWN |
                            RAWMOUSE.RI_MOUSE.MIDDLE_BUTTON_DOWN |
                            RAWMOUSE.RI_MOUSE.RIGHT_BUTTON_DOWN |
                            RAWMOUSE.RI_MOUSE.BUTTON_4_DOWN |
                            RAWMOUSE.RI_MOUSE.BUTTON_5_DOWN);
                        // Check UP after DOWN in case both are true in a single coalesced message
                        if (buttons.HasFlag(RAWMOUSE.RI_MOUSE.LEFT_BUTTON_UP     )) _buttons &= ~RAWMOUSE.RI_MOUSE.LEFT_BUTTON_DOWN  ;
                        if (buttons.HasFlag(RAWMOUSE.RI_MOUSE.MIDDLE_BUTTON_UP   )) _buttons &= ~RAWMOUSE.RI_MOUSE.MIDDLE_BUTTON_DOWN;
                        if (buttons.HasFlag(RAWMOUSE.RI_MOUSE.RIGHT_BUTTON_UP    )) _buttons &= ~RAWMOUSE.RI_MOUSE.RIGHT_BUTTON_DOWN ;
                        if (buttons.HasFlag(RAWMOUSE.RI_MOUSE.BUTTON_4_UP        )) _buttons &= ~RAWMOUSE.RI_MOUSE.BUTTON_4_DOWN     ;
                        if (buttons.HasFlag(RAWMOUSE.RI_MOUSE.BUTTON_5_UP        )) _buttons &= ~RAWMOUSE.RI_MOUSE.BUTTON_5_DOWN     ;
                        // Update complement to enable MouseButtonsFilter with UP requirements
                        if (_buttons.HasFlag(RAWMOUSE.RI_MOUSE.LEFT_BUTTON_DOWN  )) _buttons &= ~RAWMOUSE.RI_MOUSE.LEFT_BUTTON_UP  ; else _buttons |= RAWMOUSE.RI_MOUSE.LEFT_BUTTON_UP  ;
                        if (_buttons.HasFlag(RAWMOUSE.RI_MOUSE.MIDDLE_BUTTON_DOWN)) _buttons &= ~RAWMOUSE.RI_MOUSE.MIDDLE_BUTTON_UP; else _buttons |= RAWMOUSE.RI_MOUSE.MIDDLE_BUTTON_UP;
                        if (_buttons.HasFlag(RAWMOUSE.RI_MOUSE.RIGHT_BUTTON_DOWN )) _buttons &= ~RAWMOUSE.RI_MOUSE.RIGHT_BUTTON_UP ; else _buttons |= RAWMOUSE.RI_MOUSE.RIGHT_BUTTON_UP ;
                        if (_buttons.HasFlag(RAWMOUSE.RI_MOUSE.BUTTON_4_DOWN     )) _buttons &= ~RAWMOUSE.RI_MOUSE.BUTTON_4_UP     ; else _buttons |= RAWMOUSE.RI_MOUSE.BUTTON_4_UP     ;
                        if (_buttons.HasFlag(RAWMOUSE.RI_MOUSE.BUTTON_5_DOWN     )) _buttons &= ~RAWMOUSE.RI_MOUSE.BUTTON_5_UP     ; else _buttons |= RAWMOUSE.RI_MOUSE.BUTTON_5_UP     ;

                        for (int i = 0; i < Axis.Mappings.Count; i++)
                        {
                            var m = Axis.Mappings[i];
                            m.IsActive = _buttons.HasFlag(m.ButtonsFilter);
                            if (m.IsActive)
                            {
                                // Ignore smallest of changes in XY directions to avoid changing 2 Axis at the same time and increase the effect of SmartIncreaseDirection
                                if (Math.Abs(mouse.LastX) < Math.Abs(mouse.LastY))
                                    mouse.LastX = 0;
                                else
                                    mouse.LastY = 0;

                                var d = m.IncreaseDirection;
                                double change =
                                        d == Axis.Direction.Push ? -mouse.LastY :
                                        d == Axis.Direction.Draw ?  mouse.LastY :
                                        d == Axis.Direction.Right ? mouse.LastX : -mouse.LastX;
                                var d2 = m.IncreaseDirection2;
                                if (change == 0 && d2 != null)
                                    change =
                                        d2 == Axis.Direction.Push ? -mouse.LastY :
                                        d2 == Axis.Direction.Draw ?  mouse.LastY :
                                        d2 == Axis.Direction.Right ? mouse.LastX : -mouse.LastX;

                                var inDetent = m.IsThrottle && Math.Abs(m.Value) < m.SimVarScale * Settings.Default.ReverseDetentWidthInPercent / 100;
                                var inReverse = m.IsThrottle && m.Value < 0;
                                var reverse = d == Axis.Direction.Push || d == Axis.Direction.Draw ? Axis.Direction.Right : Axis.Direction.Draw;
                                if (inDetent && mouse.LastX != 0 && reverse == Axis.Direction.Right)
                                    change = -mouse.LastX;
                                else if (inDetent && mouse.LastY != 0 && reverse == Axis.Direction.Draw)
                                    change = -mouse.LastY;
                                else if (inReverse && change <= 0 && reverse == Axis.Direction.Right)
                                    change = -mouse.LastX;
                                else if (inReverse && change <= 0 && reverse == Axis.Direction.Draw)
                                    change = -mouse.LastY;

                                m.UpdateRawInputChanges(Properties.Settings.Default.Sensitivity, change);
                            }
                            if (m.WaitButtonsReleased)
                            {
                                if (!m.IsActive) // CurrentChange end
                                {
                                    m.CurrentChange = 0;
                                    // Keeping the remainder for the current change would mysteriously:
                                    // - increase a subsequent move in the opposite direction after even a long time
                                    // - decrease a subsequent move in the same direction to potentially insignificant moves
                                    if (m.SimVarChange != 0)
                                    {
                                        if (_connected && m.VJoyId == 0)
                                            _simConnect?.RequestDataOnSimObject(ReadAxisValueId(i), ReadAxisValueId(i), (uint)SIMCONNECT_SIMOBJECT_TYPE.USER, SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
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
                                    if (_connected && m.VJoyId == 0)
                                        _simConnect?.RequestDataOnSimObject(ReadAxisValueId(i), ReadAxisValueId(i), (uint)SIMCONNECT_SIMOBJECT_TYPE.USER, SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
                                    else
                                        m.UpdateSimVar(m.SimVarValue);
                                }
                            }
                            m.ChangeColorForText = m.SimVarChange != 0 && m.CurrentChange == 0 ? Colors.Red : Axis.TextColorFromChange(m.CurrentChange);
                        }
                    }
                    else { Debug.WriteLine("Received unsupported RAWINPUT"); }
                }
                else { Debug.WriteLine("Received unsupported WM.INPUT"); }
            }
            return hwnd;
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            for (int i = 0; i < Axis.Mappings.Count; i++)
            {
                var m = Axis.Mappings[i];
                m.UpdateTimerChanges(((DispatcherTimer)sender).Interval.TotalSeconds);
                if (m.SimVarChange != 0)
                {
                    if (_connected && m.VJoyId == 0)
                        _simConnect?.RequestDataOnSimObject(ReadAxisValueId(i), ReadAxisValueId(i), (uint)SIMCONNECT_SIMOBJECT_TYPE.USER, SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
                    else
                        m.UpdateSimVar(m.SimVarValue);
                }
            }
        }

        public void SimConnect_OnRecvData(SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA data)
        {
            // if (data.dwObjectID != 1) return;
            // if (data.dwentrynumber != 1 || data.dwoutof != 1 || data.dwDefineCount != 1) return;
            int i = (int)data.dwRequestID;
            if (data.dwDefineID != i) return;

            try
            {
                if (i == (int)Definitions.IndicatedAirSpeedKnots)
                {
                    Axis.IndicatedAirSpeedKnots = Math.Max(0, (double)data.dwData[0]);
                }
                else if (i == (int)Definitions.DesignCruiseSpeedFeetPerSec)
                {
                    Axis.DesignCruiseSpeedKnots = Math.Max(50, ((double)data.dwData[0]) * /* ft/s to knots */ 0.592484);
                }
                else if (i == (int)Definitions.EnginesCount)
                {
                    Axis.EnginesCount = (uint)Math.Max(0, Math.Min(4, (double)data.dwData[0]));
                }
                else if ((int)Definitions.Axis <= i)
                {
                    var translatedId = i - (int)Definitions.Axis;
                    var axisId = translatedId % Axis.Mappings.Count;
                    var requestType = (RequestType)(translatedId / Axis.Mappings.Count);
                    if (requestType <= RequestType.SmartAxisValue)
                    {
                        var m = Axis.Mappings[axisId];
                        double inSimValue;
                        double trimmedAxisChange = 0;
                        if (requestType == RequestType.SmartAxisValue)
                        {
                            var inSim = (SmartTrimAxis)data.dwData[0];
                            inSimValue = inSim.Trim;
                            if (m.IsActive && !double.IsNaN(m.TrimmedAxis))
                            {
                                trimmedAxisChange = m.SimVarScale * (m.TrimmedAxis - inSim.TrimmedAxis) / (1 - -1) /* position scale */;
                            }
                            m.TrimmedAxis = inSim.TrimmedAxis;
                        }
                        else
                        {
                            inSimValue = (double)data.dwData[0];
                        }
                        if (m.UpdateSimVar(inSimValue, trimmedAxisChange))
                            if (m.ForAllEngines)
                            {
                                SmartEngineAxis newValue;
                                newValue.Engine1 = newValue.Engine2 = newValue.Engine3 = newValue.Engine4 = m.SimVarValue;
                                _simConnect?.SetDataOnSimObject(RequestId(axisId, RequestType.SmartAxisValue), (uint)SIMCONNECT_SIMOBJECT_TYPE.USER, SIMCONNECT_DATA_SET_FLAG.DEFAULT, newValue);
                            }
                            else
                            {
                                _simConnect?.SetDataOnSimObject(RequestId(axisId), (uint)SIMCONNECT_SIMOBJECT_TYPE.USER, SIMCONNECT_DATA_SET_FLAG.DEFAULT, m.SimVarValue);
                            }
                    }
                    else if (requestType == RequestType.AxisInfo)
                    {
                        Axis.MappingsUpdate(axisId, (double)data.dwData[0]);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }
    }
}
