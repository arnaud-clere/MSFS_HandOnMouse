using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Input;

using winbase;
using winuser;

using HandOnMouse.Properties;

namespace HandOnMouse
{
    public class Axis : INotifyPropertyChanged
    {
        public enum Direction { Push, Draw, Left, Right };

        static public ObservableCollection<Axis> Mappings { get; private set; } = new ObservableCollection<Axis>();
        static public string MappingsRead(string filePath, string aircraftTitle)
        {
            if (!File.Exists(filePath))
            {
                return "File not found: " + filePath;
            }
            string[] sections = Kernel32.ReadIni(filePath);
            if (sections.Length == 0)
            {
                return "No mapping found: " + filePath;
            }
            Mappings.Clear();
            MappingsFilePath = filePath;
            var errors = "";
            foreach (var section in sections)
            {
                var m = new Axis();
                errors += m.Read(section, aircraftTitle);
                if (m.ExternalName.Length > 0)
                {
                    Mappings.Add(m);
                }
            }
            return errors;
        }
        static public string MappingsFilePath { get; private set; }
        static public uint EnginesCount { get; set; }
        static public double DesignCruiseSpeedKnots { get; set; }
        static public double IndicatedAirSpeedKnots { get; set; }

        static public readonly IReadOnlyList<string> EngineSimVars = new string[] {
            "GENERAL ENG THROTTLE LEVER POSITION",
            "GENERAL ENG MIXTURE LEVER POSITION",
            "GENERAL ENG PROPELLER LEVER POSITION",
            };
        static public readonly IReadOnlyDictionary<string, string> AxisForTrim = new Dictionary<string, string> {
            { "ELEVATOR TRIM POSITION", "ELEVATOR POSITION" },
            { "AILERON TRIM PCT"      ,  "AILERON POSITION" },
            { "RUDDER TRIM PCT"       ,   "RUDDER POSITION" },
            };

        // Instance members

