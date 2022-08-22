﻿using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

using winuser;

using vJoyInterfaceWrap;

using Microsoft.FlightSimulator.SimConnect;
using Microsoft.Win32;

using HandOnMouse.Properties;
using winbase;

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
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct SimString
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=256)]
        public string value;
    }

    public class ViewModel : INotifyPropertyChanged
    {
        public string Status { get => _status; set { if (_status != value) { _status = value; NotifyPropertyChanged(); } } }
        public bool GaugeVisible => !Settings.Default.GaugeHidden;
        public double GaugeWidth => Settings.Default.GaugeFontSize * 11; // works with "HandOnMouse Gauge" title which is approximately the longest text
        public double GaugeOpacity { get => _gaugeOpacity; set { if (_gaugeOpacity != value) { _gaugeOpacity = value; NotifyPropertyChanged(); } } }
        public List<string> MappingFiles { get { var fs = new List<string>(); foreach(var f in new DirectoryInfo(MainWindow.MappingsDir()).GetFiles("*.cfg")) { fs.Add(f.Name.Remove(f.Name.Length-f.Extension.Length)); } return fs; } }
        public ObservableCollection<Axis> Mappings => Axis.Mappings;
        public Brush StatusBrushForText { get => _statusBrushForText; set { if (_statusBrushForText != value) { _statusBrushForText = value; NotifyPropertyChanged(); } } }

        public event PropertyChangedEventHandler PropertyChanged;

        public void NotifyPropertyChanged([CallerMemberName] string name = "") => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        
        // Implementation

        private string _status = "HandOnMouse Gauge";
        private double _gaugeOpacity = 0;
        private Brush _statusBrushForText = new SolidColorBrush(Colors.Gray);
    }

    /// <summary>Interaction logic for MainWindow.xaml</summary>
    public partial class MainWindow : Window
    {
        public static string MappingsDir() { return Path.Combine(Directory.GetCurrentDirectory(), "Mappings"); }
        public static string MappingFile() { return Path.ChangeExtension(Path.Combine(MappingsDir(), Settings.Default.MappingFile), ".cfg"); }
        public static string SimAircraftTitle { get; private set; } = "";
        public static Controller.Buttons SimJoystickButtons { get; private set; }
        public static bool vJoyIsAvailable { get; private set; }

        const int WM_USER_SIMCONNECT = (int)WM.USER + 2;

        /// <summary>SimConnect Requests and Data Definitions are actually defined dynamically for each Axis in Axis.Mappings</summary>
        public enum Definitions
        {
            None = 0,
            JoystickButtonPressed = 1,
            AircraftLoaded = 2,
            IndicatedAirSpeedKnots = 3,
            DesignCruiseSpeedFeetPerSec = 4,
            EnginesCount = 5,
            BrakesAvailable = 6,
            SpoilerAvailable = 7,
            FlapsAvailable = 8,
            IsGearRetractable = 9,
            ThrottleLowerLimit = 10,
            FlapsNumHandlePosition = 11,
            ElevatorTrimDisabled = 12,
            AileronTrimDisabled = 13,
            RudderTrimDisabled = 14,
            ElevatorTrimMinDegrees = 15,
            ElevatorTrimMaxDegrees = 16,
            AircraftTitle = 17,
            Axis = 18
        }

        public MainWindow()
        {
            Trace.WriteLine($"MainWindow {DateTime.Now}");

            _gaugeWindow.DataContext = DataContext = new ViewModel();
            _gaugeWindow.Width = ((ViewModel)DataContext).GaugeWidth;
            if (Settings.Default.GaugeHidden)
                _gaugeWindow.Hide();
            else
                _gaugeWindow.Show();

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

            Controller.UpdateDevices();

            var rid = new RAWINPUTDEVICE[1];
            rid[0].UsagePage = HID_USAGE_PAGE.GENERIC;
            rid[0].Usage = HID_USAGE.MOUSE;
            rid[0].Flags = RAWINPUTDEVICE.RIDEV.INPUTSINK;
            rid[0].Target = _hwnd;
            if (!User32.RegisterRawInputDevices(rid, (uint)rid.Length, (uint)Marshal.SizeOf(rid[0])))
            {
                Trace.WriteLine($"User32.RegisterRawInputDevices failed Marshal.GetLastWin32Error: {Marshal.GetLastWin32Error()}");
            }
            Mouse.Device.RawMouseMove += new Mouse.RawMouseMoveHandler(Mouse_Move);

            var simConnectTimer = new DispatcherTimer();
            simConnectTimer.Interval = new TimeSpan(0, 0, 0, 0, 1000 / 36);
            simConnectTimer.Tick += new EventHandler(SimConnectTimer_Tick);
            simConnectTimer.Start();

            var simFrameTimer = new DispatcherTimer();
            simFrameTimer.Interval = new TimeSpan(0, 0, 0, 0, 1000 / 36);
            simFrameTimer.Tick += new EventHandler(SimFrameTimer_Tick);
            simFrameTimer.Start();
        }
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            try
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
            }
            catch (Exception ex) { Trace.WriteLine($"{ex.Message} at: {ex.StackTrace}"); }
            return hwnd;
        }

        private void Settings_Changed(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.Default.Sensitivity))
            {
                ((ViewModel)DataContext).StatusBrushForText = new SolidColorBrush(_connected && Settings.Default.Sensitivity > 0 ? Colors.Black : Colors.Gray);
            }
            if (e.PropertyName == nameof(Settings.Default.GaugeFontSize))
            {
                // FIXME Binding ((ViewModel)DataContext).NotifyPropertyChanged(nameof(ViewModel.GaugeWidth));
                _gaugeWindow.Width = ((ViewModel)DataContext).GaugeWidth;
            }
            if (e.PropertyName == nameof(Settings.Default.GaugeHidden))
            {
                // FIXME Binding ((ViewModel)DataContext).NotifyPropertyChanged(nameof(ViewModel.GaugeVisible));
                if (Settings.Default.GaugeHidden)
                    _gaugeWindow.Hide();
                else
                    _gaugeWindow.Show();
            }
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.System && e.SystemKey == Key.F4)
            {
                e.Handled = true;
            }
        }
        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (_connected)
            {
                MessageBox.Show("Please DISCONNECT before closing!", "HandOnMouse");
                e.Cancel = true;
            }
            else
            {
                _gaugeWindow.Close();
                Settings.Default.Save();
                Trace.WriteLine($"MainWindow Close {DateTime.Now}");
            }
        }
        private void Window_Minimize(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void GaugePanel_MouseEnter(object sender, MouseEventArgs e)
        {
            ((ViewModel)DataContext).GaugeOpacity = 0.5;
        }

        private void GaugePanel_MouseLeave(object sender, MouseEventArgs e)
        {
            ((ViewModel)DataContext).GaugeOpacity = 0;
        }

        public void Axis_Click(object sender, RoutedEventArgs e)
        {
            var b = (Button)sender;
            var w = new AxisWindow((Axis)((Button)sender).Tag, SimAircraftTitle);
            var midBottom = b.PointToScreen(new Point((b.Width - w.MinWidth) / 2, b.Height + 3));

            var m = PresentationSource.FromVisual(this).CompositionTarget.TransformToDevice;
            var windowsToDeviceX = m.M11;
            var windowsToDeviceY = m.M22;

            w.Owner = this;
            w.Top = midBottom.Y / windowsToDeviceY;
            w.Left = midBottom.X / windowsToDeviceX;
            w.ShowDialog();
        }

        public void Connect_Click(object sender, RoutedEventArgs e)
        {
            if (TryConnect())
            {
                _manuallyDisconnected = false;
            }
            else
            {
                if (TryDisconnect())
                {
                    _manuallyDisconnected = true;
                }
            }
        }

        private bool TryConnect()
        {
            if (_simConnect != null)
                return false;

            try
            {
                _simConnect = new SimConnect("HandOnMouse", _hwnd, WM_USER_SIMCONNECT, null, 0);

                _simConnect.OnRecvOpen += new SimConnect.RecvOpenEventHandler(SimConnect_OnRecvOpen);
                _simConnect.OnRecvQuit += new SimConnect.RecvQuitEventHandler(SimConnect_OnRecvQuit);
                _simConnect.OnRecvEvent += new SimConnect.RecvEventEventHandler(SimConnect_OnRecvEvent);
                _simConnect.OnRecvEventFilename += new SimConnect.RecvEventFilenameEventHandler(SimConnect_OnRecvEventFilename);
                _simConnect.OnRecvException += new SimConnect.RecvExceptionEventHandler(SimConnect_OnRecvException);
                _simConnect.OnRecvSimobjectData += new SimConnect.RecvSimobjectDataEventHandler(SimConnect_OnRecvData);

                ChangeButtonStatus(false, connectButton, true, "DISCONNECT");
                ((ViewModel)DataContext).StatusBrushForText = new SolidColorBrush(_connected && Settings.Default.Sensitivity > 0 ? Colors.Black : Colors.Gray);
                ((ViewModel)DataContext).Status = "Connected";
                EraseStatusAfter(TimeSpan.FromSeconds(3));
            }
            catch (Exception ex) { Trace.WriteLine($"{ex.Message} at: {ex.StackTrace}"); }

            return _simConnect != null;
        }
        public bool TryDisconnect()
        {
            if (_simConnect == null)
                return false;

            try
            {
                _simConnect.Dispose();
                _simConnect = null;
                _connected = false;

                ChangeButtonStatus(true, connectButton, true, "CONNECT");
                ((ViewModel)DataContext).StatusBrushForText = new SolidColorBrush(Colors.DarkOrange);
                ((ViewModel)DataContext).Status = "Disconnected";
            }
            catch (Exception ex) { Trace.WriteLine($"{ex.Message} at: {ex.StackTrace}"); }

            return _simConnect == null;
        }

        private void EraseStatusAfter(TimeSpan grace)
        {
            var timer = new DispatcherTimer { Interval = grace };
            timer.Tick += (s, args) =>
            {
                ((ViewModel)DataContext).StatusBrushForText = new SolidColorBrush(_connected && Settings.Default.Sensitivity > 0 ? Colors.Black : Colors.Gray);
                ((ViewModel)DataContext).Status = "";
                timer.Stop();
            };
            timer.Start();
        }
        public void ChangeButtonStatus(bool active, Button b, bool enabled, string text)
        {
            b.BorderBrush = new SolidColorBrush(active ? Colors.DarkGreen : Colors.DarkRed);
            b.Foreground = new SolidColorBrush(active ? Colors.DarkGreen : Colors.DarkRed);

            if (!string.IsNullOrEmpty(text))
                b.Content = text;

            b.IsEnabled = enabled;
        }

        private enum RequestType { AxisValue = 0, SmartAxisValue = 1, Count = SmartAxisValue + 1 }
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
            var appVersion = new Version(
                (int)data.dwApplicationVersionMajor,
                (int)data.dwApplicationVersionMinor,
                (int)data.dwApplicationBuildMajor,
                (int)data.dwApplicationBuildMinor);
            var simConnectVersion = new Version(
                (int)data.dwSimConnectVersionMajor,
                (int)data.dwSimConnectVersionMinor,
                (int)data.dwSimConnectBuildMajor,
                (int)data.dwSimConnectBuildMinor);
            Trace.WriteLine($"Connected to: {data.szApplicationName} appVersion: {appVersion} simConnectVersion:{simConnectVersion}");

            ((ViewModel)DataContext).StatusBrushForText = new SolidColorBrush(_connected && Settings.Default.Sensitivity > 0 ? Colors.Black : Colors.Gray);

            _requestFlaps = false;
            _requestGear = false;
            _requestSpoiler = false;
            _requestBrakes = false;
            _requestEngines = false;
            _requestSpeeds = false;
            _requestJoystickButtonInputEvents.Clear();
            for (int id = 0; id < Axis.Mappings.Count; id++)
            {
                var m = Axis.Mappings[id];
                if (m.SimVarName.Length > 0)
                {
                    if (m.TrimCounterCenteringMove && Axis.AxisForTrim.ContainsKey(m.SimVarName))
                    {
                        RegisterData(RequestId(id), m.SimVarName, m.ValueUnit, (float)m.ValueIncrement);

                        _simConnect.AddToDataDefinition(ReadAxisValueId(id), m.SimVarName, m.ValueUnit, SIMCONNECT_DATATYPE.FLOAT64, (float)m.ValueIncrement, SimConnect.SIMCONNECT_UNUSED);
                        _simConnect.AddToDataDefinition(ReadAxisValueId(id), Axis.AxisForTrim[m.SimVarName], "Position", SIMCONNECT_DATATYPE.FLOAT64, (float)((1 - -1) / Math.Max(1, Settings.Default.ContinuousValueIncrements)), SimConnect.SIMCONNECT_UNUSED);
                        _simConnect.RegisterDataDefineStruct<SmartTrimAxis>(ReadAxisValueId(id));
                        RequestData(ReadAxisValueId(id), SIMCONNECT_PERIOD.SIM_FRAME);
                    }
                    else if (m.ForAllEngines)
                    {
                        RegisterData(RequestId(id), m.SimVarName + ":1", m.ValueUnit, (float)m.ValueIncrement);
                        RequestData(RequestId(id), SIMCONNECT_PERIOD.SIM_FRAME);

                        for (uint engineId = 1; engineId < 5; engineId++)
                            _simConnect.AddToDataDefinition(RequestId(id, RequestType.SmartAxisValue), m.SimVarName + ":" + engineId, m.ValueUnit, SIMCONNECT_DATATYPE.FLOAT64, (float)m.ValueIncrement, SimConnect.SIMCONNECT_UNUSED);
                        _simConnect.RegisterDataDefineStruct<SmartEngineAxis>(RequestId(id, RequestType.SmartAxisValue));
                    }
                    else
                    {
                        RegisterData(RequestId(id), m.SimVarName, m.ValueUnit, (float)m.ValueIncrement);
                        RequestData(RequestId(id), SIMCONNECT_PERIOD.SIM_FRAME);
                    }
                    if (m.IsThrottle && !m.DisableThrottleReverse)          _requestReverse = true;
                    if (m.SimVarName == "FLAPS HANDLE INDEX")               _requestFlaps = true;
                    if (m.SimVarName == "GEAR HANDLE POSITION")             _requestGear = true;
                    if (m.SimVarName == "SPOILERS HANDLE POSITION")         _requestSpoiler = true;
                    if (m.SimVarName.StartsWith("BRAKE LEFT POSITION") || 
                        m.SimVarName.StartsWith("BRAKE RIGHT POSITION"))    _requestBrakes = true;
                    if (m.SimVarName.StartsWith("ELEVATOR TRIM"))           _requestElevatorTrim = true;
                    if (m.SimVarName.StartsWith("AILERON TRIM"))            _requestAileronTrim = true;
                    if (m.SimVarName.StartsWith("RUDDER TRIM"))             _requestRudderTrim = true;
                    foreach (var v in Axis.EngineSimVars) 
                        if (m.SimVarName.StartsWith(v))                     _requestEngines = true;
                    if (m.SensitivityAtCruiseSpeed)                         _requestSpeeds = true;
                }
                if (m.SimJoystickButtonFilter > 0)
                {
                    _requestJoystickButtonInputEvents.Add(m.SimJoystickButtonFilter-1);
                }
            }
            if (_requestReverse)
            {
                RegisterData(Definitions.ThrottleLowerLimit, "THROTTLE LOWER LIMIT", "Percent" /* <0 */);
                RequestData(Definitions.ThrottleLowerLimit);
            }
            if (_requestSpeeds)
            {
                RegisterData(Definitions.IndicatedAirSpeedKnots, "AIRSPEED INDICATED", "Knots", 5);
                RequestData(Definitions.IndicatedAirSpeedKnots, SIMCONNECT_PERIOD.SECOND);

                RegisterData(Definitions.DesignCruiseSpeedFeetPerSec, "DESIGN SPEED VC", "Feet per second", 5);
                RequestData(Definitions.DesignCruiseSpeedFeetPerSec);
            }
            if (_requestEngines)
            {
                RegisterData(Definitions.EnginesCount, "NUMBER OF ENGINES", "Number");
                RequestData(Definitions.EnginesCount);
            }
            if (_requestBrakes)
            {
                RegisterData(Definitions.BrakesAvailable, "TOE BRAKES AVAILABLE", "Bool");
                RequestData(Definitions.BrakesAvailable);
            }
            if (_requestSpoiler)
            {
                RegisterData(Definitions.SpoilerAvailable, "SPOILER AVAILABLE", "Bool");
                RequestData(Definitions.SpoilerAvailable);
            }
            if (_requestGear)
            {
                RegisterData(Definitions.IsGearRetractable, "IS GEAR RETRACTABLE", "Bool");
                RequestData(Definitions.IsGearRetractable);
            }
            if (_requestFlaps)
            {
                RegisterData(Definitions.FlapsAvailable, "FLAPS AVAILABLE", "Bool");
                RequestData(Definitions.FlapsAvailable);

                RegisterData(Definitions.FlapsNumHandlePosition, "FLAPS NUM HANDLE POSITIONS", "Number");
                RequestData(Definitions.FlapsNumHandlePosition);
            }
            if (_requestElevatorTrim)
            {
                RegisterData(Definitions.ElevatorTrimMinDegrees, "ELEVATOR TRIM DOWN LIMIT", "Degrees");
                RegisterData(Definitions.ElevatorTrimMaxDegrees, "ELEVATOR TRIM UP LIMIT", "Degrees");
                RegisterData(Definitions.ElevatorTrimDisabled, "ELEVATOR TRIM DISABLED", "Bool");
                RequestData(Definitions.ElevatorTrimMinDegrees);
                RequestData(Definitions.ElevatorTrimMaxDegrees);
                RequestData(Definitions.ElevatorTrimDisabled);
            }
            if (_requestAileronTrim)
            {
                RegisterData(Definitions.AileronTrimDisabled, "AILERON TRIM DISABLED", "Bool");
                RequestData(Definitions.AileronTrimDisabled);
            }
            if (_requestRudderTrim)
            {
                RegisterData(Definitions.RudderTrimDisabled, "RUDDER TRIM DISABLED", "Bool");
                RequestData(Definitions.RudderTrimDisabled);
            }
            if (_requestJoystickButtonInputEvents.Count > 0)
            {
                _simConnect.MapClientEventToSimEvent(Definitions.None, "");
                _simConnect.MapClientEventToSimEvent(Definitions.JoystickButtonPressed, "");
                _simConnect.AddClientEventToNotificationGroup(Definitions.None, Definitions.None, true);
                _simConnect.AddClientEventToNotificationGroup(Definitions.None, Definitions.JoystickButtonPressed, true);
                _simConnect.SetNotificationGroupPriority(Definitions.None, 1000000);
            }
            foreach (var button in _requestJoystickButtonInputEvents)
            {
                for (uint i = 0; i < 10; i++) // joystick id can be > 0, so subscribe to any joystick id
                {
                    _simConnect.MapInputEventToClientEvent(Definitions.None, $"joystick:{i}:button:{button}", Definitions.JoystickButtonPressed, button, Definitions.None, button, false);
                }
            }
            _simConnect.SubscribeToSystemEvent(Definitions.AircraftLoaded, "AircraftLoaded");
            _simConnect.AddToDataDefinition(Definitions.AircraftTitle, "TITLE", null, SIMCONNECT_DATATYPE.STRING256, 0, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.RegisterDataDefineStruct<SimString>(Definitions.AircraftTitle);
            RequestData(Definitions.AircraftTitle);
        }
        private void RegisterData(Definitions id, string simVarName, string simVarType, float epsilon = 1)
        {
            Debug.Assert(_simConnect != null);
            _simConnect?.AddToDataDefinition(id, simVarName, simVarType, SIMCONNECT_DATATYPE.FLOAT64, epsilon, SimConnect.SIMCONNECT_UNUSED);
            _simConnect?.RegisterDataDefineStruct<double>(id);
        }
        private void RequestData(Definitions id, SIMCONNECT_PERIOD period = SIMCONNECT_PERIOD.ONCE)
        {
            Debug.Assert(_simConnect != null);
            _simConnect?.RequestDataOnSimObject(id, id, (uint)SIMCONNECT_SIMOBJECT_TYPE.USER, period, period > SIMCONNECT_PERIOD.ONCE ? SIMCONNECT_DATA_REQUEST_FLAG.CHANGED : SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
        }
        private void SimConnect_OnRecvEvent(SimConnect sender, SIMCONNECT_RECV_EVENT data)
        {
            Trace.WriteLine($"Received event: {data.uEventID} group: {data.uGroupID} data: {data.dwData}");

            if (data.uEventID == (uint)Definitions.JoystickButtonPressed)
            {
                SimJoystickButtons |= (Controller.Buttons)(1u << (int)data.dwData);
            }
            else
            {
                SimJoystickButtons &= ~(Controller.Buttons)(1u << (int)data.dwData);
            }
        }
        private void SimConnect_OnRecvEventFilename(SimConnect sender, SIMCONNECT_RECV_EVENT_FILENAME data)
        {
            Trace.WriteLine($"Received event: {data.uEventID} szFileName: {data.szFileName}");

            if (data.uEventID == (uint)Definitions.AircraftLoaded)
            {
                TryDisconnect();
                TryConnect();
            }
        }
        private void SimConnect_OnRecvQuit(SimConnect sender, SIMCONNECT_RECV data)
        {
            Trace.WriteLine("Received quit");

            TryDisconnect();
        }
        private void SimConnect_OnRecvException(SimConnect sender, SIMCONNECT_RECV_EXCEPTION data)
        {
            SIMCONNECT_EXCEPTION e = (SIMCONNECT_EXCEPTION)data.dwException;
            Trace.WriteLine($"SIMCONNECT_RECV_EXCEPTION: {e} dwSendID: {data.dwSendID} dwIndex: {data.dwIndex}");
            if (e != SIMCONNECT_EXCEPTION.UNRECOGNIZED_ID)
            {
                ((ViewModel)DataContext).StatusBrushForText = new SolidColorBrush(Colors.Red);
                ((ViewModel)DataContext).Status = e.ToString();
                EraseStatusAfter(TimeSpan.FromSeconds(9));
            }
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_connected && !TryDisconnect())
            {
                MessageBox.Show("Please DISCONNECT before changing mappings!", "HandOnMouse");
                e.Handled = false;
            }
            else if (e.AddedItems.Count > 0)
            {
                TryReadMappings(e.AddedItems[0].ToString());
            }
        }
        public void Button_MappingFile(object sender, RoutedEventArgs e)
        {
            if (_connected && !TryDisconnect())
            {
                MessageBox.Show("Please DISCONNECT before changing mappings!", "HandOnMouse");
            }
            else
            {
                var openFileDialog = new OpenFileDialog
                {
                    InitialDirectory = MappingsDir(),
                    Filter = "HandOnMouse mappings file (*.cfg)|*.cfg",
                    FilterIndex = 2
                };
                if (openFileDialog.ShowDialog() == true)
                {
                    TryReadMappings(openFileDialog.FileName);
                }
            }
        }
        private bool TryReadMappings(string filePath = null)
        {
            filePath = Path.ChangeExtension(filePath, ".cfg");
            if (filePath != null && !Path.IsPathRooted(filePath))
            {
                filePath = Path.Combine(MappingsDir(), filePath);
            }
            if (filePath == null || !File.Exists(filePath))
            {
                filePath = MappingFile();
            }
            var errors = Axis.MappingsRead(filePath, SimAircraftTitle);
            var revert = false;
            if (Axis.Mappings.Count == 0 && filePath != MappingFile()) // revert to previous file
            {
                Axis.MappingsRead(MappingFile(), SimAircraftTitle);
                revert = true;
            }
            else
            {
                Settings.Default.MappingFile = Path.ChangeExtension(filePath.Replace(MappingsDir() + @"\", ""), null);
                foreach (var m in Axis.Mappings)
                {
                    if (m.VJoyId > 0)
                    {
                        if (_vJoy == null)
                        {
                            _vJoy = new vJoy();
                            if (_vJoy.vJoyEnabled())
                            {
                                UInt32 dllVersion = 0, driverVersion = 0;
                                if (!(_vJoy.DriverMatch(ref dllVersion, ref driverVersion) || (dllVersion == 536 && driverVersion == 537)))
                                {
                                    errors += m.SimVarName + ": " + $"vJoy DLL version {dllVersion} does not support installed vJoy driver version {driverVersion}\r\n";
                                    _vJoy = null;
                                }
                            }
                            else
                            {
                                errors += m.SimVarName + ": " + $"vJoy driver not installed or not enabled\r\n";
                                _vJoy = null;
                            }
                        }
                        vJoyIsAvailable = _vJoy != null;
                        if (_vJoy?.AcquireVJD(m.VJoyId) != true)
                        {
                            errors += m.SimVarName + ": " + $"vJoy axis {m.VJoyId} not acquired\r\n";
                            m.IsAvailable = false;
                        }
                        var status = _vJoy?.GetVJDStatus(m.VJoyId);
                        if (status != VjdStat.VJD_STAT_OWN)
                        {
                            errors += m.SimVarName + ": " + $"vJoy device {m.VJoyId} not available: {status}\r\n";
                            m.IsAvailable = false;
                        }
                        else if (_vJoy?.GetVJDAxisExist(m.VJoyId, m.VJoyAxis) != true)
                        {
                            errors += m.SimVarName + ": " + $"vJoy device {m.VJoyId} axis {m.VJoyAxis} not found\r\n";
                            m.IsAvailable = false;
                        }
                    }
                }
            }
            foreach (var m in Axis.Mappings)
            {
                m.PropertyChanged += new PropertyChangedEventHandler(Axis_SimVarValueChanged);
            }
            if (errors.Length > 0)
            {
                var message = filePath + ":\r\n" + errors;
                Trace.WriteLine(message);
                MessageBox.Show(message, "HandOnMouse");
            }
            return revert;
        }

        private IntPtr RawInput_Handler(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == (int)WM.INPUT)
            {
                RAWINPUT input;
                int inputSize = Marshal.SizeOf(typeof(RAWINPUT));
                int headerSize = Marshal.SizeOf(typeof(RAWINPUTHEADER));
                int outSize = User32.GetRawInputData(lParam, RID.INPUT, out input, ref inputSize, headerSize);
                if (outSize == inputSize)
                {
                    if (input.header.Type != RAWINPUTHEADER.RIM.TYPEMOUSE || !Mouse.Device.Update(input.mouse)) { Trace.WriteLine($"Received unsupported RAWINPUT header.Type: {input.header.Type}, mouse.Flags: {input.mouse.Flags}"); }
                }
                else { Trace.WriteLine($"Received unsupported WM.INPUT outSize: {outSize}"); }
            }
            return hwnd;
        }

        private void Mouse_Move(Vector move)
        {
            foreach (var m in Axis.Mappings)
            {
                var errors = m.UpdateMove(move);
                if (errors != null && !displayedErrors.Contains(errors))
                {
                    displayedErrors.Add(errors);
                    Trace.WriteLine(errors);
                    MessageBox.Show(errors, "HandOnMouse");
                }
            }
        }

        private void SimConnectTimer_Tick(object sender, EventArgs e)
        {
            if (_wasAutoConnect && !Settings.Default.AutoConnect)
            {
                _wasAutoConnect = false;
            }
            if (!_wasAutoConnect && Settings.Default.AutoConnect)
            {
                _wasAutoConnect = true;
                _manuallyDisconnected = false;
            }
            if (!_manuallyDisconnected && Settings.Default.AutoConnect)
            {
                TryConnect();
            }
        }
        private void SimFrameTimer_Tick(object sender, EventArgs e)
        {
            Mouse_Move(new Vector(0, 0)); // to at least detect a trigger change without mouse move
            foreach (var m in Axis.Mappings)
            {
                m.UpdateTime(((DispatcherTimer)sender).Interval.TotalSeconds);
            }
        }

        private void Axis_SimVarValueChanged(object sender, PropertyChangedEventArgs p)
        {
            var m = (Axis)sender;
            if (m.IsAvailable && p.PropertyName == "SimVarValue")
            {
                if (m.VJoyId > 0)
                {
                    _vJoy?.SetAxis((int)m.SimVarValue, m.VJoyId, m.VJoyAxis);
                }
                else if (m.ForAllEngines)
                {
                    SmartEngineAxis newValue;
                    newValue.Engine1 = newValue.Engine2 = newValue.Engine3 = newValue.Engine4 = m.SimVarValue;
                    _simConnect?.SetDataOnSimObject(RequestId(m.Id, RequestType.SmartAxisValue), (uint)SIMCONNECT_SIMOBJECT_TYPE.USER, SIMCONNECT_DATA_SET_FLAG.DEFAULT, newValue);
                }
                else
                {
                    _simConnect?.SetDataOnSimObject(RequestId(m.Id), (uint)SIMCONNECT_SIMOBJECT_TYPE.USER, SIMCONNECT_DATA_SET_FLAG.DEFAULT, m.SimVarValue);
                }
            }
        }

        private void SimConnect_OnRecvData(SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA data)
        {
            // if (data.dwObjectID != 1) return;
            // if (data.dwentrynumber != 1 || data.dwoutof != 1 || data.dwDefineCount != 1) return;
            int i = (int)data.dwRequestID;
            if (data.dwDefineID != i) return;

            try
            {
                if (i == (int)Definitions.AircraftTitle)
                {
                    MainWindow.SimAircraftTitle = ((SimString)data.dwData[0]).value;
                }
                else if (i == (int)Definitions.IndicatedAirSpeedKnots)
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
                    foreach (var m in Axis.Mappings)
                    {
                        var n = m.SimVarName.Split(':');
                        uint engine = 0;
                        if (n.Length > 1 && uint.TryParse(n[1], out engine))
                        {
                            m.IsAvailable = engine <= Axis.EnginesCount;
                        }
                    }
                }
                else if (i == (int)Definitions.BrakesAvailable && (double)data.dwData[0] == 0)
                {
                    foreach (var m in Axis.Mappings)
                        if (m.SimVarName.StartsWith("BRAKE LEFT POSITION") ||
                            m.SimVarName.StartsWith("BRAKE RIGHT POSITION"))
                            m.IsAvailable = false;
                }
                else if (i == (int)Definitions.SpoilerAvailable && (double)data.dwData[0] == 0)
                {
                    foreach (var m in Axis.Mappings)
                        if (m.SimVarName == "SPOILERS HANDLE POSITION")
                            m.IsAvailable = false;
                }
                else if (i == (int)Definitions.FlapsAvailable && (double)data.dwData[0] == 0)
                {
                    foreach (var m in Axis.Mappings)
                        if (m.SimVarName.StartsWith("FLAPS HANDLE INDEX"))
                            m.IsAvailable = false;
                }
                else if (i == (int)Definitions.IsGearRetractable && (double)data.dwData[0] == 0)
                {
                    foreach (var m in Axis.Mappings)
                        if (m.SimVarName == "GEAR HANDLE POSITION")
                            m.IsAvailable = false;
                }
                else if (i == (int)Definitions.ElevatorTrimDisabled && (double)data.dwData[0] != 0)
                {
                    foreach (var m in Axis.Mappings)
                        if (m.SimVarName.StartsWith("ELEVATOR TRIM"))
                            m.IsAvailable = false;
                }
                else if (i == (int)Definitions.ElevatorTrimMinDegrees)
                {
                    foreach (var m in Axis.Mappings)
                        if (m.SimVarName == "ELEVATOR TRIM POSITION")
                            m.UpdateElevatorTrimMinValue((double)data.dwData[0]);
                }
                else if (i == (int)Definitions.ElevatorTrimMaxDegrees)
                {
                    foreach (var m in Axis.Mappings)
                        if (m.SimVarName == "ELEVATOR TRIM POSITION")
                            m.UpdateElevatorTrimMaxValue((double)data.dwData[0]);
                }
                else if (i == (int)Definitions.AileronTrimDisabled && (double)data.dwData[0] != 0)
                {
                    foreach (var m in Axis.Mappings)
                        if (m.SimVarName.StartsWith("AILERON TRIM"))
                            m.IsAvailable = false;
                }
                else if (i == (int)Definitions.RudderTrimDisabled && (double)data.dwData[0] != 0)
                {
                    foreach (var m in Axis.Mappings)
                        if (m.SimVarName.StartsWith("RUDDER TRIM"))
                            m.IsAvailable = false;
                }
                else if (i == (int)Definitions.ThrottleLowerLimit)
                {
                    var n = (double)data.dwData[0];
                    if (n < 0)
                        foreach (var m in Axis.Mappings)
                            m.UpdateThrottleLowerLimit((double)data.dwData[0]);
                }
                else if (i == (int)Definitions.FlapsNumHandlePosition)
                {
                    var n = (double)data.dwData[0];
                    if (n >= 1)
                        foreach (var m in Axis.Mappings)
                            m.UpdateFlapsNumHandlePosition((uint)n);
                }
                else if ((int)Definitions.Axis <= i) // translate i to an Axis.Mappings index and requestType for processing
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
                            trimmedAxisChange = m.TrimmedAxis - inSim.TrimmedAxis;
                        }
                        else // requestType == RequestType.AxisValue
                        {
                            inSimValue = (double)data.dwData[0];
                        }
                        m.UpdateSimVarValue(inSimValue - m.SimVarValue, trimmedAxisChange);
                    }
                }
            }
            catch (Exception ex) { Trace.WriteLine($"{ex.Message} at: {ex.StackTrace}"); }
        }

        // Implementation

        IntPtr _hwnd;
        GaugeWindow _gaugeWindow = new GaugeWindow();
        List<string> displayedErrors = new List<string>();

        SimConnect _simConnect;
        bool _connected = false;
        bool _manuallyDisconnected = false;
        bool _wasAutoConnect = false;

        bool _requestFlaps = false;
        bool _requestGear = false;
        bool _requestSpoiler = false;
        bool _requestBrakes = false;
        bool _requestEngines = false;
        bool _requestSpeeds = false;
        bool _requestReverse = false;
        bool _requestElevatorTrim = false;
        bool _requestAileronTrim = false;
        bool _requestRudderTrim = false;
        HashSet<uint> _requestJoystickButtonInputEvents = new HashSet<uint>();

        vJoy _vJoy;
    }
}