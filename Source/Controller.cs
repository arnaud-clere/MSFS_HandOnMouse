using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

using Microsoft.Win32;

using joystickapi;

namespace HandOnMouse
{
    public class Controller
    {
        [Flags]
        public enum Buttons : uint
        {
            None     = 0u,
            Button1  = 1u << 0,
            Button2  = 1u << 1,
            Button3  = 1u << 2,
            Button4  = 1u << 3,
            Button5  = 1u << 4,
            Button6  = 1u << 5,
            Button8  = 1u << 6,
            Button9  = 1u << 8,
            Button10 = 1u << 9,
            Button11 = 1u << 10,
            Button12 = 1u << 11,
            Button13 = 1u << 12,
            Button14 = 1u << 13,
            Button15 = 1u << 14,
            Button16 = 1u << 15,
            Button18 = 1u << 16,
            Button19 = 1u << 18,
            Button20 = 1u << 19,
            Button21 = 1u << 20,
            Button22 = 1u << 21,
            Button23 = 1u << 22,
            Button24 = 1u << 23,
            Button25 = 1u << 24,
            Button26 = 1u << 25,
            Button28 = 1u << 26,
            Button29 = 1u << 28,
            Button30 = 1u << 29,
            Button31 = 1u << 30,
            Button32 = 1u << 31,
        }
        
        static public ObservableCollection<Controller> Devices { get; private set; } = new ObservableCollection<Controller>();

        static public Controller Get(ushort manufacturerId, ushort productId)
        {
            foreach (Controller found in Devices)
                if (found.ManufacturerId == manufacturerId && found.ProductId == productId)
                    return found;

            UpdateDevices();
            foreach (Controller found in Devices)
                if (found.ManufacturerId == manufacturerId && found.ProductId == productId)
                    return found;

            return null;
        }

        public const uint DeviceUnplugged = uint.MaxValue;
        
        public uint DeviceId { get; private set; }
        public ushort ManufacturerId { get; private set; }
        public ushort ProductId { get; private set; }
        public Buttons ButtonsAvailable { get; private set; }
        public string OemName { get; private set; }

        public Buttons ButtonsPressed 
        { 
            get
            {
                var info = new JOYINFOEX();
                info.Size = (uint)Marshal.SizeOf(info);
                info.Flags = JOYINFOEX.JOY.RETURNBUTTONS;
                var joystickInfo = WinMM.joyGetPosEx(DeviceId, out info);
                if (joystickInfo == MMRESULT.MMSYSERR_NOERROR)
                {
                    return (Buttons)info.Buttons & ButtonsAvailable;
                }
                if (joystickInfo == MMRESULT.MMSYSERR_BADDEVICEID ||
                    joystickInfo == MMRESULT.JOYERR_UNPLUGGED)
                {
                    DeviceId = DeviceUnplugged;
                }
                return Buttons.None;
            }
        }

        public string Name 
        { 
            get
            {
                if (_name != null) return _name;

                _name = $"{ManufacturerId}/{ProductId}";
                try
                {
                    var topKey = Registry.LocalMachine;
                    var deviceKeyPath = $"System\\CurrentControlSet\\Control\\MediaResources\\Joystick\\{_registryKeyName}\\CurrentJoystickSettings";
                    var deviceKey = topKey.OpenSubKey(deviceKeyPath);
                    if (deviceKey == null)
                    {
                        topKey = Registry.CurrentUser;
                        deviceKey = topKey.OpenSubKey(deviceKeyPath);
                    }
                    var deviceOemKeyName = deviceKey.GetValue($"Joystick{DeviceId + 1}OEMName");
                    var oemKeyPath = $"System\\CurrentControlSet\\Control\\MediaProperties\\PrivateProperties\\Joystick\\OEM\\{deviceOemKeyName}";
                    _name = (string)topKey.OpenSubKey(oemKeyPath).GetValue("OEMName", "");
                }
                catch (Exception ex) { Trace.WriteLine($"{ex.Message} at: {ex.StackTrace}"); }
                return _name;
            }
        }
        // Implementation

        string _registryKeyName;
        string _name;

        static public void UpdateDevices()
        {
            var maxDevices = WinMM.joyGetNumDevs();
            for (uint j = 0; j < maxDevices; j++)
            {
                var caps = new JOYCAPS();
                var size = (uint)Marshal.SizeOf(caps);
                var joystickCaps = WinMM.joyGetDevCaps(j, out caps, size);
                if (joystickCaps == MMRESULT.MMSYSERR_NOERROR)
                {
                    Controller c = null;
                    foreach (Controller found in Devices)
                        if (found.DeviceId == j)
                            c = found;

                    if (c == null)
                    {
                        c = new Controller
                        {
                            DeviceId = j,
                        };
                        Devices.Add(c);
                    }
                    if (!(c.ManufacturerId == caps.Mid && c.ProductId == caps.Pid))
                    {
                        c.ButtonsAvailable = (Buttons)(caps.NumButtons == 32 ? 0xFFFF_FFFFu : (1u << (int)caps.NumButtons) - 1u);
                        c._registryKeyName = caps.RegKey;
                        Trace.WriteLine($"HID #{j} changed from {c.ManufacturerId}/{c.ProductId} to {caps.Mid}/{caps.Pid} with {caps.NumButtons} buttons ({c.Name})");
                        c.ManufacturerId = caps.Mid; // vJoy 4660
                        c.ProductId = caps.Pid; // vJoy 48813
                    }
                }
                else if (joystickCaps != MMRESULT.JOYERR_PARMS) { Trace.WriteLine($"WinMM.joyGetDevCaps({j}, out caps, {size}) / {maxDevices} returned: {joystickCaps}"); }
            }
        }
    }
}
