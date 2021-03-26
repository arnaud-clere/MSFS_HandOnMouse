﻿using HandOnMouse.Properties;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using winbase;
using winuser;

namespace HandOnMouse
{
    public class Axis : INotifyPropertyChanged
    {
        public enum Direction { Push, Draw, Left, Right };

        static public ObservableCollection<Axis> Mappings { get; private set; } = new ObservableCollection<Axis>();
        static public string Read(string filePath)
        {
            if (!Path.IsPathRooted(filePath))
            {
                filePath = Path.Combine(Directory.GetCurrentDirectory(), "Mappings", filePath);
            }
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
                    m.SimVarName = Kernel32.ReadIni(filePath, "SimVarName", section);
                    m.SimVarUnit = Kernel32.ReadIni(filePath, "SimVarUnit", section, "Percent").Trim();
                    var min = double.Parse(Kernel32.ReadIni(filePath, "SimVarMin", section, "0"), NumberStyles.Float, CultureInfo.InvariantCulture);
                    var max = double.Parse(Kernel32.ReadIni(filePath, "SimVarMax", section, "100"), NumberStyles.Float, CultureInfo.InvariantCulture);
                    m.SimVarMax = Math.Max(min, max);
                    m.SimVarMin = Math.Min(min, max);
                    m.SimVarValue = Math.Max(0, m.SimVarMin);
                    m.Sensitivity = Math.Max(1 / 100, Math.Min(100, double.Parse(
                        Kernel32.ReadIni(filePath, "Sensitivity", section, "1"), NumberStyles.Float, CultureInfo.InvariantCulture)));
                    m.IncreaseDirection = (Direction)Enum.Parse(typeof(Direction),
                        Kernel32.ReadIni(filePath, "IncreaseDirection", section, "Push").Trim(), true);
                    m.WaitButtonsReleased = bool.Parse(
                        Kernel32.ReadIni(filePath, "WaitButtonsReleased", section, "False").Trim());
                }
                catch (Exception e)
                {
                    errors += section + ": " + e.Message + '\n';
                }
                finally
                {
                    if (m.SimVarName.Length > 0)
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
        static public Color TextColorFromChange(double normalizedChange)
        {
            var maxChangeColor = Colors.DarkOrange; 
            var change = Math.Min(1, Math.Abs(normalizedChange) * 3); // max if change > max(normalizedChange)/3
            return Color.FromRgb(
                (byte)(maxChangeColor.R * change),
                (byte)(maxChangeColor.G * change),
                (byte)(maxChangeColor.B * change));
        }

        public Axis()
        {
            SimVarName = "";
            SimVarUnit = "Percent";
            SimVarMin = 0;
            SimVarMax = 100;
            SimVarValue = Math.Max(0, SimVarMin);
            ChangeColorForText = Colors.Black;
            IncreaseDirection = Direction.Push;
            ButtonsFilter = RAWMOUSE.RI_MOUSE.Reserved;
            CurrentChange = 0;
            SimVarChange = 0;
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
                    btn += btnUp.Length > 0 ? "-" + btnUp : "";
                    btn +=
                        IncreaseDirection == Direction.Left ? " ←" :
                        IncreaseDirection == Direction.Push ? " ↑" :
                        IncreaseDirection == Direction.Right ? " →" : " ↓";
                }
                else
                {
                    btn = "X";
                }
                if (WaitButtonsReleased)
                {
                    btn = "(" + btn + ")";
                }
                var sim = SimVarName.Length > 0 ? SimVarName.Replace("GENERAL ", "").Replace(" PCT", "").Replace(" POSITION", "").ToLower() : "-";
                return btn + " " + sim;
            }
        }

        public string SimVarName
        {
            get { return _simVarName; }
            private set
            {
                _simVarName = value.Trim().ToUpper();
                NotifyPropertyChanged();
                NotifyPropertyChanged("Text");
            }
        }
        public string SimVarUnit
        {
            get { return _simVarUnit; }
            private set
            {
                if (_simVarUnit != value)
                {
                    _simVarUnit = value;
                    NotifyPropertyChanged();
                }
            }
        }
        public double SimVarIncrement 
        { 
            get 
            {
                var u = SimVarUnit.ToLower(); 
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
                }
            }
        }
        public double SimVarScale { get { return _simVarMax - _simVarMin; } }
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
        public Direction IncreaseDirection
        {
            get { return _increaseDirection; }
            private set
            {
                if (_increaseDirection != value)
                {
                    _increaseDirection = value;
                    NotifyPropertyChanged();
                    NotifyPropertyChanged("Text");
                }
            }
        }
        /// <summary>A filter of mouse buttons down encoded as a combination of RAWMOUSE.RI_MOUSE</summary>
        public RAWMOUSE.RI_MOUSE ButtonsFilter
        {
            get { return _buttonsFilter; }
            private set
            {
                if (_buttonsFilter != value)
                {
                    _buttonsFilter = value;
                    NotifyPropertyChanged();
                    NotifyPropertyChanged("Text");
                }
            }
        }

        // R/W properties

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

        public void UpdateChanges(double rawScale, double lastRawOffset)
        {
            if (rawScale > 0)
            {
                CurrentChange += Sensitivity * lastRawOffset / (rawScale * 100);
                SimVarChange = SimVarScale * CurrentChange;
                NotifyPropertyChanged("Value");
            }
        }
        public void UpdateSimVar(double valueInSim)
        {
            if (SimVarValue != valueInSim)
            {
                SimVarValue = valueInSim;
                NotifyPropertyChanged("Value");
            }
            if (SimVarChange != 0 && (!WaitButtonsReleased || CurrentChange == 0))
            {
                SimVarValue += SimVarChange;
                SimVarChange = 0;
            }
        }

        // Events

        public event PropertyChangedEventHandler PropertyChanged;

        // Implementation
        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "") => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private string _simVarName;
        private string _simVarUnit;
        private double _simVarMin;
        private double _simVarMax;
        private double _simVarValue;
        private double _simVarChange;
        private double _change;
        private Color _color;
        
        private Direction _increaseDirection;
        private RAWMOUSE.RI_MOUSE _buttonsFilter;
    }
}