        public string Read(string section, string aircraftTitle)
        {
            MappingName = section;
            var customFilePath = File.Exists(CustomFilePath(aircraftTitle)) ? CustomFilePath(aircraftTitle) : MappingsFilePath;
            var errors = "";
            try 
            {
                SimJoystickButtonFilter = (uint)Kernel32.ReadIni(AxisFilePath(), AxisName, "SimJoystickButtonFilter", 0u, ref errors);

                var space = new char[] { ' ' };
                var key = "VJoyAxis";
                var vJoyNameAndId = Kernel32.ReadIni(AxisFilePath(), AxisName, key).Split(space, StringSplitOptions.RemoveEmptyEntries);
                if (vJoyNameAndId.Length > 1)
                {
                    try
                    {
                        VJoyId = uint.Parse(vJoyNameAndId[1]);
                        if (VJoyId > 16)
                        {
                            errors += $"[{section}]{key}={string.Join(" ", vJoyNameAndId)} device {VJoyId} is not supported\r\n";
                            VJoyId = 0;
                        }
                    }
                    catch (Exception e)
                    {
                        errors += $"[{section}]{key}={string.Join(" ", vJoyNameAndId)} is invalid: {e.Message}\r\n";
                        VJoyId = 0;
                    }
                }
                else if (vJoyNameAndId.Length > 0)
                {
                    VJoyId = 1;
                    switch (vJoyNameAndId[0].Trim().ToUpperInvariant())
                    {
                        case "LX": VJoyAxis = HID_USAGES.HID_USAGE_X; break;
                        case "LY": VJoyAxis = HID_USAGES.HID_USAGE_Y; break;
                        case "LZ": VJoyAxis = HID_USAGES.HID_USAGE_Z; break;
                        case "RX": VJoyAxis = HID_USAGES.HID_USAGE_RX; break;
                        case "RY": VJoyAxis = HID_USAGES.HID_USAGE_RY; break;
                        case "RZ": VJoyAxis = HID_USAGES.HID_USAGE_RZ; break;
                        case "SLIDERX": VJoyAxis = HID_USAGES.HID_USAGE_SL0; break;
                        case "SLIDERY": VJoyAxis = HID_USAGES.HID_USAGE_SL1; break;
                        default:
                            errors += $"[{section}]{key}={string.Join(" ", vJoyNameAndId)} is invalid (use one of LX, LY, LZ, RX, RY, RZ, SLIDERX, SLIDERY)\r\n";
                            VJoyId = 0;
                            break;
                    }
                }
                if (VJoyId > 0)
                {
                    ValueUnit = "Increment";
                    ValueMin = 0;
                    ValueMax = 32767;
                    ValueZero = (uint)Kernel32.ReadIni(customFilePath, section, "VJoyAxisZero", 0u, ref errors);
                }
                else
                {
                    ValueZero = 0;
                    SimVarName = Kernel32.ReadIni(AxisFilePath(), AxisName, "SimVarName").Trim().ToUpperInvariant() + AxisSuffix;
                    ValueUnit = Kernel32.ReadIni(AxisFilePath(), AxisName, "SimVarUnit", "Percent").Trim();
                    if (ValueUnit.ToLowerInvariant() == "bool")
                    {
                        ValueMin = 0;
                        ValueMax = 1;
                    }
                    else
                    {
                        var min = (double)Kernel32.ReadIni(AxisFilePath(), AxisName, "SimVarMin", 0.0, ref errors);
                        var max = (double)Kernel32.ReadIni(AxisFilePath(), AxisName, "SimVarMax", 100.0, ref errors);
                        ValueMin = Math.Min(min, max);
                        ValueMax = Math.Max(min, max);
                    };
                    SimVarValue = Math.Max(0, ValueMin);
                    TrimCounterCenteringMove = 
                        (bool)Kernel32.ReadIni(customFilePath, section, "TrimCounterCenteringMove",
                        (bool)Kernel32.ReadIni(AxisFilePath(), AxisName, "TrimCounterCenteringMove", false, ref errors), ref errors);
                }

                var btn = RAWMOUSE.RI_MOUSE.None;
                var btnString = Kernel32.ReadIni(customFilePath, section, "MouseButtonsFilter",
                                Kernel32.ReadIni(AxisFilePath(), AxisName, "MouseButtonsFilter")).ToUpper().Split(new char[] { '-' });
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
                MouseButtonsFilter = btn == RAWMOUSE.RI_MOUSE.None ? RAWMOUSE.RI_MOUSE.Reserved : btn; // to avoid changing the axis with no button down

                ControllerManufacturerId = 0;
                ControllerProductId = 0;
                ControllerButtonsFilter = Controller.Buttons.None;
                key = "ControllerButtonsFilter";
                var controllerBtnString = Kernel32.ReadIni(customFilePath, section, key).Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (controllerBtnString.Length > 0)
                {
                    if (controllerBtnString.Length != 3)
                    {
                        errors += $"[{section}]{key}={string.Join("/", controllerBtnString)} requires 3 integers separated with '/'\r\n";
                    }
                    else
                        try
                        {
                            var mpi0 = ushort.Parse(controllerBtnString[0]);
                            var mpi1 = ushort.Parse(controllerBtnString[1]);
                            var mpi2 = uint.Parse(controllerBtnString[2]);
                            ControllerManufacturerId = mpi0;
                            ControllerProductId = mpi1;
                            ControllerButtonsFilter = (Controller.Buttons)(1u << (int)Math.Min(32u, mpi2) - 1);
                        }
                        catch (Exception e)
                        {
                            errors += $"[{section}]{key}={string.Join("/", controllerBtnString)} is invalid: {e.Message}\r\n";
                        }
                }

                KeyboardKeyDownFilter = (Key)Kernel32.ReadIni(customFilePath, section, "KeyboardKeyDownFilter", Key.None, ref errors);

                Sensitivity = Math.Max(1 / 100, Math.Min(100, 
                    (double)Kernel32.ReadIni(customFilePath, section, "Sensitivity",
                    (double)Kernel32.ReadIni(AxisFilePath(), AxisName, "Sensitivity", 1.0, ref errors), ref errors)));
                SensitivityAtCruiseSpeed = 
                    (bool)Kernel32.ReadIni(customFilePath, section, "SensitivityAtCruiseSpeed",
                    (bool)Kernel32.ReadIni(AxisFilePath(), AxisName, "SensitivityAtCruiseSpeed", false, ref errors), ref errors);
                AllowedExternalChangePerSec = Math.Max(0, Math.Min(20, (double)Kernel32.ReadIni(customFilePath, section, "AllowedExternalChangePerSec", IsThrottleSimVar ? 5.0 : 20.0, ref errors)));

                IncreaseDirection = (Direction)Enum.Parse(typeof(Direction), 
                    Kernel32.ReadIni(customFilePath, section, "IncreaseDirection",
                    Kernel32.ReadIni(AxisFilePath(), AxisName, "IncreaseDirection", "Push")).Trim(), true);
                key = "IncreaseDirection2";
                var direction = 
                    Kernel32.ReadIni(customFilePath, section, key,
                    Kernel32.ReadIni(AxisFilePath(), AxisName, key)).Trim();
                try
                {
                    if (direction != "")
                        IncreaseDirection2 = (Direction)Enum.Parse(typeof(Direction), direction, true);
                }
                catch (Exception e)
                {
                    errors += $"[{section}]{key}={direction} is invalid: {e.Message}\r\n";
                }
                WaitTriggerReleased = 
                    (bool)Kernel32.ReadIni(customFilePath, section, "WaitTriggerReleased",
                    (bool)Kernel32.ReadIni(AxisFilePath(), AxisName, "WaitTriggerReleased", false, ref errors), ref errors);
                DecreaseScaleTimeSecs = Math.Max(0, Math.Min(10, 
                    (double)Kernel32.ReadIni(customFilePath, section, "DecreaseScaleTimeSecs",
                    (double)Kernel32.ReadIni(AxisFilePath(), AxisName, "DecreaseScaleTimeSecs", 0.0, ref errors), ref errors)));

                NegativeDetent = 
                    (double)Kernel32.ReadIni(customFilePath, section, "NegativeDetent",
                    (double)Kernel32.ReadIni(AxisFilePath(), AxisName, "NegativeDetent", 100.0, ref errors), ref errors);
                PositiveDetent = 
                    (double)Kernel32.ReadIni(customFilePath, section, "PositiveDetent",
                    (double)Kernel32.ReadIni(AxisFilePath(), AxisName, "PositiveDetent", 100.0, ref errors), ref errors);

                var scaleColors = Kernel32.ReadIni(AxisFilePath(), AxisName, "MinNegativePositiveMaxScaleColors").Split(space, StringSplitOptions.RemoveEmptyEntries);
                MinScaleColor      = ReadColor(scaleColors, 0, $"{section}[MinNegativePositiveMaxScaleColors]", ref errors);
                NegativeScaleColor = ReadColor(scaleColors, 1, $"{section}[MinNegativePositiveMaxScaleColors]", ref errors);
                PositiveScaleColor = ReadColor(scaleColors, 2, $"{section}[MinNegativePositiveMaxScaleColors]", ref errors);
                MaxScaleColor      = ReadColor(scaleColors, 3, $"{section}[MinNegativePositiveMaxScaleColors]", ref errors);

                Description = Kernel32.ReadIni(AxisFilePath(), AxisName, "Description").Trim();

                IsEnabled = (bool)Kernel32.ReadIni(customFilePath, section, "IsEnabled", 
                            (bool)Kernel32.ReadIni(AxisFilePath(), AxisName, "IsEnabled", true, ref errors), ref errors);
                IsHidden  = (bool)Kernel32.ReadIni(customFilePath, section, "IsHidden",
                            (bool)Kernel32.ReadIni(AxisFilePath(), AxisName, "IsHidden", false, ref errors), ref errors);
            }
            catch (Exception e)
            {
                errors += $"[{MappingName}] error while reading {customFilePath}: {e.Message}\r\n";
            }
            return errors;
        }
        public string Save(string aircraftPattern = "")
        {
            aircraftPattern = aircraftPattern ?? "";
            if (Id < 0)
            {
                return $"Cannot save axis not correctly read from: {MappingsFilePath}";
            }
            var errors = "";
            var customFilePath = Path.ChangeExtension(MappingsFilePath, null)+ValidFileName($"_{MappingName}_{aircraftPattern}.ini");
            if (ExternalName.Length > 0)
            {
                try
                {
                    Kernel32.WriteIni(customFilePath, MappingName, "MouseButtonsFilter"          , MouseButtonsText ?? "");
                    Kernel32.WriteIni(customFilePath, MappingName, "ControllerButtonsFilter"     , ControllerButtonsText == null ? "" : $"{ControllerManufacturerId}/{ControllerProductId}/{ControllerButtonsText}");
                    Kernel32.WriteIni(customFilePath, MappingName, "KeyboardKeyDownFilter"       , KeyboardKeyDownFilter == Key.None ? "" : $"{KeyboardKeyDownFilter}");
                    Kernel32.WriteIni(customFilePath, MappingName, "TrimCounterCenteringMove"    , TrimCounterCenteringMove.ToString());
                    Kernel32.WriteIni(customFilePath, MappingName, "Sensitivity"                 , Sensitivity.ToString(CultureInfo.InvariantCulture));
                    Kernel32.WriteIni(customFilePath, MappingName, "SensitivityAtCruiseSpeed"    , SensitivityAtCruiseSpeed.ToString());
                    Kernel32.WriteIni(customFilePath, MappingName, "AllowedExternalChangePerSec" , AllowedExternalChangePerSec.ToString(CultureInfo.InvariantCulture));
                    Kernel32.WriteIni(customFilePath, MappingName, "IncreaseDirection"           , Enum.Format(typeof(Direction), IncreaseDirection, "G"));
                    Kernel32.WriteIni(customFilePath, MappingName, "IncreaseDirection2"          , IncreaseDirection2 == null ? "" : Enum.Format(typeof(Direction), IncreaseDirection2, "G"));
                    Kernel32.WriteIni(customFilePath, MappingName, "WaitTriggerReleased"         , WaitTriggerReleased.ToString());
                    Kernel32.WriteIni(customFilePath, MappingName, "DecreaseScaleTimeSecs"       , DecreaseScaleTimeSecs.ToString(CultureInfo.InvariantCulture));
                    Kernel32.WriteIni(customFilePath, MappingName, "NegativeDetent"              , NegativeDetent.ToString());
                    Kernel32.WriteIni(customFilePath, MappingName, "PositiveDetent"              , PositiveDetent.ToString());
                    Kernel32.WriteIni(customFilePath, MappingName, "IsEnabled"                   , IsEnabled.ToString());
                    Kernel32.WriteIni(customFilePath, MappingName, "IsHidden"                    , IsHidden.ToString());
                }
                catch (Exception e)
                {
                    errors += $"[{MappingName}]: error while saving {customFilePath}: {e.Message}\r\n";
                }
            }
            return errors;
        }
        public string Reset(string aircraftPattern)
        {
            aircraftPattern = ValidFileName(aircraftPattern ?? "");
            if (Id < 0)
            {
                return $"Cannot reset axis not correctly read from: {MappingsFilePath}";
            }
            var errors = "";
            try
            {
                var file = Path.GetFileName(MappingsFilePath);
                var dir = Path.GetDirectoryName(MappingsFilePath);
                var prefix = Path.ChangeExtension(file, null)+ValidFileName($"_{MappingName}_");
                foreach (var iniFile in new DirectoryInfo(dir).GetFiles(prefix + "*.ini"))
                {
                    if ((aircraftPattern=="" && prefix.Length == Path.ChangeExtension(iniFile.Name, null).Length) ||
                        (aircraftPattern!="" && prefix.Length <  Path.ChangeExtension(iniFile.Name, null).Length))
                        File.Delete(iniFile.FullName);
                }
                errors += Read(MappingName, aircraftPattern);
            }
            catch (Exception e)
            {
                errors += $"[{AxisName}]: error while deleting custom {MappingsFilePath}: {e.Message}\r\n";
            }
            return errors;
        }

