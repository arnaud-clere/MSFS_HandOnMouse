using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

/// <see cref="https://docs.microsoft.com/en-us/windows/win32/api/joystickapi/"/>
namespace joystickapi
{
        public enum MMRESULT : UInt32
        {
            MMSYSERR_NOERROR = 0,
            MMSYSERR_ERROR = 1,
            MMSYSERR_BADDEVICEID = 2,
            MMSYSERR_NOTENABLED = 3,
            MMSYSERR_ALLOCATED = 4,
            MMSYSERR_INVALHANDLE = 5,
            MMSYSERR_NODRIVER = 6,
            MMSYSERR_NOMEM = 7,
            MMSYSERR_NOTSUPPORTED = 8,
            MMSYSERR_BADERRNUM = 9,
            MMSYSERR_INVALFLAG = 10,
            MMSYSERR_INVALPARAM = 11,
            MMSYSERR_HANDLEBUSY = 12,
            MMSYSERR_INVALIDALIAS = 13,
            MMSYSERR_BADDB = 14,
            MMSYSERR_KEYNOTFOUND = 15,
            MMSYSERR_READERROR = 16,
            MMSYSERR_WRITEERROR = 17,
            MMSYSERR_DELETEERROR = 18,
            MMSYSERR_VALNOTFOUND = 19,
            MMSYSERR_NODRIVERCB = 20,

            JOYERR_UNPLUGGED = 167,
            JOYERR_PARMS = 165,
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct JOYINFOEX
        {
            [Flags()]
            public enum JOY : UInt32
            {
                RETURNX		    = 0x00000001,
                RETURNY		    = 0x00000002,
                RETURNZ		    = 0x00000004,
                RETURNR		    = 0x00000008,
                RETURNU		    = 0x00000010,
                RETURNV		    = 0x00000020,
                RETURNPOV		= 0x00000040,
                RETURNBUTTONS	= 0x00000080,
                RETURNCENTERED	= 0x00000400,
                RETURNALL		= RETURNX | RETURNY | RETURNZ | RETURNR | RETURNU | RETURNV | RETURNPOV | RETURNBUTTONS,
            }

            /// <summary>Must be set to Marshal.Sizeof(JOYINFOEX)</summary>
            public UInt32 Size;
            /// <summary>Must be set to desired values</summary>
            public JOY Flags;
            public UInt32 Xpos;
            public UInt32 Ypos;
            public UInt32 Zpos;
            public UInt32 Rpos;
            public UInt32 Upos;
            public UInt32 Vpos;
            public UInt32 Buttons;
            public UInt32 ButtonNumber;
            public UInt32 POV;
            public UInt32 Reserved1;
            public UInt32 Reserved2;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct JOYCAPS
        {
            public UInt16 Mid;
            public UInt16 Pid;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string Pname;
            public UInt32 Xmin;
            public UInt32 Xmax;
            public UInt32 Ymin;
            public UInt32 Ymax;
            public UInt32 Zmin;
            public UInt32 Zmax;
            public UInt32 NumButtons;
            public UInt32 PeriodMin;
            public UInt32 PeriodMax;
            public UInt32 Rmin;
            public UInt32 Rmax;
            public UInt32 Umin;
            public UInt32 Umax;
            public UInt32 Vmin;
            public UInt32 Vmax;
            public UInt32 Caps;
            public UInt32 MaxAxes;
            public UInt32 NumAxes;
            public UInt32 MaxButtons;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string RegKey;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string OEMVxD;
        }

    static public class WinMM
    {
        /// <see cref="https://docs.microsoft.com/en-us/windows/win32/api/joystickapi/nf-joystickapi-joygetnumdevs"/>
        [DllImport("WinMM.dll")]
        public static extern UInt32 joyGetNumDevs();

        /// <see cref="https://docs.microsoft.com/en-us/windows/win32/api/joystickapi/nf-joystickapi-joygetposex"/>
        [DllImport("WinMM.dll", CharSet = CharSet.Unicode)]
        public static extern MMRESULT joyGetPosEx(UInt32 id, [Out] out JOYINFOEX info);
    
        /// <see cref="https://docs.microsoft.com/en-us/windows/win32/api/joystickapi/nf-joystickapi-joygetdevcaps"/>
        [DllImport("WinMM.dll", CharSet = CharSet.Unicode)]
        public static extern MMRESULT joyGetDevCaps(UInt32 id, [Out] out JOYCAPS caps, UInt32 capsBytes);
    }
}

/// <see cref="https://docs.microsoft.com/en-us/windows/win32/api/winbase/"/>
namespace winbase
{
    static public class Kernel32
    {
        /// <see cref="https://docs.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getprivateprofilestring"/>
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern int GetPrivateProfileString(string section, string key, string defaultValue, StringBuilder value, int maxLength, string filePath);

        static public string ReadIni(string filePath, string key, string section = null, string defaultValue = "", int maxLength = 255)
        {
            var value = new StringBuilder(maxLength);
            var read = GetPrivateProfileString(section, key, defaultValue, value, maxLength, filePath);
            Debug.Assert(read == value.Length);
            return value.ToString();
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool WritePrivateProfileString(string section, string key, string value, string filePath);
        
        static public bool WriteIni(string filePath, string key, string section, string value)
        {
            return WritePrivateProfileString(section, key, value, filePath);
        }
    }
}

/// <see cref="https://docs.microsoft.com/en-us/windows/win32/api/winuser/"/>
namespace winuser
{
    public enum RID : uint
    {
        HEADER = 0x10000005,
        INPUT  = 0x10000003,
    }

