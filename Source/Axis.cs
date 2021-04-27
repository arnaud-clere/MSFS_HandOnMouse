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
                return "File not found";
            }
            Mappings.Clear();
            var errors = "";
            var stop = false;
            for (int i = 0; !stop; i++)
            {
                var section = $"Axis{i + 1}";
                var m = new Axis();
                try
                {
                    var btn = RAWMOUSE.RI_MOUSE.None;
                    var btnString = Kernel32.ReadIni(filePath, "MouseButtonsFilter", section).ToUpper().Split(new char[] { '-' });
                    if (btnString[0].Contains("L")) btn |= RAWMOUSE.RI_MOUSE.LEFT_BUTTON_DOWN;
                    if (btnString[0].Contains("M")) btn |= RAWMOUSE.RI_MOUSE.MIDDLE_BUTTON_DOWN;
                    if (btnString[0].Contains("R")) btn |= RAWMOUSE.RI_MOUSE.RIGHT_BUTTON_DOWN;
                    if (btnString[0].Contains("B")) btn |= RAWMOUSE.RI_MOUSE.BUTTON_4_DOWN;
                    if (btnString[0].Contains("F")) btn |= RAWMOUSE.RI_MOUSE.BUTTON_5_DOWN;
                    if (btnString.Length > 1)
                    {
                        if (btnString[1].Contains("L")) btn |= RAWMOUSE.RI_MOUSE.LEFT_BUTTON_UP;
                        if (btnString[1].Contains("M")) btn |= RAWMOUSE.RI_MOUSE.MIDDLE_BUTTON_UP;
                        if (btnString[1].Contains("R")) btn |= RAWMOUSE.RI_MOUSE.RIGHT_BUTTON_UP;
                        if (btnString[1].Contains("B")) btn |= RAWMOUSE.RI_MOUSE.BUTTON_4_UP;
                        if (btnString[1].Contains("F")) btn |= RAWMOUSE.RI_MOUSE.BUTTON_5_UP;
                    }
                    m.ButtonsFilter = btn == RAWMOUSE.RI_MOUSE.None ? RAWMOUSE.RI_MOUSE.Reserved : btn; // to avoid changing the axis with no button down

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
                            default: m.VJoyId = 0; break;
                        }
                    }
                    if (m.VJoyId > 0)
                    {
                        m.SimVarMin = 0;
                        m.SimVarMax = 32767;
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
                        m.TrimCounterCenteringMove = bool.Parse(
                            Kernel32.ReadIni(filePath, "TrimCounterCenteringMove", section, "False").Trim());
                        m.DisableThrottleReverse = bool.Parse(
                            Kernel32.ReadIni(filePath, "DisableThrottleReverse", section, "False").Trim());
                    }
                    m.Sensitivity = Math.Max(1 / 100, Math.Min(100, double.Parse(
                        Kernel32.ReadIni(filePath, "Sensitivity", section, "1"), NumberStyles.Float, CultureInfo.InvariantCulture)));
                    m.SensitivityAtCruiseSpeed = bool.Parse(
                        Kernel32.ReadIni(filePath, "SensitivityAtCruiseSpeed", section, "False").Trim());
                    var directions = Kernel32.ReadIni(filePath, "IncreaseDirection", section, "Push").Split(" ".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
                    m.IncreaseDirection = (Direction)Enum.Parse(typeof(Direction), directions[0].Trim(), true);
                    if (directions.Length > 1)
                    {
                        try
                        {
                            m.IncreaseDirection2 = (Direction)Enum.Parse(typeof(Direction), directions[1].Trim(), true);
                            var decreaseDirection =
                                m.IncreaseDirection == Direction.Draw ? Direction.Push :
                                m.IncreaseDirection == Direction.Push ? Direction.Draw :
                                m.IncreaseDirection == Direction.Left ? Direction.Right : Direction.Left;
                            if (m.IncreaseDirection2 == m.IncreaseDirection || m.IncreaseDirection2 == decreaseDirection)
                                m.IncreaseDirection2 = null;
                        }
                        catch (Exception e)
                        {
                            errors += section + ": " + e.Message + '\n';
                        }
                    }
                    m.DecreaseScaleTimeSecs = Math.Max(0, Math.Min(10, double.Parse(
                        Kernel32.ReadIni(filePath, "DecreaseScaleTimeSecs", section, "0"), NumberStyles.Float, CultureInfo.InvariantCulture)));
                    m.WaitButtonsReleased = bool.Parse(
                        Kernel32.ReadIni(filePath, "WaitButtonsReleased", section, "False").Trim());
                    var scaleColors = Kernel32.ReadIni(filePath, "SimVarNegativePositiveColors", section, "").Split(" ".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
                    m.HasSimVarNegativeColor = false;
                    m.HasSimVarPositiveColor = false;
                    m.SimVarNegativeColor = Brushes.Red;
                    m.SimVarPositiveColor = Brushes.White;
                    if (scaleColors.Length > 0)
                    {
                        m.SimVarNegativeColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString(scaleColors[0]));
                        m.HasSimVarNegativeColor = true;
                    }
                    if (scaleColors.Length > 1)
                    {
                        m.SimVarPositiveColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString(scaleColors[1]));
                        m.HasSimVarPositiveColor = true;
                    }
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
        static public void MappingsUpdate(int i, double inSimValue)
        {
            var m = Axis.Mappings[i];
            if (m.SimVarName.StartsWith("GENERAL ENG THROTTLE LEVER POSITION") && inSimValue < 0)
            {
                m.SimVarMin = inSimValue;
                m.SimVarValue = m.SimVarValue; // in case it is not yet updated and out of bounds?
            }
            if (m.SimVarName == "FLAPS HANDLE INDEX")
            {
                m.SimVarMax = inSimValue;
                m.SimVarValue = m.SimVarValue; // in case it is not yet updated and out of bounds?
            }
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
            { "AILERON TRIM PCT"      , "AILERON POSITION"  },
            { "RUDDER TRIM PCT"       , "RUDDER POSITION"   },
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
            ButtonsFilter = RAWMOUSE.RI_MOUSE.Reserved;
            TrimmedAxis = double.NaN;
        }

        // R/O properties

        public string Text
        {
            get
            {
                var btn = "";
                if (ButtonsFilter != RAWMOUSE.RI_MOUSE.Reserved)
                {
                    if (ButtonsFilter.HasFlag(RAWMOUSE.RI_MOUSE.LEFT_BUTTON_DOWN  )) btn += "L";
                    if (ButtonsFilter.HasFlag(RAWMOUSE.RI_MOUSE.MIDDLE_BUTTON_DOWN)) btn += "M";
                    if (ButtonsFilter.HasFlag(RAWMOUSE.RI_MOUSE.RIGHT_BUTTON_DOWN )) btn += "R";
                    if (ButtonsFilter.HasFlag(RAWMOUSE.RI_MOUSE.BUTTON_4_DOWN     )) btn += "B";
                    if (ButtonsFilter.HasFlag(RAWMOUSE.RI_MOUSE.BUTTON_5_DOWN     )) btn += "F";
                    var btnUp = "";
                    if (ButtonsFilter.HasFlag(RAWMOUSE.RI_MOUSE.LEFT_BUTTON_UP  )) btnUp += "L";
                    if (ButtonsFilter.HasFlag(RAWMOUSE.RI_MOUSE.MIDDLE_BUTTON_UP)) btnUp += "M";
                    if (ButtonsFilter.HasFlag(RAWMOUSE.RI_MOUSE.RIGHT_BUTTON_UP )) btnUp += "R";
                    if (ButtonsFilter.HasFlag(RAWMOUSE.RI_MOUSE.BUTTON_4_UP     )) btnUp += "B";
                    if (ButtonsFilter.HasFlag(RAWMOUSE.RI_MOUSE.BUTTON_5_UP     )) btnUp += "F";
                    if (btnUp.Length > 0) btn += "-" + btnUp;
                    btn +=
                        IncreaseDirection == Direction.Left ? " ←" :
                        IncreaseDirection == Direction.Push ? " ↑" :
                        IncreaseDirection == Direction.Right ? " →" : " ↓";
                    if (TrimCounterCenteringMove) btn += "+";
                }
                else
                {
                    btn = "X";
                }
                if (WaitButtonsReleased)
                {
                    btn = "(" + btn + ")";
                }
                var sim = Name.Length > 0 ? Name.Replace("GENERAL ", "").Replace(" PCT", "").Replace(" POSITION", "").ToLowerInvariant() : "-";
                return btn + " " + sim;
            }
        }
        public string Name { get { return SimVarName.Length > 0 ? SimVarName : VJoyAxisName; } }

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
        public double SimVarNegativeScale { get { return _simVarMin < 0 ? _simVarMax < 0 ? 1 : (0-_simVarMin)/SimVarScale : 0; } }
        public double SimVarPositiveScale { get { return _simVarMax > 0 ? _simVarMin > 0 ? 1 : (_simVarMax-0)/SimVarScale : 0; } }
        public string SimVarNegativeScaleString { get { return string.Format(CultureInfo.InvariantCulture, "{0:0.##}*", SimVarNegativeScale); } }
        public string SimVarPositiveScaleString { get { return string.Format(CultureInfo.InvariantCulture, "{0:0.##}*", SimVarPositiveScale); } }
        public Brush SimVarNegativeColor { get; private set; }
        public Brush SimVarPositiveColor { get; private set; }
        public bool HasSimVarNegativeColor { get; private set; }
        public bool HasSimVarPositiveColor { get; private set; }
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
        public bool WaitButtonsReleased { get; private set; }
        public double Sensitivity { get; private set; }
        public bool SensitivityAtCruiseSpeed { get; private set; }
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
        public bool TrimCounterCenteringMove { get; private set; }
        public bool DisableThrottleReverse { get; private set; }
        /// <summary>Last trimmed axis position in [-1..1] to compute moves centering to 0</summary>
        public double TrimmedAxis { get; set; }
        public Direction IncreaseDirection { get; private set; }
        public Direction? IncreaseDirection2 { get; private set; }
        public double DecreaseScaleTimeSecs { get; private set; }
        public bool IsThrottle { get; private set; }
        public bool ForAllEngines { get; private set; }
        /// <summary>A filter of mouse buttons down encoded as a combination of RAWMOUSE.RI_MOUSE</summary>
        public RAWMOUSE.RI_MOUSE ButtonsFilter { get; private set; }

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

        public void UpdateRawInputChanges(double rawScale, double lastRawOffset)
        {
            if (rawScale > 0)
            {
                CurrentChange += SmartSensitivity * lastRawOffset / (rawScale * 100);
                SimVarChange = SimVarScale * CurrentChange;
                NotifyPropertyChanged("Value");
            }
        }
        public void UpdateTimerChanges(double intervalSecs)
        {
            if (!IsActive && DecreaseScaleTimeSecs > 0 && Value != 0)
            {
                SimVarChange = -Math.Sign(Value) * Math.Min(Math.Abs(Value), SimVarScale * intervalSecs / DecreaseScaleTimeSecs);
                NotifyPropertyChanged("Value");
            }
        }

        /// <returns>True whenever valueInSim must be set to the updated SimVarValue</returns>
        public bool UpdateSimVar(double valueInSim, double trimmedAxisChange = 0)
        {
            if (valueInSim < SimVarMin)
            {
                CurrentChange = SimVarChange = 0; // Mouse input may be defined by the user for [SimVarMin..SimVarMax] range on purpose
                return false;
            }
            if (valueInSim > SimVarMax)
            {
                CurrentChange = SimVarChange = 0; // Mouse input may be defined by the user for [SimVarMin..SimVarMax] range on purpose
                return false;
            }

            if (SimVarValue != valueInSim)
            {
                SimVarValue = valueInSim;
                NotifyPropertyChanged("Value");
            }
            if (SimVarChange != 0 && (!WaitButtonsReleased || CurrentChange == 0))
            {
                SimVarValue += SimVarChange;
                SimVarChange = 0;
                NotifyPropertyChanged("Value");
            }
            if (trimmedAxisChange != 0)
            {
                SimVarValue += SmartSensitivity * trimmedAxisChange;
                NotifyPropertyChanged("Value");
            }

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

        // Events

        public event PropertyChangedEventHandler PropertyChanged;

        // Implementation
        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "") => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private string _simVarName;
        private double _simVarMin;
        private double _simVarMax;
        private double _simVarValue;
        private double _simVarChange;
        private double _change;
        private Color _color;
    }
}