        // Creation properties

        public int Id => Mappings.IndexOf(this);
        public string AxisName => MappingName.Split(':')[0].Trim();
        public string AxisSuffix => MappingName.Remove(0, AxisName.Length);
        public string MappingName { get; private set; }
        public string CustomFilePath(string aircraftPattern = null) => FindBestIniFileFor(aircraftPattern ?? MainWindow.SimAircraftTitle, Path.GetDirectoryName(MappingsFilePath), Path.GetFileName(MappingsFilePath));
        public string AxisFilePath(string aircraftPattern = null) => FindBestIniFileFor(aircraftPattern ?? MainWindow.SimAircraftTitle, Path.GetDirectoryName(MappingsFilePath));
        public string ValidFileName(string fileName) { foreach (var c in Path.GetInvalidFileNameChars()) { fileName = fileName.Replace(c, '!'); } return fileName; }

        // TODO Plane specific file paths
        private string FindBestIniFileFor(string aircraftPattern, string dir, string mappingsFile = "")
        {
            aircraftPattern = ValidFileName(aircraftPattern);
            Debug.Assert(Directory.Exists(dir));
            var found = "";
            var prefix = mappingsFile == "" ? $"_{ValidFileName(AxisName)}_" : Path.ChangeExtension(mappingsFile, null)+ValidFileName($"_{MappingName}_");
            foreach (var iniFile in new DirectoryInfo(dir).GetFiles(prefix+"*.ini"))
            {
                var suffix = Path.ChangeExtension(iniFile.Name.Remove(0, prefix.Length), null);
                if (aircraftPattern.Contains(suffix.Trim()) &&
                    found.Length < iniFile.Name.Length)
                    found = iniFile.Name;
            }
            return Path.Combine(dir, found.Length > 0 ? found : mappingsFile);
        }

// Configurable properties

public string Description { get; private set; }
        
        public uint VJoyId { get; private set; }
        public HID_USAGES VJoyAxis { get; private set; }
        /// <summary>For smart axis features only (value sent to vJoy remains in range 0..32763)</summary>
        public uint ValueZero { get; private set; }

        public string SimVarName
        {
            get => _simVarName;
            private set
            {
                _simVarName = value;
                IsThrottleSimVar = _simVarName.StartsWith("GENERAL ENG THROTTLE LEVER POSITION");
                foreach (var v in EngineSimVars)
                    if (v == _simVarName)
                        ForAllEngines = true;
            }
        }

        public ushort ControllerManufacturerId { get; set; }
        public ushort ControllerProductId { get; set; }

        public uint SimJoystickButtonFilter { get; private set; }

        public string ValueUnit { get; private set; } = "Percent";

        public Brush MinScaleColor { get; private set; }
        public Brush NegativeScaleColor { get; private set; }
        public Brush PositiveScaleColor { get; private set; }
        public Brush MaxScaleColor { get; private set; }

        // Dynamically modifiable properties