    /// <see cref="https://docs.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-rawinput"/>
    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUT
    {
        public RAWINPUTHEADER header;
        public RAWMOUSE mouse; // Embed in a struct to allow the header size to align correctly for 32/64 bit
    }

    /// <see cref="https://docs.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-rawinputheader"/>
    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTHEADER
    {
        public enum RIM : int
        {
            TYPEMOUSE       = 0,
            TYPEKEYBOARD    = 1,
            TYPEHID         = 2,
        }

        public RIM      Type;   
        public uint     Size;   
        public IntPtr   hDevice;
        public IntPtr   wParam; 

        public override string ToString()
        {
            return string.Format("RawInputHeader\n dwType : {0}\n dwSize : {1}\n hDevice : {2}\n wParam : {3}", Type, Size, hDevice, wParam);
        }
    }

    /// <see cref="https://docs.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-rawmouse"/>
    [StructLayout(LayoutKind.Explicit)]
    public struct RAWMOUSE
    {
        [Flags()]
        public enum MOUSE : ushort
        {
            MOVE_RELATIVE = 0,
        }

        [Flags()]
        public enum RI_MOUSE : ushort
        {
            None = 0,
            Reserved            = 0xF000,
            LEFT_BUTTON_DOWN    = 0x0001,
            LEFT_BUTTON_UP      = 0x0002,
            RIGHT_BUTTON_DOWN   = 0x0004,
            RIGHT_BUTTON_UP     = 0x0008,
            MIDDLE_BUTTON_DOWN  = 0x0010,
            MIDDLE_BUTTON_UP    = 0x0020,
            BUTTON_4_DOWN       = 0x0040,
            BUTTON_4_UP         = 0x0080,
            BUTTON_5_DOWN       = 0x0100,
            BUTTON_5_UP         = 0x0200,
            WHEEL               = 0x0400,
            HWHEEL              = 0x0800,
        }

        [FieldOffset(0)]
        public MOUSE Flags;
        [FieldOffset(4)]
        public uint Buttons;
        [FieldOffset(4)]
        public RI_MOUSE ButtonFlags;
        [FieldOffset(6)]
        public ushort ButtonData;
        [FieldOffset(8)]
        public uint RawButtons;
        [FieldOffset(12)]
        public int LastX;
        [FieldOffset(16)]
        public int LastY;
        [FieldOffset(20)]
        public uint ExtraInformation;
    }

    /// <see cref="https://docs.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-rawinputdevice"/>
    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTDEVICE
    {
        /// <see cref="https://docs.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-rawinputdevice"/>
        [Flags]
        public enum RIDEV
        {
            None = 0,
            REMOVE = 0x00000001,
            EXCLUDE = 0x00000010,
            PAGEONLY = 0x00000020,
            NOLEGACY = 0x00000030,
            INPUTSINK = 0x00000100,
            CAPTUREMOUSE = 0x00000200,
            NOHOTKEYS = 0x00000200,
            APPKEYS = 0x00000400,
            EXINPUTSINK = 0x00001000,
            DEVNOTIFY = 0x00002000
        }

        public HID_USAGE_PAGE UsagePage;
        public HID_USAGE Usage;
        public RIDEV Flags;
        public IntPtr Target;

        public override string ToString()
        {
            return string.Format("{0}/{1}, flags: {2}, target: {3}", UsagePage, Usage, Flags, Target);
        }
    }

    /// <see cref="https://docs.microsoft.com/en-us/windows-hardware/drivers/hid/hid-usages#usage-page"/>
    public enum HID_USAGE_PAGE : ushort
    {
        Undefined = 0x00,   // Unknown usage page
        GENERIC = 0x01,     // Generic desktop controls
    }

    /// <see cref="https://docs.microsoft.com/en-us/windows-hardware/drivers/hid/hid-usages#usage-id"/>
    public enum HID_USAGE : ushort
    {
        Undefined = 0x00,
        MOUSE = 0x02,
    }

    /// <see cref=""/>
    public enum WM : int
    {
        INPUT = 0x00FF,
        USER = 0x0400,
    }

    static public class User32
    {
        /// <see cref="https://docs.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getrawinputdata"/>
        [DllImport("User32.dll", SetLastError = true)]
        internal static extern int GetRawInputData(IntPtr hRawInput, RID command, [Out] out RAWINPUT buffer, [In, Out] ref int size, int cbSizeHeader);

        /// <see cref="https://docs.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getrawinputdata"/>
        [DllImport("User32.dll", SetLastError = true)]
        internal static extern int GetRawInputData(IntPtr hRawInput, RID command, [Out] IntPtr pData, [In, Out] ref int size, int sizeHeader);

        /// <see cref="https://docs.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerrawinputdevices"/>
        [DllImport("User32.dll", SetLastError = true)]
        internal static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] pRawInputDevice, uint numberDevices, uint size);
    }
}

