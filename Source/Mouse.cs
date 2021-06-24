using System;

using winuser;

namespace HandOnMouse
{
    public class Vector
    {
        public int X;
        public int Y;

        public Vector(int x, int y) { X = x; Y = y; }
        public bool IsZero { get { return X==0 && Y==0; } }
        public double Length {  get { return Math.Sqrt(X*X + Y*Y); } }
    }

    public class Mouse
    {
        public static Mouse Device
        {
            get
            {
                if (_device == null)
                    _device = new Mouse();

                return _device;
            }
        }

        public bool Update(RAWMOUSE mouse)
        {
            if (mouse.Flags != RAWMOUSE.MOUSE.MOVE_RELATIVE)
                return false;

            // Coalesce button events in Buttons status
            var buttons = mouse.ButtonFlags;
            Buttons |= buttons & (
                RAWMOUSE.RI_MOUSE.LEFT_BUTTON_DOWN |
                RAWMOUSE.RI_MOUSE.MIDDLE_BUTTON_DOWN |
                RAWMOUSE.RI_MOUSE.RIGHT_BUTTON_DOWN |
                RAWMOUSE.RI_MOUSE.BUTTON_4_DOWN |
                RAWMOUSE.RI_MOUSE.BUTTON_5_DOWN);
            // Check UP after DOWN in case both are true in a single coalesced message
            if (buttons.HasFlag(RAWMOUSE.RI_MOUSE.LEFT_BUTTON_UP    )) Buttons &= ~RAWMOUSE.RI_MOUSE.LEFT_BUTTON_DOWN;
            if (buttons.HasFlag(RAWMOUSE.RI_MOUSE.MIDDLE_BUTTON_UP  )) Buttons &= ~RAWMOUSE.RI_MOUSE.MIDDLE_BUTTON_DOWN;
            if (buttons.HasFlag(RAWMOUSE.RI_MOUSE.RIGHT_BUTTON_UP   )) Buttons &= ~RAWMOUSE.RI_MOUSE.RIGHT_BUTTON_DOWN;
            if (buttons.HasFlag(RAWMOUSE.RI_MOUSE.BUTTON_4_UP       )) Buttons &= ~RAWMOUSE.RI_MOUSE.BUTTON_4_DOWN;
            if (buttons.HasFlag(RAWMOUSE.RI_MOUSE.BUTTON_5_UP       )) Buttons &= ~RAWMOUSE.RI_MOUSE.BUTTON_5_DOWN;
            // Update complement to enable MouseButtonsFilter with UP requirements
            if (Buttons.HasFlag(RAWMOUSE.RI_MOUSE.LEFT_BUTTON_DOWN  )) Buttons &= ~RAWMOUSE.RI_MOUSE.LEFT_BUTTON_UP  ; else Buttons |= RAWMOUSE.RI_MOUSE.LEFT_BUTTON_UP;
            if (Buttons.HasFlag(RAWMOUSE.RI_MOUSE.MIDDLE_BUTTON_DOWN)) Buttons &= ~RAWMOUSE.RI_MOUSE.MIDDLE_BUTTON_UP; else Buttons |= RAWMOUSE.RI_MOUSE.MIDDLE_BUTTON_UP;
            if (Buttons.HasFlag(RAWMOUSE.RI_MOUSE.RIGHT_BUTTON_DOWN )) Buttons &= ~RAWMOUSE.RI_MOUSE.RIGHT_BUTTON_UP ; else Buttons |= RAWMOUSE.RI_MOUSE.RIGHT_BUTTON_UP;
            if (Buttons.HasFlag(RAWMOUSE.RI_MOUSE.BUTTON_4_DOWN     )) Buttons &= ~RAWMOUSE.RI_MOUSE.BUTTON_4_UP     ; else Buttons |= RAWMOUSE.RI_MOUSE.BUTTON_4_UP;
            if (Buttons.HasFlag(RAWMOUSE.RI_MOUSE.BUTTON_5_DOWN     )) Buttons &= ~RAWMOUSE.RI_MOUSE.BUTTON_5_UP     ; else Buttons |= RAWMOUSE.RI_MOUSE.BUTTON_5_UP;

            if (mouse.LastX != 0 ||
                mouse.LastY != 0)
            {
                if (Drag != null)
                {
                    Drag.X += mouse.LastX;
                    Drag.Y += mouse.LastY;
                }
                RawMouseMove(new Vector(mouse.LastX, mouse.LastY));
            }

            return true;
        }

        public RAWMOUSE.RI_MOUSE Buttons;
        public RAWMOUSE.RI_MOUSE ButtonsPressed
        {
            get
            {
                return Buttons & (
                    // RAWMOUSE.RI_MOUSE.LEFT_BUTTON_DOWN Excluded to use for GUI
                    RAWMOUSE.RI_MOUSE.MIDDLE_BUTTON_DOWN |
                    RAWMOUSE.RI_MOUSE.RIGHT_BUTTON_DOWN |
                    RAWMOUSE.RI_MOUSE.BUTTON_4_DOWN |
                    RAWMOUSE.RI_MOUSE.BUTTON_5_DOWN);
            }
        }

        public Vector Drag { get { return _drag; } }

        public void StartDrag(Vector drag) { _drag = drag; }
        public void StopDrag() { _drag.X = 0; _drag.Y = 0; }

        // Events

        public event RawMouseMoveHandler RawMouseMove;
        public delegate void RawMouseMoveHandler(Vector move);

        // Implementation

        private static Mouse _device;
        private Vector _drag = new Vector(0, 0);
    }
}
