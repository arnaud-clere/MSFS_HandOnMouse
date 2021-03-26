using System;
using System.Runtime.InteropServices;
using System.Text;

/// <see cref="https://docs.microsoft.com/en-us/windows/win32/api/winbase/"/>
namespace winbase
{
    static public class Kernel32
    {
        /// <see cref="https://docs.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getprivateprofilestring"/>
        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        static extern int GetPrivateProfileString(string section, string key, string defaultValue, StringBuilder value, int maxLength, string filePath);

        static public string ReadIni(string filePath, string key, string section = null, string defaultValue = "", int maxLength = 255)
        {
            var value = new StringBuilder(maxLength);
            var read = GetPrivateProfileString(section, key, defaultValue, value, maxLength, filePath);
            return value.ToString();
        }
    }
}

/// <see cref="https://docs.microsoft.com/en-us/windows/win32/api/winuser/"/>
namespace winuser
{
    /// <see cref=""/>
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

