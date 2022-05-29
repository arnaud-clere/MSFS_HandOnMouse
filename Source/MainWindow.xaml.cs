using System;
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
        public string Status { get { return _status; } set { if (_status != value) { _status = value; NotifyPropertyChanged(); } } }
        public bool GaugeVisible { get { return !Settings.Default.GaugeHidden; } }
        public List<string> MappingFiles { get { var fs = new List<string>(); foreach(var f in new DirectoryInfo(MainWindow.MappingsDir()).GetFiles("*.ini")) { fs.Add(f.Name.Remove(f.Name.Length-f.Extension.Length)); } return fs; } }
        public ObservableCollection<Axis> Mappings { get { return Axis.Mappings; } }
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

        public void NotifyPropertyChanged([CallerMemberName] string propertyName = "") => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        
        private string _status = "HandOnMouse Gauges";
        private Brush _statusBrushForText = new SolidColorBrush(Colors.Gray);
    }

    /// <summary>Interaction logic for MainWindow.xaml</summary>
    public partial class MainWindow : Window
    {
        public static string MappingsDir() { return Path.Combine(Directory.GetCurrentDirectory(), "Mappings"); }
        public static string MappingFile() { return Path.ChangeExtension(Path.Combine(MappingsDir(), Settings.Default.MappingFile), ".ini"); }

        const int WM_USER_SIMCONNECT = (int)WM.USER + 2;

        /// <summary>SimConnect Requests and Data Definitions are actually defined dynamically for each Axis in Axis.Mappings</summary>
        public enum Definitions
        {
            None = 0,
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
            Axis = 15
        }

        public MainWindow()
        {
            Trace.WriteLine($"MainWindow {DateTime.Now}");

            _gaugeWindow.DataContext = DataContext = new ViewModel();
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

            var simFrameTimer = new DispatcherTimer();
            simFrameTimer.Interval = new TimeSpan(0, 0, 0, 0, 1000 / 36);
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
            if (e.PropertyName == "GaugeHidden")
            {
                ((ViewModel)DataContext).NotifyPropertyChanged("GaugeVisible");
                if (Settings.Default.GaugeHidden)
                    _gaugeWindow.Hide();
                else
                    _gaugeWindow.Show();
            }
            if (e.PropertyName == "MappingFile")
            {
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
            if (_simConnect != null && _connected)
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
        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.WidthChanged && WindowState == WindowState.Normal) Left += e.PreviousSize.Width - e.NewSize.Width;
        }
        private void Window_Minimize(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        public void Axis_Click(object sender, RoutedEventArgs e)
        {
            var b = (Button)sender;
            var w = new AxisWindow((Axis)((Button)sender).Tag);
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
            if (_simConnect == null)
            {
                try
                {
                    _simConnect = new SimConnect("HandOnMouse", _hwnd, WM_USER_SIMCONNECT, null, 0);

                    ChangeButtonStatus(false, connectButton, true, "DISCONNECT");

                    ((ViewModel)DataContext).Status = "Connected";
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                    timer.Tick += (s, args) =>
                    {
                        ((ViewModel)DataContext).Status = "";
                        timer.Stop();
                    };
                    timer.Start();

                    _simConnect.OnRecvOpen += new SimConnect.RecvOpenEventHandler(SimConnect_OnRecvOpen);
                    _simConnect.OnRecvQuit += new SimConnect.RecvQuitEventHandler(SimConnect_OnRecvQuit);
                    _simConnect.OnRecvEventFilename += new SimConnect.RecvEventFilenameEventHandler(SimConnect_OnRecvEventFilename);
                    _simConnect.OnRecvException += new SimConnect.RecvExceptionEventHandler(SimConnect_OnRecvException);
                    _simConnect.OnRecvSimobjectData += new SimConnect.RecvSimobjectDataEventHandler(SimConnect_OnRecvData);
                }
                catch (Exception ex) { Trace.WriteLine($"{ex.Message} at: {ex.StackTrace}"); }
            }
            else
            {
                Disconnect();
            }
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

            ((ViewModel)DataContext).StatusBrushForText = new SolidColorBrush(Settings.Default.Sensitivity > 0 ? Colors.Black : Colors.Gray);

            requestFlaps = false;
            requestGear = false;
            requestSpoiler = false;
            requestBrakes = false;
            requestEngines = false;
            requestSpeeds = false;
            for (int id = 0; id < Axis.Mappings.Count; id++)
            {
                var m = Axis.Mappings[id];
                if (m.SimVarName.Length > 0)
                {
                    if (m.TrimCounterCenteringMove && Axis.AxisForTrim.ContainsKey(m.SimVarName))
                    {
                        RegisterData(RequestId(id), m.SimVarName, m.SimVarUnit, (float)(m.SimVarScale / Settings.Default.ContinuousSimVarIncrements));

                        _simConnect.AddToDataDefinition(ReadAxisValueId(id), m.SimVarName, m.SimVarUnit, SIMCONNECT_DATATYPE.FLOAT64, (float)(m.SimVarScale / Settings.Default.ContinuousSimVarIncrements), SimConnect.SIMCONNECT_UNUSED);
                        _simConnect.AddToDataDefinition(ReadAxisValueId(id), Axis.AxisForTrim[m.SimVarName], "Position", SIMCONNECT_DATATYPE.FLOAT64, (float)((1 - -1) / Settings.Default.ContinuousSimVarIncrements), SimConnect.SIMCONNECT_UNUSED);
                        _simConnect.RegisterDataDefineStruct<SmartTrimAxis>(ReadAxisValueId(id));
                        RequestData(ReadAxisValueId(id), SIMCONNECT_PERIOD.SIM_FRAME);
                    }
                    else if (m.ForAllEngines)
                    {
                        RegisterData(RequestId(id), m.SimVarName + ":1", m.SimVarUnit, (float)(m.SimVarScale / Settings.Default.ContinuousSimVarIncrements));
                        RequestData(RequestId(id), SIMCONNECT_PERIOD.SIM_FRAME);

                        for (uint engineId = 1; engineId < 5; engineId++)
                            _simConnect.AddToDataDefinition(RequestId(id, RequestType.SmartAxisValue), m.SimVarName + ":" + engineId, m.SimVarUnit, SIMCONNECT_DATATYPE.FLOAT64, (float)(m.SimVarScale / Settings.Default.ContinuousSimVarIncrements), SimConnect.SIMCONNECT_UNUSED);
                        _simConnect.RegisterDataDefineStruct<SmartEngineAxis>(RequestId(id, RequestType.SmartAxisValue));
                    }
                    else
                    {
                        RegisterData(RequestId(id), m.SimVarName, m.SimVarUnit, (float)(m.SimVarScale / Settings.Default.ContinuousSimVarIncrements));
                        RequestData(RequestId(id), SIMCONNECT_PERIOD.SIM_FRAME);
                    }
                    if (m.IsThrottle && !m.DisableThrottleReverse) requestReverse = true;
                    if (m.SimVarName == "FLAPS HANDLE INDEX") requestFlaps = true;
                    if (m.SimVarName == "GEAR HANDLE POSITION") requestGear = true;
                    if (m.SimVarName == "SPOILERS HANDLE POSITION") requestSpoiler = true;
                    if (m.SimVarName.StartsWith("BRAKE LEFT POSITION") || m.SimVarName.StartsWith("BRAKE RIGHT POSITION")) requestBrakes = true;
                    if (m.SimVarName.StartsWith("ELEVATOR TRIM")) requestElevatorTrim = true;
                    if (m.SimVarName.StartsWith("AILERON TRIM")) requestAileronTrim = true;
                    if (m.SimVarName.StartsWith("RUDDER TRIM")) requestRudderTrim = true;
                    foreach (var v in Axis.EngineSimVars) if (m.SimVarName.StartsWith(v)) requestEngines = true;
                    if (m.SensitivityAtCruiseSpeed) requestSpeeds = true;
                }
            }
            if (requestReverse)
            {
                RegisterData(Definitions.ThrottleLowerLimit, "THROTTLE LOWER LIMIT", "Percent" /* <0 */);
                RequestData(Definitions.ThrottleLowerLimit);
            }
            if (requestSpeeds)
            {
                RegisterData(Definitions.IndicatedAirSpeedKnots, "AIRSPEED INDICATED", "Knots", 5);
                RequestData(Definitions.IndicatedAirSpeedKnots, SIMCONNECT_PERIOD.SECOND);

                RegisterData(Definitions.DesignCruiseSpeedFeetPerSec, "DESIGN SPEED VC", "Feet per second", 5);
                RequestData(Definitions.DesignCruiseSpeedFeetPerSec);
            }
            if (requestEngines)
            {
                RegisterData(Definitions.EnginesCount, "NUMBER OF ENGINES", "Number");
                RequestData(Definitions.EnginesCount);
            }
            if (requestBrakes)
            {
                RegisterData(Definitions.BrakesAvailable, "TOE BRAKES AVAILABLE", "Bool");
                RequestData(Definitions.BrakesAvailable);
            }
            if (requestSpoiler)
            {
                RegisterData(Definitions.SpoilerAvailable, "SPOILER AVAILABLE", "Bool");
                RequestData(Definitions.SpoilerAvailable);
            }
            if (requestGear)
            {
                RegisterData(Definitions.IsGearRetractable, "IS GEAR RETRACTABLE", "Bool");
                RequestData(Definitions.IsGearRetractable);
            }
            if (requestFlaps)
            {
                RegisterData(Definitions.EnginesCount, "FLAPS AVAILABLE", "Bool");
                RequestData(Definitions.FlapsAvailable);

                RegisterData(Definitions.FlapsNumHandlePosition, "FLAPS NUM HANDLE POSITIONS", "Number");
                RequestData(Definitions.FlapsNumHandlePosition);
            }
            if (requestElevatorTrim)
            {
                RegisterData(Definitions.ElevatorTrimDisabled, "ELEVATOR TRIM DISABLED", "Bool");
                RequestData(Definitions.ElevatorTrimDisabled);
            }
            if (requestAileronTrim)
            {
                RegisterData(Definitions.AileronTrimDisabled, "AILERON TRIM DISABLED", "Bool");
                RequestData(Definitions.AileronTrimDisabled);
            }
            if (requestRudderTrim)
            {
                RegisterData(Definitions.RudderTrimDisabled, "RUDDER_TRIM_DISABLED", "Bool");
                RequestData(Definitions.RudderTrimDisabled);
            }
            _simConnect.SubscribeToSystemEvent(Definitions.AircraftLoaded, "AircraftLoaded");
        }
        private void RegisterData(Definitions id, string simVarName, string simVarType, float epsilon = 1)
        {
            _simConnect.AddToDataDefinition(id, simVarName, simVarType, SIMCONNECT_DATATYPE.FLOAT64, epsilon, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.RegisterDataDefineStruct<double>(id);
        }
        private void RequestData(Definitions id, SIMCONNECT_PERIOD period = SIMCONNECT_PERIOD.ONCE)
        {
            Debug.Assert(_simConnect != null);
            _simConnect?.RequestDataOnSimObject(id, id, (uint)SIMCONNECT_SIMOBJECT_TYPE.USER, period, period > SIMCONNECT_PERIOD.ONCE ? SIMCONNECT_DATA_REQUEST_FLAG.CHANGED : SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
        }
        private void SimConnect_OnRecvEventFilename(SimConnect sender, SIMCONNECT_RECV_EVENT_FILENAME data)
        {
            Trace.WriteLine($"Received event: {data.uEventID} szFileName: {data.szFileName}");

            if (data.uEventID == (uint)Definitions.AircraftLoaded)
            {
                Axis.MappingsRead(MappingFile()); // to reset axis info?!
                foreach (var m in Axis.Mappings)
                {
                    m.PropertyChanged += new PropertyChangedEventHandler(Axis_SimVarValueChanged);
                }
            }
            if (requestReverse) RequestData(Definitions.ThrottleLowerLimit);
            if (requestSpeeds) RequestData(Definitions.DesignCruiseSpeedFeetPerSec);
            if (requestEngines) RequestData(Definitions.EnginesCount);
            if (requestBrakes) RequestData(Definitions.BrakesAvailable);
            if (requestSpoiler) RequestData(Definitions.SpoilerAvailable);
            if (requestGear) RequestData(Definitions.IsGearRetractable);
            if (requestFlaps) RequestData(Definitions.FlapsAvailable);
            if (requestElevatorTrim) RequestData(Definitions.ElevatorTrimDisabled);
            if (requestAileronTrim) RequestData(Definitions.AileronTrimDisabled);
            if (requestRudderTrim) RequestData(Definitions.RudderTrimDisabled);
        }
        private void SimConnect_OnRecvQuit(SimConnect sender, SIMCONNECT_RECV data)
        {
            Trace.WriteLine("Received quit");

            Disconnect();
        }
        public void Disconnect()
        {
            if (_simConnect == null)
                return;

            try
            {
                ((ViewModel)DataContext).Status = "Disconnected";
                _simConnect.Dispose();
                _simConnect = null;
                _connected = false;
                ((ViewModel)DataContext).StatusBrushForText = new SolidColorBrush(Colors.Gray);

                ChangeButtonStatus(true, connectButton, true, "CONNECT FS");
            }
            catch (Exception ex) { Trace.WriteLine($"{ex.Message} at: {ex.StackTrace}"); }
        }

        private void SimConnect_OnRecvException(SimConnect sender, SIMCONNECT_RECV_EXCEPTION data)
        {
            SIMCONNECT_EXCEPTION e = (SIMCONNECT_EXCEPTION)data.dwException;
            Trace.WriteLine($"SIMCONNECT_RECV_EXCEPTION: {e} dwSendID: {data.dwSendID} dwIndex: {data.dwIndex}");
            ((ViewModel)DataContext).StatusBrushForText = new SolidColorBrush(Colors.Red);
        }

        public void ChangeButtonStatus(bool active, Button b, bool enabled, string text)
        {
            b.BorderBrush = new SolidColorBrush(active ? Colors.DarkGreen : Colors.DarkRed);
            b.Foreground = new SolidColorBrush(active ? Colors.DarkGreen : Colors.DarkRed);

            if (!string.IsNullOrEmpty(text))
                b.Content = text;

            b.IsEnabled = enabled;
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_simConnect != null && _connected)
            {
                MessageBox.Show("Please DISCONNECT before changing mappings!", "HandOnMouse");
                e.Handled = false;
            }
            else if (e.AddedItems.Count > 0)
            {
                TryReadMappings(e.AddedItems[0].ToString());
            }
        }
        public void Window_File(object sender, RoutedEventArgs e)
        {
            if (_simConnect != null && _connected)
            {
                MessageBox.Show("Please DISCONNECT before changing mappings!", "HandOnMouse");
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
        private bool TryReadMappings(string filePath = null)
        {
            filePath = Path.ChangeExtension(filePath, ".ini");
            if (filePath != null && !Path.IsPathRooted(filePath))
            {
                filePath = Path.Combine(MappingsDir(), filePath);
            }
            if (filePath == null || !File.Exists(filePath))
            {
                filePath = MappingFile();
            }
            var errors = Axis.MappingsRead(filePath);
            var revert = false;
            if (Axis.Mappings.Count == 0 && filePath != MappingFile()) // revert to previous file
            {
                Axis.MappingsRead(MappingFile());
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
                                    errors += m.SimVarName + ": " + $"vJoy DLL version {dllVersion} does not support installed vJoy driver version {driverVersion}\n";
                                    _vJoy = null;
                                }
                            }
                            else
                            {
                                errors += m.SimVarName + ": " + $"vJoy driver not installed or not enabled\n";
                                _vJoy = null;
                            }
                        }
                        if (_vJoy?.AcquireVJD(m.VJoyId) != true)
                        {
                            errors += m.SimVarName + ": " + $"vJoy axis {m.VJoyId} not acquired\n";
                            m.IsAvailable = false;
                        }
                        var status = _vJoy?.GetVJDStatus(m.VJoyId);
                        if (status != VjdStat.VJD_STAT_OWN)
                        {
                            errors += m.SimVarName + ": " + $"vJoy device {m.VJoyId} not available: {status}\n";
                            m.IsAvailable = false;
                        }
                        else if (_vJoy?.GetVJDAxisExist(m.VJoyId, m.VJoyAxis) != true)
                        {
                            errors += m.SimVarName + ": " + $"vJoy device {m.VJoyId} axis {m.VJoyAxis} not found\n";
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
                var message = filePath + ":\n" + errors;
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
        private void Timer_Tick(object sender, EventArgs e)
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
                else if (i == (int)Definitions.AileronTrimDisabled && (double)data.dwData[0] != 0)
                {
                    foreach (var m in Axis.Mappings)
                        if (m.SimVarName.StartsWith("AILERON TRIM"))
                            m.IsAvailable = false;
                }
                else if (i == (int)Definitions.RudderTrimDisabled && (double)data.dwData[0] != 0)
                {
                    foreach (var m in Axis.Mappings)
                        if (m.SimVarName.StartsWith("RUDDER_TRIM"))
                            m.IsAvailable = false;
                }
                else if ((i == (int)Definitions.ThrottleLowerLimit) ||
                         (i == (int)Definitions.FlapsNumHandlePosition))
                {
                    foreach (var m in Axis.Mappings)
                        m.UpdateSimInfo((double)data.dwData[0]);
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
        SimConnect _simConnect;
        vJoy _vJoy;
        bool _connected = false;
        GaugeWindow _gaugeWindow = new GaugeWindow();

        List<string> displayedErrors = new List<string>();
        bool requestFlaps = false;
        bool requestGear = false;
        bool requestSpoiler = false;
        bool requestBrakes = false;
        bool requestEngines = false;
        bool requestSpeeds = false;
        bool requestReverse = false;
        bool requestElevatorTrim = false;
        bool requestAileronTrim = false;
        bool requestRudderTrim = false;
    }
}