        public bool IsPersistentlyHidden { get => _isHidden; set { if (_isHidden != value) { IsHidden = value; Save(); } } }
        public bool IsPersistentlyEnabled { get => _isEnabled; set { if (_isEnabled != value) { IsEnabled = value; Save(); } } }
        public bool IsHidden { get => _isHidden; set { if (_isHidden != value) { _isHidden = value; NotifyPropertyChanged(); } } }
        public bool IsEnabled { get => _isEnabled; set { if (_isEnabled != value) { _isEnabled = value; NotifyPropertyChanged(); } } }
        public bool IsAvailable { get => _isAvailable; set { if (_isAvailable != value) { _isAvailable = value; NotifyPropertyChanged(); } } }

        /// <summary>A filter of mouse buttons down encoded as a combination of RAWMOUSE.RI_MOUSE</summary>
        public RAWMOUSE.RI_MOUSE MouseButtonsFilter { get => _mouseButtonsFilter; set { if (_mouseButtonsFilter != value) { _mouseButtonsFilter = value; NotifyPropertyChanged(); } } }
        public Key KeyboardKeyDownFilter { get => _keyboardKeyDownFilter; set { if (_keyboardKeyDownFilter != value) { _keyboardKeyDownFilter = value; NotifyPropertyChanged(); } } }
        public Controller.Buttons ControllerButtonsFilter { get => _controllerButtonsFilter; set { if (_controllerButtonsFilter != value) { _controllerButtonsFilter = value; NotifyPropertyChanged(); } } }
        
        public double AllowedExternalChangePerSec { get => _allowedExternalChangePerSec; set { _allowedExternalChangePerSec = Math.Max(0, Math.Min(20, value)); NotifyPropertyChanged(); } }
        public Direction IncreaseDirection { get => _increaseDirection; set { if (_increaseDirection != value) { _increaseDirection = value; NotifyPropertyChanged(); } } }
        public Direction? IncreaseDirection2
        {
            get
            {
                var decreaseDirection =
                    _increaseDirection == Direction.Draw ? Direction.Push :
                    _increaseDirection == Direction.Push ? Direction.Draw :
                    _increaseDirection == Direction.Left ? Direction.Right :
                    Direction.Left;
                return
                    (_increaseDirection2 == _increaseDirection || _increaseDirection2 == decreaseDirection) ? null : // not orthogonal to IncreaseDirection
                    _increaseDirection2;
            }
            set { if (_increaseDirection2 != value) { _increaseDirection2 = value; NotifyPropertyChanged(); } }
        }
        public double Sensitivity { get => _isEnabled ? _sensitivity : 0; set { if (_sensitivity != value) { _sensitivity = value; NotifyPropertyChanged(); } } }
        public bool SensitivityAtCruiseSpeed { get => _sensitivityAtCruiseSpeed; set { if (_sensitivityAtCruiseSpeed != value) { _sensitivityAtCruiseSpeed = value; NotifyPropertyChanged(); } } }
        public bool WaitTriggerReleased { get => _waitTriggerReleased; set { if (_waitTriggerReleased != value) { _waitTriggerReleased = value; NotifyPropertyChanged(); } } }
        public bool TrimCounterCenteringMove { get => _trimCounterCenteringMove; set { if (_trimCounterCenteringMove != value) { _trimCounterCenteringMove = value; NotifyPropertyChanged(); } } }
        public double NegativeDetent { get => _negativeDetentPercent; set { if (_negativeDetentPercent != value) { _negativeDetentPercent = value; NotifyPropertyChanged(); } } }
        public double PositiveDetent { get => _positiveDetentPercent; set { if (_positiveDetentPercent != value) { _positiveDetentPercent = value; NotifyPropertyChanged(); } } }
        public double DecreaseScaleTimeSecs { get => _decreaseScaleTimeSecs; set { _decreaseScaleTimeSecs = value; NotifyPropertyChanged(); } }

        public double ValueMin
        {
            get => _valueMin;
            private set
            {
                if (_valueMin != value)
                {
                    if (_valueMax < value)
                    {
                        _valueMax = value;
                        NotifyPropertyChanged(nameof(ValueMax));
                    }
                    _valueMin = value;
                    Debug.Assert(_valueMin <= _valueMax);
                    NotifyPropertyChanged();
                }
            }
        }
        public double ValueMax
        {
            get => _valueMax;
            private set
            {
                if (_valueMax != value)
                {
                    if (value < _valueMin)
                    {
                        _valueMin = value;
                        NotifyPropertyChanged(nameof(ValueMin));
                    }
                    _valueMax = value; 
                    Debug.Assert(_valueMin <= _valueMax);
                    NotifyPropertyChanged();
                }
            }
        }

        // Configurable Read only properties

