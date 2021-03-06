using HandOnMouse.Properties;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using winbase;
using winuser;

using vJoyInterfaceWrap;
using System.Windows.Input;

namespace HandOnMouse
{
    public class Axis : INotifyPropertyChanged
    {
        public enum Direction { Push, Draw, Left, Right };

        static public ObservableCollection<Axis> Mappings { get; private set; } = new ObservableCollection<Axis>();
        static public string MappingsRead(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return "File not found: " + filePath;
            }
            Mappings.Clear();
            var errors = "";
            var stop = false;
            for (int i = 0; !stop; i++)
            {
                var m = new Axis();
                var section = $"Axis{i + 1}";
                try
                {
                    errors += MappingsReadAxis(filePath, i, m);
                }
                catch (Exception e)
                {
                    errors += section + ": " + e.Message + '\n';
                }
                finally
                {
                    if (m.Name.Length > 0)
                    {
                        Mappings.Add(m);
                    }
                    else
                    {
                        stop = true;
                    }
                }
            }
            return errors;
        }
        static public string MappingsReadAxis(string filePath, int i, Axis m)
        {
            var errors = "";
            var section = $"Axis{i + 1}";
            var customFilePath = File.Exists(filePath + '.' + (1 + i)) ? filePath + '.' + (1 + i) : filePath;
            var btn = RAWMOUSE.RI_MOUSE.None;
            var btnString = Kernel32.ReadIni(customFilePath, "MouseButtonsFilter", section).ToUpper().Split(new char[] { '-' });
            if (btnString.Length > 0)
            {
                if (btnString[0].Contains("L")) btn |= RAWMOUSE.RI_MOUSE.LEFT_BUTTON_DOWN;
                if (btnString[0].Contains("M")) btn |= RAWMOUSE.RI_MOUSE.MIDDLE_BUTTON_DOWN;
                if (btnString[0].Contains("R")) btn |= RAWMOUSE.RI_MOUSE.RIGHT_BUTTON_DOWN;
                if (btnString[0].Contains("B")) btn |= RAWMOUSE.RI_MOUSE.BUTTON_4_DOWN;
                if (btnString[0].Contains("F")) btn |= RAWMOUSE.RI_MOUSE.BUTTON_5_DOWN;
            }
            if (btnString.Length > 1)
            {
                if (btnString[1].Contains("L")) btn |= RAWMOUSE.RI_MOUSE.LEFT_BUTTON_UP;
                if (btnString[1].Contains("M")) btn |= RAWMOUSE.RI_MOUSE.MIDDLE_BUTTON_UP;
                if (btnString[1].Contains("R")) btn |= RAWMOUSE.RI_MOUSE.RIGHT_BUTTON_UP;
                if (btnString[1].Contains("B")) btn |= RAWMOUSE.RI_MOUSE.BUTTON_4_UP;
                if (btnString[1].Contains("F")) btn |= RAWMOUSE.RI_MOUSE.BUTTON_5_UP;
            }
            m.MouseButtonsFilter = btn == RAWMOUSE.RI_MOUSE.None ? RAWMOUSE.RI_MOUSE.Reserved : btn; // to avoid changing the axis with no button down

            m.ControllerManufacturerId = 0;
            m.ControllerProductId = 0;
            m.ControllerButtonsFilter = Controller.Buttons.None;
            var controllerBtnString = Kernel32.ReadIni(customFilePath, "ControllerButtonsFilter", section).ToUpper().Split(new char[] { '-' });
            if (controllerBtnString.Length > 0 && m.MouseButtonsFilter == RAWMOUSE.RI_MOUSE.Reserved)
            {
                var mpi = controllerBtnString[0].Split(new char[] { '/' });
                if (mpi.Length == 3)
                {
                    m.ControllerManufacturerId = ushort.Parse(mpi[0]);
                    m.ControllerProductId = ushort.Parse(mpi[1]);
                    m.ControllerButtonsFilter = (Controller.Buttons)(1u << (int)Math.Min(32u, uint.Parse(mpi[2])) - 1);
                }
            }

            m.KeyboardKeyDownFilter = Key.None;
            var keyboardString = Kernel32.ReadIni(customFilePath, "KeyboardKeyDownFilter", section);
            if (keyboardString != "" && m.MouseButtonsFilter == RAWMOUSE.RI_MOUSE.Reserved && m.ControllerManufacturerId == 0)
            {
                m.KeyboardKeyDownFilter = (Key)Enum.Parse(typeof(Key), keyboardString);
            }

            var nameAndId = Kernel32.ReadIni(filePath, "VJoyAxis", section).Split(" ".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
            if (nameAndId.Length > 1)
            {
                try
                {
                    m.VJoyId = uint.Parse(nameAndId[1]);
                    if (m.VJoyId > 16)
                    {
                        errors += section + ": " + $"vJoy device {m.VJoyId} not supported\n";
                        m.VJoyId = 0;
                    }
                }
                catch (Exception e)
                {
                    errors += section + ": " + e.Message + '\n';
                    m.VJoyId = 0;
                }
            }
            else if (nameAndId.Length > 0)
            {
                m.VJoyId = 1;
                switch (nameAndId[0].Trim().ToUpperInvariant())
                {
                    case "LX"     : m.VJoyAxis = HID_USAGES.HID_USAGE_X  ; break;
                    case "LY"     : m.VJoyAxis = HID_USAGES.HID_USAGE_Y  ; break;
                    case "LZ"     : m.VJoyAxis = HID_USAGES.HID_USAGE_Z  ; break;
                    case "RX"     : m.VJoyAxis = HID_USAGES.HID_USAGE_RX ; break;
                    case "RY"     : m.VJoyAxis = HID_USAGES.HID_USAGE_RY ; break;
                    case "RZ"     : m.VJoyAxis = HID_USAGES.HID_USAGE_RZ ; break;
                    case "SLIDERX": m.VJoyAxis = HID_USAGES.HID_USAGE_SL0; break;
                    case "SLIDERY": m.VJoyAxis = HID_USAGES.HID_USAGE_SL1; break;
                    default:
                        errors += section + ": Unsupported vJoy axis: " + nameAndId[0] + " (use one of LX, LY, LZ, RX, RY, RZ, SLIDERX, SLIDERY)\n";
                        m.VJoyId = 0;
                        break;
                }
            }
            if (m.VJoyId > 0)
            {
                m.SimVarMin = 0;
                m.SimVarMax = 32767;
                m.VJoyAxisZero = uint.Parse(Kernel32.ReadIni(filePath, "VJoyAxisZero", section, "0").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture);
                m.VJoyAxisIsThrottle = bool.Parse(Kernel32.ReadIni(filePath, "VJoyAxisIsThrottle", section, "False").Trim());
                if (_vJoy == null)
                {
                    _vJoy = new vJoy();
                    if (_vJoy.vJoyEnabled())
                    {
                        UInt32 dllVersion = 0, driverVersion = 0;
                        if (!(_vJoy.DriverMatch(ref dllVersion, ref driverVersion) || (dllVersion == 536 && driverVersion == 537)))
                        {
                            errors += section + ": " + $"vJoy DLL version {dllVersion} does not support installed vJoy driver version {driverVersion}\n";
                            _vJoy = null;
                        }
                    }
                    else
                    {
                        errors += section + ": " + $"vJoy driver not installed or not enabled\n";
                        _vJoy = null;
                    }
                }
                _vJoy?.AcquireVJD(m.VJoyId);
                var status = _vJoy?.GetVJDStatus(m.VJoyId);
                if (status != VjdStat.VJD_STAT_OWN)
                {
                    errors += section + ": " + $"vJoy device {m.VJoyId} not available: {status}\n";
                    m.VJoyId = 0;
                }
                else if (!_vJoy?.GetVJDAxisExist(m.VJoyId, m.VJoyAxis) ?? false)
                {
                    errors += section + ": " + $"vJoy device {m.VJoyId} axis {m.VJoyAxis} not found\n";
                    m.VJoyId = 0;
                }
            }
            else
            {
                m.SimVarName = Kernel32.ReadIni(filePath, "SimVarName", section).Trim().ToUpperInvariant();
                m.SimVarUnit = Kernel32.ReadIni(filePath, "SimVarUnit", section, "Percent").Trim();
                var min = double.Parse(Kernel32.ReadIni(filePath, "SimVarMin", section, "0"), NumberStyles.Float, CultureInfo.InvariantCulture);
                var max = double.Parse(Kernel32.ReadIni(filePath, "SimVarMax", section, "100"), NumberStyles.Float, CultureInfo.InvariantCulture);
                m.SimVarMax = Math.Max(min, max);
                m.SimVarMin = Math.Min(min, max);
                m.SimVarValue = Math.Max(0, m.SimVarMin);
                m.TrimCounterCenteringMove = bool.Parse(Kernel32.ReadIni(customFilePath, "TrimCounterCenteringMove", section, "False").Trim());
                m.DisableThrottleReverse = bool.Parse(Kernel32.ReadIni(customFilePath, "DisableThrottleReverse", section, "False").Trim());
            }
            m.Sensitivity = Math.Max(1 / 100, Math.Min(100, double.Parse(
                Kernel32.ReadIni(customFilePath, "Sensitivity", section, "1"), NumberStyles.Float, CultureInfo.InvariantCulture)));
            m.SensitivityAtCruiseSpeed = bool.Parse(
                Kernel32.ReadIni(customFilePath, "SensitivityAtCruiseSpeed", section, "False").Trim());
            var directions = Kernel32.ReadIni(customFilePath, "IncreaseDirection", section, "Push").Split(" ".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
            m.IncreaseDirection = (Direction)Enum.Parse(typeof(Direction), directions[0].Trim(), true);
            if (directions.Length > 1)
            {
                try
                {
                    m.IncreaseDirection2 = (Direction)Enum.Parse(typeof(Direction), directions[1].Trim(), true);
                }
                catch (Exception e)
                {
                    errors += section + ": " + e.Message + '\n';
                }
            }
            m.DecreaseScaleTimeSecs = Math.Max(0, Math.Min(10, double.Parse(
                Kernel32.ReadIni(customFilePath, "DecreaseScaleTimeSecs", section, "0"), NumberStyles.Float, CultureInfo.InvariantCulture)));
            m.WaitButtonsReleased = bool.Parse(
                Kernel32.ReadIni(customFilePath, "WaitButtonsReleased", section, "False").Trim());
            m.PositiveDetent = double.Parse(
                Kernel32.ReadIni(customFilePath, "PositiveDetent", section, "0"), NumberStyles.Float, CultureInfo.InvariantCulture);

            m.DisableIncreaseDirection2 = bool.Parse(Kernel32.ReadIni(customFilePath, "DisableIncreaseDirection2", section, "False").Trim());
            m.IsHidden = bool.Parse(Kernel32.ReadIni(customFilePath, "IsHidden", section, "False").Trim());

            var scaleColors = Kernel32.ReadIni(filePath, "SimVarNegativePositiveColors", section, "").Split(" ".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
            m.SimVarNegativeColor = ReadColor(scaleColors, 0, section + "/SimVarNegativePositiveColors", ref errors);
            m.SimVarPositiveColor = ReadColor(scaleColors, 1, section + "/SimVarNegativePositiveColors", ref errors);
            m.SimVarPositiveDetentColor = ReadColor(scaleColors, 2, section + "/SimVarNegativePositiveColors", ref errors);

            return errors;
        }
        static public string MappingsSaveCustom(string filePath, Axis m)
        {
            var errors = "";
            var id = Mappings.IndexOf(m);
            if (id < 0)
            {
                return $"Cannot save axis not correctly read from: {filePath}";
            }
            var customFilePath = filePath + '.' + (1 + id);
            var section = $"Axis{1 + id}";
            if (m.Name.Length > 0)
            {
                try
                {
                    Kernel32.WriteIni(customFilePath, "ControllerButtonsFilter", section, m.ControllerButtonsText == null ? "" : $"{m.ControllerManufacturerId}/{m.ControllerProductId}/{m.ControllerButtonsText}");
                    Kernel32.WriteIni(customFilePath, "DecreaseScaleTimeSecs", section, m.DecreaseScaleTimeSecs.ToString(CultureInfo.InvariantCulture));
                    Kernel32.WriteIni(customFilePath, "DisableIncreaseDirection2", section, m.DisableIncreaseDirection2.ToString());
                    Kernel32.WriteIni(customFilePath, "DisableThrottleReverse", section, m.DisableThrottleReverse.ToString());
                    Kernel32.WriteIni(customFilePath, "IncreaseDirection", section, $"{Enum.Format(typeof(Direction), m.IncreaseDirection, "G")} {(m.IncreaseDirection2 == null ? null : Enum.Format(typeof(Direction), m.IncreaseDirection2, "G"))}");
                    Kernel32.WriteIni(customFilePath, "KeyboardKeyDownFilter", section, m.KeyboardKeyDownFilter == Key.None ? "" : $"{m.KeyboardKeyDownFilter}");
                    Kernel32.WriteIni(customFilePath, "MouseButtonsFilter", section, m.MouseButtonsText ?? "");
                    Kernel32.WriteIni(customFilePath, "Sensitivity", section, m.Sensitivity.ToString(CultureInfo.InvariantCulture));
                    Kernel32.WriteIni(customFilePath, "SensitivityAtCruiseSpeed", section, m.SensitivityAtCruiseSpeed.ToString());
                    Kernel32.WriteIni(customFilePath, "TrimCounterCenteringMove", section, m.TrimCounterCenteringMove.ToString());
                    Kernel32.WriteIni(customFilePath, "WaitButtonsReleased", section, m.WaitButtonsReleased.ToString());
                    Kernel32.WriteIni(customFilePath, "PositiveDetent", section, m.PositiveDetent.ToString());
                    Kernel32.WriteIni(customFilePath, "IsHidden", section, m.IsHidden.ToString());
                }
                catch (Exception e)
                {
                    errors += section + ": " + e.Message + '\n';
                }
            }
            return errors;
        }
        static public string MappingsReset(string filePath, Axis m)
        {
            var id = Mappings.IndexOf(m);
            if (id < 0)
            {
                return $"Cannot reset axis not correctly read from: {filePath}";
            }
            var customFilePath = filePath + '.' + (1 + id);
            var errors = "";
            try
            {
                File.Delete(customFilePath);
                errors += MappingsReadAxis(filePath, id, m);
            }
            catch (Exception e)
            {
                errors += customFilePath + ": " + e.Message + '\n';
            }
            return errors;
        }
        static public Color TextColorFromChange(double normalizedChange)
        {
            var maxChangeColor = Colors.DarkOrange;
            var change = Math.Min(1, Math.Abs(normalizedChange) * 3); // max if change > max(normalizedChange)/3
            return Color.FromRgb(
                (byte)(maxChangeColor.R * change),
                (byte)(maxChangeColor.G * change),
                (byte)(maxChangeColor.B * change));
        }
        static public readonly IReadOnlyList<string> EngineSimVars = new string[] {
            "GENERAL ENG THROTTLE LEVER POSITION",
            "GENERAL ENG MIXTURE LEVER POSITION",
            "GENERAL ENG PROPELLER LEVER POSITION",
            };
        static public uint EnginesCount;
        static public readonly IReadOnlyDictionary<string, string> AxisForTrim = new Dictionary<string, string> {
            { "ELEVATOR TRIM POSITION", "ELEVATOR POSITION" },
            { "AILERON TRIM PCT"      ,  "AILERON POSITION" },
            { "RUDDER TRIM PCT"       ,   "RUDDER POSITION" },
            };
        static public double DesignCruiseSpeedKnots;
        static public double IndicatedAirSpeedKnots;

        static private vJoy _vJoy;

        public Axis()
        {
            SimVarName = "";
            SimVarUnit = "Percent";
            SimVarMax = 100;
            SimVarValue = Math.Max(0, SimVarMin);
            ChangeColorForText = Colors.Black;
            IncreaseDirection = Direction.Push;
            MouseButtonsFilter = RAWMOUSE.RI_MOUSE.Reserved;
            TrimmedAxis = double.NaN;
            IsAvailable = true;
        }

        // R/O properties

        public int Id { get { return Mappings.IndexOf(this); } }
        public string Text
        {
            get
            {
                var control = TriggerText;
                if (TriggerText != null)
                {
                    control += IncreaseDirectionText;
                    if (TrimCounterCenteringMove) control += "+";
                }
                else
                {
                    control = "X";
                }
                if (WaitButtonsReleased)
                {
                    control = "(" + control + ")";
                }
                var na = IsAvailable ? "" : "N/A ";
                var axis = Name.Length > 0 ? Name.Replace("GENERAL ", "").Replace(" PCT", "").Replace(" POSITION", "").ToLowerInvariant() : "-";
                return control + " " + na + axis;
            }
        }
        public string TriggerDeviceName 
        { 
            get 
            { 
                return 
                    MouseButtonsText != null ? "Mouse" :
                    KeyboardKeyText != null ? "Keyboard" :
                    ControllerButtonsText != null ? Controller.Get(ControllerManufacturerId, ControllerProductId)?.Name :
                    "None"; 
            } 
        }
        public string TriggerText { get { return MouseButtonsText ?? KeyboardKeyText ?? ControllerButtonsText; } }
        public string MouseButtonsText
        {
            get
            {
                string btn = null;
                if (MouseButtonsFilter != RAWMOUSE.RI_MOUSE.Reserved)
                {
                    if (MouseButtonsFilter.HasFlag(RAWMOUSE.RI_MOUSE.LEFT_BUTTON_DOWN)) btn += "L";
                    if (MouseButtonsFilter.HasFlag(RAWMOUSE.RI_MOUSE.MIDDLE_BUTTON_DOWN)) btn += "M";
                    if (MouseButtonsFilter.HasFlag(RAWMOUSE.RI_MOUSE.RIGHT_BUTTON_DOWN)) btn += "R";
                    if (MouseButtonsFilter.HasFlag(RAWMOUSE.RI_MOUSE.BUTTON_4_DOWN)) btn += "B";
                    if (MouseButtonsFilter.HasFlag(RAWMOUSE.RI_MOUSE.BUTTON_5_DOWN)) btn += "F";
                    var btnUp = "";
                    if (MouseButtonsFilter.HasFlag(RAWMOUSE.RI_MOUSE.LEFT_BUTTON_UP)) btnUp += "L";
                    if (MouseButtonsFilter.HasFlag(RAWMOUSE.RI_MOUSE.MIDDLE_BUTTON_UP)) btnUp += "M";
                    if (MouseButtonsFilter.HasFlag(RAWMOUSE.RI_MOUSE.RIGHT_BUTTON_UP)) btnUp += "R";
                    if (MouseButtonsFilter.HasFlag(RAWMOUSE.RI_MOUSE.BUTTON_4_UP)) btnUp += "B";
                    if (MouseButtonsFilter.HasFlag(RAWMOUSE.RI_MOUSE.BUTTON_5_UP)) btnUp += "F";
                    if (btnUp.Length > 0) btn += "-" + btnUp;
                }
                return btn;
            }
        }
        public string KeyboardKeyText { get { return KeyboardKeyDownFilter != Key.None ? KeyboardKeyDownFilter.ToString() : null; } }
        public string ControllerButtonsText
        {
            get
            {
                string btn = null;
                if (ControllerButtonsFilter != Controller.Buttons.None)
                {
                    for (uint n = 0; n < 8*sizeof(uint); n++)
                    {
                        var b = 1u << (int)n;
                        if (ControllerButtonsFilter.HasFlag((Controller.Buttons)b)) 
                            btn += n < 10 ?
                                (char)(n+'1'):
                                (char)(n+'A'-10);
                    }
                }
                return btn;
            }
        }
        public string IncreaseDirectionText
        {
            get
            {
                return
                    IncreaseDirection == Direction.Left ? " ←" :
                    IncreaseDirection == Direction.Push ? " ↑" :
                    IncreaseDirection == Direction.Right ? " →" : " ↓";
            }
        }
        public string IncreaseDirection2Text
        {
            get
            {
                return
                    DisableIncreaseDirection2 || IncreaseDirection2 == null ? "X" :
                    IncreaseDirection2 == Direction.Left ? " ←" :
                    IncreaseDirection2 == Direction.Push ? " ↑" :
                    IncreaseDirection2 == Direction.Right ? " →" : " ↓";
            }
        }
        public string Name { get { return SimVarName.Length > 0 ? SimVarName : VJoyAxisName; } }
        public bool IsTrim { get { return AxisForTrim.ContainsKey(Name); } }
        public string TrimmedAxisName { get { return AxisForTrim.ContainsKey(Name) ? AxisForTrim[Name] : "Not Available"; } }

        public string VJoyAxisName
        {
            get
            {
                if (VJoyId > 0)
                {
                    var axisName =
                        VJoyAxis == HID_USAGES.HID_USAGE_X   ? "L-Axis X" :
                        VJoyAxis == HID_USAGES.HID_USAGE_Y   ? "L-Axis Y" :
                        VJoyAxis == HID_USAGES.HID_USAGE_Z   ? "L-Axis Z" :
                        VJoyAxis == HID_USAGES.HID_USAGE_RX  ? "R-Axis X" :
                        VJoyAxis == HID_USAGES.HID_USAGE_RY  ? "R-Axis Y" :
                        VJoyAxis == HID_USAGES.HID_USAGE_RZ  ? "R-Axis Z" :
                        VJoyAxis == HID_USAGES.HID_USAGE_SL0 ? "Slider X" :
                        VJoyAxis == HID_USAGES.HID_USAGE_SL1 ? "Slider Y" :
                        "";
                    return $"vJoy {VJoyId} {axisName}";
                }
                else
                {
                    return "";
                } 
            }
        }
        public uint VJoyId { get; private set; }
        public HID_USAGES VJoyAxis { get; private set; }

        /// <summary>For smart axis features only (value sent to vJoy remains in range 0..32763)</summary>
        public uint VJoyAxisZero { get; private set; }
        public bool VJoyAxisIsThrottle { get; private set; }

        public string SimVarName 
        { 
            get { return _simVarName; } 
            private set 
            { 
                _simVarName = value; 
                IsThrottle = _simVarName.StartsWith("GENERAL ENG THROTTLE LEVER POSITION");
                foreach (var v in EngineSimVars)
                    if (v == _simVarName)
                        ForAllEngines = true;
            }
        }
        public string SimVarUnit { get; private set; }
        public double SimVarIncrement 
        { 
            get 
            {
                var u = SimVarUnit.ToLowerInvariant(); 
                return 
                    u == "number" || u == "bool" ? 1 :
                    SimVarScale / Settings.Default.ContinuousSimVarIncrements; 
            } 
        }
        public double SimVarMin
        {
            get { return _simVarMin; }
            private set
            {
                if (_simVarMin != value && value <= _simVarMax)
                {
                    _simVarMin = value;
                    NotifyPropertyChanged();
                    NotifyPropertyChanged("SimVarNegativeScaleString");
                    NotifyPropertyChanged("SimVarPositiveScaleString");
                }
            }
        }
        public double SimVarMax
        {
            get { return _simVarMax; }
            private set
            {
                if (_simVarMax != value && value >= _simVarMin)
                {
                    _simVarMax = value;
                    NotifyPropertyChanged();
                    NotifyPropertyChanged("SimVarNegativeScaleString");
                    NotifyPropertyChanged("SimVarPositiveScaleString");
                }
            }
        }
        public double SimVarScale { get { return _simVarMax - _simVarMin; } }
        public double SimVarNegativeScale       { get { var zero = VJoyAxisZero > 0 ? VJoyAxisZero : 0; return _simVarMin < zero ? _simVarMax < zero ? 1 : (zero-_simVarMin)/SimVarScale : 0; } }
        public double SimVarPositiveScale       { get { var zero = VJoyAxisZero > 0 ? VJoyAxisZero : 0; var positiveDetent = PositiveDetent > 0 ? PositiveDetent : _simVarMax; return (positiveDetent-zero)/SimVarScale; } }
        public double SimVarPositiveDetentScale { get { var zero = VJoyAxisZero > 0 ? VJoyAxisZero : 0; var positiveDetent = PositiveDetent > 0 ? PositiveDetent : _simVarMax; return _simVarMax > positiveDetent ? _simVarMin > positiveDetent ? 1 : (_simVarMax-positiveDetent)/SimVarScale : 0; } }
        public string SimVarNegativeScaleString       { get { return string.Format(CultureInfo.InvariantCulture, "{0:0.##}*", SimVarNegativeScale); } }
        public string SimVarPositiveScaleString       { get { return string.Format(CultureInfo.InvariantCulture, "{0:0.##}*", SimVarPositiveScale); } }
        public string SimVarPositiveDetentScaleString { get { return string.Format(CultureInfo.InvariantCulture, "{0:0.##}*", SimVarPositiveDetentScale); } }
        public Brush SimVarNegativeColor { get; private set; }
        public Brush SimVarPositiveColor { get; private set; }
        public Brush SimVarPositiveDetentColor { get; private set; }
        public double SimVarValue
        {
            get { return _simVarValue; }
            private set
            {
                _simVarValue = Math.Max(SimVarMin, Math.Min(SimVarMax, value));
            }
        }
        public double SimVarChange
        {
            get { return _simVarChange; }
            private set
            {
                var newValue = Math.Max(SimVarMin - SimVarValue, Math.Min(SimVarMax - SimVarValue, value));
                newValue = SimVarIncrement * (int)(newValue / SimVarIncrement);
                _simVarChange = newValue;
            }
        }

        public double Value
        {
            get { return _simVarValue + _simVarChange; }
        }

        public bool IsVisible { get { return _isAvailable && TriggerText != null && !_isHidden; } }
        public bool IsAvailable { get { return _isAvailable; } set { if (_isAvailable != value) { _isAvailable = value; NotifyPropertyChanged(); NotifyPropertyChanged("IsVisible"); } } }
        public bool IsHidden { get { return _isHidden; } set { if (_isHidden != value) { _isHidden = value; NotifyPropertyChanged(); NotifyPropertyChanged("IsVisible"); } } }
        public bool WaitButtonsReleased { get { return _waitButtonsReleased; } set { if (_waitButtonsReleased != value) { _waitButtonsReleased = value; NotifyPropertyChanged(); } } }
        public double Sensitivity { get { return _sensitivity; } set { if (_sensitivity != value) { _sensitivity = value; NotifyPropertyChanged(); } } }
        public bool SensitivityAtCruiseSpeed { get { return _sensitivityAtCruiseSpeed; } set { if (_sensitivityAtCruiseSpeed != value) { _sensitivityAtCruiseSpeed = value; NotifyPropertyChanged(); } } }
        public double SmartSensitivity 
        { 
            get 
            { 
                return SensitivityAtCruiseSpeed && DesignCruiseSpeedKnots > 0 ?
                    Sensitivity / Math.Max(0.5, // 0.5 Floor to keep some trim sensitivity at speeds < Vc/4
                        Math.Sqrt(IndicatedAirSpeedKnots / DesignCruiseSpeedKnots)) : // Sqrt to balance aerodynamic trim forces which grow with Velocity^2
                    Sensitivity; 
            } 
        }
        public bool TrimCounterCenteringMove { get { return _trimCounterCenteringMove; } set { if (_trimCounterCenteringMove != value) { _trimCounterCenteringMove = value; NotifyPropertyChanged(); } } }
        public bool DisableThrottleReverse { get { return _disableThrottleReverse; } set { if (_disableThrottleReverse != value) { _disableThrottleReverse = value; NotifyPropertyChanged(); } } }
        public double PositiveDetent { get { return _positiveDetentPercent; } set { if (_positiveDetentPercent != value) { _positiveDetentPercent = value; NotifyPropertyChanged(); } } }
        /// <summary>Last trimmed axis position in [-1..1] to compute moves centering to 0</summary>
        public double TrimmedAxis { get; set; }
        public Direction IncreaseDirection { get { return _increaseDirection; } set { if (_increaseDirection != value) { _increaseDirection = value; NotifyPropertyChanged(); NotifyPropertyChanged("IncreaseDirectionText"); NotifyPropertyChanged("IncreaseDirection2"); NotifyPropertyChanged("IncreaseDirection2Text"); NotifyPropertyChanged("Text"); } } }
        public Direction? IncreaseDirection2 
        { 
            get 
            {
                var decreaseDirection =
                    _increaseDirection == Direction.Draw ? Direction.Push :
                    _increaseDirection == Direction.Push ? Direction.Draw :
                    _increaseDirection == Direction.Left ? Direction.Right : Direction.Left;
                return
                    (_increaseDirection2 == _increaseDirection || _increaseDirection2 == decreaseDirection) ? null :
                    _increaseDirection2; 
            } 
            set 
            { 
                if (_increaseDirection2 != value) 
                { 
                    _increaseDirection2 = value; 
                    NotifyPropertyChanged(); 
                    NotifyPropertyChanged("IncreaseDirection2Text"); 
                } 
            } 
        }
        public bool DisableIncreaseDirection2 { get { return _disableIncreaseDirection2; } set { _disableIncreaseDirection2 = value; NotifyPropertyChanged(); NotifyPropertyChanged("IncreaseDirection2Text"); } }
        public double DecreaseScaleTimeSecs { get { return _decreaseScaleTimeSecs; } set { _decreaseScaleTimeSecs = value; NotifyPropertyChanged(); } }
        public bool IsThrottle { get; private set; }
        public bool ForAllEngines { get; private set; }
        /// <summary>A filter of mouse buttons down encoded as a combination of RAWMOUSE.RI_MOUSE</summary>
        
        public RAWMOUSE.RI_MOUSE MouseButtonsFilter { get { return _mouseButtonsFilter; } set { if (_mouseButtonsFilter != value) { _mouseButtonsFilter = value; NotifyPropertyChanged(); NotifyPropertyChanged("MouseButtonsText"); NotifyPropertyChanged("TriggerDeviceName"); NotifyPropertyChanged("TriggerText"); NotifyPropertyChanged("IsVisible"); NotifyPropertyChanged("Text"); } } }

        public Key KeyboardKeyDownFilter { get { return _keyboardKeyDownFilter; } set { if (_keyboardKeyDownFilter != value) { _keyboardKeyDownFilter = value; NotifyPropertyChanged(); NotifyPropertyChanged("KeyboardKeyText"); NotifyPropertyChanged("TriggerDeviceName"); NotifyPropertyChanged("TriggerText"); NotifyPropertyChanged("IsVisible"); NotifyPropertyChanged("Text"); } } }

        public Controller.Buttons ControllerButtonsFilter { get { return _controllerButtonsFilter; } set { if (_controllerButtonsFilter != value) { _controllerButtonsFilter = value; NotifyPropertyChanged(); NotifyPropertyChanged("ControllerButtonsText"); NotifyPropertyChanged("TriggerDeviceName"); NotifyPropertyChanged("TriggerText"); NotifyPropertyChanged("IsVisible"); NotifyPropertyChanged("Text"); } } }
        public ushort ControllerManufacturerId { get; set; }
        public ushort ControllerProductId { get; set; }

        // R/W properties

        public bool IsActive { get; set; }
        public double CurrentChange 
        {
            get { return _change; }
            set
            {
                _change = Math.Max((SimVarMin - SimVarValue) / SimVarScale, Math.Min((SimVarMax - SimVarValue) / SimVarScale, value));
            }
        }
        public Color ChangeColorForText
        {
            get { return _color; }
            set
            {
                if (_color != value)
                {
                    _color = value;
                    NotifyPropertyChanged();
                }
            }
        }

        // Methods

        // Events:
        // RawInput:
        //    buttons: Change+=lastOffset ; SimVarOffset=Change%SimVarIncrement
        //   !buttons: Change=0
        // SimConnect:
        //   SimVarValue = simValue+SimVarOffset ; SimVarOffset=0

        public string UpdateTrigger()
        {
            IsActive = false;
            if (MouseButtonsFilter != RAWMOUSE.RI_MOUSE.Reserved)
                IsActive = Mouse.Device.Buttons.HasFlag(MouseButtonsFilter);

            if (KeyboardKeyDownFilter != Key.None)
                IsActive |= Keyboard.IsKeyDown(KeyboardKeyDownFilter);

            bool hid = ControllerManufacturerId > 0 && ControllerProductId > 0;
            bool found = false;
            bool plugged = false;
            if (hid)
            {
                foreach (var c in Controller.Devices)
                {
                    if (c.ManufacturerId == ControllerManufacturerId && c.ProductId == ControllerProductId)
                    {
                        found = true;
                        if (c.DeviceId != Controller.DeviceUnplugged)
                        {
                            plugged = true;
                            IsActive |= c.ButtonsPressed.HasFlag(ControllerButtonsFilter);
                        }
                    }
                }
            }
            return
                !hid     ? null :
                !found   ? $"Controller mapped to {Name} not installed: {ControllerManufacturerId}/{ControllerProductId}" :
                !plugged ? $"Controller mapped to {Name} not plugged: {Controller.Get(ControllerManufacturerId, ControllerProductId)?.Name}" :
                null;
        }
        public void UpdateMove(Vector move)
        {
            // Ignore smallest of changes in XY directions to avoid changing 2 Axis at the same time and increase the effect of SmartIncreaseDirection
            if (Math.Abs(move.X) < Math.Abs(move.Y))
                move.X = 0;
            else
                move.Y = 0;

            var d = IncreaseDirection;
            double change = // between negative/positive detent(s)
                    d == Direction.Push ? -move.Y :
                    d == Direction.Draw ? move.Y :
                    d == Direction.Right ? move.X : -move.X;
            var d2 = IncreaseDirection2;
            if (change == 0 && d2 != null)
                change =
                    d2 == Direction.Push ? -move.Y :
                    d2 == Direction.Draw ? move.Y :
                    d2 == Direction.Right ? move.X : -move.X;

            var inDetent =
                (IsThrottle && Math.Abs(Value) < SimVarScale * Settings.Default.ReverseDetentWidthInPercent / 100) ||
                (VJoyAxisIsThrottle && VJoyAxisZero > 0 && Math.Abs(Value - VJoyAxisZero) < SimVarScale * Settings.Default.ReverseDetentWidthInPercent / 100) ||
                (PositiveDetent > 0 && Math.Abs(Value - PositiveDetent) < SimVarScale * Settings.Default.ReverseDetentWidthInPercent / 100);
            var negativeDetent =
                (IsThrottle && Value < 0) ||
                (VJoyAxisIsThrottle && VJoyAxisZero > 0 && Value < VJoyAxisZero);
            var positiveDetent =
                PositiveDetent > 0 && PositiveDetent < Value;
            var orthogonal = d == Direction.Push || d == Direction.Draw ? Direction.Right : Direction.Draw;
            
            if (inDetent && move.X != 0 && orthogonal == Direction.Right)
                change = -move.X;
            else if (inDetent && move.Y != 0 && orthogonal == Direction.Draw)
                change = -move.Y;
            else if (negativeDetent && change <= 0)
                change = orthogonal == Direction.Right ? -move.X : -move.Y;
            else if (positiveDetent && change >= 0)
                change = orthogonal == Direction.Right ? -move.X : -move.Y;

            if (Settings.Default.Sensitivity > 0)
            {
                CurrentChange += SmartSensitivity * change / (Settings.Default.Sensitivity * 100);
                SimVarChange = SimVarScale * CurrentChange;
                NotifyPropertyChanged("Value");
            }
        }
        public void UpdateTime(double intervalSecs)
        {
            var v = VJoyAxisZero > 0 ? Value - VJoyAxisZero : Value;
            if (!IsActive && DecreaseScaleTimeSecs > 0 && v != 0)
            {
                SimVarChange = -Math.Sign(v) * Math.Min(Math.Abs(v), SimVarScale * intervalSecs / DecreaseScaleTimeSecs);
                NotifyPropertyChanged("Value");
            }
        }

        /// <returns>True whenever valueInSim must be set to the updated SimVarValue</returns>
        public bool UpdateSimVarValue(double valueInSim, double trimmedAxisChange = 0)
        {
            if (valueInSim < SimVarMin-SimVarIncrement)
            {
                CurrentChange = SimVarChange = 0; // Mouse input may be defined by the user for [SimVarMin..SimVarMax] range on purpose
                return false;
            }
            if (valueInSim > SimVarMax+SimVarIncrement)
            {
                CurrentChange = SimVarChange = 0; // Mouse input may be defined by the user for [SimVarMin..SimVarMax] range on purpose
                return false;
            }
            bool changed = false;
            if (SimVarValue != valueInSim)
            {
                SimVarValue = valueInSim;
                changed = true;
            }
            if (SimVarChange != 0 && (!WaitButtonsReleased || CurrentChange == 0))
            {
                SimVarValue += SimVarChange;
                SimVarChange = 0;
                changed = true;
            }
            if (trimmedAxisChange != 0)
            {
                SimVarValue += SmartSensitivity * trimmedAxisChange;
                changed = true;
            }
            if (changed)
                NotifyPropertyChanged("Value");

            if (VJoyId > 0)
            {
                _vJoy?.SetAxis((int)SimVarValue, VJoyId, VJoyAxis);
                return false;
            }
            else
            {
                return SimVarValue != valueInSim;
            }
        }
        public void UpdateSimInfo(double simInfo)
        {
            if (SimVarName.StartsWith("GENERAL ENG THROTTLE LEVER POSITION") && simInfo < 0)
            {
                SimVarMin = simInfo;
                SimVarValue = SimVarValue; // in case it is not yet updated and out of bounds?
            }
            else if (SimVarName == "FLAPS HANDLE INDEX")
            {
                SimVarMax = simInfo;
                SimVarValue = SimVarValue; // in case it is not yet updated and out of bounds?
            }
        }

        // Events

        public event PropertyChangedEventHandler PropertyChanged;

        // Implementation
        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "") => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private double _change;
        private Color _color;
        private double _decreaseScaleTimeSecs;
        private bool _disableIncreaseDirection2;
        private bool _disableThrottleReverse;
        private double _positiveDetentPercent;
        private Direction _increaseDirection;
        private Direction? _increaseDirection2;
        private RAWMOUSE.RI_MOUSE _mouseButtonsFilter;
        private Key _keyboardKeyDownFilter;
        private Controller.Buttons _controllerButtonsFilter;
        private double _sensitivity;
        private bool _sensitivityAtCruiseSpeed;
        private string _simVarName;
        private double _simVarMin;
        private double _simVarMax;
        private double _simVarValue;
        private double _simVarChange;
        private bool _trimCounterCenteringMove;
        private bool _waitButtonsReleased;
        private bool _isHidden;
        private bool _isAvailable;

        private static Brush ReadColor(string[] scaleColors, uint i, string section, ref string errors)
        {
            if (scaleColors.Length > i && scaleColors[i] != "_")
            {
                try
                {
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString(scaleColors[i]));
                }
                catch (Exception e) { errors += section + "/" + i + ": " + e.Message + '\n'; }
            }
            return Brushes.Transparent;
        }
    }
}