        public string AxisText => Join(IsAvailable ? "" : "(N/A)", MappingName);
        public string AxisToolTip => Join(IsAvailable ? "" : "(N/A)", MappingName);
        public string ExternalName => SimVarName.Length > 0 ? SimVarName : VJoyAxisName;
        public string SimJoystickButtonText => SimJoystickButtonFilter < 0 ? null : SimJoystickButtonFilter.ToString();
        public bool IsTrim => AxisForTrim.ContainsKey(SimVarName);
        public string TrimmedAxisName => AxisForTrim.ContainsKey(SimVarName) ? AxisForTrim[SimVarName] : "Not Available";
        public string VJoyAxisName
        {
            get
            {
                if (VJoyId > 0)
                {
                    var axisName =
                        VJoyAxis == HID_USAGES.HID_USAGE_X ? "L-Axis X" :
                        VJoyAxis == HID_USAGES.HID_USAGE_Y ? "L-Axis Y" :
                        VJoyAxis == HID_USAGES.HID_USAGE_Z ? "L-Axis Z" :
                        VJoyAxis == HID_USAGES.HID_USAGE_RX ? "R-Axis X" :
                        VJoyAxis == HID_USAGES.HID_USAGE_RY ? "R-Axis Y" :
                        VJoyAxis == HID_USAGES.HID_USAGE_RZ ? "R-Axis Z" :
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

        /// <summary>Last trimmed axis position in [-1..1] to compute moves centering to 0</summary>
        public double TrimmedAxis { get; set; }
        public bool IsThrottleSimVar { get; private set; }
        public bool ForAllEngines { get; private set; }

        // Dynamically modifiable Read only properties

        public string Text => Join(InputText, AxisText);
        public string InputText
        {
            get
            {
                if (!IsEnabled)
                    return "";

                string s;
                if (TriggerText == null)
                {
                    s = "X";
                }
                else
                {
                    s = Join(TriggerText, IncreaseDirectionText+IncreaseDirection2Text);
                    if (TrimCounterCenteringMove) s += "+";
                    if (WaitTriggerReleased)      s = $"({s})";
                }
                return s;
            }
        }
        public string InputToolTip
        {
            get
            {
                var s = IsEnabled ? "" : "Unlock using the button on the left to enable the following mouse gesture:";
                var a = Join(AxisToolTip, Description.Length > 0 ? $"({Description})" : "");
                s = Join(s, $"To change {a}:", "\r\n");
                s = Join(s, !IsAvailable ? "(BEWARE axis is not available!)" : "", "\r\n");
                s = Join(s, VJoyAxisName.Length > 0 && !MainWindow.vJoyIsAvailable ? "(BEWARE vJoy is not available!)" : "", "\r\n");
                s += "\r\n";
                if (TriggerToolTip == null)
                {
                    s += "Define a trigger using the button on the right";
                }
                else
                {
                    s += $"1. PRESS {TriggerToolTip}\r\n";
                    s += $"2. MOVE mouse to {IncreaseDirectionText}{IncreaseDirection2Text} to increase";
                    if (TrimCounterCenteringMove) s += " or move joystick to center";
                    if (!WaitTriggerReleased)      s += " continuously";
                    // TODO explain detents
                    s += "\r\n3. RELEASE";
                    if (WaitTriggerReleased) s += " (MSFS will change now)";
                }
                return s;
            }
        }
        public string TriggerDeviceName =>
            MouseButtonsText != null ? "Mouse" :
            KeyboardKeyText != null ? "Keyboard" :
            ControllerButtonsText != null ? Controller.Get(ControllerManufacturerId, ControllerProductId)?.Name :
            "None";
        public string TriggerText => MouseButtonsText ?? KeyboardKeyText ?? ControllerButtonsText ?? SimJoystickButtonText;
        public string TriggerToolTip =>
            MouseButtonsToolTip ?? 
            (KeyboardKeyText != null ? KeyboardKeyText + " key" : null) ?? 
            (ControllerButtonsText != null ? TriggerDeviceName + " button(s) " + ControllerButtonsText : null) ??
            (SimJoystickButtonText != null ? $"joystick button {SimJoystickButtonText} (when MSFS flight is loaded)" : null);
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
        public string MouseButtonsToolTip
        {
            get
            {
                string btn = null;
                if (MouseButtonsFilter != RAWMOUSE.RI_MOUSE.Reserved)
                {
                    btn = "";
                    if (MouseButtonsFilter.HasFlag(RAWMOUSE.RI_MOUSE.LEFT_BUTTON_DOWN  )) btn = Join(btn, "Left"   , "+");
                    if (MouseButtonsFilter.HasFlag(RAWMOUSE.RI_MOUSE.MIDDLE_BUTTON_DOWN)) btn = Join(btn, "Middle" , "+");
                    if (MouseButtonsFilter.HasFlag(RAWMOUSE.RI_MOUSE.BUTTON_4_DOWN     )) btn = Join(btn, "Back"   , "+");
                    if (MouseButtonsFilter.HasFlag(RAWMOUSE.RI_MOUSE.RIGHT_BUTTON_DOWN )) btn = Join(btn, "Right"  , "+");
                    if (MouseButtonsFilter.HasFlag(RAWMOUSE.RI_MOUSE.BUTTON_5_DOWN     )) btn = Join(btn, "Forward", "+");
                    var btnUp = "";
                    if (MouseButtonsFilter.HasFlag(RAWMOUSE.RI_MOUSE.LEFT_BUTTON_UP  )) btnUp = Join(btnUp, "Left"   , "+");
                    if (MouseButtonsFilter.HasFlag(RAWMOUSE.RI_MOUSE.MIDDLE_BUTTON_UP)) btnUp = Join(btnUp, "Middle" , "+");
                    if (MouseButtonsFilter.HasFlag(RAWMOUSE.RI_MOUSE.RIGHT_BUTTON_UP )) btnUp = Join(btnUp, "Right"  , "+");
                    if (MouseButtonsFilter.HasFlag(RAWMOUSE.RI_MOUSE.BUTTON_4_UP     )) btnUp = Join(btnUp, "Back"   , "+");
                    if (MouseButtonsFilter.HasFlag(RAWMOUSE.RI_MOUSE.BUTTON_5_UP     )) btnUp = Join(btnUp, "Forward", "+");
                    if (btnUp.Length > 0) btn += " without " + btnUp;
                    btn += " mouse button" + (btnUp.Length > 0 ? "s" : "");
                }
                return btn;
            }
        }
        public string KeyboardKeyText => KeyboardKeyDownFilter != Key.None ? KeyboardKeyDownFilter.ToString() : null;
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
        public string IncreaseDirectionText => DirectionText(IncreaseDirection);
        public string IncreaseDirection2Text => DirectionText(IncreaseDirection2);
        public Direction DetentDirection => IncreaseDirection2 ?? (_increaseDirection == Direction.Draw || _increaseDirection == Direction.Push ? Direction.Right : Direction.Push);
        public string DetentDirectionText => DirectionText(DetentDirection);
        public double ValueScale => ValueMax > ValueMin ? ValueMax - ValueMin : 1; // to avoid division by 0
        public double ValueIncrement
        {
            get
            {
                var u = ValueUnit.ToLowerInvariant();
                return
                    u == "number" || u == "bool" ? 1 :
                    ValueScale / Math.Max(1, Settings.Default.ContinuousValueIncrements); // to avoid division by 0
            }
        }
        public double MinScale => (ValueMin*NegativeDetent/100 - ValueMin) / ValueScale;
        public double NegativeScale => (ValueZero - ValueMin*NegativeDetent/100) / ValueScale;
        public double PositiveScale => (ValueMax*PositiveDetent/100 - ValueZero) / ValueScale;
        public double MaxScale => (ValueMax - ValueMax*PositiveDetent/100) / ValueScale;
        public string MinScaleString => string.Format(CultureInfo.InvariantCulture, "{0:0.##}*", MinScale);
        public string NegativeScaleString => string.Format(CultureInfo.InvariantCulture, "{0:0.##}*", NegativeScale);
        public string PositiveScaleString => string.Format(CultureInfo.InvariantCulture, "{0:0.##}*", PositiveScale);
        public string MaxScaleString => string.Format(CultureInfo.InvariantCulture, "{0:0.##}*", MaxScale);

        public bool IsVisible => _isAvailable && TriggerText != null && !_isHidden;
        public Color ChangeColorForText { get => _color; set { if (_color != value) { _color = value; NotifyPropertyChanged(); } } }
        public double SmartSensitivity() =>
            SensitivityAtCruiseSpeed && DesignCruiseSpeedKnots > 0 ?
                Sensitivity / Math.Max(0.5, // 0.5 Floor to keep some trim sensitivity at speeds < Vc/4
                    Math.Sqrt(IndicatedAirSpeedKnots / DesignCruiseSpeedKnots)) : // Sqrt to balance aerodynamic trim forces which grow with Velocity^2
                Sensitivity;

        // Updateable properties

        public bool IsActive { get => _isActive; set { if (_isActive != value) { _isActive = value; NotifyPropertyChanged(); } } }
        public double InputChange
        {
            get => Math.Max((ValueMin - SimVarValue) / ValueScale, Math.Min((ValueMax - SimVarValue) / ValueScale, Valid(_inputChange))); // for changes in ValueMin..ValueMax
            private set {
                value = Math.Max((ValueMin - SimVarValue) / ValueScale, Math.Min((ValueMax - SimVarValue) / ValueScale, Valid(value)));
                if (InputChange != value) { _inputChange = value; NotifyPropertyChanged(); } 
            }
        }
        public double SimVarChange
        {
            get => Math.Max(ValueMin, Math.Min(ValueMax, _simVarChange + SimVarValue)) - SimVarValue; // for changes in ValueMin..ValueMax
            private set { 
                value = Math.Max(ValueMin, Math.Min(ValueMax, Valid(value) + SimVarValue)) - SimVarValue;
                value = (int)(value / ValueIncrement) * ValueIncrement;
                if (SimVarChange != value) { _simVarChange = value; NotifyPropertyChanged(); } 
            } 
        }
        public double SimVarValue
        {
            get => Math.Max(ValueMin, Math.Min(ValueMax, _simVarValue)); // for changes in ValueMin..ValueMax
            private set { value = Math.Max(ValueMin, Math.Min(ValueMax, Valid(value))); if (SimVarValue != value) { _simVarValue = value; NotifyPropertyChanged(); } }
        }
        public double Value => SimVarValue + SimVarChange;

        // Update Methods

        /// <seealso cref="https://www.plantuml.com/plantuml/uml/"/>
        //! @startuml
        //! Mouse -> HOM : Mouse_Move\n(trigger, move)
        //! HOM -> Sim : RequestData(...,SIM_FRAME)
        //! HOM <- Timer : SimFrameTimer_Tick\n(decrease)
        //! alt AllowedExternalChange > 0 && _connected && ...
        //! HOM -> Sim : RequestData(...,ONCE)
        //! ...
        //! Mouse -> HOM : Mouse_Move
        //! Sim -> HOM : SimConnect_OnRecvData\n(newSimValue)
        //! end
        //! rnote over HOM
        //! homChange = UpdateSimVarValue(simValue, extChange,...)
        //! endrnote
        //! alt homChange<>0
        //! HOM -> Sim : SimConnect.SetDataOnSimObject(simValue+homChange)
        //! end
        //! Mouse -> HOM : Mouse_Move
        //! HOM <- Timer : SimFrameTimer_Tick
        //! Mouse -> HOM : Mouse_Move
        //! @enduml

        public string UpdateTrigger()
        {
            if (!IsAvailable || !IsEnabled) return null;

            var isActive = false;
            if (MouseButtonsFilter != RAWMOUSE.RI_MOUSE.Reserved)
                isActive = Mouse.Device.Buttons.HasFlag(MouseButtonsFilter);

            if (KeyboardKeyDownFilter != Key.None)
                isActive |= Keyboard.IsKeyDown(KeyboardKeyDownFilter);

            if (SimJoystickButtonFilter > 0)
                isActive |= MainWindow.SimJoystickButtons.HasFlag((Controller.Buttons)(1u << (int)(SimJoystickButtonFilter-1)));

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
                            isActive |= c.ButtonsPressed.HasFlag(ControllerButtonsFilter);
                        }
                    }
                }
            }
            IsActive = isActive;
            return
                !hid     ? null :
                !found   ? $"Controller mapped to {MappingName} not installed: {ControllerManufacturerId}/{ControllerProductId}" :
                !plugged ? $"Controller mapped to {MappingName} not plugged: {Controller.Get(ControllerManufacturerId, ControllerProductId)?.Name}" :
                null;
        }
        public string UpdateMove(Vector move)
        {
            if (!IsAvailable || !IsEnabled) return null;

            var errors = UpdateTrigger();
            if (errors == null)
            {
                if (IsActive)
                {
                    // Ignore smallest of changes in XY directions to avoid changing 2 Axis at the same time and increase the effect of DetentDirection
                    if (Math.Abs(move.X) < Math.Abs(move.Y))
                        move.X = 0;
                    else
                        move.Y = 0;

                    double change = ChangeIn(IncreaseDirection, move);
                    if (NegativeDetent == 100 && PositiveDetent == 100)
                    {
                        var d2 = IncreaseDirection2;
                        if (change == 0 && d2 != null)
                            change = ChangeIn(d2.Value, move);
                    }
                    else
                    {
                        var closeToNegativeDetent = NegativeDetent < 100 && 
                            (Math.Abs(Value-ValueZero - (ValueMin-ValueZero)*NegativeDetent/100) 
                            < ValueScale * Settings.Default.DetentWidthInPercent / 100);
                        var closeToPositiveDetent = PositiveDetent < 100 && 
                            (Math.Abs(Value-ValueZero - (ValueMax-ValueZero)*PositiveDetent/100) 
                            < ValueScale * Settings.Default.DetentWidthInPercent / 100);
                        var belowNegativeDetent = (Value-ValueZero) < ((ValueMin-ValueZero)*NegativeDetent/100);
                        var abovePositiveDetent = (Value-ValueZero) > ((ValueMax-ValueZero)*PositiveDetent/100);

                        if (belowNegativeDetent)
                            change /= Settings.Default.NegativeRangeFriction;
                        else if (abovePositiveDetent)
                            change /= Settings.Default.MaxRangeFriction;
                        else if (closeToNegativeDetent || closeToPositiveDetent)
                            change /= Settings.Default.DetentFriction;

                        var detentChange = ChangeIn(DetentDirection, move) / Settings.Default.DetentFriction;
                        if (belowNegativeDetent && change == 0) // allow +/- change or ...
                            change = -detentChange;
                        else if (closeToNegativeDetent && change <= 0) // allow + change or ...
                            change = -detentChange;

                        if (closeToPositiveDetent && change <= 0) // allow + change or ...
                            change = detentChange;
                        else if (abovePositiveDetent && change == 0) // allow +/- change or ...
                            change = detentChange;
                    }

                    if (Settings.Default.Sensitivity > 0)
                    {
                        InputChange += change * SmartSensitivity() / (Settings.Default.Sensitivity * 100);
                        SimVarChange = ValueScale * InputChange;
                        NotifyPropertyChanged(nameof(Value));
                    }
                }
                if (WaitTriggerReleased)
                {
                    if (!IsActive) // InputChange end
                    {
                        InputChange = 0;
                        // Keeping the remainder for the current change would mysteriously:
                        // - increase a subsequent move in the opposite direction after even a long time
                        // - decrease a subsequent move in the same direction to potentially insignificant moves
                        if (SimVarChange != 0)
                            UpdateSimVarValue();
                    }
                }
                else // !WaitTriggerReleased
                {
                    if (SimVarChange != 0) // InputChange end
                    {
                        InputChange = 0;
                        // Since SimVarChange is proportional to InputChange modulo SimVarIncrement
                        UpdateSimVarValue();
                    }
                }
                ChangeColorForText = SimVarChange != 0 && InputChange == 0 ? Colors.Red : Axis.TextColorFromChange(InputChange);
            }
            return errors;
        }
        public void UpdateTime(double intervalSecs)
        {
            if (!IsAvailable || !IsEnabled) return;

            var v = ValueZero > 0 ? Value - ValueZero : Value;
            if (!IsActive && DecreaseScaleTimeSecs > 0 && v != 0)
            {
                SimVarChange = -Math.Sign(v) * Math.Min(Math.Abs(v), ValueScale * intervalSecs / DecreaseScaleTimeSecs);
                NotifyPropertyChanged(nameof(Value));
            }
            if (SimVarChange != 0)
            {
                UpdateSimVarValue();
            }
        }
        public void UpdateSimVarValue(double externalChange = 0, double trimmedAxisChange = 0)
        {
            if (!IsAvailable) return;

            if (trimmedAxisChange != 0)
            {
                TrimmedAxis -= trimmedAxisChange;
                UpdateTrigger();
                trimmedAxisChange = IsActive ? ValueScale * trimmedAxisChange / (1 - -1) /* position scale */ : 0;
            }

            var valueInSim = SimVarValue + externalChange;
            if (valueInSim < -ValueIncrement+ValueMin || ValueMax+ValueIncrement < valueInSim)
            {
                InputChange = SimVarChange = 0; // to allow user to restrict HandOnMouse action to [ValueMin..ValueMax] range on purpose
            }
            else
            {
                var lastSimVarValue = SimVarValue;
                var lastUpdateElapsedSecs = _lastUpdate.Elapsed.TotalSeconds;
                if (externalChange != 0)
                    _lastUpdate.Restart();

                // HandOnMouse changes
                if (SimVarChange != 0 && (!WaitTriggerReleased || InputChange == 0))
                {
                    SimVarValue += SimVarChange;
                    SimVarChange = 0;
                }
                if (trimmedAxisChange != 0)
                {
                    SimVarValue += SmartSensitivity() * trimmedAxisChange;
                }
                if (externalChange != 0)
                {
                    SimVarValue += externalChange * Math.Min(1, AllowedExternalChangePerSec * Math.Min(0.1, lastUpdateElapsedSecs)); // in case MSFS would not update SimVar during a pause or configuration
                }
                if (Math.Abs(lastSimVarValue - SimVarValue) >= ValueIncrement)
                    NotifyPropertyChanged(nameof(Value));

                if (Math.Abs(SimVarValue - valueInSim) >= ValueIncrement)
                    NotifyPropertyChanged(nameof(SimVarValue));
            }
        }
        public void UpdateThrottleLowerLimit(double simInfo)
        {
            Debug.Assert(simInfo < 0);
            if (SimVarName.StartsWith("GENERAL ENG THROTTLE LEVER POSITION"))
            {
                ValueMin = simInfo;
                SimVarValue = SimVarValue; // in case it is not yet updated and out of bounds?
            }
        }
        public void UpdateElevatorTrimMinValue(double simInfo)
        {
            if (SimVarName == "ELEVATOR TRIM POSITION")
            {
                if (simInfo > 0)
                {
                    simInfo *= -1;
                }
                if (ValueUnit == "Radians")
                {
                    simInfo *= Math.PI / 180; // Degrees
                }
                ValueMin = simInfo;
                SimVarValue = SimVarValue; // in case it is not yet updated and out of bounds?
            }
        }
        public void UpdateElevatorTrimMaxValue(double simInfo)
        {
            if (SimVarName == "ELEVATOR TRIM POSITION")
            {
                if (ValueUnit == "Radians")
                {
                    simInfo *= Math.PI / 180; // Degrees
                }
                ValueMax = simInfo;
                SimVarValue = SimVarValue; // in case it is not yet updated and out of bounds?
            }
        }
        public void UpdateFlapsNumHandlePosition(uint simInfo)
        {
            Debug.Assert(simInfo > 0);
            if (SimVarName == "FLAPS HANDLE INDEX")
            {
                ValueMax = simInfo;
                SimVarValue = SimVarValue; // in case it is not yet updated and out of bounds?
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        // Implementation

        private void NotifyPropertyChanges(params string[] names) { foreach (var name in names) NotifyPropertyChanged(name); }
        private void NotifyPropertyChanged([CallerMemberName] string name = "")
        {
            Debug.WriteIf(name != nameof(Value), $"{name} ");
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            // When PropertyChanged for property below:       | NotifyPropertyChanges for all direct dependent properties below (except those that are also indirect):
            // -----------------------------------------------|-----------------------------------------------------------------------------------------------------------------------------------------
            // AxisText and AxisToolTip are not updated after creation
            // Value and SimVarValue are notified by Update...
            /**/ if (name == nameof(ControllerButtonsFilter))   NotifyPropertyChanges(nameof(ControllerButtonsText), nameof(TriggerDeviceName), nameof(TriggerText), nameof(TriggerToolTip));
            else if (name == nameof(InputText))                 NotifyPropertyChanged(nameof(Text));
            else if (name == nameof(IncreaseDirection))         NotifyPropertyChanges(nameof(IncreaseDirectionText), nameof(IncreaseDirection2), nameof(InputToolTip));
            else if (name == nameof(IncreaseDirection2))        NotifyPropertyChanges(nameof(IncreaseDirection2Text), nameof(InputText), nameof(DetentDirection), nameof(DetentDirectionText));
            else if (name == nameof(IsAvailable))               NotifyPropertyChanges(nameof(IsVisible), nameof(AxisText), nameof(AxisToolTip), nameof(InputToolTip));
            else if (name == nameof(IsEnabled))                 NotifyPropertyChanges(nameof(IsPersistentlyEnabled), nameof(Sensitivity), nameof(InputText), nameof(InputToolTip));
            else if (name == nameof(IsHidden))                  NotifyPropertyChanges(nameof(IsPersistentlyHidden), nameof(IsVisible));
            else if (name == nameof(KeyboardKeyDownFilter))     NotifyPropertyChanges(nameof(KeyboardKeyText), nameof(TriggerDeviceName), nameof(TriggerText), nameof(TriggerToolTip));
            else if (name == nameof(MouseButtonsFilter))        NotifyPropertyChanges(nameof(MouseButtonsText), nameof(MouseButtonsToolTip), nameof(TriggerDeviceName), nameof(TriggerText), nameof(TriggerToolTip));
            else if (name == nameof(NegativeDetent))            NotifyPropertyChanges(nameof(NegativeScale), nameof(NegativeScaleString), nameof(MinScale), nameof(MinScaleString));
            else if (name == nameof(PositiveDetent))            NotifyPropertyChanges(nameof(PositiveScale), nameof(PositiveScaleString), nameof(MaxScale), nameof(MaxScaleString));
            else if (name == nameof(Sensitivity))               NotifyPropertyChanged(nameof(IncreaseDirection2Text));
            else if (name == nameof(TriggerText))               NotifyPropertyChanges(nameof(InputText), nameof(IsVisible));
            else if (name == nameof(TriggerToolTip))            NotifyPropertyChanges(nameof(InputToolTip));
            else if (name == nameof(WaitTriggerReleased))       NotifyPropertyChanges(nameof(InputText), nameof(InputToolTip));
            else if (name == nameof(TrimCounterCenteringMove))  NotifyPropertyChanges(nameof(InputText), nameof(InputToolTip));
            else if (name == nameof(ValueMin))                  NotifyPropertyChanges(nameof(ValueScale), nameof(ValueIncrement), nameof(NegativeScale), nameof(NegativeScaleString), nameof(MinScale), nameof(MinScaleString)); // Do not notify SimVarValue, SimVarChange, Value
            else if (name == nameof(ValueMax))                  NotifyPropertyChanges(nameof(ValueScale), nameof(ValueIncrement), nameof(PositiveScale), nameof(PositiveScaleString), nameof(MaxScale), nameof(MaxScaleString)); // Do not notify SimVarValue, SimVarChange, Value
            //else if (name == nameof(SimVarValue))               // Do not notify SimVarChange, Value
            // BEWARE of cycles in notifications of dependent properties!
        }

        private Stopwatch   _lastUpdate = new Stopwatch();
        private double      _inputChange;
        private double      _allowedExternalChangePerSec;
        private Color       _color = Colors.Black;
        private double      _decreaseScaleTimeSecs;
        private double      _negativeDetentPercent;
        private double      _positiveDetentPercent;
        private Direction   _increaseDirection = Direction.Push;
        private Direction?  _increaseDirection2;
        private RAWMOUSE.RI_MOUSE _mouseButtonsFilter = RAWMOUSE.RI_MOUSE.Reserved;
        private Key         _keyboardKeyDownFilter;
        private Controller.Buttons _controllerButtonsFilter;
        private bool    _isEnabled = true;
        private bool    _isActive;
        private double  _sensitivity;
        private bool    _sensitivityAtCruiseSpeed;
        private string  _simVarName = "";
        private double  _valueMin = 0;
        private double  _valueMax = 100;
        private double  _simVarValue = 0;
        private double  _simVarChange;
        private bool    _trimCounterCenteringMove;
        private bool    _waitTriggerReleased;
        private bool    _isHidden;
        private bool    _isAvailable = true;

        static private double ChangeIn(Direction increaseDirection, Vector move)
        {
            return
                increaseDirection == Direction.Push ? -move.Y :
                increaseDirection == Direction.Draw ? move.Y :
                increaseDirection == Direction.Right ? move.X :
                -move.X;
        }
        static private string DirectionText(Direction? d, string nullText = "")
        {
            return d == null ? nullText :
                d == Direction.Left ? "←" :
                d == Direction.Push ? "↑" :
                d == Direction.Right ? "→" :
                "↓";
        }
        static private Brush ReadColor(string[] scaleColors, uint i, string section, ref string errors)
        {
            if (scaleColors.Length > i && scaleColors[i] != "_")
            {
                try
                {
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString(scaleColors[i]));
                }
                catch (Exception e) 
                {
                    errors += $"{section}={string.Join(" ", scaleColors)} is invalid: {e.Message}\r\n";
                }
            }
            return Brushes.LightCyan;
        }
        static private Color TextColorFromChange(double normalizedChange)
        {
            var maxChangeColor = Colors.DarkOrange;
            var change = Math.Min(1, Math.Abs(normalizedChange) * 3); // max if change > max(normalizedChange)/3
            return Color.FromRgb(
                (byte)(maxChangeColor.R * change),
                (byte)(maxChangeColor.G * change),
                (byte)(maxChangeColor.B * change));
        }
        static private string Join(string a, string b, string sep = " ")
        {
            Debug.Assert(a != null && b != null);
            return
                a +
                (a.Length > 0 && b.Length > 0 ? sep : "") +
                b;
        }
        static private double Valid(double d, double valid = 0) => double.IsNaN(d) ? valid : d;
    }
}
